// @vitest-environment jsdom
// 品牌 server 分支守门：导航 logo 不是 UI 模式裁剪面（App.tsx 导航区无条件渲染
// client / server 双构建同源）——守门点 = server 构建（VITE_UI_MODE=server 分支）导航同样渲染
// 承形色重设计后新标（珊瑚 #C9553B 微型几何 + 深青 #123B3C 剥离角），防止品牌替换被误加
// isServerUi 裁剪或回归旧标。

import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { cleanup, configure, render, screen } from '@testing-library/react'
import { ManagementApp as App } from '../App'

// 与 App.server.test.tsx 同口径：挂载链为多段 promise + React 真实宏任务调度，统一放宽到 8000ms
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

vi.mock('../lib/api/client', () => ({
  serverApi: mocks.server,
  localApi: mocks.local,
  setServerBaseUrl: vi.fn(),
  probeHealthz: mocks.probeHealthz,
  clientPackageDownloadUrl: (fileName: string) => `/api/client-packages/${encodeURIComponent(fileName)}`,
  pdaPackageDownloadUrl: (fileName: string) => `/api/pda-packages/${encodeURIComponent(fileName)}`,
  pluginPackageDownloadUrl: (fileName: string) => `/api/plugin-packages/${encodeURIComponent(fileName)}`,
}))

vi.mock('../lib/uiMode', () => ({ UI_MODE: 'server', isServerUi: true }))

beforeEach(() => {
  vi.clearAllMocks()
  window.localStorage.clear()
  window.sessionStorage.clear()
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

describe('server 构建：导航 logo 品牌守门（承形色重设计）', () => {
  it('nav-logo 渲染品牌微型几何（#C9553B 标签体 + #123B3C 剥离角，固定填充，旧标几何不残存）', async () => {
    const { container } = render(<App />)
    // 挂载完成锚点：server 构建导航 tab 就位（logo 与 tabs 同一同步首帧）
    expect(await screen.findByRole('button', { name: '工作台' })).toBeTruthy()

    const logo = container.querySelector('.nav-logo')
    expect(logo).not.toBeNull()
    expect(logo?.querySelector('path[fill="#C9553B"]')).not.toBeNull()
    expect(logo?.querySelector('path[fill="#123B3C"]')).not.toBeNull()
    // 旧标（虚线标签纸 rect + 条码线）不应在 server 构建残存
    expect(logo?.querySelector('rect')).toBeNull()
  })
})
