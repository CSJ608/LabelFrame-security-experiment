// @vitest-environment jsdom
// F2-F4：设置页三分组——服务端地址（机器级配置，保存即生效）/ 连接方式（本机 Client）/ 打印机。
// mock 覆盖组件树用到的全部 client 方法（含 AppContext 启动链：getHostConfig / getTransport / healthz）。

import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { cleanup, configure, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { ApiError } from '../lib/api/types'
import type { InstalledPluginInfo, PluginPackageInfo } from '../lib/api/types'
import { AppProvider, useApp } from '../state/AppContext'
import { Settings } from './Settings'

// 挂载链（getHostConfig → getTransport / healthz → 各分组列表）为多段 promise +
// React 真实宏任务调度，CI 高负载 runner 上偶发超过 findBy / waitFor 默认 1000ms（实证：TCP 参数表单
// findByLabelText 1s 超时）。本文件统一把异步查询超时放宽到 8000ms（与 JobHistory / DataPrint 的
// MOUNT_WAIT 同口径），消除对默认 1s 的隐式时序依赖；单测总超时由 vitest.config testTimeout（20s）兜住。
configure({ asyncUtilTimeout: 8000 })

const mocks = vi.hoisted(() => ({
  server: {
    healthz: vi.fn(),
    listClientPackages: vi.fn(),
    listPluginPackages: vi.fn(),
    downloadPluginPackage: vi.fn(),
  },
  local: {
    getHostConfig: vi.fn(),
    getTransport: vi.fn(),
    setTransport: vi.fn(),
    testTransport: vi.fn(),
    getPrinterStatus: vi.fn(),
    testPrinter: vi.fn(),
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
  clientPackageDownloadUrl: (fileName: string) => `http://127.0.0.1:53961/api/client-packages/${encodeURIComponent(fileName)}`,
  pluginPackageDownloadUrl: (fileName: string) => `http://127.0.0.1:53961/api/plugin-packages/${encodeURIComponent(fileName)}`,
}))
// 本文件为 client 构建语义用例，显式注入 client 分支（VITE_UI_MODE=server 整仓测试时保持稳定）
vi.mock('../lib/uiMode', () => ({ UI_MODE: 'client', isServerUi: false }))

function renderSettings() {
  return render(
    <AppProvider>
      <Settings />
    </AppProvider>,
  )
}

/** 状态栏消息探针（深链等待 / 超时提示经 app.setStatus 呈现，测试树内以探针捕获——Designer.test 同法）。 */
function StatusProbe() {
  const app = useApp()
  return <div data-testid="status-msg">{app.statusMsg}</div>
}

/** 按分组限定查询（「测试连接」按钮在服务端地址与连接方式两组各有一个；「服务端地址」文本在 panel-head 与 label 各一处）。 */
function withinSection(title: string): HTMLElement {
  const el = title === '服务端地址' ? screen.getByLabelText('服务端地址') : screen.getByText(title)
  return el.closest('section') as HTMLElement
}

/* * （决议 1）：连接方式面板默认折叠为当前连接摘要一行——点击面板头展开编辑区。 */
function openTransportPanel() {
  fireEvent.click(screen.getByText('连接方式'))
}

const HOST_CONFIG = { serverUrl: 'http://127.0.0.1:53961', deviceId: 'PC-1', deviceName: 'PC-1' }

beforeEach(() => {
  vi.clearAllMocks()
  window.localStorage.clear()
  window.sessionStorage.clear()
  mocks.server.healthz.mockResolvedValue({ service: 'LabelFrame.Server', status: 'ok' })
  mocks.server.listClientPackages.mockResolvedValue([])
  mocks.server.listPluginPackages.mockResolvedValue([])
  mocks.server.downloadPluginPackage.mockResolvedValue({ blob: new Blob(['pkg']), filename: 'sample-1.0.0.lfplugin' })
  mocks.local.listInstalledPlugins.mockResolvedValue([])
  mocks.local.installPlugin.mockResolvedValue({
    ok: true,
    message: '插件「示例插件 1.0.0」已安装，重启客户端后生效。',
    plugin: { pluginId: 'sample', name: '示例插件', version: '1.0.0', loaded: false, source: 'package' },
  })
  mocks.local.uninstallPlugin.mockResolvedValue({ ok: true, message: '插件「sample」已卸载，重启客户端后生效。' })
  mocks.local.getHostConfig.mockResolvedValue(HOST_CONFIG)
  mocks.local.getTransport.mockResolvedValue({ mode: 'Log', params: {} })
  mocks.local.setTransport.mockResolvedValue({ ok: true, message: '已切换到 TCP。', config: { mode: 'Tcp', params: { tcpHost: '192.168.1.50', tcpPort: 9100 } } })
  mocks.local.testTransport.mockResolvedValue({ ok: true, message: '测试通过。', config: { mode: 'Tcp', params: { tcpHost: '192.168.1.50', tcpPort: 9100 } } })
  mocks.local.getPrinterStatus.mockResolvedValue({ isOnline: true, isPaperOut: false, isPaused: false, message: '就绪' })
  mocks.local.testPrinter.mockResolvedValue({ sent: true, bytes: 128 })
  mocks.local.setHostConfig.mockResolvedValue(undefined)
  mocks.local.getPrintSettings.mockResolvedValue({ batchEnabled: false, batchSize: 10, batchIntervalMs: 500 })
  mocks.local.setPrintSettings.mockResolvedValue(undefined)
  mocks.probeHealthz.mockResolvedValue(true)
})

afterEach(() => {
  cleanup()
})

describe('设置页三分组（迭代 18）', () => {
  it('渲染服务端地址 / 连接方式 / 打印机三个分组', async () => {
    renderSettings()
    expect(screen.getByLabelText('服务端地址')).toBeTruthy()
    expect(screen.getByText('连接方式')).toBeTruthy()
    expect(screen.getByText('打印机')).toBeTruthy()
    expect(screen.getByRole('button', { name: /测试打印/ })).toBeTruthy()
  })

  it('连接方式默认折叠为当前连接摘要一行，点击面板头展开编辑区（迭代 73 决议 1，AC-02）', async () => {
    renderSettings()
    // 默认折叠：面板头显示当前摘要（getTransport 异步加载后 Log → 模拟打印），编辑区不渲染
    expect(await screen.findByText('模拟打印')).toBeTruthy()
    expect(screen.queryByRole('radio', { name: /模拟打印/ })).toBeNull()
    expect(screen.queryByRole('button', { name: /保存并应用/ })).toBeNull()
    // 点击面板头（1 步）展开：模式单选与操作按钮出现
    openTransportPanel()
    expect(await screen.findByRole('radio', { name: /模拟打印/ })).toBeTruthy()
    expect(screen.getByRole('radio', { name: /网络打印机/ })).toBeTruthy()
    expect(screen.getByRole('radio', { name: /Windows 驱动/ })).toBeTruthy()
    expect(screen.getByRole('radio', { name: /Zebra/ })).toBeTruthy()
    expect(screen.getByRole('button', { name: /保存并应用/ })).toBeTruthy()
    // 再点击收起
    openTransportPanel()
    expect(screen.queryByRole('radio', { name: /模拟打印/ })).toBeNull()
  })

  it('启动加载机器级配置：服务端地址显示 hostConfig.serverUrl', async () => {
    renderSettings()
    await screen.findByDisplayValue('http://127.0.0.1:53961')
    expect(mocks.local.getHostConfig).toHaveBeenCalled()
  })
})

describe('服务端地址（F2）', () => {
  it('测试连接：探测输入框地址（probeHealthz），不保存（setHostConfig 不调用）', async () => {
    renderSettings()
    fireEvent.change(screen.getByLabelText('服务端地址'), { target: { value: 'http://192.168.1.10:53961' } })
    fireEvent.click(within(withinSection('服务端地址')).getByRole('button', { name: /^测试连接/ }))
    expect(await screen.findByText(/连接成功：该地址可访问服务端/)).toBeTruthy()
    expect(mocks.probeHealthz).toHaveBeenCalledWith('http://192.168.1.10:53961')
    expect(mocks.local.setHostConfig).not.toHaveBeenCalled()
  })

  it('测试连接失败：提示失败原因', async () => {
    mocks.probeHealthz.mockResolvedValue(false)
    renderSettings()
    fireEvent.click(within(withinSection('服务端地址')).getByRole('button', { name: /^测试连接/ }))
    expect(await screen.findByText(/连接失败：请确认服务端已启动/)).toBeTruthy()
  })

  it('保存并生效：setHostConfig({ serverUrl }) + localStorage 兜底 + 提示立即生效', async () => {
    renderSettings()
    fireEvent.change(screen.getByLabelText('服务端地址'), { target: { value: 'http://192.168.1.10:53961' } })
    fireEvent.click(screen.getByRole('button', { name: /保存并生效/ }))
    expect(await screen.findByText('已保存到本机配置并立即生效。')).toBeTruthy()
    expect(mocks.local.setHostConfig).toHaveBeenCalledWith({ serverUrl: 'http://192.168.1.10:53961' })
    expect(window.localStorage.getItem('labelframe.baseUrl')).toBe('http://192.168.1.10:53961')
  })

  it('旧客户端（setHostConfig 失败）：回退浏览器本地保存并提示', async () => {
    mocks.local.setHostConfig.mockRejectedValue(new Error('no api'))
    renderSettings()
    fireEvent.change(screen.getByLabelText('服务端地址'), { target: { value: 'http://192.168.1.10:53961' } })
    fireEvent.click(screen.getByRole('button', { name: /保存并生效/ }))
    expect(await screen.findByText(/当前客户端版本较旧：地址已保存在本浏览器中/)).toBeTruthy()
    expect(window.localStorage.getItem('labelframe.baseUrl')).toBe('http://192.168.1.10:53961')
  })
})

describe('服务端连接徽标三态与切换（迭代 86，#142 a 案）', () => {
  it('AC-01 空地址：徽标「服务端未连接（单机模式可用）」且全程不做网络探测；安装包 / 插件区提示单机模式（同屏无矛盾）', async () => {
    mocks.local.getHostConfig.mockResolvedValue({ ...HOST_CONFIG, serverUrl: '' })
    renderSettings()
    expect(await screen.findByText('服务端未连接（单机模式可用）')).toBeTruthy()
    // 同屏一致：「更新与安装包」徽标与提示、「插件管理」服务端可用插件区均为单机模式口径
    expect(screen.getByText('单机模式')).toBeTruthy()
    expect(screen.getByText(/当前未连接服务端（单机模式）/)).toBeTruthy()
    expect(screen.getByText(/当前未连接服务端，处于单机模式/)).toBeTruthy()
    // a 案：空地址无目标可探，不发起 healthz；业务列表不拉取（serverApi 不以空串 base 落同源打到本机 WinHost）
    await waitFor(() => expect(mocks.local.getHostConfig).toHaveBeenCalled())
    expect(mocks.server.healthz).not.toHaveBeenCalled()
    expect(mocks.server.listClientPackages).not.toHaveBeenCalled()
    expect(mocks.server.listPluginPackages).not.toHaveBeenCalled()
  })

  it('AC-02 可达地址：探测成功徽标「服务端已连接」，安装包区按服务端拉取列表', async () => {
    renderSettings()
    expect((await screen.findAllByText('服务端已连接')).length).toBeGreaterThan(0)
    await waitFor(() => expect(mocks.server.listClientPackages).toHaveBeenCalled())
  })

  it('AC-02 不可达地址：探测失败徽标「服务端未连接（单机模式可用）」', async () => {
    mocks.server.healthz.mockRejectedValue(new Error('down'))
    renderSettings()
    expect(await screen.findByText('服务端未连接（单机模式可用）')).toBeTruthy()
    await waitFor(() => expect(mocks.server.healthz).toHaveBeenCalled())
  })

  it('AC-02 切换刷新：保存不可达地址转「未连接」，再保存可达地址转回「已连接」（保存即重探测，无竞态双态）', async () => {
    renderSettings()
    expect((await screen.findAllByText('服务端已连接')).length).toBeGreaterThan(0)
    // 切到不可达地址并保存 → 徽标转未连接
    fireEvent.change(screen.getByLabelText('服务端地址'), { target: { value: 'http://192.168.0.99:53961' } })
    mocks.server.healthz.mockRejectedValue(new Error('down'))
    fireEvent.click(screen.getByRole('button', { name: /保存并生效/ }))
    expect(await screen.findByText('服务端未连接（单机模式可用）')).toBeTruthy()
    expect(mocks.local.setHostConfig).toHaveBeenCalledWith({ serverUrl: 'http://192.168.0.99:53961' })
    // 再切回可达地址并保存 → 徽标转回已连接
    fireEvent.change(screen.getByLabelText('服务端地址'), { target: { value: 'http://192.168.1.10:53961' } })
    mocks.server.healthz.mockResolvedValue({ service: 'LabelFrame.Server', status: 'ok' })
    fireEvent.click(screen.getByRole('button', { name: /保存并生效/ }))
    expect((await screen.findAllByText('服务端已连接')).length).toBeGreaterThan(0)
  })

  it('保存空地址：徽标即转「服务端未连接（单机模式可用）」且不再探测（a 案；空终态不被旧探测结果覆盖）', async () => {
    renderSettings()
    expect((await screen.findAllByText('服务端已连接')).length).toBeGreaterThan(0)
    mocks.server.healthz.mockClear()
    fireEvent.change(screen.getByLabelText('服务端地址'), { target: { value: '' } })
    fireEvent.click(screen.getByRole('button', { name: /保存并生效/ }))
    expect(await screen.findByText('服务端未连接（单机模式可用）')).toBeTruthy()
    expect(mocks.local.setHostConfig).toHaveBeenCalledWith({ serverUrl: '' })
    expect(mocks.server.healthz).not.toHaveBeenCalled()
  })

  it('探测按配置就绪串行化：机器级配置加载完成前不发起服务端探测（消除初始化与配置加载的探测竞态）', async () => {
    let resolveConfig!: (cfg: typeof HOST_CONFIG) => void
    mocks.local.getHostConfig.mockImplementation(() => new Promise<typeof HOST_CONFIG>((resolve) => { resolveConfig = resolve }))
    renderSettings()
    await waitFor(() => expect(mocks.local.getHostConfig).toHaveBeenCalled())
    // 配置未决：初始化探测被闸住（不再先按默认地址 127.0.0.1:53961 探测，与配置加载后的探测交错）
    expect(mocks.server.healthz).not.toHaveBeenCalled()
    // 配置就绪（serverUrl 生效）后首次探测才串行发起，且仅一次
    resolveConfig({ ...HOST_CONFIG, serverUrl: 'http://192.168.1.9:53961' })
    await waitFor(() => expect(mocks.server.healthz).toHaveBeenCalledTimes(1))
  })
})

describe('连接方式（F3，恢复迭代 15；迭代 73 起默认折叠，交互前先展开）', () => {
  it('模式单选只显示当前模式参数（切到网络打印机显示 IP / 端口）', async () => {
    renderSettings()
    openTransportPanel()
    fireEvent.click(await screen.findByRole('radio', { name: /^网络打印机/ }))
    expect(await screen.findByLabelText('打印机 IP / 主机名')).toBeTruthy()
    expect(screen.getByLabelText('端口')).toBeTruthy()
  })

  it('测试连接：发送候选参数（testOnly 注入在 client 层，组件只传表单参数），成功后显示后端 message 且不调 setTransport', async () => {
    renderSettings()
    openTransportPanel()
    fireEvent.click(await screen.findByRole('radio', { name: /^网络打印机/ }))
    await screen.findByLabelText('打印机 IP / 主机名')
    fireEvent.change(screen.getByLabelText('打印机 IP / 主机名'), { target: { value: '192.168.1.50' } })
    fireEvent.click(within(withinSection('连接方式')).getByRole('button', { name: /^测试连接/ }))
    expect(await screen.findByText('测试通过。')).toBeTruthy()
    expect(mocks.local.testTransport).toHaveBeenCalledWith(expect.objectContaining({ mode: 'Tcp', tcpHost: '192.168.1.50', tcpPort: 9100 }))
    expect(mocks.local.setTransport).not.toHaveBeenCalled()
  })

  it('保存并应用：setTransport 不带 testOnly，成功后当前连接徽标更新', async () => {
    renderSettings()
    openTransportPanel()
    fireEvent.click(await screen.findByRole('radio', { name: /^网络打印机/ }))
    await screen.findByLabelText('打印机 IP / 主机名')
    fireEvent.change(screen.getByLabelText('打印机 IP / 主机名'), { target: { value: '192.168.1.50' } })
    fireEvent.click(screen.getByRole('button', { name: /保存并应用/ }))
    expect(await screen.findByText('已切换到 TCP。')).toBeTruthy()
    expect(mocks.local.setTransport).toHaveBeenCalledWith(expect.objectContaining({ mode: 'Tcp', tcpHost: '192.168.1.50', tcpPort: 9100 }))
    expect(mocks.local.setTransport.mock.calls[0][0].testOnly).toBeUndefined()
    // 全局状态立即更新（折叠头摘要与面板内徽标均显示新连接，不依赖轮询）
    expect((await within(withinSection('连接方式')).findAllByText(/网络打印机 192\.168\.1\.50:9100/)).length).toBeGreaterThan(0)
  })

  it('保存失败（后端返回 ok:false）：展示 message，当前生效连接保持', async () => {
    mocks.local.setTransport.mockResolvedValue({ ok: false, message: '连接测试失败：无法连接打印机。', config: { mode: 'Log', params: {} } })
    renderSettings()
    openTransportPanel()
    fireEvent.click(screen.getByRole('button', { name: /保存并应用/ }))
    expect(await screen.findByText('连接测试失败：无法连接打印机。')).toBeTruthy()
    // 生效连接仍是模拟打印（未切换）——折叠头摘要 + 面板徽标
    expect(screen.getAllByText('模拟打印').length).toBeGreaterThan(0)
  })
})

describe('连接文案用户化（迭代 82，#130：评审 #114 B-1 / A-1 / B-5 / B-2）', () => {
  /** 插件模式目录：镜像后端 BuiltinTransportPlugins（Log + TCP 9100）的 DisplayName / Description。 */
  const BUILTIN_PLUGINS = [
    {
      id: 'log',
      displayName: '模拟打印',
      description: '模拟打印：不连接真实打印机，作业按打印成功处理；图片模式的标签图片会保存到本机，便于先确认打印效果。',
      parameters: [],
    },
    {
      id: 'tcp9100',
      displayName: '网口打印机（TCP 9100）',
      description: 'TCP 9100 网络打印机（Zebra 等）：连接打印机 IP 的 9100 端口发送指令，状态查询用 ~HS。',
      parameters: [
        { key: 'host', label: '打印机地址 / IP', type: 'String' as const, required: true, hint: '如 192.168.1.50' },
        { key: 'port', label: '端口', type: 'Int' as const, defaultValue: '9100', hint: '默认 9100' },
        { key: 'timeoutSeconds', label: '超时（秒）', type: 'Int' as const, defaultValue: '10' },
      ],
    },
  ]

  /** 新后端 Log 连接：displayText = 插件 Describe「模拟打印」+ availablePlugins 插件目录。 */
  function mockLogPluginTransport() {
    mocks.local.getTransport.mockResolvedValue({
      pluginId: 'log',
      displayText: '模拟打印',
      params: {},
      availablePlugins: BUILTIN_PLUGINS,
      mode: 'Log',
    })
  }

  it('AC-01：连接方式摘要与测试打印说明均显示「模拟打印」，无「LOG」直出', async () => {
    mockLogPluginTransport()
    renderSettings()
    // 连接方式折叠头摘要（displayText 优先 = 插件 Describe 用户语）
    expect(await screen.findByText('模拟打印')).toBeTruthy()
    // 测试打印说明（打印机分组）：当前连接方式：模拟打印
    const printerSection = withinSection('打印机')
    await waitFor(() => expect(printerSection.textContent).toContain('当前连接方式：模拟打印'))
    // 全页无「LOG」直出（起选项名亦为「模拟打印」，页面无任何「Log」字样）
    expect(screen.queryByText(/LOG/)).toBeNull()
    expect(screen.queryByText(/Log/)).toBeNull()
  })

  it('选项列表 Log 项显示「模拟打印」，无「Log」字样（迭代 87，#143）', async () => {
    mockLogPluginTransport()
    renderSettings()
    openTransportPanel()
    // 选项列表（radio 可访问名 = 后端插件 DisplayName）：显示「模拟打印」，无技术前缀「Log」
    expect(await screen.findByRole('radio', { name: '模拟打印' })).toBeTruthy()
    // 选中态（默认连接 = log）：说明文直出 Description，与摘要同源用户语
    expect((await screen.findAllByText(/模拟打印：不连接真实打印机/)).length).toBeGreaterThan(0)
    expect(screen.queryByText(/Log/)).toBeNull()
  })

  it('AC-02：展开 Log 编辑区说明完整通顺——Description 直出，无「（无参数）。」残句、无「联调」用语', async () => {
    mockLogPluginTransport()
    renderSettings()
    openTransportPanel()
    // 说明文两处直出同一 Description（面板说明行 + 无参数编辑区占位），均不含拼接残句
    const hints = await screen.findAllByText(/模拟打印：不连接真实打印机/)
    expect(hints.length).toBeGreaterThan(0)
    expect(screen.queryByText(/（无参数）/)).toBeNull()
    expect(screen.queryByText(/联调/)).toBeNull()
  })

  it('AC-02：TCP 选项列表显示「网口打印机（TCP 9100）」，选中后参数表单保持', async () => {
    mockLogPluginTransport()
    renderSettings()
    // 挂载链先落定（连接配置已加载——折叠头摘要「模拟打印」出现为准），再展开交互：
    // 实证（run 35553586518）：配置未落定即展开点选时，挂载期并发 mock 的迟到
    // 渲染会与单选列表 / 参数表单的渲染交错（选中态与表单不同步），findByLabelText 默认 1s 超时
    expect(await screen.findByText('模拟打印')).toBeTruthy()
    openTransportPanel()
    fireEvent.click(await screen.findByRole('radio', { name: /网口打印机（TCP 9100）/ }))
    // 必填参数标签带「 *」必填标记，用正则匹配
    expect(await screen.findByLabelText(/打印机地址 \/ IP/)).toBeTruthy()
    expect(screen.getByLabelText('端口')).toBeTruthy()
  })
})

describe('原生指令模式提示（迭代 78，DESIGN §5.4.2 / #120 AC-04）', () => {
  /** 插件模式连接配置：带编译能力的 Zebra 插件 + printMode Select 参数。 */
  const ZEBRA_PLUGIN = {
    id: 'labelframe-transport-zebra',
    displayName: 'Zebra',
    description: 'Zebra 官方 Link-OS SDK',
    supportsDocumentCompile: true,
    parameters: [
      { key: 'kind', label: '连接类型', type: 'Select' as const, required: true, defaultValue: 'Tcp', options: [{ value: 'Tcp', label: 'TCP' }] },
      {
        key: 'printMode',
        label: '打印方式',
        type: 'Select' as const,
        required: true,
        defaultValue: 'image',
        options: [
          { value: 'image', label: '图片（默认）' },
          { value: 'native', label: '原生指令' },
        ],
      },
    ],
  }

  function mockNativeTransport(printMode: string) {
    mocks.local.getTransport.mockResolvedValue({
      pluginId: 'labelframe-transport-zebra',
      displayName: 'Zebra',
      displayText: 'Zebra TCP 127.0.0.1:9100',
      params: { kind: 'Tcp', host: '127.0.0.1', printMode },
      availablePlugins: [ZEBRA_PLUGIN],
      mode: 'Log',
    })
  }

  it('当前连接为原生指令：展开连接面板即出现「无预览，效果以真机为准」提示', async () => {
    mockNativeTransport('native')
    renderSettings()
    openTransportPanel()
    const hint = await screen.findByTestId('native-print-mode-hint')
    expect(hint.textContent).toContain('原生指令模式无预览，效果以真机为准')
  })

  it('切回图片模式：提示消失（选择即时反馈）', async () => {
    mockNativeTransport('native')
    renderSettings()
    openTransportPanel()
    await screen.findByTestId('native-print-mode-hint')
    fireEvent.change(screen.getByDisplayValue('原生指令'), { target: { value: 'image' } })
    expect(screen.queryByTestId('native-print-mode-hint')).toBeNull()
  })

  it('图片（默认）连接：不出现提示', async () => {
    mockNativeTransport('image')
    renderSettings()
    openTransportPanel()
    await screen.findByDisplayValue('图片（默认）')
    expect(screen.queryByTestId('native-print-mode-hint')).toBeNull()
  })
})

describe('打印机（F4）', () => {
  it('状态展示：在线 + 附加信息', async () => {
    renderSettings()
    expect(await screen.findByText('在线')).toBeTruthy()
    expect(screen.getByText('就绪')).toBeTruthy()
  })

  it('缺纸 / 暂停徽标', async () => {
    mocks.local.getPrinterStatus.mockResolvedValue({ isOnline: true, isPaperOut: true, isPaused: true, message: '' })
    renderSettings()
    expect(await screen.findByText('缺纸')).toBeTruthy()
    expect(screen.getByText('已暂停')).toBeTruthy()
  })

  it('测试打印：调用 testPrinter 并提示确认出纸（迭代 73 文案用户化，不再展示字节数）', async () => {
    renderSettings()
    fireEvent.click(screen.getByRole('button', { name: /测试打印/ }))
    expect(await screen.findByText('测试页已发送，请确认打印机是否出纸。')).toBeTruthy()
    expect(mocks.local.testPrinter).toHaveBeenCalled()
  })
})

describe('更新与安装包（迭代 22 §2.3）', () => {
  const PKGS = [
    { fileName: 'LabelFrame.Client-0.18.0.msi', sizeBytes: 42 * 1024 * 1024, modifiedAt: '2026-08-17T10:00:00Z', url: '/api/client-packages/LabelFrame.Client-0.18.0.msi' },
    { fileName: 'LabelFrame.Client-linux.zip', sizeBytes: 1024, modifiedAt: '2026-08-17T09:00:00Z' },
  ]

  it('服务端已连接：拉取安装包列表，显示文件名 / 大小 / 修改时间与下载链接', async () => {
    mocks.server.listClientPackages.mockResolvedValue(PKGS)
    renderSettings()
    expect(await screen.findByText('LabelFrame.Client-0.18.0.msi')).toBeTruthy()
    expect(screen.getByText('LabelFrame.Client-linux.zip')).toBeTruthy()
    expect(screen.getByText('42.0 MB')).toBeTruthy()
    // 下载链接指向 {serverBaseUrl}/api/client-packages/{fileName}（client 构建 baseUrl = hostConfig.serverUrl）
    const links = screen.getAllByRole('link', { name: /下载/ })
    expect(links[0].getAttribute('href')).toBe('http://127.0.0.1:53961/api/client-packages/LabelFrame.Client-0.18.0.msi')
    expect(links[1].getAttribute('href')).toBe('http://127.0.0.1:53961/api/client-packages/LabelFrame.Client-linux.zip')
  })

  it('服务端已连接但无安装包：提示去服务端管理界面上传', async () => {
    renderSettings()
    expect(await screen.findByText(/服务端暂无客户端安装包/)).toBeTruthy()
    expect(mocks.server.listClientPackages).toHaveBeenCalled()
  })

  it('单机模式（服务端不可达）：提示需先连接服务端，不调 listClientPackages', async () => {
    mocks.server.healthz.mockRejectedValue(new Error('down'))
    renderSettings()
    expect(await screen.findByText(/当前未连接服务端（单机模式）/)).toBeTruthy()
    expect(screen.getByText(/请先在上方「服务端地址」中连接服务端/)).toBeTruthy()
    expect(mocks.server.listClientPackages).not.toHaveBeenCalled()
  })
})

describe('检查更新（迭代 64，决策 #126）', () => {
  const pkg = (fileName: string) => ({ fileName, sizeBytes: 1024, modifiedAt: '2026-09-14T10:00:00Z' })

  it('服务端有新版客户端包：显示新版本号与本机版本 + 获取入口提示', async () => {
    mocks.local.getHostConfig.mockResolvedValue({ ...HOST_CONFIG, version: '0.26.0' })
    mocks.server.listClientPackages.mockResolvedValue([pkg('LabelFrame-Client-0.27.0.msi')])
    renderSettings()
    const notice = await screen.findByTestId('update-available')
    expect(notice.textContent).toContain('0.27.0')
    expect(notice.textContent).toContain('0.26.0')
    expect(notice.textContent).toContain('下载新版安装包并运行安装')
    expect(screen.queryByTestId('update-uptodate')).toBeNull()
  })

  it('服务端包不高于本机（已是最新）：明确「已是最新」且无误导', async () => {
    mocks.local.getHostConfig.mockResolvedValue({ ...HOST_CONFIG, version: '0.27.0' })
    mocks.server.listClientPackages.mockResolvedValue([pkg('LabelFrame-Client-0.27.0.msi'), pkg('LabelFrame-Client-0.26.0.msi')])
    renderSettings()
    const notice = await screen.findByTestId('update-uptodate')
    expect(notice.textContent).toContain('已是最新')
    expect(notice.textContent).toContain('0.27.0')
    expect(screen.queryByTestId('update-available')).toBeNull()
  })

  it('本机版本未知（旧客户端无 version 字段）：不比较不显示结论', async () => {
    mocks.server.listClientPackages.mockResolvedValue([pkg('LabelFrame-Client-0.27.0.msi')])
    renderSettings()
    await screen.findByText('LabelFrame-Client-0.27.0.msi')
    expect(screen.queryByTestId('update-available')).toBeNull()
    expect(screen.queryByTestId('update-uptodate')).toBeNull()
  })

  it('列表无可解析版本号的包：不比较不显示结论（多版本取最高断言归 lib 单测）', async () => {
    mocks.local.getHostConfig.mockResolvedValue({ ...HOST_CONFIG, version: '0.26.0' })
    mocks.server.listClientPackages.mockResolvedValue([pkg('客户端安装包.zip')])
    renderSettings()
    await screen.findByText('客户端安装包.zip')
    expect(screen.queryByTestId('update-available')).toBeNull()
    expect(screen.queryByTestId('update-uptodate')).toBeNull()
  })
})

describe('插件管理（迭代 23 §5.6）', () => {
  const PKG: PluginPackageInfo = {
    fileName: 'sample-1.0.0.lfplugin',
    pluginId: 'sample',
    name: '示例插件',
    version: '1.0.0',
    sizeBytes: 2048,
    modifiedAt: '2026-08-17T10:00:00Z',
    valid: true,
  }
  const INVALID: PluginPackageInfo = {
    fileName: 'broken.lfplugin',
    sizeBytes: 1024,
    modifiedAt: '2026-08-17T09:00:00Z',
    valid: false,
    invalidReason: 'manifest 缺少 pluginId',
  }
  const INSTALLED: InstalledPluginInfo = {
    pluginId: 'sample',
    name: '示例插件',
    version: '1.0.0',
    loaded: true,
    source: 'package',
  }

  it('可用列表渲染：valid 显示名称 / 版本 / pluginId / 有效徽标与安装按钮；invalid 红标 + 原因 + 安装禁用', async () => {
    mocks.server.listPluginPackages.mockResolvedValue([PKG, INVALID])
    renderSettings()
    expect(await screen.findByText('示例插件')).toBeTruthy()
    expect(screen.getByText('1.0.0')).toBeTruthy()
    expect(screen.getByText('sample')).toBeTruthy()
    expect(screen.getByText('有效')).toBeTruthy()
    expect(screen.getByText('无效')).toBeTruthy()
    expect(screen.getByText(/manifest 缺少 pluginId/)).toBeTruthy()
    const buttons = screen.getAllByRole('button', { name: /^安装$/ })
    expect(buttons).toHaveLength(2)
    expect((buttons[0] as HTMLButtonElement).disabled).toBe(false)
    expect((buttons[1] as HTMLButtonElement).disabled).toBe(true)
  })

  it('安装流程：下载插件包 → 本机安装（保留原始文件名）→ 提示后端 message + 刷新已安装列表', async () => {
    mocks.server.listPluginPackages.mockResolvedValue([PKG])
    renderSettings()
    fireEvent.click(await screen.findByRole('button', { name: /^安装$/ }))
    await waitFor(() => expect(mocks.server.downloadPluginPackage).toHaveBeenCalledWith('sample-1.0.0.lfplugin'))
    await waitFor(() => expect(mocks.local.installPlugin).toHaveBeenCalledTimes(1))
    const file = mocks.local.installPlugin.mock.calls[0][0] as File
    expect(file.name).toBe('sample-1.0.0.lfplugin')
    expect(await screen.findByText(/已安装，重启客户端后生效/)).toBeTruthy()
    // 挂载 1 次 + 安装成功后刷新
    await waitFor(() => expect(mocks.local.listInstalledPlugins).toHaveBeenCalledTimes(2))
  })

  it('覆盖安装：已安装同 pluginId 时弹确认 Modal「将覆盖」；确认后安装', async () => {
    mocks.server.listPluginPackages.mockResolvedValue([PKG])
    mocks.local.listInstalledPlugins.mockResolvedValue([INSTALLED])
    renderSettings()
    fireEvent.click(await screen.findByRole('button', { name: /^安装$/ }))
    // 自研 Modal 确认（替代原生 confirm 弹窗）
    expect(await screen.findByText(/将覆盖为/)).toBeTruthy()
    fireEvent.click(screen.getByRole('button', { name: '确认覆盖安装' }))
    await waitFor(() => expect(mocks.local.installPlugin).toHaveBeenCalledTimes(1))
  })

  it('覆盖安装取消：关闭确认 Modal → 不下载不安装', async () => {
    mocks.server.listPluginPackages.mockResolvedValue([PKG])
    mocks.local.listInstalledPlugins.mockResolvedValue([INSTALLED])
    renderSettings()
    fireEvent.click(await screen.findByRole('button', { name: /^安装$/ }))
    expect(await screen.findByText(/将覆盖为/)).toBeTruthy()
    fireEvent.click(screen.getByRole('button', { name: '取消' }))
    expect(mocks.server.downloadPluginPackage).not.toHaveBeenCalled()
    expect(mocks.local.installPlugin).not.toHaveBeenCalled()
  })

  it('单机模式：可用插件区提示需先连接服务端、不调 listPluginPackages；已安装区仍渲染', async () => {
    mocks.server.healthz.mockRejectedValue(new Error('down'))
    renderSettings()
    expect(await screen.findByText(/当前未连接服务端，处于单机模式/)).toBeTruthy()
    expect(mocks.server.listPluginPackages).not.toHaveBeenCalled()
    expect(mocks.local.listInstalledPlugins).toHaveBeenCalled()
  })

  it('旧 Server（可用插件区 404）：区分展示「服务端版本较旧，暂不支持插件管理」', async () => {
    mocks.server.listPluginPackages.mockRejectedValue(new ApiError('HTTP_404', '请求失败（HTTP 404）。'))
    renderSettings()
    expect(await screen.findByText(/服务端版本较旧，暂不支持插件管理/)).toBeTruthy()
  })

  it('已安装徽标四态：已加载 / 待重启生效 / 加载失败 + 原因 / 手动放置只读无卸载', async () => {
    mocks.local.listInstalledPlugins.mockResolvedValue([
      { pluginId: 'a', name: '已加载插件', version: '1.0.0', loaded: true, source: 'package' },
      { pluginId: 'b', name: '待重启插件', version: '1.0.0', loaded: false, source: 'package' },
      { pluginId: 'c', name: '坏插件', version: '1.0.0', loaded: false, loadError: '加载失败：找不到依赖', source: 'package' },
      { pluginId: 'd', name: '手动插件', version: '?', loaded: true, source: 'manual' },
    ])
    renderSettings()
    expect(await screen.findByText('已加载插件')).toBeTruthy()
    expect(screen.getByText('已加载')).toBeTruthy()
    expect(screen.getByText('待重启生效')).toBeTruthy()
    expect(screen.getByText('加载失败')).toBeTruthy()
    expect(screen.getByText(/找不到依赖/)).toBeTruthy()
    expect(screen.getByText('手动放置')).toBeTruthy()
    expect(screen.getAllByRole('button', { name: /^卸载$/ })).toHaveLength(3)
    expect(screen.queryByText('卸载中…')).toBeNull()
  })

  it('卸载：确认 Modal → uninstallPlugin(pluginId) → 提示 message + 刷新', async () => {
    mocks.local.listInstalledPlugins.mockResolvedValue([INSTALLED])
    renderSettings()
    fireEvent.click(await screen.findByRole('button', { name: /^卸载$/ }))
    // 自研 Modal 确认（替代原生 confirm 弹窗）
    expect(await screen.findByText(/确认卸载插件/)).toBeTruthy()
    fireEvent.click(screen.getByRole('button', { name: '确认卸载' }))
    await waitFor(() => expect(mocks.local.uninstallPlugin).toHaveBeenCalledWith('sample'))
    expect(await screen.findByText(/已卸载，重启客户端后生效/)).toBeTruthy()
  })

  it('安装失败（后端 400 ErrorView）：展示 message（文件锁提示）', async () => {
    mocks.server.listPluginPackages.mockResolvedValue([PKG])
    mocks.local.installPlugin.mockRejectedValue(
      new ApiError('LF_PLUGIN_BUSY', '插件「sample」正在使用中（DLL 被客户端占用），请重启客户端后重试。'),
    )
    renderSettings()
    fireEvent.click(await screen.findByRole('button', { name: /^安装$/ }))
    expect(await screen.findByText(/正在使用中（DLL 被客户端占用），请重启客户端后重试/)).toBeTruthy()
  })

  it('已安装列表加载失败：错误提示（非 404 不显示版本提示）', async () => {
    mocks.local.listInstalledPlugins.mockRejectedValue(new Error('network down'))
    renderSettings()
    expect(await screen.findByText(/获取已安装插件列表失败/)).toBeTruthy()
    expect(screen.queryByText(/当前客户端版本不支持插件管理/)).toBeNull()
  })

  it('旧 WinHost（已安装区 404）：显示「当前客户端版本不支持插件管理」', async () => {
    mocks.local.listInstalledPlugins.mockRejectedValue(new ApiError('HTTP_404', '请求失败（HTTP 404）。'))
    renderSettings()
    expect(await screen.findByText('当前客户端版本不支持插件管理。')).toBeTruthy()
  })

  it('超过 64MB 的插件包：安装按钮禁用（预检提示）', async () => {
    mocks.server.listPluginPackages.mockResolvedValue([{ ...PKG, sizeBytes: 64 * 1024 * 1024 + 1 }])
    renderSettings()
    const btn = (await screen.findByRole('button', { name: /^安装$/ })) as HTMLButtonElement
    expect(btn.disabled).toBe(true)
  })
})

describe('打印批次（迭代 24 §4.4）', () => {
  /** 批次卡片内限定查询（「保存」按钮在服务端地址与打印批次两组各有一个）。 */
  function withinBatch(): HTMLElement {
    return screen.getByText('打印批次').closest('section') as HTMLElement
  }

  it('卡片渲染：挂载即拉取设置，显示开关（默认关）与数量 / 间隔默认值 + 提示文案', async () => {
    renderSettings()
    // 面板与默认值（GET 返回默认：关 / 10 张 / 500ms）
    expect(screen.getByText('打印批次')).toBeTruthy()
    expect(await screen.findByLabelText('开启批次作业')).toBeTruthy()
    const enabled = screen.getByLabelText('开启批次作业') as HTMLInputElement
    const size = screen.getByLabelText('每批次打印数量') as HTMLInputElement
    const interval = screen.getByLabelText('批次打印间隔（毫秒）') as HTMLInputElement
    expect(enabled.checked).toBe(false)
    expect(size.value).toBe('10')
    expect(interval.value).toBe('500')
    expect(mocks.local.getPrintSettings).toHaveBeenCalled()
    // 提示文案
    expect(screen.getByText(/每 10 张一批发送到打印机，批与批之间间隔 500 毫秒/)).toBeTruthy()
    // 卡片位于「连接方式」之下
    const conn = screen.getByText('连接方式').closest('section') as HTMLElement
    expect(conn.compareDocumentPosition(withinBatch()) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy()
  })

  it('开关联动：关闭时数量 / 间隔输入禁用置灰，开启后可用，再关闭恢复禁用', async () => {
    renderSettings()
    const enabled = (await screen.findByLabelText('开启批次作业')) as HTMLInputElement
    const size = screen.getByLabelText('每批次打印数量') as HTMLInputElement
    const interval = screen.getByLabelText('批次打印间隔（毫秒）') as HTMLInputElement
    // 默认关 → 两个数字输入禁用
    expect(size.disabled).toBe(true)
    expect(interval.disabled).toBe(true)
    // 开启 → 可用
    fireEvent.click(enabled)
    expect(enabled.checked).toBe(true)
    expect(size.disabled).toBe(false)
    expect(interval.disabled).toBe(false)
    // 再关闭 → 恢复禁用
    fireEvent.click(enabled)
    expect(enabled.checked).toBe(false)
    expect(size.disabled).toBe(true)
    expect(interval.disabled).toBe(true)
  })

  it('保存成功：setPrintSettings 携带表单值，提示「已保存并立即生效」', async () => {
    renderSettings()
    const enabled = (await screen.findByLabelText('开启批次作业')) as HTMLInputElement
    fireEvent.click(enabled)
    fireEvent.change(screen.getByLabelText('每批次打印数量'), { target: { value: '20' } })
    fireEvent.change(screen.getByLabelText('批次打印间隔（毫秒）'), { target: { value: '800' } })
    fireEvent.click(within(withinBatch()).getByRole('button', { name: /^保存$/ }))
    expect(await screen.findByText('已保存并立即生效。')).toBeTruthy()
    expect(mocks.local.setPrintSettings).toHaveBeenCalledWith({ batchEnabled: true, batchSize: 20, batchIntervalMs: 800 })
  })

  it('保存失败（后端 400 ErrorView）：展示后端中文 message，不提示成功', async () => {
    mocks.local.setPrintSettings.mockRejectedValue(new ApiError('LF_BATCH_INVALID', '每批次打印数量必须 ≥ 1。'))
    renderSettings()
    const enabled = (await screen.findByLabelText('开启批次作业')) as HTMLInputElement
    fireEvent.click(enabled)
    fireEvent.change(screen.getByLabelText('每批次打印数量'), { target: { value: '0' } })
    fireEvent.click(within(withinBatch()).getByRole('button', { name: /^保存$/ }))
    expect(await screen.findByText('每批次打印数量必须 ≥ 1。')).toBeTruthy()
    expect(screen.queryByText('已保存并立即生效。')).toBeNull()
  })

  it('保存失败（网络错误）：回退通用文案', async () => {
    mocks.local.setPrintSettings.mockRejectedValue(new Error('network down'))
    renderSettings()
    const enabled = (await screen.findByLabelText('开启批次作业')) as HTMLInputElement
    fireEvent.click(enabled)
    fireEvent.click(within(withinBatch()).getByRole('button', { name: /^保存$/ }))
    expect(await screen.findByText('保存批次设置失败。')).toBeTruthy()
  })

  it('旧 WinHost（GET 404）：显示版本提示「当前客户端版本不支持批次作业」，不渲染表单', async () => {
    mocks.local.getPrintSettings.mockRejectedValue(new ApiError('HTTP_404', '请求失败（HTTP 404）。'))
    renderSettings()
    expect(await screen.findByText('当前客户端版本不支持批次作业。')).toBeTruthy()
    expect(screen.queryByLabelText('开启批次作业')).toBeNull()
    expect(screen.queryByLabelText('每批次打印数量')).toBeNull()
    expect(screen.queryByLabelText('批次打印间隔（毫秒）')).toBeNull()
    expect(screen.queryByRole('button', { name: /^保存$/ })).toBeNull()
  })

  it('获取设置失败（非 404）：展示错误信息，不渲染表单', async () => {
    mocks.local.getPrintSettings.mockRejectedValue(new Error('network down'))
    renderSettings()
    expect(await screen.findByText(/获取批次设置失败/)).toBeTruthy()
    expect(screen.queryByLabelText('开启批次作业')).toBeNull()
  })
})

describe('组件级「?」文档点（迭代 126 · #308 AC-03）', () => {
  it('7 张卡片标题挂「?」（data-help 语义锚点）', async () => {
    renderSettings()
    await screen.findByLabelText('服务端地址')
    for (const a of ['settings.language', 'settings.serverAddress', 'settings.transport', 'settings.batch', 'settings.printer', 'settings.updates', 'settings.plugins']) {
      expect(document.querySelector('[data-help="' + a + '"]')).toBeTruthy()
    }
  })

  it('点「?」弹出对应卡片文档气泡，Esc 关闭；连接方式卡「?」不触发折叠连带（stopPropagation）', async () => {
    renderSettings()
    await screen.findByLabelText('服务端地址')
    const transportDot = document.querySelector('[data-help="settings.transport"]') as HTMLButtonElement
    expect(transportDot).toBeTruthy()
    fireEvent.click(transportDot)
    const dialog = screen.getByRole('dialog')
    // 气泡不再渲染可见标题（无障碍名保留 title 词条）、正文直出
    expect(dialog.getAttribute('aria-label')).toBe('连接方式')
    expect(within(dialog).queryByText('连接方式')).toBeNull()
    expect(within(dialog).getByText(/卡片收起时显示当前连接/)).toBeTruthy()
    // 折叠面板未连带展开（面板头 onClick 被冒泡拦截）
    expect(screen.queryByRole('button', { name: /保存并应用/ })).toBeNull()
    fireEvent.keyDown(document, { key: 'Escape' })
    expect(screen.queryByRole('dialog')).toBeNull()
  })
})

// ---------- ：帮助深链 = 路由 sub 段承载，页内轮询自动弹卡片气泡 ----------

describe('帮助深链（#316 决策 #178）', () => {
  function renderSettingsWithLink(helpAnchor: string) {
    return render(
      <AppProvider>
        <StatusProbe />
        <Settings helpAnchor={helpAnchor} />
      </AppProvider>,
    )
  }

  it('深链锚点常在即刻命中：挂载即自动弹对应卡片气泡（settings.language → 「语言」）', async () => {
    renderSettingsWithLink('settings.language')
    await screen.findByLabelText('服务端地址')
    const dialog = await screen.findByRole('dialog')
    expect(dialog.getAttribute('aria-label')).toBe('语言')
  })

  it('深链轮询超时通用降级：锚点运行时不可见（未登记串无 [data-help]）→ 状态栏通用提示，不弹气泡', async () => {
    renderSettingsWithLink('settings.nonexistent')
    await screen.findByLabelText('服务端地址')
    await waitFor(() => expect(screen.getByTestId('status-msg').textContent).toContain('已切换页面'), { timeout: 6000 })
    expect(screen.queryByRole('dialog')).toBeNull()
  })
})
