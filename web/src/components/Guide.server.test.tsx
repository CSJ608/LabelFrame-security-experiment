// @vitest-environment jsdom
// server 构建分支（VITE_UI_MODE=server）引导不渲染守门：
// 引导组件与状态栏「使用引导」重看入口均以 !isServerUi 条件挂载——引导仅 client 模式，
// server 模式（web/dist-server）菜单结构不同，需单独裁剪。
// 拆分独立文件与 App.server.test.tsx 同款双跑机制（vi.mock uiMode 注入分支，uiMode.ts:3 注释明示）。

import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { cleanup, configure, render, screen } from '@testing-library/react'
import { ManagementApp as App } from '../App'
import { GUIDE_SEEN_KEY } from '../lib/guide'

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

describe('server 构建：引导整特性不渲染（迭代 119 · #276）', () => {
  it('无首见标记（首次用户）也不自动启动引导，不渲染引导层', async () => {
    expect(window.localStorage.getItem(GUIDE_SEEN_KEY)).toBeNull()
    render(<App />)
    // 等壳层启动完成，再等过首见自动启动窗口（300ms）确认引导未出现——非异步时序假绿
    expect(await screen.findByText(`${window.location.origin} · 服务端管理界面`)).toBeTruthy()
    await new Promise((resolve) => setTimeout(resolve, 500))
    expect(document.querySelector('.guide-layer')).toBeNull()
    expect(screen.queryByText('欢迎来到工作台')).toBeNull()
  })

  it('状态栏不渲染「使用引导」重看入口', async () => {
    render(<App />)
    expect(await screen.findByText(`${window.location.origin} · 服务端管理界面`)).toBeTruthy()
    expect(screen.queryByRole('button', { name: '使用引导' })).toBeNull()
    expect(screen.queryByRole('button', { name: 'Replay Tour' })).toBeNull()
  })
})
