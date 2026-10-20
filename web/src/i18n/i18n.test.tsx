// @vitest-environment jsdom
// i18n 基座行为自证：
// - 首启默认：detectInitialLocale 纯函数（依赖注入 localStorage / 浏览器语言列表）——
//   保存的偏好优先；未保存时 `zh*` → zh-CN、其他 → en；
// - 单例初始化：模块加载即选定语言并同步 <html lang>（含已保存偏好在全新模块加载下的生效——「刷新后保持」的机制证明）；
// - 切换即时生效：App 级组件测试——设置页切 English 后导航 / 状态栏 / 日期格式随之变化
//   localStorage 持久化、documentElement.lang 同步，切回中文恢复。
// 环境前提：vitest.setup.ts 把 navigator.language 钉在 zh-CN（既有断言确定性），本文件不依赖其切换。

import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { cleanup, configure, fireEvent, render, screen, waitFor } from '@testing-library/react'
import App from '../App'
import i18n, { APP_LOCALES, LOCALE_STORAGE_KEY, detectInitialLocale, formatDate, formatDateTime, formatLogTime } from './index'
import type { AppLocale } from './index'
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
    // 设置页卡片（语言切换入口所在页）用到的 API
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
    // 设置页卡片（语言切换入口所在页）用到的 API
    listInstalledPlugins: vi.fn(),
    installPlugin: vi.fn(),
    uninstallPlugin: vi.fn(),
    getPrintSettings: vi.fn(),
    setPrintSettings: vi.fn(),
  },
  probeHealthz: vi.fn(),
}))

vi.mock('../lib/api/client', () => ({
  serverApi: mocks.server,
  localApi: mocks.local,
  setServerBaseUrl: vi.fn(),
  probeHealthz: mocks.probeHealthz,
  clientPackageDownloadUrl: (fileName: string) => `http://127.0.0.1:53961/api/client-packages/${encodeURIComponent(fileName)}`,
  pluginPackageDownloadUrl: (fileName: string) => `http://127.0.0.1:53961/api/plugin-packages/${encodeURIComponent(fileName)}`,
}))

vi.mock('../lib/uiMode', () => ({ UI_MODE: 'client', isServerUi: false }))

/** 内存版 Storage 桩（detectInitialLocale 依赖注入用）。 */
function storageWith(entries: Record<string, string>): Pick<Storage, 'getItem'> {
  return { getItem: (key: string) => (key in entries ? entries[key] : null) }
}

beforeEach(() => {
  vi.clearAllMocks()
  window.localStorage.clear()
  window.sessionStorage.clear()
  // App 级用例测语言切换，以「已看过引导」用户运行——首见自动启动行为归 Guide.test.tsx
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
  mocks.local.getPrinterStatus.mockResolvedValue({ isOnline: true, isPaperOut: false, isPaused: false, message: '就绪' })
  mocks.local.getPrintSettings.mockResolvedValue({ batchEnabled: false, batchSize: 10, batchIntervalMs: 500, dpi: 203 })
})

afterEach(() => {
  cleanup()
  // 语言单例回落 zh-CN + <html lang> 复位，避免用例间串扰
  window.localStorage.clear()
  document.documentElement.lang = 'zh-CN'
  if (i18n.language !== 'zh-CN') void i18n.changeLanguage('zh-CN')
})

describe('AC-02 首启默认语言（detectInitialLocale 纯函数）', () => {
  it('已保存的偏好优先于浏览器语言', () => {
    expect(detectInitialLocale(storageWith({ [LOCALE_STORAGE_KEY]: 'en' }), ['zh-CN'])).toBe('en')
    expect(detectInitialLocale(storageWith({ [LOCALE_STORAGE_KEY]: 'zh-CN' }), ['en-US'])).toBe('zh-CN')
  })

  it('无保存偏好：浏览器语言 zh*（含变体）→ zh-CN', () => {
    expect(detectInitialLocale(storageWith({}), ['zh-CN', 'en-US'])).toBe('zh-CN')
    expect(detectInitialLocale(storageWith({}), ['zh'])).toBe('zh-CN')
    expect(detectInitialLocale(storageWith({}), ['zh-TW', 'en'])).toBe('zh-CN')
  })

  it('无保存偏好：非 zh 浏览器语言（含空列表）→ en', () => {
    expect(detectInitialLocale(storageWith({}), ['en-US'])).toBe('en')
    expect(detectInitialLocale(storageWith({}), ['fr-FR', 'en-US'])).toBe('en')
    expect(detectInitialLocale(storageWith({}), [])).toBe('en')
  })

  it('保存值非法（历史脏数据）→ 回退浏览器语言探测', () => {
    expect(detectInitialLocale(storageWith({ [LOCALE_STORAGE_KEY]: 'fr' }), ['zh-CN'])).toBe('zh-CN')
    expect(detectInitialLocale(null, ['en-US'])).toBe('en')
  })
})

describe('i18n 单例初始化（模块加载即生效）', () => {
  it('测试环境钉住 zh-CN：默认语言 zh-CN 且 <html lang> 同步', () => {
    // vitest.setup.ts 钉住 navigator.language = zh-CN 且 localStorage 为空 → 首启默认 zh-CN（中文浏览器分支）
    expect(i18n.language).toBe('zh-CN')
    expect(document.documentElement.lang).toBe('zh-CN')
  })

  it('已保存 en：全新模块加载（等价刷新）即恢复 en 并同步 <html lang>（AC-01「刷新后保持」机制）', async () => {
    window.localStorage.setItem(LOCALE_STORAGE_KEY, 'en')
    vi.resetModules()
    const fresh = await import('./index')
    try {
      expect(fresh.default.language).toBe('en')
      expect(document.documentElement.lang).toBe('en')
    } finally {
      // 复位：静态引用的单例保持 zh-CN，后续用例不受新实例影响
      window.localStorage.clear()
      document.documentElement.lang = 'zh-CN'
    }
  })
})

