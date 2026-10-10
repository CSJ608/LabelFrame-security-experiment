// @vitest-environment jsdom
// hash 路由地基（迭代 127 · #312，决策 #177）client 分支——AC-01 / AC-02：
// - AC-01 逐页切换 hash 实时反映；预设 hash 挂载直达（刷新保持同机制＝带 hash 重挂载初始解析）；hashchange 页面跟随。
// - AC-02 后退 / 前进在应用页面间导航；dirty 设计器离开守卫照常生效——back 触发守卫时页面与 hash 均不动、
//   不新增栈条目（pre-revert 按戳回退）；「继续编辑」停留、「放弃更改」前进且恰新增一条。
// server 分支（受限 tab 集越权回退 / #/packages 直达）在 App.route.server.test.tsx。

import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { cleanup, configure, fireEvent, render, screen, waitFor } from '@testing-library/react'
import App from './App'
import { GUIDE_SEEN_KEY } from './lib/guide'

// 挂载链（AppContext 启动链 → 模板加载）为多段 promise + 真实宏任务调度，统一放宽异步等待（同 App.langSwitch 口径）
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

// 文件级钉住 client 构建（外层 VITE_UI_MODE=server 整仓跑时本文件行为不变）
vi.mock('./lib/uiMode', () => ({ UI_MODE: 'client', isServerUi: false }))

// 画布桩：设计器 dirty 用例真实挂载 Designer（jsdom 无 2d context，konva 画布以桩替换——Designer.test.tsx 同法）
vi.mock('./pages/designer/CanvasViewport', () => ({
  CanvasViewport: () => null,
}))

beforeEach(() => {
  vi.clearAllMocks()
  window.localStorage.clear()
  window.sessionStorage.clear()
  // 路由用例前置：重置 hash（jsdom location 跨用例共享，残留会使初始页非 workbench）
  window.location.hash = ''
  // 本文件测路由，以「已看过引导」用户运行（首见自动启动归 Guide.test.tsx）
  window.localStorage.setItem(GUIDE_SEEN_KEY, 'done')
  mocks.server.healthz.mockResolvedValue({ service: 'LabelFrame.Server', status: 'ok' })
  mocks.local.healthz.mockResolvedValue({ service: 'LabelFrame.WinHost', status: 'ok' })
  mocks.local.getHostConfig.mockResolvedValue({ serverUrl: 'http://127.0.0.1:53961', deviceId: 'PC-1', deviceName: 'PC-1', ips: [] })
  mocks.local.getTransport.mockResolvedValue({ mode: 'Log', params: {} })
  mocks.local.listTemplates.mockResolvedValue([])
  mocks.server.listTemplates.mockResolvedValue([])
  mocks.server.listDevices.mockResolvedValue([])
  mocks.local.listDevices.mockResolvedValue([])
  mocks.local.listInstalledPlugins.mockResolvedValue([])
  mocks.local.getPrintSettings.mockResolvedValue({ batchEnabled: false, batchSize: 10, batchIntervalMs: 500 })
  mocks.server.listClientPackages.mockResolvedValue([])
  mocks.server.listPluginPackages.mockResolvedValue([])
})

afterEach(() => {
  cleanup()
})

/** 各 client 页的内容锚点（页副标题——主导航按钮同名文本不复用，防误命中导航）。 */
const PAGE_MARKER = {
  workbench: '模板管理',
  designer: '尚未打开模板',
  data: '填写数据并打印 / Excel 批量打印',
  jobs: '最近 100 条打印记录；有作业进行中时自动刷新',
  settings: '服务端地址 / 本机连接与打印机',
  help: '五个页面的使用文章；界面内「?」就近说明',
} as const

