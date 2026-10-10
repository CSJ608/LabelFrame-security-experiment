// @vitest-environment jsdom
// hash 路由地基（迭代 127 · #312，决策 #177）server 分支——AC-03：
// server 七页同形态路由（#/devices / #/packages / #/plugin-packages 等）；受限 tab 集不可经 hash 越权
// 进入（#/help、#/settings 解析即回退 workbench 并规范化 URL，页面不挂载）；#/packages/<sub> 直达下载中心
// 页内 tab（含旧 #dc= 分享链接兼容映射）。vi.mock uiMode 注入 server 分支（Guide.server.test.tsx 双跑先例）。

import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { cleanup, configure, fireEvent, render, screen, waitFor } from '@testing-library/react'
import App from './App'

configure({ asyncUtilTimeout: 8000 })

const mocks = vi.hoisted(() => ({
  server: {
    healthz: vi.fn(),
    listTemplates: vi.fn(),
    getTemplate: vi.fn(),
    saveTemplate: vi.fn(),
    deleteTemplate: vi.fn(),
    exportTemplate: vi.fn(),
    importTemplate: vi.fn(),
    importExcel: vi.fn(),
    submitJob: vi.fn(),
    getJob: vi.fn(),
    retryJobItem: vi.fn(),
    getJobs: vi.fn(),
    listDevices: vi.fn(),
    renderImage: vi.fn(),
    renderImages: vi.fn(),
    getLogs: vi.fn(),
    listClientPackages: vi.fn(),
    uploadClientPackage: vi.fn(),
    deleteClientPackage: vi.fn(),
    listPdaPackages: vi.fn(),
    uploadPdaPackage: vi.fn(),
    deletePdaPackage: vi.fn(),
    listServerIpv4Candidates: vi.fn(),
    listPluginPackages: vi.fn(),
    uploadPluginPackage: vi.fn(),
    deletePluginPackage: vi.fn(),
    downloadPluginPackage: vi.fn(),
  },
  local: {
    healthz: vi.fn(),
    listTemplates: vi.fn(),
    getTemplate: vi.fn(),
    saveTemplate: vi.fn(),
    deleteTemplate: vi.fn(),
    exportTemplate: vi.fn(),
    importTemplate: vi.fn(),
    importExcel: vi.fn(),
    submitJob: vi.fn(),
    getJob: vi.fn(),
    retryJobItem: vi.fn(),
    getJobs: vi.fn(),
    listDevices: vi.fn(),
    renderImage: vi.fn(),
    renderImages: vi.fn(),
    getLogs: vi.fn(),
    getTransport: vi.fn(),
    setTransport: vi.fn(),
    testTransport: vi.fn(),
    getPrinterStatus: vi.fn(),
    testPrinter: vi.fn(),
    getHostConfig: vi.fn(),
    setHostConfig: vi.fn(),
    listInstalledPlugins: vi.fn(),
    installPlugin: vi.fn(),
    uninstallPlugin: vi.fn(),
    getPrintSettings: vi.fn(),
    setPrintSettings: vi.fn(),
  },
  probeHealthz: vi.fn(),
}))

vi.mock('./lib/api/client', () => ({
  serverApi: mocks.server,
  localApi: mocks.local,
  setServerBaseUrl: vi.fn(),
  probeHealthz: mocks.probeHealthz,
  clientPackageDownloadUrl: (fileName: string) => `/api/client-packages/${encodeURIComponent(fileName)}`,
  pdaPackageDownloadUrl: (fileName: string) => `/api/pda-packages/${encodeURIComponent(fileName)}`,
  pluginPackageDownloadUrl: (fileName: string) => `/api/plugin-packages/${encodeURIComponent(fileName)}`,
}))

vi.mock('./lib/uiMode', () => ({ UI_MODE: 'server', isServerUi: true }))