describe('Intl 格式化跟随 locale（决策 #164 ④）', () => {
  // 本地时间构造（与时区无关）：2026-09-29 15:04:05
  const d = new Date(2026, 8, 29, 15, 4, 5)

  it('日期 / 日期时间 / 日志时间：zh-CN 与 en 产出各自 locale 形态', () => {
    expect(formatDate('zh-CN', d)).toBe('2026/9/29')
    expect(formatDate('en', d)).toBe('9/29/2026')
    expect(formatDateTime('zh-CN', d)).toBe('2026/9/29 15:04:05')
    expect(formatDateTime('en', d)).toBe('9/29/2026, 15:04:05')
    // 24 小时制两种 locale 一致（hour12: false）
    expect(formatLogTime('zh-CN', d)).toBe('15:04:05')
    expect(formatLogTime('en', d)).toBe('15:04:05')
  })
})

describe('AC-01 设置页切换语言（App 级：即时生效 + 持久化 + lang 同步 + 日期格式跟随）', () => {
  /** 切到设置页、进入「语言」分节并选择目标语言（默认分节为连接方式——先切分节再取 select；导航 / 菜单名随语言变，用双语正则）。 */
  async function switchLanguage(target: AppLocale) {
    fireEvent.click(await screen.findByRole('button', { name: /^(设置|Settings)$/ }))
    fireEvent.click(await screen.findByRole('button', { name: /^(语言|Language)$/ }))
    const select = (await screen.findByTestId('language-panel')).querySelector('select') as HTMLSelectElement
    fireEvent.change(select, { target: { value: target } })
    await waitFor(() => expect(document.documentElement.lang).toBe(target))
  }

  it('默认中文：导航 / 状态栏中文；切 English 后壳层即时切换、localStorage 持久化、<html lang>=en、日期格式跟随，切回中文恢复', async () => {
    mocks.server.listTemplates.mockResolvedValue([{ name: '示例模板', group: '默认', updatedAt: '2026-09-29T15:04:05' }])
    render(<App />)
    // 默认 zh-CN（本机打印服务探测成功后工作台卡片渲染）
    expect(await screen.findByRole('button', { name: '设置' })).toBeTruthy()
    expect(await screen.findByText('示例模板')).toBeTruthy()
    expect(screen.getByText('2026/9/29')).toBeTruthy() // 卡片日期 zh-CN 形态
    expect(screen.getByRole('button', { name: '日志' })).toBeTruthy() // 状态栏

    // 切 English（导航 / 状态栏即时切换；此时位于设置页，回工作台验证日期格式跟随）
    await switchLanguage('en')
    expect(screen.getByRole('button', { name: 'Settings' })).toBeTruthy() // 导航即时切换
    expect(screen.getByRole('button', { name: 'Workbench' })).toBeTruthy()
    expect(screen.queryByRole('button', { name: '设置' })).toBeNull()
    expect(screen.getByRole('button', { name: 'Logs' })).toBeTruthy() // 状态栏
    expect(window.localStorage.getItem(LOCALE_STORAGE_KEY)).toBe('en') // 持久化
    expect(document.documentElement.lang).toBe('en') // index.html lang 动态化
    fireEvent.click(screen.getByRole('button', { name: 'Workbench' }))
    expect(await screen.findByText('9/29/2026')).toBeTruthy() // 工作台卡片日期跟随 en

    // 切回中文
    await switchLanguage('zh-CN')
    expect(screen.getByRole('button', { name: '设置' })).toBeTruthy()
    fireEvent.click(screen.getByRole('button', { name: '工作台' }))
    expect(await screen.findByText('2026/9/29')).toBeTruthy()
    expect(window.localStorage.getItem(LOCALE_STORAGE_KEY)).toBe('zh-CN')
    expect(document.documentElement.lang).toBe('zh-CN')
  })

  it('语言选项自身不随语言翻译：「中文」/「English」固定原文', async () => {
    render(<App />)
    await switchLanguage('en')
    const select = (screen.getByTestId('language-panel') as HTMLElement).querySelector('select') as HTMLSelectElement
    const options = Array.from(select.options).map((o) => o.textContent)
    expect(options).toEqual(['中文', 'English'])
    expect(select.value).toBe('en')
  })

  it('确认弹窗文案随语言切换（清空运行日志确认框）', async () => {
    render(<App />)
    expect(await screen.findByText('本机打印服务：运行中')).toBeTruthy()
    await switchLanguage('en')
    // 打开日志抽屉 → Clear → 确认弹窗（英文文案 + common 域取消按钮）
    fireEvent.click(screen.getByRole('button', { name: 'Logs' }))
    fireEvent.click(document.querySelector('.log-drawer .btn.danger') as HTMLElement)
    expect(await screen.findByRole('dialog')).toBeTruthy()
    expect(screen.getByText('Clear all run logs? This action cannot be undone.')).toBeTruthy()
    expect(screen.getByRole('button', { name: 'Cancel' })).toBeTruthy()
    expect(screen.getByRole('button', { name: 'Clear Logs' })).toBeTruthy()
  })
})

describe('APP_LOCALES 常量', () => {
  it('语言全集 = zh-CN（源）+ en', () => {
    expect(APP_LOCALES).toEqual(['zh-CN', 'en'])
  })
})
