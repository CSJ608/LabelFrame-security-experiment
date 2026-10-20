// @vitest-environment jsdom
// 壳层语言切换器——client 构建：
// - 在位：nav-foot（连接状态点旁）显示当前语言短名「中 / EN」
// - 即点即生效：点击后词条 / <html lang> / localStorage 即时变化，再点切回恢复
//   「刷新保持」经 vi.resetModules 全新模块图重载 App 验证（localStorage 偏好在模块加载即生效）；
// - 双入口一致：设置页语言卡（select）与壳层切换器同一 changeLocale 单点，显示同步、互切一致。
// 环境前提：vitest.setup.ts 钉住 navigator.language = zh-CN（首启默认中文断言确定性）。

import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { cleanup, configure, fireEvent, render, screen, waitFor } from '@testing-library/react'
import App from './App'
import i18n, { LOCALE_STORAGE_KEY } from './i18n'
import { GUIDE_SEEN_KEY } from './lib/guide'

// 挂载链（AppContext 启动链 → 模板列表加载）为多段 promise + 真实宏任务调度，统一放宽异步等待（同 App.server.test.tsx 口径）
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
    listPluginPackages: vi.fn(),
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
  pluginPackageDownloadUrl: (fileName: string) => `/api/plugin-packages/${encodeURIComponent(fileName)}`,
}))

// 文件级钉住 client 构建（与 i18n.test.tsx 同法）——外层 VITE_UI_MODE=server 整仓跑时本文件行为不变
vi.mock('./lib/uiMode', () => ({ UI_MODE: 'client', isServerUi: false }))

/** 壳层切换器按钮（aria-label 随当前语言变，用正则双语定位）。 */
function findLangButton() {
  return screen.findByRole('button', { name: /^(切换到 English|Switch to Chinese)$/ })
}

beforeEach(() => {
  vi.clearAllMocks()
  window.localStorage.clear()
  window.sessionStorage.clear()
  // 本文件测壳层语言切换器，以「已看过引导」用户运行——首见自动启动行为归 Guide.test.tsx
  window.localStorage.setItem(GUIDE_SEEN_KEY, 'done')
  mocks.server.healthz.mockResolvedValue({ service: 'LabelFrame.Server', status: 'ok' })
  mocks.local.healthz.mockResolvedValue({ service: 'LabelFrame.WinHost', status: 'ok' })
  mocks.local.getHostConfig.mockResolvedValue({ serverUrl: 'http://127.0.0.1:53961', deviceId: 'PC-1', deviceName: 'PC-1', ips: [] })
  mocks.local.getTransport.mockResolvedValue({ mode: 'Log', params: {} })
  mocks.local.listTemplates.mockResolvedValue([])
  mocks.server.listTemplates.mockResolvedValue([])
  mocks.server.listClientPackages.mockResolvedValue([])
  mocks.server.listPluginPackages.mockResolvedValue([])
  mocks.local.listInstalledPlugins.mockResolvedValue([])
  mocks.local.getPrintSettings.mockResolvedValue({ batchEnabled: false, batchSize: 10, batchIntervalMs: 500, dpi: 203 })
})

afterEach(() => {
  cleanup()
  // 语言单例回落 zh-CN + <html lang> 复位（resetModules 重载出的新单例可能已把 lang 置 en），避免用例间串扰
  window.localStorage.clear()
  document.documentElement.lang = 'zh-CN'
  if (i18n.language !== 'zh-CN') void i18n.changeLanguage('zh-CN')
})

describe('AC-01 壳层切换器在位（client 构建）', () => {
  it('nav-foot 显示当前语言短名「中」，提示为「切换到 English」，连接状态点同在', async () => {
    render(<App />)
    const btn = await findLangButton()
    expect(btn.textContent).toBe('中')
    expect(btn.getAttribute('title')).toBe('切换到 English')
    expect(document.querySelector('.nav-foot .status-dot')).toBeTruthy()
  })
})

