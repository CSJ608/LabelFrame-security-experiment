// @vitest-environment jsdom
// Server UI「插件管理」页——列表（名称 / 版本 / pluginId / 大小 / 修改时间 / valid 状态，invalid 红标 + 原因）
// 上传（multipart，64MB 预检——纯函数阈值由 pluginLimits.test.ts 覆盖，此处 mock 返回控制）、下载、删除（确认后调用 + 刷新）；空态提示。

import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { PluginPackages } from './PluginPackages'
import { pluginPackageTooLarge } from '../lib/pluginLimits'

const mocks = vi.hoisted(() => ({
  server: {
    listPluginPackages: vi.fn(),
    uploadPluginPackage: vi.fn(),
    deletePluginPackage: vi.fn(),
  },
}))

vi.mock('../lib/api/client', () => ({
  serverApi: mocks.server,
  pluginPackageDownloadUrl: (fileName: string) => `/api/plugin-packages/${encodeURIComponent(fileName)}`,
}))

// 64MB 预检：组件内默认放行（null），超限场景用 mockReturnValueOnce 模拟；真实阈值判定由 pluginLimits.test.ts 覆盖
vi.mock('../lib/pluginLimits', () => ({
  PLUGIN_PACKAGE_MAX_BYTES: 64 * 1024 * 1024,
  pluginPackageTooLarge: vi.fn(() => null),
}))

const PKGS = [
  {
    fileName: 'sample-1.0.0.lfplugin',
    pluginId: 'sample',
    name: '示例插件',
    version: '1.0.0',
    description: '',
    sizeBytes: 2048,
    modifiedAt: '2026-08-17T10:00:00Z',
    url: '/api/plugin-packages/sample-1.0.0.lfplugin',
    valid: true,
  },
  {
    fileName: 'broken.lfplugin',
    sizeBytes: 1024,
    modifiedAt: '2026-08-17T09:00:00Z',
    valid: false,
    invalidReason: 'manifest 缺少 pluginId',
  },
]

beforeEach(() => {
  vi.clearAllMocks()
  mocks.server.listPluginPackages.mockResolvedValue(PKGS)
  mocks.server.uploadPluginPackage.mockResolvedValue([])
  mocks.server.deletePluginPackage.mockResolvedValue(undefined)
})

afterEach(() => {
  cleanup()
})

describe('插件管理页（迭代 23 §5.4）', () => {
  it('列表渲染：名称 / 版本 / pluginId / 大小 / 修改时间 / 有效徽标；invalid 红标 + 原因 + 文件名兜底显示', async () => {
    render(<PluginPackages />)
    expect(await screen.findByText('示例插件')).toBeTruthy()
    expect(screen.getByText('1.0.0')).toBeTruthy()
    expect(screen.getByText('sample')).toBeTruthy()
    expect(screen.getByText('2.0 KB')).toBeTruthy()
    expect(screen.getByText('有效')).toBeTruthy()
    // invalid 条目：元数据缺失显示「—」+ 红标原因 + 文件名兜底
    expect(screen.getByText('无效')).toBeTruthy()
    expect(screen.getByText(/manifest 缺少 pluginId/)).toBeTruthy()
    // 下载链接（同源相对路径）
    const links = screen.getAllByRole('link', { name: /下载/ })
    expect(links[0].getAttribute('href')).toBe('/api/plugin-packages/sample-1.0.0.lfplugin')
    expect(links[1].getAttribute('href')).toBe('/api/plugin-packages/broken.lfplugin')
  })

  it('空列表：空态提示上传入口与客户端安装去处（迭代 73 删除目录 / 扩展名技术细节）', async () => {
    mocks.server.listPluginPackages.mockResolvedValue([])
    render(<PluginPackages />)
    expect(await screen.findByText('暂无插件包')).toBeTruthy()
    expect(screen.getByText(/上传后客户端可在「设置 → 插件管理」中安装/)).toBeTruthy()
    expect(screen.getByRole('button', { name: '上传插件包' })).toBeTruthy()
  })

  it('加载失败：显示错误信息', async () => {
    mocks.server.listPluginPackages.mockRejectedValue(new Error('network down'))
    render(<PluginPackages />)
    expect(await screen.findByText(/获取插件包列表失败/)).toBeTruthy()
  })

  it('上传：选择文件 → uploadPluginPackage(file) + 列表刷新', async () => {
    render(<PluginPackages />)
    await screen.findByText('示例插件')
    const file = new File(['pkg'], 'sample-1.1.0.lfplugin')
    fireEvent.change(document.getElementById('pluginPkgFile')!, { target: { files: [file] } })
    await waitFor(() => expect(mocks.server.uploadPluginPackage).toHaveBeenCalledWith(file))
    // 上传后重新拉取列表
    await waitFor(() => expect(mocks.server.listPluginPackages).toHaveBeenCalledTimes(2))
    expect(await screen.findByText(/已上传/)).toBeTruthy()
  })

  it('上传超过 64MB：预检拦截，不调 uploadPluginPackage，显示中文提示', async () => {
    vi.mocked(pluginPackageTooLarge).mockReturnValueOnce('插件包超过大小上限（最大约 64MB）。')
    render(<PluginPackages />)
    await screen.findByText('示例插件')
    const file = new File(['pkg'], 'big.lfplugin')
    fireEvent.change(document.getElementById('pluginPkgFile')!, { target: { files: [file] } })
    expect(await screen.findByText('插件包超过大小上限（最大约 64MB）。')).toBeTruthy()
    expect(mocks.server.uploadPluginPackage).not.toHaveBeenCalled()
  })

  it('删除：确认 Modal 后调用 deletePluginPackage + 刷新；取消不调用', async () => {
    render(<PluginPackages />)
    await screen.findByText('示例插件')

    fireEvent.click(screen.getAllByRole('button', { name: /删除/ })[0])
    // 自研 Modal 确认（替代原生 confirm 弹窗）——确认后删除 + 刷新
    expect(await screen.findByText('删除插件包')).toBeTruthy()
    fireEvent.click(screen.getByRole('button', { name: '确认删除' }))
    await waitFor(() => expect(mocks.server.deletePluginPackage).toHaveBeenCalledWith('sample-1.0.0.lfplugin'))
    await waitFor(() => expect(mocks.server.listPluginPackages).toHaveBeenCalledTimes(2))

    // 取消：关闭确认框，不再调用删除
    fireEvent.click(screen.getAllByRole('button', { name: /删除/ })[0])
    fireEvent.click(await screen.findByRole('button', { name: '取消' }))
    expect(mocks.server.deletePluginPackage).toHaveBeenCalledTimes(1)
  })
})