describe('client 构建 · AC-01：hash 与当前页双向同步', () => {
  it('逐页切换：六个主导航点击后 hash 实时反映当前页', async () => {
    render(<App />)
    await screen.findByText(PAGE_MARKER.workbench)
    expect(window.location.hash).toBe('')

    const cases: [string, string][] = [
      ['设计器', '#/designer'],
      ['数据与打印', '#/data'],
      ['作业历史', '#/jobs'],
      ['设置', '#/settings'],
      ['帮助', '#/help'],
      ['工作台', '#/workbench'],
    ]
    for (const [nav, hash] of cases) {
      fireEvent.click(screen.getByRole('button', { name: nav }))
      await waitFor(() => expect(window.location.hash).toBe(hash))
    }
  })

  it('URL 直达：挂载前预设 #/settings / #/help → 直接落在对应页（刷新保持同机制——初始解析）', async () => {
    window.location.hash = '#/settings'
    render(<App />)
    expect(await screen.findByText(PAGE_MARKER.settings)).toBeTruthy()
    expect(screen.queryByText(PAGE_MARKER.workbench)).toBeNull()
    cleanup()

    window.location.hash = '#/help'
    render(<App />)
    expect(await screen.findByText(PAGE_MARKER.help)).toBeTruthy()
    expect(screen.queryByText(PAGE_MARKER.workbench)).toBeNull()
  })

  it('旧 #dc= 分享链接（client 构建 packages 越权）→ 回退 workbench 并规范化 URL（PR #313 修复轮回归）', async () => {
    window.location.hash = '#dc=windows'
    render(<App />)
    expect(await screen.findByText(PAGE_MARKER.workbench)).toBeTruthy()
    // 与 #/packages/windows 在 client 下回退 workbench 同口径——旧形态不得成为越权旁路
    await waitFor(() => expect(window.location.hash).toBe('#/workbench'))
  })

  it('hashchange 页面跟随：运行中改 hash 到 #/data → 经切页入口落到数据与打印页', async () => {
    render(<App />)
    await screen.findByText(PAGE_MARKER.workbench)

    window.location.hash = '#/data'
    window.dispatchEvent(new HashChangeEvent('hashchange'))
    await screen.findByText(PAGE_MARKER.data)
    expect(window.location.hash).toBe('#/data')
  })
})