describe('AC-02 壳层点击切换（client 构建）', () => {
  it('点击「中」即时切 en：词条 / <html lang> / localStorage 变化，再点「EN」切回恢复', async () => {
    render(<App />)
    const btn = await findLangButton()
    expect(screen.getByRole('button', { name: '设置' })).toBeTruthy() // 壳层默认中文

    fireEvent.click(btn)
    // 词条即时切换 + html lang 同步 + localStorage 持久化
    await waitFor(() => expect(document.documentElement.lang).toBe('en'))
    expect(screen.getByRole('button', { name: 'Workbench' })).toBeTruthy()
    expect(screen.getByRole('button', { name: 'Settings' })).toBeTruthy()
    expect(screen.queryByRole('button', { name: '设置' })).toBeNull()
    expect(window.localStorage.getItem(LOCALE_STORAGE_KEY)).toBe('en')
    // 按钮转为当前语言「EN」，提示转为切回中文
    const btnEn = await findLangButton()
    expect(btnEn.textContent).toBe('EN')
    expect(btnEn.getAttribute('title')).toBe('Switch to Chinese')

    // 再点切回中文恢复
    fireEvent.click(btnEn)
    await waitFor(() => expect(document.documentElement.lang).toBe('zh-CN'))
    expect(screen.getByRole('button', { name: '设置' })).toBeTruthy()
    expect(window.localStorage.getItem(LOCALE_STORAGE_KEY)).toBe('zh-CN')
    const btnZh = await findLangButton()
    expect(btnZh.textContent).toBe('中')
  })

  it('刷新保持：壳层切 en 后全新模块图重载（等价刷新），切换器与壳层仍为 en 态', async () => {
    render(<App />)
    fireEvent.click(await findLangButton())
    await waitFor(() => expect(window.localStorage.getItem(LOCALE_STORAGE_KEY)).toBe('en'))
    cleanup()
    // 等价刷新：清模块缓存重新加载 App（其 import 链上的 i18n 单例在模块加载期读 localStorage 恢复 en）
    vi.resetModules()
    const { default: FreshApp } = await import('./App')
    render(<FreshApp />)
    expect(await screen.findByRole('button', { name: 'Workbench' })).toBeTruthy() // 壳层词条仍英文
    const btn = await findLangButton()
    expect(btn.textContent).toBe('EN')
    expect(document.documentElement.lang).toBe('en')
  })
})

describe('AC-03 双入口一致（设置页语言卡 ↔ 壳层切换器，同一 changeLocale 单点）', () => {
  /** 进入设置页「语言」分节并返回语言卡 select（client 构建专属入口；默认分节为连接方式——先切分节，菜单名随语言用双语正则）。 */
  async function openSettingsSelect() {
    fireEvent.click(await screen.findByRole('button', { name: /^(设置|Settings)$/ }))
    fireEvent.click(await screen.findByRole('button', { name: /^(语言|Language)$/ }))
    const select = (await screen.findByTestId('language-panel')).querySelector('select') as HTMLSelectElement
    return select
  }

  it('初始显示同步（select=zh-CN、按钮「中」）；select 切 en → 壳层按钮即时变「EN」；点壳层按钮 → select 回 zh-CN', async () => {
    render(<App />)
    const select = await openSettingsSelect()
    // 初始同步：语言卡与壳层切换器均为中文态
    expect(select.value).toBe('zh-CN')
    expect((await findLangButton()).textContent).toBe('中')

    // 设置页入口切 en → 壳层切换器即时跟随（同一 i18next 单例，useTranslation 订阅重渲染）
    fireEvent.change(select, { target: { value: 'en' } })
    await waitFor(() => expect(document.documentElement.lang).toBe('en'))
    expect((await findLangButton()).textContent).toBe('EN')

    // 壳层入口切回 → 语言卡 select 即时跟随
    fireEvent.click(await findLangButton())
    await waitFor(() => expect(document.documentElement.lang).toBe('zh-CN'))
    expect(select.value).toBe('zh-CN')
    expect(screen.getByRole('button', { name: '设置' })).toBeTruthy()
  })
})