beforeEach(() => {
  vi.clearAllMocks()
  window.localStorage.clear()
  window.sessionStorage.clear()
  // 路由用例前置：重置 hash（jsdom location 跨用例共享，残留会使初始页非 workbench）
  window.location.hash = ''
  mocks.server.healthz.mockResolvedValue({ service: 'LabelFrame.Server', status: 'ok' })
  mocks.server.listTemplates.mockResolvedValue([])
  mocks.server.listDevices.mockResolvedValue([])
  mocks.server.listClientPackages.mockResolvedValue([])
  mocks.server.listPdaPackages.mockResolvedValue([])
  mocks.server.listServerIpv4Candidates.mockResolvedValue({ candidates: [] })
  mocks.server.listPluginPackages.mockResolvedValue([])
  mocks.local.listTemplates.mockResolvedValue([])
})

afterEach(() => {
  cleanup()
})

/** 各 server 页的内容锚点（页副标题——与主导航按钮同名文本区分；下载中心以首屏「客户端」区块断言）。 */
const PAGE_MARKER = {
  workbench: '模板管理',
  designer: '尚未打开模板',
  data: '填写数据并打印 / Excel 批量打印',
  devices: '点击设备可将其设为「数据与打印」的默认目标',
  jobs: '最近 100 条打印记录；有作业进行中时自动刷新',
  pluginPackages: '插件包统一在此分发（上传 / 下载 / 删除）',
} as const

describe('server 构建 · AC-03：路由对 server tab 集生效且形态一致', () => {
  it('七页逐 tab 点击：hash 与 client 构建同形态（#/<page>）', async () => {
    render(<App />)
    await screen.findByText(PAGE_MARKER.workbench)

    const cases: [string, string, string][] = [
      ['设计器', '#/designer', PAGE_MARKER.designer],
      ['数据与打印', '#/data', PAGE_MARKER.data],
      ['在线设备', '#/devices', PAGE_MARKER.devices],
      ['作业历史', '#/jobs', PAGE_MARKER.jobs],
      ['下载中心', '#/packages', '客户端'],
      ['插件管理', '#/plugin-packages', PAGE_MARKER.pluginPackages],
      ['工作台', '#/workbench', PAGE_MARKER.workbench],
    ]
    for (const [nav, hash, marker] of cases) {
      fireEvent.click(screen.getByRole('button', { name: nav }))
      await waitFor(() => expect(window.location.hash).toBe(hash))
      expect(await screen.findByText(marker)).toBeTruthy()
    }
  })

  it('越权 hash（#/help / #/settings 为 client 专属）：解析回退 workbench 并规范化 URL，页面不挂载', async () => {
    window.location.hash = '#/help'
    render(<App />)
    expect(await screen.findByText(PAGE_MARKER.workbench)).toBeTruthy()
    // help / settings 页不渲染（server 构建受限 tab 集——帮助页副标题 / 设置页副标题均不出现）
    expect(screen.queryByText('五个页面的使用文章；界面内「?」就近说明')).toBeNull()
    expect(screen.queryByText('服务端地址 / 本机连接与打印机')).toBeNull()
    // 决策 #177 拍板 A：回退同时 URL 规范化为 #/workbench（与所见一致、可安全分享）
    await waitFor(() => expect(window.location.hash).toBe('#/workbench'))
    cleanup()

    window.location.hash = '#/settings'
    render(<App />)
    expect(await screen.findByText(PAGE_MARKER.workbench)).toBeTruthy()
    await waitFor(() => expect(window.location.hash).toBe('#/workbench'))
  })

  it('#/packages/windows 直达下载中心 Windows 管理 tab（迭代 118 直链语义经路由 sub 段保留）', async () => {
    window.location.hash = '#/packages/windows'
    render(<App />)
    expect(await screen.findByRole('button', { name: /上传 Windows 安装包/ })).toBeTruthy()
    // 快速访问首屏不渲染
    expect(screen.queryByText('客户端')).toBeNull()
  })

  it('旧 #dc=windows 分享链接兼容映射：server 构建同样直达 Windows 管理并规范化为 #/packages/windows', async () => {
    window.location.hash = '#dc=windows'
    render(<App />)
    expect(await screen.findByRole('button', { name: /上传 Windows 安装包/ })).toBeTruthy()
    await waitFor(() => expect(window.location.hash).toBe('#/packages/windows'))
  })
})