describe('client 构建 · AC-02：历史栈导航与设计器离开守卫', () => {
  it('后退 / 前进在应用页面间导航：back 回上一页、forward 回下一页', async () => {
    render(<App />)
    await screen.findByText(PAGE_MARKER.workbench)

    fireEvent.click(screen.getByRole('button', { name: '数据与打印' }))
    await screen.findByText(PAGE_MARKER.data)
    fireEvent.click(screen.getByRole('button', { name: '作业历史' }))
    await screen.findByText(PAGE_MARKER.jobs)

    window.history.back()
    await waitFor(() => expect(window.location.hash).toBe('#/data'))
    expect(await screen.findByText(PAGE_MARKER.data)).toBeTruthy()

    window.history.forward()
    await waitFor(() => expect(window.location.hash).toBe('#/jobs'))
    expect(await screen.findByText(PAGE_MARKER.jobs)).toBeTruthy()
  })

  /** 进入 dirty 设计器（空态新建 → 改纸张宽提交历史更改，Designer.test.tsx 同款手法）。 */
  async function openDirtyDesigner() {
    fireEvent.click(screen.getByRole('button', { name: '设计器' }))
    expect(await screen.findByText(PAGE_MARKER.designer)).toBeTruthy()
    fireEvent.click(screen.getByRole('button', { name: '新建模板' }))
    await screen.findByDisplayValue('100')
    fireEvent.change(screen.getByDisplayValue('100'), { target: { value: '80' } })
    expect(screen.getByDisplayValue('80')).toBeTruthy()
  }

  it('dirty 设计器按后退：守卫挂起 → pre-revert 回 #/designer，页面与栈均不动；「继续编辑」停留', async () => {
    render(<App />)
    await screen.findByText(PAGE_MARKER.workbench)
    await openDirtyDesigner()
    await waitFor(() => expect(window.location.hash).toBe('#/designer'))
    const len0 = window.history.length

    // back → 落 workbench 条目 → 守卫三选挂起 → 路由按戳回退（hash / 栈 / 页面均不动）
    window.history.back()
    expect(await screen.findByText('未保存的更改')).toBeTruthy()
    await waitFor(() => expect(window.location.hash).toBe('#/designer'))
    expect(window.history.length).toBe(len0)
    // 编辑未丢（设计器仍挂载）
    expect(screen.getByDisplayValue('80')).toBeTruthy()

    fireEvent.click(screen.getByRole('button', { name: '继续编辑' }))
    await waitFor(() => expect(screen.queryByText('未保存的更改')).toBeNull())
    expect(window.location.hash).toBe('#/designer')
    expect(window.history.length).toBe(len0)
  })

  it('dirty 设计器点导航「放弃更改」：守卫确认后前进，恰新增一条历史（#/workbench）', async () => {
    render(<App />)
    await screen.findByText(PAGE_MARKER.workbench)
    await openDirtyDesigner()
    await waitFor(() => expect(window.location.hash).toBe('#/designer'))
    const len0 = window.history.length

    // 导航点「工作台」：switchTab 返回 false——hash 与栈在守卫期间不动
    fireEvent.click(screen.getByRole('button', { name: '工作台' }))
    expect(await screen.findByText('未保存的更改')).toBeTruthy()
    expect(window.location.hash).toBe('#/designer')
    expect(window.history.length).toBe(len0)

    fireEvent.click(screen.getByRole('button', { name: '放弃更改' }))
    await waitFor(() => expect(window.location.hash).toBe('#/workbench'))
    expect(await screen.findByText(PAGE_MARKER.workbench)).toBeTruthy()
    // 三选确认后才恰新增一条（决策 #177：守卫放行 → setTab → pushState）
    expect(window.history.length).toBe(len0 + 1)
  })

  it('dirty 设计器按后退（back 截断序列回归）：back 放行后再切设计器截断前向历史，守卫取消仍回 #/designer（PR #313 修复轮回归）', async () => {
    // 评审阻断项 2 高频序列：wb→data→jobs→back 回 data→切设计器（pushState 截断 jobs）→ dirty 守卫 back
    render(<App />)
    await screen.findByText(PAGE_MARKER.workbench)

    fireEvent.click(screen.getByRole('button', { name: '数据与打印' }))
    await screen.findByText(PAGE_MARKER.data)
    fireEvent.click(screen.getByRole('button', { name: '作业历史' }))
    await screen.findByText(PAGE_MARKER.jobs)
    // back 放行回 data（经 hashchange 切页入口，不重复入栈）
    window.history.back()
    await waitFor(() => expect(window.location.hash).toBe('#/data'))
    expect(await screen.findByText(PAGE_MARKER.data)).toBeTruthy()

    // 在 data 上切设计器并弄脏（pushState 截断 jobs——此后相邻条目戳差必须仍为 1）
    await openDirtyDesigner()
    await waitFor(() => expect(window.location.hash).toBe('#/designer'))
    const len0 = window.history.length

    // back → 落 data 条目 → 守卫挂起 → pre-revert 按戳差精确回 designer：hash / 栈 / 页面均不动
    window.history.back()
    expect(await screen.findByText('未保存的更改')).toBeTruthy()
    await waitFor(() => expect(window.location.hash).toBe('#/designer'))
    expect(window.history.length).toBe(len0)
    expect(screen.getByDisplayValue('80')).toBeTruthy()
  })

  it('无未保存更改（非 dirty）后退不拦截：直接放行回 workbench', async () => {
    render(<App />)
    await screen.findByText(PAGE_MARKER.workbench)
    // 空态设计器（未开模板——无 dirty，守卫不注册）
    fireEvent.click(screen.getByRole('button', { name: '设计器' }))
    await screen.findByText(PAGE_MARKER.designer)
    await waitFor(() => expect(window.location.hash).toBe('#/designer'))

    window.history.back()
    await waitFor(() => expect(window.location.hash).toBe(''))
    expect(await screen.findByText(PAGE_MARKER.workbench)).toBeTruthy()
  })
})
