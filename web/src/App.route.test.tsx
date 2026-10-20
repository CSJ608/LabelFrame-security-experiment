// @vitest-environment jsdom
// hash 路由地基 client 分支——
// - 逐页切换 hash 实时反映；预设 hash 挂载直达（刷新保持同机制＝带 hash 重挂载初始解析）；hashchange 页面跟随。
// - 后退 / 前进在应用页面间导航；dirty 设计器离开守卫照常生效——back 触发守卫时页面与 hash 均不动
//   不新增栈条目（pre-revert 按戳回退）；「继续编辑」停留、「放弃更改」前进且恰新增一条。
// 帮助深链 sub 段用例——「去界面查看」hash 赋值导航、设计器空态自动开
// 示例草稿（元素入初始基线并选中）＋气泡弹出＋draftOpened 终态、刷新 / 直链还原（sub 驻留）、
// 已开模板不重开走既有锚点路径、超时通用降级、settings 深链弹卡片气泡。
// server 分支（受限 tab 集越权回退 / #/packages 直达）在 App.route.server.test.tsx。

import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { cleanup, configure, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import App from './App'
import { GUIDE_SEEN_KEY } from './lib/guide'
import type { TemplatePackage } from './lib/api/types'

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
  mocks.local.getPrintSettings.mockResolvedValue({ batchEnabled: false, batchSize: 10, batchIntervalMs: 500, dpi: 203 })
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
  settings: '常规 / 打印 / 更新与插件',
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
    // 帮助页目录树页节点与主导航同词条（单源双视图）——点击须圈定主导航区域防歧义命中
    const mainNav = screen.getByRole('navigation', { name: '主导航' })
    for (const [nav, hash] of cases) {
      fireEvent.click(within(mainNav).getByRole('button', { name: nav }))
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

  it('非 dirty 设计器按后退：守卫直接放行——不弹三选，页面与 hash 前进到上一页（#312 取证缺陷回归：switchTab 不得无条件按挂起处理）', async () => {
    render(<App />)
    await screen.findByText(PAGE_MARKER.workbench)
    // 进入设计器并新建草稿，但不做任何提交更改（undoCount = 0）
    fireEvent.click(screen.getByRole('button', { name: '设计器' }))
    expect(await screen.findByText(PAGE_MARKER.designer)).toBeTruthy()
    fireEvent.click(screen.getByRole('button', { name: '新建模板' }))
    await screen.findByDisplayValue('100')
    await waitFor(() => expect(window.location.hash).toBe('#/designer'))

    // back：无未保存更改应直接放行切回 workbench——页面立即切走，不弹三选；
    // 落点为挂载初始条目（hash 空——空 hash 不动是既定语义），tab→hash 同步不写 hash
    window.history.back()
    expect(await screen.findByText(PAGE_MARKER.workbench)).toBeTruthy()
    expect(screen.queryByText('未保存的更改')).toBeNull()
    expect([ '', '#/workbench' ]).toContain(window.location.hash)
  })

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
    // 三选确认后才恰新增一条（守卫放行 → setTab → pushState）
    expect(window.history.length).toBe(len0 + 1)
  })

  it('dirty 设计器按后退（back 截断序列回归）：back 放行后再切设计器截断前向历史，守卫取消仍回 #/designer（PR #313 修复轮回归）', async () => {
    // 高频序列：wb→data→jobs→back 回 data→切设计器（pushState 截断 jobs）→ dirty 守卫 back
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

/** 带 text 元素的模板（已开模板深链用例——图层可见可选，锚点路径可复现）。 */
const PKG_TEXT: TemplatePackage = {
  name: '库位标签',
  group: '默认',
  contract: { name: 'contract-1', version: '1', fields: [] },
  layout: {
    name: 'layout-1',
    contractName: 'contract-1',
    contractVersion: '1',
    widthMm: 70,
    heightMm: 50,
    elements: [{ type: 'text', literal: '深链样板', xMm: 5, yMm: 5, widthMm: 40, heightMm: 10 }],
  },
  testData: {},
}

/** 经帮助页「去界面查看」触发深链（hash 赋值 + 手动 hashchange——jsdom 原生派发时序不定，双派发幂等）。 */
async function deepLinkFromHelp(sectionTopicId: string) {
  fireEvent.click(screen.getByRole('button', { name: '帮助' }))
  await screen.findByText(PAGE_MARKER.help)
  fireEvent.click(screen.getByText('在画布上设计标签版式'))
  const section = document.querySelector(`[data-topic-id="${sectionTopicId}"]`) as HTMLElement
  fireEvent.click(within(section).getByRole('button', { name: /去界面查看/ }))
  window.dispatchEvent(new HashChangeEvent('hashchange'))
}

describe('client 构建 · 帮助深链 sub 段（#316 · 决策 #178）', () => {
  it('深链到达性（陪验反馈 1）：示例草稿上看另一小节——选中类型不含目标组时重建示例集，气泡直达', async () => {
    render(<App />)
    await screen.findByText(PAGE_MARKER.workbench)
    await deepLinkFromHelp('fill')
    // 第一次深链：Text 示例选中、填充组气泡弹出
    expect(await screen.findByRole('dialog')).toBeTruthy()
    expect(document.querySelector('[role=dialog]')?.getAttribute('aria-label')).toBe('填充：固定值或字段填充')

    // 二次深链条码参数（同页 sub 变化）：选中仍是 Text（条码组不渲染）→ 到达性保障按新锚点重建
    // 示例元素集（Barcode）并选中 → 锚点出现 → 气泡切到条码参数（openDot 单值——旧气泡自动关闭）
    window.location.hash = '#/designer/designer.barcode'
    window.dispatchEvent(new HashChangeEvent('hashchange'))
    await waitFor(() => expect(document.querySelector('[role=dialog]')?.getAttribute('aria-label')).toBe('条码参数'), { timeout: 5000 })
    // 重建后条码组字段在场（码制选择）
    expect(screen.getByText('码制')).toBeTruthy()
  })

  it('深链到达性（陪验反馈 1）：已开模板未选中——自动选中既有匹配型元素，气泡直达不超时降级', async () => {
    mocks.server.listTemplates.mockResolvedValue([{ name: '库位标签', group: '默认', updatedAt: '2026-10-01' }])
    mocks.server.getTemplate.mockResolvedValue(PKG_TEXT)
    render(<App />)
    await screen.findByText(PAGE_MARKER.workbench)
    await screen.findByText('库位标签')
    fireEvent.click(screen.getByRole('button', { name: '编辑' }))
    await screen.findByDisplayValue('库位标签')
    // 打开后无选中（属性面板空态）——旧口径此态深链只会超时降级；现在自动选中 text 元素（fill 组对其可见）
    window.location.hash = '#/designer/designer.fill'
    window.dispatchEvent(new HashChangeEvent('hashchange'))
    const dlg = await screen.findByRole('dialog')
    expect(dlg.getAttribute('aria-label')).toBe('填充：固定值或字段填充')
    expect(screen.queryByText(/若未看到/)).toBeNull()
  })

  it('原型链属性名 sub 段不崩溃不误触发：#/designer/toString 挂载 → 设计器空态、不自动开草稿（Object.hasOwn 回归，评审实证缺陷）', async () => {
    window.location.hash = '#/designer/toString'
    render(<App />)
    // 'toString'/'constructor' 等经 in 判定为真会取到原型链函数 → .map 抛 TypeError 渲染崩溃；
    // Object.hasOwn 后：非自有键不触发自动开草稿——初始解析 page=designer 落空态（不被示例草稿取代）
    expect(await screen.findByText('尚未打开模板')).toBeTruthy()
    expect(screen.queryByDisplayValue('100')).toBeNull()
  })

  it('「去界面查看」→ #/designer/designer.fill：自动开示例草稿＋示例元素选中＋气泡弹出＋draftOpened 终态；未编辑退出静默弃（AC-03）', async () => {
    render(<App />)
    await screen.findByText(PAGE_MARKER.workbench)
    await deepLinkFromHelp('fill')

    // hash 带 sub 段（可分享 / 刷新还原的权威载体）；空态被自动草稿取代
    expect(window.location.hash).toBe('#/designer/designer.fill')
    await screen.findByDisplayValue('100')
    expect(screen.queryByText(PAGE_MARKER.designer)).toBeNull()
    // 示例元素入基线并选中：属性面板头显示文本元素键名 location（fill 预设 = 演示同款示例值）
    expect(screen.getByText('location')).toBeTruthy()
    // 页内轮询锚点入 DOM → 自动弹填充组文档点气泡（无可见标题、正文直出）
    const dialog = await screen.findByRole('dialog')
    expect(dialog.getAttribute('aria-label')).toBe('填充：固定值或字段填充')
    // draftOpened 终态提示驻留状态栏（pending 一闪被覆盖属预期）
    expect(screen.getByText(/帮助打开的是临时示例草稿/)).toBeTruthy()

    // 未编辑（undoCount=0）退出：不弹三选、静默弃、回工作台（守卫语义与普通草稿一致）
    fireEvent.click(screen.getByTitle('返回工作台'))
    expect(await screen.findByText(PAGE_MARKER.workbench)).toBeTruthy()
    expect(screen.queryByText('未保存的更改')).toBeNull()
  })

  it('深链草稿编辑后退出：三选守卫照常（拍板 A——示例元素入基线不豁免 dirty 守卫）', async () => {
    render(<App />)
    await screen.findByText(PAGE_MARKER.workbench)
    await deepLinkFromHelp('fill')
    await screen.findByDisplayValue('100')

    // 人为提交一处编辑（改纸张宽）→ dirty
    fireEvent.change(screen.getByDisplayValue('100'), { target: { value: '80' } })
    fireEvent.click(screen.getByTitle('返回工作台'))
    expect(await screen.findByText('未保存的更改')).toBeTruthy()
    fireEvent.click(screen.getByRole('button', { name: '放弃更改' }))
    expect(await screen.findByText(PAGE_MARKER.workbench)).toBeTruthy()
  })

  it('刷新 / 直链还原（sub 段驻留，拍板 2-A）：挂载前预设 #/designer/designer.fill → 同一深链行为', async () => {
    window.location.hash = '#/designer/designer.fill'
    render(<App />)
    await screen.findByDisplayValue('100')
    expect(screen.queryByText(PAGE_MARKER.designer)).toBeNull()
    expect((await screen.findByRole('dialog')).getAttribute('aria-label')).toBe('填充：固定值或字段填充')
  })

  it('已打开模板深链：不重开草稿走既有锚点路径——选中元素后锚点入 DOM 自动弹气泡（AC-03 不回归面）', async () => {
    // serverMode 探测先走 server healthz（beforeEach 已 mock ok）→ biz = serverApi，两处都喂
    mocks.server.listTemplates.mockResolvedValue([{ name: '库位标签', group: '默认', updatedAt: '2026-10-01' }])
    mocks.server.getTemplate.mockResolvedValue(PKG_TEXT)
    render(<App />)
    await screen.findByText(PAGE_MARKER.workbench)
    await screen.findByText('库位标签')
    fireEvent.click(screen.getByRole('button', { name: '编辑' }))
    await screen.findByDisplayValue('库位标签')
    await screen.findByDisplayValue('70')

    // 同页 sub 变化也经 hashchange 通道落位（router 零改动）；designerReq 存在 → 不自动开草稿
    window.location.hash = '#/designer/designer.fill'
    window.dispatchEvent(new HashChangeEvent('hashchange'))
    // 图层列表选中样例元素 → 填充组渲染锚点 → 轮询命中弹气泡
    fireEvent.click(screen.getByText('深链样板'))
    const dialog = await screen.findByRole('dialog')
    expect(dialog.getAttribute('aria-label')).toBe('填充：固定值或字段填充')
    // 模板未被草稿顶替（名称 / 纸宽保持模板值）
    expect(screen.getByDisplayValue('库位标签')).toBeTruthy()
    expect(screen.getByDisplayValue('70')).toBeTruthy()
  })

  it('深链超时通用降级：锚点运行时不可见（已开空模板、无选中元素）→ 状态栏通用提示（AC-04 保留面）', async () => {
    mocks.server.listTemplates.mockResolvedValue([{ name: '库位标签', group: '默认', updatedAt: '2026-10-01' }])
    mocks.server.getTemplate.mockResolvedValue({ ...PKG_TEXT, layout: { ...PKG_TEXT.layout, elements: [] } })
    render(<App />)
    await screen.findByText(PAGE_MARKER.workbench)
    await screen.findByText('库位标签')
    fireEvent.click(screen.getByRole('button', { name: '编辑' }))
    await screen.findByDisplayValue('库位标签')

    window.location.hash = '#/designer/designer.fill'
    window.dispatchEvent(new HashChangeEvent('hashchange'))
    // 80ms/4s 轮询耗尽 → 通用降级词条（不指认单一原因）
    await waitFor(() => expect(screen.getByText(/已切换页面；未出现说明时/)).toBeTruthy(), { timeout: 6000 })
    expect(screen.queryByRole('dialog')).toBeNull()
  })

  it('settings 深链：#/settings/settings.language → 自动弹对应卡片气泡', async () => {
    window.location.hash = '#/settings/settings.language'
    render(<App />)
    await screen.findByText(PAGE_MARKER.settings)
    const dialog = await screen.findByRole('dialog')
    expect(dialog.getAttribute('aria-label')).toBe('语言')
  })
})
