// @vitest-environment jsdom
// 迭代 20：server 构建（VITE_UI_MODE=server）菜单裁剪——含 在线设备，移除 设置 与一切
// 打印机相关入口；迭代 75（#112）：「设备日志 / PDA 日志」页下线（导航入口移除，回传链路不存在前
// 的界面收敛）；状态栏显示服务端地址（页面 origin /「同源」）与 UI 模式；
// K2 守门：跳过 localApi 探测（getHostConfig / getTransport 不被调用）。

import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { cleanup, configure, fireEvent, render, screen, waitFor } from '@testing-library/react'
import App from './App'
// 迭代 96（#183）：挂载链（AppContext 启动链 → 模板列表加载）为多段 promise + React 真实宏任务
// 调度，CI 高负载 runner 上偶发超过 findBy / waitFor 默认 1000ms；统一放宽到 8000ms（与
// JobHistory / DataPrint / Settings 同口径），单测总超时由 vitest.config testTimeout（20s）兜住。
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
  mocks.server.healthz.mockResolvedValue({ service: 'LabelFrame.Server', status: 'ok' })
  mocks.server.listTemplates.mockResolvedValue([])
  mocks.server.listDevices.mockResolvedValue([
    { deviceId: 'device-1', name: '仓库-1 打印电脑', registeredAt: '2026-08-11T00:00:00Z', lastSeenAt: '2026-08-11T01:00:00Z', status: 'Online', lastIp: '192.168.1.5' },
  ])
  mocks.server.listClientPackages.mockResolvedValue([])
  mocks.server.listPdaPackages.mockResolvedValue([])
  mocks.server.listServerIpv4Candidates.mockResolvedValue({ candidates: [] })
  mocks.server.listPluginPackages.mockResolvedValue([])
  mocks.local.listTemplates.mockResolvedValue([])
})

afterEach(() => {
  cleanup()
})

describe('server 构建：菜单裁剪（迭代 20 §2.2 / Y5）', () => {
  it('含 在线设备 / 下载中心 / 插件管理 / 工作台 / 设计器 / 数据与打印 / 作业历史 / 帮助（迭代 129 纳入）；不含 设置 / 设备日志 / PDA 日志（迭代 75 日志页下线）', async () => {
    render(<App />)
    expect(await screen.findByRole('button', { name: '在线设备' })).toBeTruthy()
    expect(screen.getByRole('button', { name: '工作台' })).toBeTruthy()
    expect(screen.getByRole('button', { name: '设计器' })).toBeTruthy()
    expect(screen.getByRole('button', { name: '数据与打印' })).toBeTruthy()
    expect(screen.getByRole('button', { name: '作业历史' })).toBeTruthy()
    // 迭代 59（决策 #119）：Server UI 统一「下载中心」页入口（原「客户端下载」页升级）
    expect(screen.getByRole('button', { name: '下载中心' })).toBeTruthy()
    // 迭代 23 §5.4：Server UI「插件管理」页入口（与「下载中心」并列）
    expect(screen.getByRole('button', { name: '插件管理' })).toBeTruthy()
    // 迭代 129（#320，决策 #179）：「帮助」页纳入 server 主导航（末位，与 client 对齐）
    expect(screen.getByRole('button', { name: '帮助' })).toBeTruthy()
    // 设置页不存在；「设备日志 / PDA 日志」页已下线（迭代 75，#112——/api/logs 端点与 logs.db 后端保留）
    expect(screen.queryByRole('button', { name: '设置' })).toBeNull()
    expect(screen.queryByRole('button', { name: '设备日志' })).toBeNull()
    expect(screen.queryByRole('button', { name: 'PDA 日志' })).toBeNull()
  })
})

describe('server 构建：K2 跳过 localApi 探测', () => {
  it('AppProvider 启动不调 getHostConfig / getTransport，healthz 正常探测', async () => {
    render(<App />)
    await waitFor(() => expect(mocks.server.healthz).toHaveBeenCalled())
    expect(mocks.local.getHostConfig).not.toHaveBeenCalled()
    expect(mocks.local.getTransport).not.toHaveBeenCalled()
  })
})

describe('server 构建：状态栏（服务端地址 + UI 模式，无打印机内容）', () => {
  it('显示 页面 origin · 服务端管理界面（迭代 73 去除「同源」开发者术语）', async () => {
    render(<App />)
    expect(await screen.findByText(`${window.location.origin} · 服务端管理界面`)).toBeTruthy()
    // 无本机 IP 显示（server 构建不读 /api/host/config）
    expect(screen.queryByText(/本机 IP：/)).toBeNull()
  })
})

