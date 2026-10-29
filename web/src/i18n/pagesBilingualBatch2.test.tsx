// @vitest-environment jsdom
// 双语冒烟（第二批迁移页面）：设计器 / 属性面板 / 设置 / 下载中心中英两语言渲染——
// zh-CN（vitest.setup 钉住首启语言）断言中文文案；changeLocale('en') 切换后断言英文文案且页面文本不再含 CJK
// （业务数据一律 ASCII，避免数据噪声）；再切回中文恢复（en 行为用 changeLocale 显式切换）。
// 另覆盖：UI 层数据性默认值随 locale（defaultElement 文本「文本」/「Text」、
// 新建模板分组「默认」/「Default」）——模板存储值不受语言影响的语义由 zh 态与现状等价保证。

import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { cleanup, configure, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import type { ClientPackageInfo, InstalledPluginInfo, PdaPackageInfo, PluginPackageInfo, PrintSettings, PrinterStatus, TemplatePackage } from '../lib/api/types'
import { AppProvider } from '../state/AppContext'
import { Designer } from '../pages/Designer'
import { PropsPanel } from '../pages/designer/PropsPanel'
import { Settings } from '../pages/Settings'
import { DownloadCenter } from '../pages/DownloadCenter'
import { defaultElement } from '../lib/design/types'
import i18next, { changeLocale } from './index'

configure({ asyncUtilTimeout: 8000 })

const mocks = vi.hoisted(() => ({
  server: {
    healthz: vi.fn(),
    listTemplates: vi.fn(),
    getTemplate: vi.fn(),
    saveTemplate: vi.fn(),
    listClientPackages: vi.fn(),
    listPluginPackages: vi.fn(),
    listPdaPackages: vi.fn(),
    listServerIpv4Candidates: vi.fn(),
    getServerInfo: vi.fn(),
    uploadClientPackage: vi.fn(),
    uploadPdaPackage: vi.fn(),
    deleteClientPackage: vi.fn(),
    deletePdaPackage: vi.fn(),
  },
  local: {
    healthz: vi.fn(),
    getTemplate: vi.fn(),
    getHostConfig: vi.fn(),
    getTransport: vi.fn(),
    getPrinterStatus: vi.fn(),
    getPrintSettings: vi.fn(),
    setPrintSettings: vi.fn(),
    listInstalledPlugins: vi.fn(),
  },
}))

vi.mock('../lib/api/client', () => ({
  serverApi: mocks.server,
  localApi: mocks.local,
  setServerBaseUrl: vi.fn(),
  clientPackageDownloadUrl: (name: string) => `/api/client-packages/${name}`,
  pdaPackageDownloadUrl: (name: string) => `/api/pda-packages/${name}`,
}))
vi.mock('../lib/uiMode', () => ({ UI_MODE: 'client', isServerUi: false }))
// 画布桩：react-konva 重组件（jsdom 无 2d context），本文件只验文案不测画布交互（Designer.test 同口径）
vi.mock('../pages/designer/CanvasViewport', () => ({
  CanvasViewport: () => null,
}))

// 业务数据一律 ASCII（no-CJK 断言只针对界面文案）
const PKG: TemplatePackage = {
  name: 'Bin-Label',
  group: 'Default',
  contract: { name: 'contract-1', version: '1', fields: [] },
  layout: { name: 'layout-1', contractName: 'contract-1', contractVersion: '1', widthMm: 70, heightMm: 50, elements: [] },
  testData: {},
}
const CLIENT_PKG: ClientPackageInfo = { fileName: 'LabelFrameClient-1.2.0.msi', sizeBytes: 1048576, modifiedAt: '2026-09-01T10:00:00Z' }
const PDA_PKG: PdaPackageInfo = { fileName: 'LabelFramePda-1.2.0.apk', sizeBytes: 2097152, modifiedAt: '2026-09-01T11:00:00Z' }
const PLUGIN_PKG: PluginPackageInfo = { fileName: 'demo.lfpkg', pluginId: 'demo', name: 'Demo', version: '1.0', sizeBytes: 1024, modifiedAt: '2026-09-01T12:00:00Z', valid: true }
const INSTALLED: InstalledPluginInfo[] = []
const PRINTER: PrinterStatus = { isOnline: true, isPaperOut: false, isPaused: false, message: '' }
const PRINT_SETTINGS: PrintSettings = { batchEnabled: false, batchSize: 10, batchIntervalMs: 500, dpi: 203 }

const CJK = /[\u3400-\u4DBF\u4E00-\u9FFF\uF900-\uFAFF]/

beforeEach(() => {
  vi.clearAllMocks()
  window.localStorage.clear()
  window.sessionStorage.clear()
  mocks.server.healthz.mockResolvedValue({ service: 'LabelFrame.Server', status: 'ok' })
  mocks.local.healthz.mockResolvedValue({ service: 'LabelFrame.WinHost', status: 'ok' })
  mocks.local.getHostConfig.mockResolvedValue({ serverUrl: 'http://127.0.0.1:53961', deviceId: 'device-1', deviceName: 'PC-1' })
  mocks.local.getTransport.mockResolvedValue({ mode: 'Log', params: {} })
  mocks.local.getPrinterStatus.mockResolvedValue(PRINTER)
  mocks.local.getPrintSettings.mockResolvedValue(PRINT_SETTINGS)
  mocks.local.listInstalledPlugins.mockResolvedValue(INSTALLED)
  mocks.server.listTemplates.mockResolvedValue([PKG])
  mocks.server.getTemplate.mockResolvedValue(PKG)
  mocks.server.listClientPackages.mockResolvedValue([CLIENT_PKG])
  mocks.server.listPluginPackages.mockResolvedValue([PLUGIN_PKG])
  mocks.server.listPdaPackages.mockResolvedValue([PDA_PKG])
  mocks.server.listServerIpv4Candidates.mockResolvedValue({ candidates: ['192.168.1.30'] })
  mocks.server.getServerInfo.mockResolvedValue({ listenUrl: 'http://localhost:53961', uiEnabled: true, version: '0.0.0', publicBaseUrl: null })
})

afterEach(() => {
  cleanup()
  window.localStorage.clear()
  if (i18next.language !== 'zh-CN') changeLocale('zh-CN')
})

describe('第二批迁移页面双语渲染冒烟（迭代 112 · #246）', () => {
  it('设计器（新建模板）：中文态工具栏 / 左栏 / 右栏 → 切 en 全英文且无 CJK；分组默认值随语言（待决议-1）', async () => {
    const { container } = render(
      <AppProvider>
        <Designer request={{ kind: 'new' }} onClose={() => {}} />
      </AppProvider>,
    )
    // zh 态：工具栏与左栏（画布已桩掉）
    expect(await screen.findByDisplayValue('默认')).toBeTruthy() // 数据性默认值随语言
    expect(screen.getByRole('button', { name: /保存并退出/ })).toBeTruthy()
    // 导出 / 导入设计收纳于 ⋯ 菜单（menuitem 角色）
    fireEvent.click(screen.getByRole('button', { name: '更多操作' }))
    expect(screen.getByRole('menuitem', { name: /导出设计/ })).toBeTruthy()
    expect(screen.getByRole('menuitem', { name: /导入设计/ })).toBeTruthy()
    expect(screen.getByText('控件栏')).toBeTruthy()
    expect(screen.getByText('打印字段')).toBeTruthy()
    expect(screen.getByText('图层')).toBeTruthy()
    expect(screen.getByRole('button', { name: '属性' })).toBeTruthy()
    expect(screen.getByRole('button', { name: '测试默认值' })).toBeTruthy()

    changeLocale('en')
    await waitFor(() => expect(screen.getByRole('button', { name: /Save & Exit/ })).toBeTruthy())
    expect(screen.getByRole('menuitem', { name: /Export Design/ })).toBeTruthy()
    expect(screen.getByText('Widgets')).toBeTruthy()
    expect(screen.getByText('Print Fields')).toBeTruthy()
    expect(screen.getByText('Layers')).toBeTruthy()
    expect(screen.getByRole('button', { name: 'Properties' })).toBeTruthy()
    expect(screen.getByRole('button', { name: 'Test Defaults' })).toBeTruthy()
    expect(CJK.test(container.textContent ?? '')).toBe(false)

    changeLocale('zh-CN')
    await waitFor(() => expect(screen.getByRole('button', { name: /保存并退出/ })).toBeTruthy())
  })

  it('属性面板（文本元素）：中文态分组属性与字体显示名 → 切 en 全英文且无 CJK；字体内部值不变', async () => {
    const textEl = { ...defaultElement('Text', 't1'), text: 'A-01' }
    const onChange = vi.fn()
    const { container } = render(<PropsPanel elements={[textEl]} selected={['t1']} viewMode="fit" onChange={onChange} onAlign={() => {}} onDelete={() => {}} />)
    // zh 态：分组标题 + 字体显示名「微软雅黑」（内部值 Microsoft YaHei 不随语言变化）
    expect(screen.getByText('文本')).toBeTruthy()
    expect((screen.getByLabelText('字体') as HTMLSelectElement).value).toBe('Microsoft YaHei')
    expect((screen.getByLabelText('字体') as HTMLSelectElement).textContent).toContain('微软雅黑')
    expect(screen.getByLabelText('字高')).toBeTruthy()
    expect(screen.getByLabelText('水平对齐') as HTMLSelectElement).toHaveProperty('value', 'Left')

    changeLocale('en')
    await waitFor(() => expect(screen.getByText('Text')).toBeTruthy())
    const fontEn = screen.getByLabelText('Font') as HTMLSelectElement
    expect(fontEn.value).toBe('Microsoft YaHei') // 内部名不变
    expect(fontEn.textContent).toContain('Microsoft YaHei') // en 显示名 = 内部名
    expect(screen.getByLabelText('Font height')).toBeTruthy()
    expect(CJK.test(container.textContent ?? '')).toBe(false)

    changeLocale('zh-CN')
    await waitFor(() => expect(screen.getByText('文本')).toBeTruthy())
  })

  it('属性面板（空态 / 多选对齐）：中英两态', async () => {
    render(<PropsPanel elements={[]} selected={[]} viewMode="fit" onChange={() => {}} onAlign={() => {}} onDelete={() => {}} />)
    expect(screen.getByText('在画布上选中元素后显示属性。')).toBeTruthy()
    changeLocale('en')
    await waitFor(() => expect(screen.getByText('Select an element on the canvas to show its properties.')).toBeTruthy())
  })

  it('设置页：中文态四分节 → 切 en 全英文且无 CJK（含连接配置面板展开）', async () => {
    const { container } = render(
      <AppProvider>
        <Settings />
      </AppProvider>,
    )
    // zh 态：分节菜单列全部分节；默认分节 = 打印设置（连接配置 / 作业配置 / 测试打印三子节在场）
    await screen.findByRole('navigation', { name: '设置菜单' })
    for (const title of ['常规设置', '打印设置', '更新与安装包', '插件管理']) {
      expect(screen.getByRole('button', { name: title })).toBeTruthy()
    }
    expect(await screen.findByRole('heading', { name: /连接配置/ })).toBeTruthy()
    // 连接配置：模式单选项（modeLabel 随语言）
    expect(await screen.findByRole('radio', { name: /模拟打印/ })).toBeTruthy()
    expect(screen.getByRole('radio', { name: /网络打印机/ })).toBeTruthy()
    // 常规设置分节：服务端地址子标题与测试连接按钮
    fireEvent.click(screen.getByRole('button', { name: '常规设置' }))
    expect(await screen.findByRole('heading', { name: '服务端地址' })).toBeTruthy()
    expect(screen.getByRole('button', { name: /测试连接/ })).toBeTruthy()

    changeLocale('en')
    // 打印设置分节：菜单名与模式单选项随语言
    await waitFor(() => expect(screen.getByRole('button', { name: 'Printing' })).toBeTruthy())
    fireEvent.click(screen.getByRole('button', { name: 'Printing' }))
    await waitFor(() => expect(screen.getByRole('radio', { name: /Simulated printing/ })).toBeTruthy())
    expect(screen.getByRole('radio', { name: /Network printer/ })).toBeTruthy()
    // 常规设置分节：子标题与按钮英文
    fireEvent.click(screen.getByRole('button', { name: 'General' }))
    expect(screen.getByRole('heading', { name: 'Server Address' })).toBeTruthy()
    expect(screen.getByRole('button', { name: /Test Connection/ })).toBeTruthy()
    // 更新与安装包 / 插件管理分节：表格表头与 pluginId 单元格随语言
    fireEvent.click(screen.getByRole('button', { name: 'Updates & Packages' }))
    await waitFor(() => expect(screen.getByRole('columnheader', { name: 'File Name' })).toBeTruthy())
    fireEvent.click(screen.getByRole('button', { name: 'Plugin Management' }))
    await waitFor(() => expect(screen.getByRole('columnheader', { name: 'Status' })).toBeTruthy())
    expect(screen.getByText('demo')).toBeTruthy() // 可用插件 pluginId（单元格内）
    // no-CJK 逐分节扫描（en 态全分节覆盖）——常规设置只扫服务端地址子节：
    // 语言子节的「中文」选项名固定各自语言原文，不随当前语言翻译
    for (const name of ['General', 'Printing', 'Updates & Packages', 'Plugin Management']) {
      fireEvent.click(screen.getByRole('button', { name }))
      const mainEl = container.querySelector('.settings-main') as HTMLElement
      if (name === 'General') {
        const serverSub = within(mainEl).getByRole('heading', { name: 'Server Address' }).closest('.settings-subsection') as HTMLElement
        expect(CJK.test(serverSub.textContent ?? ''), `section=${name}`).toBe(false)
      } else {
        expect(CJK.test(mainEl.textContent ?? ''), `section=${name}`).toBe(false)
      }
    }

    changeLocale('zh-CN')
    await waitFor(() => expect(screen.getByRole('button', { name: '插件管理' })).toBeTruthy())
  })

  it('下载中心：中文态快速访问首屏 → 切 en 全英文且无 CJK（迭代 118 · #272 三 tab + 平台优先措辞）', async () => {
    const { container } = render(
      <AppProvider>
        <DownloadCenter />
      </AppProvider>,
    )
    // zh 态：默认快速访问——「最新上传」双卡（Windows / Android 平台优先措辞）+ 连接信息卡
    expect(await screen.findByText('LabelFrameClient-1.2.0.msi')).toBeTruthy()
    expect(screen.getByText('LabelFramePda-1.2.0.apk')).toBeTruthy()
    expect(screen.getByText('下载中心')).toBeTruthy()
    expect(screen.getByText('LabelFrame 客户端各平台安装包')).toBeTruthy()
    expect(screen.getAllByText('最新上传')).toHaveLength(2)
    expect(screen.getByText('服务端信息')).toBeTruthy()
    expect(screen.getByRole('button', { name: '下载' })).toBeTruthy()
    expect(screen.getByRole('button', { name: 'Windows 客户端更新' })).toBeTruthy()
    expect(screen.getByRole('button', { name: 'PDA 客户端更新' })).toBeTruthy()

    changeLocale('en')
    await waitFor(() => expect(screen.getByText('Download Center')).toBeTruthy())
    expect(screen.getByText('LabelFrame client installers for every platform')).toBeTruthy()
    expect(screen.getAllByText('Latest upload')).toHaveLength(2)
    expect(screen.getByText('Server Info')).toBeTruthy()
    expect(CJK.test(container.textContent ?? '')).toBe(false)

    changeLocale('zh-CN')
    await waitFor(() => expect(screen.getByText('下载中心')).toBeTruthy())
  })

  it('待决议-1 锚定：defaultElement 文本默认值随 locale（zh「文本」/ en「Text」），存量 Literal 不动', () => {
    // UI 层数据性默认值跟随当前界面语言
    expect(defaultElement('Text', 'a1').text).toBe('文本')
    changeLocale('en')
    expect(defaultElement('Text', 'b2').text).toBe('Text')
    // 存量 Literal 不动：语言切换不回写已存在元素的文本（存储值不受语言影响）
    const existing = defaultElement('Text', 'a1x')
    expect(existing.text).toBe('Text') // 构造期语言即 en
    changeLocale('zh-CN')
    expect(existing.text).toBe('Text') // 切回中文后不回译（存储值语义）
    expect(defaultElement('Text', 'c3').text).toBe('文本')
  })
})
