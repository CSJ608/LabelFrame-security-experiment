// @vitest-environment jsdom
// hash 路由地基 server 分支
// server 页同形态路由（#/devices / #/packages / #/plugin-packages 等）；受限 tab 集不可经 hash 越权
// 进入（#/settings 解析即回退 workbench 并规范化 URL，页面不挂载）；#/packages/<sub> 直达下载中心
// 页内 tab（含旧 #dc= 分享链接兼容映射）。vi.mock uiMode 注入 server 分支（Guide.server.test.tsx 双跑先例）。
// 越权形态带 sub 段（#/settings/<sub>）同样回退——深链 sub 段不构成旁路。
// 帮助在 SERVER_TABS：#/help（含带 sub 段形态）直达（守门断言按「更新而非删除」口径）；#/settings 维持回退；
// 另覆盖 server 帮助内容适配（七卡 / 无设置卡 / server 副标题 / server 文章）与 #/designer/<anchor>
// server 侧自动开草稿（与 client 对称）用例。

import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { cleanup, configure, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
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
    getServerInfo: vi.fn(),
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

// 画布桩：深链自动开草稿用例真实挂载 Designer（jsdom 无 2d context / ResizeObserver，konva 画布以桩
// 替换——App.route.test.tsx / Designer.test.tsx 同法）
vi.mock('./pages/designer/CanvasViewport', () => ({
  CanvasViewport: () => null,
}))

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
  mocks.server.getServerInfo.mockResolvedValue({ listenUrl: 'http://localhost:53961', uiEnabled: true, version: '0.0.0', publicBaseUrl: null })
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
  it('八页逐 tab 点击：hash 与 client 构建同形态（#/<page>），含末位「帮助」', async () => {
    render(<App />)
    await screen.findByText(PAGE_MARKER.workbench)

    const cases: [string, string, string][] = [
      ['设计器', '#/designer', PAGE_MARKER.designer],
      ['数据与打印', '#/data', PAGE_MARKER.data],
      ['在线设备', '#/devices', PAGE_MARKER.devices],
      ['作业历史', '#/jobs', PAGE_MARKER.jobs],
      ['下载中心', '#/packages', '客户端'],
      ['插件管理', '#/plugin-packages', PAGE_MARKER.pluginPackages],
      // 「帮助」页纳入 server 主导航（末位，与 client 对齐）
      ['帮助', '#/help', '七个页面的使用文章；界面内「?」就近说明'],
      ['工作台', '#/workbench', PAGE_MARKER.workbench],
    ]
    for (const [nav, hash, marker] of cases) {
      // 帮助页目录树页节点与主导航按钮同名（如「工作台」）——限定主导航区域防误命中多个
      const navBar = document.querySelector('nav.nav') as HTMLElement
      fireEvent.click(within(navBar).getByRole('button', { name: nav }))
      await waitFor(() => expect(window.location.hash).toBe(hash))
      expect(await screen.findByText(marker)).toBeTruthy()
    }
  })

  it('#/help 直达帮助页且挂载（迭代 129 反转：帮助纳入 server tab 集）；#/settings 维持越权回退 workbench 并规范化 URL', async () => {
    window.location.hash = '#/help'
    render(<App />)
    // 帮助页挂载：server 副标题在场（server 页集口径，非 client「五个页面」计数串）
    expect(await screen.findByText('七个页面的使用文章；界面内「?」就近说明')).toBeTruthy()
    await waitFor(() => expect(window.location.hash).toBe('#/help'))
    // client 专属副标题与设置页不渲染
    expect(screen.queryByText('五个页面的使用文章；界面内「?」就近说明')).toBeNull()
    expect(screen.queryByText('服务端地址 / 本机连接与打印机')).toBeNull()
    cleanup()

    window.location.hash = '#/settings'
    render(<App />)
    expect(await screen.findByText(PAGE_MARKER.workbench)).toBeTruthy()
    // 回退同时 URL 规范化为 #/workbench（与所见一致、可安全分享）
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

  it('#/help/<sub> 带 sub 段同样直达帮助页（sub 段驻留不回退）；#/settings/<sub> 维持越权回退并规范化', async () => {
    window.location.hash = '#/help/designer.fill'
    render(<App />)
    expect(await screen.findByText('七个页面的使用文章；界面内「?」就近说明')).toBeTruthy()
    // 帮助页不消费锚点 sub 段，但驻留 URL（可分享形态）不构成回退
    await waitFor(() => expect(window.location.hash).toBe('#/help/designer.fill'))
    cleanup()

    window.location.hash = '#/settings/settings.language'
    render(<App />)
    expect(await screen.findByText(PAGE_MARKER.workbench)).toBeTruthy()
    expect(screen.queryByText('服务端地址 / 本机连接与打印机')).toBeNull()
    await waitFor(() => expect(window.location.hash).toBe('#/workbench'))
  })
})

describe('server 构建 · 帮助内容适配（迭代 129 · #320，AC-02）', () => {
  it('索引卡恰为七张（四共用页＋server 独有三页）；「设置」卡与 client 副标题不出现', async () => {
    window.location.hash = '#/help'
    render(<App />)
    expect(await screen.findByText('七个页面的使用文章；界面内「?」就近说明')).toBeTruthy()
    // 七页索引卡（索引卡 desc 与目录树页节点同词条并存——按 desc 断言恰一张索引卡）
    for (const desc of [
      '新建、导入模板并按分组管理',
      '在画布上设计标签版式',
      '填写数据并打印，支持 Excel 批量',
      '查看打印记录与逐张明细',
      '查看在线设备，点击设为默认目标',
      '扫码下载安装包，管理各平台版本',
      '统一分发插件包，供客户端安装',
    ]) {
      expect(screen.getByText(desc)).toBeTruthy()
    }
    // client 专属：设置卡与 client 计数副标题不出现
    expect(screen.queryByText('语言、服务端地址、打印机与插件等本机配置')).toBeNull()
    expect(screen.queryByText('五个页面的使用文章；界面内「?」就近说明')).toBeNull()
  })

  it('打开「在线设备」文章：intro＋两个纯文章小节渲染、无「去界面查看」深链', async () => {
    window.location.hash = '#/help'
    render(<App />)
    expect(await screen.findByText('七个页面的使用文章；界面内「?」就近说明')).toBeTruthy()
    fireEvent.click(screen.getByText('查看在线设备，点击设为默认目标'))
    expect(screen.getByText(/在线设备展示已加入服务端的设备/)).toBeTruthy()
    const article = document.querySelector('.help-article') as HTMLElement
    expect(article).toBeTruthy()
    for (const title of ['列表字段与在线状态', '设为默认目标']) {
      expect(within(article).getByText(title)).toBeTruthy()
    }
    expect(screen.queryByRole('button', { name: /去界面查看/ })).toBeNull()
  })
})

describe('server 构建 · 设计器深链自动开草稿（迭代 129 · #320，决策 #179 撤门，AC-03）', () => {
  it('手造 #/designer/designer.fill：自动开示例草稿（draftOpened 提示）且 data-help 锚点入 DOM', async () => {
    window.location.hash = '#/designer/designer.fill'
    render(<App />)
    // 空态被自动草稿取代：填充组锚点（Text 示例元素选中）入 DOM——设计器文档点在 server 渲染
    await waitFor(() => expect(document.querySelector('[data-help="designer.fill"]')).toBeTruthy(), { timeout: 5000 })
    // draftOpened 终态提示驻留状态栏（与 client 同语义， 静默空态口径反转）
    expect(await screen.findByText(/帮助打开的是临时示例草稿/)).toBeTruthy()
    // hash 带 sub 段驻留（可分享 / 刷新还原）
    expect(window.location.hash).toBe('#/designer/designer.fill')
  })
})
