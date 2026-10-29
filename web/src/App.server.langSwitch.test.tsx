// @vitest-environment jsdom
// 壳层语言切换器——server 构建（server 无设置页，切换器是唯一界面入口）：
// - 在位：server 构建导航尾部同样显示语言控件（两构建统一）
// - 全程无设置页 / F12：点击即点即生效（词条 / <html lang> / localStorage），重载（等价刷新）保持。
// 文件级 vi.mock 钉住 server 构建（同 App.server.test.tsx 骨架，跳过 localApi 探测）。

import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { cleanup, configure, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { ManagementApp as App } from './App'
import i18n, { LOCALE_STORAGE_KEY } from './i18n'

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

function findLangButton() {
  return screen.findByRole('button', { name: /^(切换到 English|Switch to Chinese)$/ })
}

beforeEach(() => {
  vi.clearAllMocks()
  window.localStorage.clear()
  window.sessionStorage.clear()
  mocks.server.healthz.mockResolvedValue({ service: 'LabelFrame.Server', status: 'ok' })
  mocks.server.listTemplates.mockResolvedValue([])
  mocks.server.listDevices.mockResolvedValue([])
  mocks.server.listClientPackages.mockResolvedValue([])
  mocks.server.listPdaPackages.mockResolvedValue([])
  mocks.server.listPluginPackages.mockResolvedValue([])
  mocks.local.listTemplates.mockResolvedValue([])
})

afterEach(() => {
  cleanup()
  window.localStorage.clear()
  document.documentElement.lang = 'zh-CN'
  if (i18n.language !== 'zh-CN') void i18n.changeLanguage('zh-CN')
})

describe('AC-01 壳层切换器在位（server 构建——无设置页也有语言入口）', () => {
  it('nav-foot 显示「中」与连接状态点；导航无「设置」入口（切换不依赖设置页）', async () => {
    render(<App />)
    const btn = await findLangButton()
    expect(btn.textContent).toBe('中')
    expect(btn.getAttribute('title')).toBe('切换到 English')
    expect(document.querySelector('.nav-foot .status-dot')).toBeTruthy()
    expect(screen.queryByRole('button', { name: '设置' })).toBeNull()
  })
})

describe('AC-02 server 构建点击切换（全程无需设置页或 F12）', () => {
  it('点击即时切 en：导航词条 / <html lang> / localStorage 即时变化，再点切回恢复', async () => {
    render(<App />)
    expect(await screen.findByRole('button', { name: '下载中心' })).toBeTruthy() // server 构建导航默认中文

    fireEvent.click(await findLangButton())
    await waitFor(() => expect(document.documentElement.lang).toBe('en'))
    expect(screen.getByRole('button', { name: 'Downloads' })).toBeTruthy() // server 构建词条即时切换
    expect(screen.getByRole('button', { name: 'Online Devices' })).toBeTruthy()
    expect(screen.queryByRole('button', { name: '下载中心' })).toBeNull()
    expect(window.localStorage.getItem(LOCALE_STORAGE_KEY)).toBe('en')
    expect((await findLangButton()).textContent).toBe('EN')

    // 再点切回中文恢复
    fireEvent.click(await findLangButton())
    await waitFor(() => expect(document.documentElement.lang).toBe('zh-CN'))
    expect(screen.getByRole('button', { name: '下载中心' })).toBeTruthy()
    expect(window.localStorage.getItem(LOCALE_STORAGE_KEY)).toBe('zh-CN')
  })

  it('刷新保持：切 en 后全新模块图重载（等价刷新），server 构建壳层仍为 en 态', async () => {
    render(<App />)
    fireEvent.click(await findLangButton())
    await waitFor(() => expect(window.localStorage.getItem(LOCALE_STORAGE_KEY)).toBe('en'))
    cleanup()
    vi.resetModules()
    const { ManagementApp: FreshApp } = await import('./App')
    render(<FreshApp />)
    expect(await screen.findByRole('button', { name: 'Downloads' })).toBeTruthy()
    expect((await findLangButton()).textContent).toBe('EN')
    expect(document.documentElement.lang).toBe('en')
  })
})
