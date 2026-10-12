// @vitest-environment jsdom
// server 构建分支（VITE_UI_MODE=server）帮助体系守门（· 起；· 
// 口径反转——断言按新口径「更新而非删除」，循 Guide.server.test.tsx 先例）：
// 设计器挂点（PropsPanel）撤 !isServerUi 门——server 分支照常渲染「?」与 data-help 锚点（设计器
// tab 在 SERVER_TABS 可达且功能完整，词条同源已进双产物）；Settings 挂点维持门（client-only 页
// 防御门，server 构建本不可达该页）；App 级「帮助」导航存在（SERVER_TABS 末位纳入）。
// 拆分独立文件与 Guide.server.test.tsx 同款双跑机制（vi.mock uiMode 注入分支，uiMode.ts:3 注释明示）。

import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { cleanup, configure, render, screen } from '@testing-library/react'
import App from '../App'
import { Settings } from '../pages/Settings'
import { PropsPanel } from '../pages/designer/PropsPanel'
import { defaultElement } from '../lib/design/types'
import type { TextElement } from '../lib/design/types'

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
  mocks.local.getHostConfig.mockResolvedValue({ serverUrl: 'http://127.0.0.1:53961', deviceId: 'PC-1', deviceName: 'PC-1' })
  mocks.local.getTransport.mockResolvedValue({ mode: 'Log', params: {} })
  mocks.local.getPrinterStatus.mockResolvedValue({ isOnline: true, isPaperOut: false, isPaused: false, message: '' })
  mocks.local.listInstalledPlugins.mockResolvedValue([])
  mocks.local.getPrintSettings.mockResolvedValue({ batchEnabled: false, batchSize: 10, batchIntervalMs: 500 })
})

afterEach(() => {
  cleanup()
})

describe('server 构建：帮助体系守门（迭代 129 · #320 口径反转后）', () => {
  it('App：导航有「帮助」入口（SERVER_TABS 末位纳入，迭代 129 反转）', async () => {
    render(<App />)
    expect(await screen.findByText(`${window.location.origin} · 服务端管理界面`)).toBeTruthy()
    expect(screen.getByRole('button', { name: '帮助' })).toBeTruthy()
  })

  it('PropsPanel：server 分支渲染「?」且 data-help 锚点在场（迭代 129 撤门反转；单选 Text 出 position/fill/box/text 四组，多选双 Rect 出 align）', () => {
    const textEl = { ...defaultElement('Text', 't1') } as TextElement
    const { unmount } = render(
      <PropsPanel elements={[textEl]} selected={['t1']} viewMode="fit" onChange={() => {}} onAlign={() => {}} onDelete={() => {}} />,
    )
    expect(screen.getByText('文本')).toBeTruthy()
    // 单选 Text：position / fill / box / text 四组锚点（ContentGroup 的 fill 不再经 serverHidden 门）
    for (const anchor of ['designer.position', 'designer.fill', 'designer.box', 'designer.text']) {
      expect(document.querySelector(`[data-help="${anchor}"]`)).toBeTruthy()
    }
    expect(screen.getAllByRole('button', { name: '查看说明' }).length).toBeGreaterThanOrEqual(4)
    unmount()

    // 多选双 Rect：对齐组锚点（align 组仅多选分支渲染）
    const r1 = { ...defaultElement('Rect', 'r1') }
    const r2 = { ...defaultElement('Rect', 'r2') }
    render(
      <PropsPanel elements={[r1, r2]} selected={['r1', 'r2']} viewMode="fit" onChange={() => {}} onAlign={() => {}} onDelete={() => {}} />,
    )
    expect(document.querySelector('[data-help="designer.align"]')).toBeTruthy()
  })

  it('Settings：server 分支无「?」、无 data-help 锚点（client-only 页防御门保留——settings tab 不在 SERVER_TABS）', async () => {
    const { AppProvider } = await import('../state/AppContext')
    render(
      <AppProvider>
        <Settings />
      </AppProvider>,
    )
    await screen.findByText('语言')
    expect(document.querySelector('[data-help]')).toBeNull()
    expect(screen.queryByRole('button', { name: '查看说明' })).toBeNull()
  })
})