describe('server 构建：在线设备页入口', () => {
  it('点击「在线设备」tab 打开设备列表（GET /api/devices）', async () => {
    render(<App />)
    fireEvent.click(await screen.findByRole('button', { name: '在线设备' }))
    expect(await screen.findByText('device-1')).toBeTruthy()
    expect(screen.getByText('192.168.1.5')).toBeTruthy()
  })
})

describe('server 构建：下载中心页入口（迭代 22 §2.3；迭代 59 统一下载中心；迭代 118 三 tab 改版）', () => {
  it('点击「下载中心」导航：默认快速访问首屏（最新上传双卡 + 连接信息卡）；切 Windows / Android 管理 tab 列表 / 上传齐全', async () => {
    mocks.server.listClientPackages.mockResolvedValue([
      { fileName: 'LabelFrame.Client-0.18.0.msi', sizeBytes: 2 * 1024 * 1024, modifiedAt: '2026-08-17T10:00:00Z', url: '/api/client-packages/LabelFrame.Client-0.18.0.msi' },
    ])
    mocks.server.listPdaPackages.mockResolvedValue([
      { fileName: 'LabelFrame-AndroidHost-0.26.0.apk', sizeBytes: 22 * 1024 * 1024, modifiedAt: '2026-09-10T08:00:00Z', url: '/api/pda-packages/LabelFrame-AndroidHost-0.26.0.apk' },
    ])
    render(<App />)
    fireEvent.click(await screen.findByRole('button', { name: '下载中心' }))
    // 默认快速访问：两平台「最新上传」卡 + 连接信息卡（AC-01/02）
    expect(await screen.findByText('LabelFrame.Client-0.18.0.msi')).toBeTruthy()
    expect(screen.getByText('LabelFrame-AndroidHost-0.26.0.apk')).toBeTruthy()
    expect(screen.getByText('服务端信息')).toBeTruthy()

    // Windows 管理 tab：上传 / 列表 / 下载链接 / 二维码（title = origin + 下载路径）
    fireEvent.click(screen.getByRole('button', { name: 'Windows 包管理' }))
    expect(await screen.findByRole('button', { name: /上传 Windows 安装包/ })).toBeTruthy()
    const clientLink = screen.getByTitle('下载 LabelFrame.Client-0.18.0.msi')
    expect(clientLink.tagName).toBe('A')
    expect(clientLink.getAttribute('href')).toBe('/api/client-packages/LabelFrame.Client-0.18.0.msi')
    expect(screen.getByTitle(`${window.location.origin}/api/client-packages/LabelFrame.Client-0.18.0.msi`)).toBeTruthy()

    // Android 管理 tab：上传 / 下载链接 / 二维码与 Android 授权文案
    fireEvent.click(screen.getByRole('button', { name: 'Android 包管理' }))
    expect(await screen.findByRole('button', { name: /上传 APK/ })).toBeTruthy()
    const pdaLink = screen.getByTitle('下载 LabelFrame-AndroidHost-0.26.0.apk')
    expect(pdaLink.tagName).toBe('A')
    expect(pdaLink.getAttribute('href')).toBe('/api/pda-packages/LabelFrame-AndroidHost-0.26.0.apk')
    expect(screen.getByTitle(`${window.location.origin}/api/pda-packages/LabelFrame-AndroidHost-0.26.0.apk`)).toBeTruthy()
    expect(screen.getByText(/未知来源/)).toBeTruthy()
  })
})

describe('server 构建：插件管理页入口（迭代 23 §5.4）', () => {
  it('点击「插件管理」tab：列表 / 上传 / 刷新按钮齐全（GET /api/plugin-packages；invalid 红标）', async () => {
    mocks.server.listPluginPackages.mockResolvedValue([
      { fileName: 'sample-1.0.0.lfplugin', pluginId: 'sample', name: '示例插件', version: '1.0.0', sizeBytes: 2048, modifiedAt: '2026-08-17T10:00:00Z', valid: true },
      { fileName: 'broken.lfplugin', sizeBytes: 1024, modifiedAt: '2026-08-17T09:00:00Z', valid: false, invalidReason: 'manifest 缺少 pluginId' },
    ])
    render(<App />)
    fireEvent.click(await screen.findByRole('button', { name: '插件管理' }))
    expect(await screen.findByText('示例插件')).toBeTruthy()
    expect(screen.getByText('sample')).toBeTruthy()
    expect(screen.getByText('无效')).toBeTruthy()
    expect(screen.getByRole('button', { name: '上传插件包' })).toBeTruthy()
    // 下载链接（server 构建同源相对路径；两行各一个）
    const links = screen.getAllByRole('link', { name: /下载/ })
    expect(links[0].getAttribute('href')).toBe('/api/plugin-packages/sample-1.0.0.lfplugin')
    expect(links[1].getAttribute('href')).toBe('/api/plugin-packages/broken.lfplugin')
  })
})
