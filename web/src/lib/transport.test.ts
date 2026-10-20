// 传输插件工具单元测试——徽标格式化（中文 displayText 优先 / 非中文本地化组合）、mode→pluginId 映射
// spec 默认值 / Select 枚举兼容解析（string[] 与 { value, label? }[]）、配置参数提取。

import { describe, expect, it, afterEach } from 'vitest'
import i18next from '../i18n'
import type { TransportConfig, TransportPluginInfo } from './api/types'
import {
  MODE_TO_PLUGIN_ID,
  defaultPluginParams,
  effectivePluginId,
  formatTransport,
  isNativePrintMode,
  isPluginMode,
  isSpecVisible,
  pluginParamsFromConfig,
  specDefaultValue,
  specOptions,
} from './transport'

const TCP9100_PLUGIN: TransportPluginInfo = {
  id: 'tcp9100',
  displayName: 'TCP 9100',
  description: '通过网络口直连打印机（ZPL 指令）。',
  parameters: [
    { key: 'host', label: '打印机 IP / 主机名', type: 'String', required: true, hint: '192.168.1.50' },
    { key: 'port', label: '端口', type: 'Int', defaultValue: '9100', hint: '默认 9100' },
    { key: 'timeoutSeconds', label: '连接超时（秒）', type: 'Int', defaultValue: 10 },
  ],
}

const ZEBRA_PLUGIN: TransportPluginInfo = {
  id: 'zebra',
  displayName: 'Zebra',
  parameters: [
    { key: 'kind', label: '连接方式', type: 'Select', defaultValue: 'Tcp', options: ['Tcp', 'Usb', 'Driver'] },
    { key: 'printerName', label: '打印机名称', type: 'String' },
  ],
}

const VENDOR_PLUGIN: TransportPluginInfo = {
  id: 'vendor-ble',
  displayName: '厂商蓝牙',
  parameters: [
    {
      key: 'mode',
      label: '模式',
      type: 'Select',
      defaultValue: 'fast',
      options: [
        { value: 'fast', label: '快速' },
        { value: 'stable', label: '稳定' },
      ],
    },
    { key: 'pairedOnly', label: '仅已配对', type: 'Bool', defaultValue: 'true' },
  ],
}

describe('formatTransport：displayText 优先（迭代 22）', () => {
  it('新后端 displayText 存在时直接使用', () => {
    const cfg: TransportConfig = {
      pluginId: 'tcp9100',
      displayName: 'TCP 9100',
      displayText: 'TCP 192.168.1.50:9100',
      params: { host: '192.168.1.50', port: 9100 },
      mode: 'Tcp',
    }
    expect(formatTransport(cfg)).toBe('TCP 192.168.1.50:9100')
  })

  it('新后端 Log 连接：displayText（= 插件 Describe「模拟打印」，迭代 82 #130）三处徽标用户语，无「LOG」直出', () => {
    const cfg: TransportConfig = {
      pluginId: 'log',
      displayText: '模拟打印',
      params: {},
      mode: 'Log',
    }
    expect(formatTransport(cfg)).toBe('模拟打印')
  })

  it('旧后端无 displayText：按 mode + params 本地格式化（迭代 73 用户语摘要）', () => {
    expect(formatTransport({ mode: 'Log', params: {} })).toBe('模拟打印')
    expect(formatTransport({ mode: 'Tcp', params: { tcpHost: '192.168.1.50', tcpPort: 9100 } })).toBe('网络打印机 192.168.1.50:9100')
    expect(formatTransport({ mode: 'Zebra', params: { zebraKind: 'Usb', zebraUsbName: 'ZDesigner' } })).toBe('Zebra USB（ZDesigner）')
    expect(formatTransport(null)).toBe('')
  })
})

describe('mode ↔ 插件 id 映射', () => {
  it('MODE_TO_PLUGIN_ID 与后端旧配置映射表一致', () => {
    expect(MODE_TO_PLUGIN_ID).toEqual({ Log: 'log', Tcp: 'tcp9100', WindowsDriver: 'winspool', Zebra: 'zebra' })
  })

  it('effectivePluginId：pluginId 优先，旧后端按 mode 映射，无配置回退 log', () => {
    expect(effectivePluginId({ pluginId: 'vendor-ble', params: {}, mode: 'Log' })).toBe('vendor-ble')
    expect(effectivePluginId({ mode: 'Tcp', params: {} })).toBe('tcp9100')
    expect(effectivePluginId({ mode: 'WindowsDriver', params: {} })).toBe('winspool')
    expect(effectivePluginId(null)).toBe('log')
  })

  it('isPluginMode：availablePlugins 非空才为插件模式（旧后端回退内置 4 模式）', () => {
    expect(isPluginMode({ mode: 'Log', params: {}, availablePlugins: [TCP9100_PLUGIN] })).toBe(true)
    expect(isPluginMode({ mode: 'Log', params: {} })).toBe(false)
    expect(isPluginMode({ mode: 'Log', params: {}, availablePlugins: [] })).toBe(false)
    expect(isPluginMode(null)).toBe(false)
  })

  it('isNativePrintMode（迭代 78，§5.4.2）：仅显式 native 为真，缺失 / 非法回退 image 语义', () => {
    expect(isNativePrintMode({ pluginId: 'labelframe-transport-zebra', params: { printMode: 'native' }, mode: 'Log' })).toBe(true)
    expect(isNativePrintMode({ pluginId: 'labelframe-transport-zebra', params: { printMode: 'Native ' }, mode: 'Log' })).toBe(true) // 忽略大小写与空白
    expect(isNativePrintMode({ pluginId: 'labelframe-transport-zebra', params: { printMode: 'image' }, mode: 'Log' })).toBe(false)
    expect(isNativePrintMode({ pluginId: 'labelframe-transport-zebra', params: {}, mode: 'Log' })).toBe(false) // 缺省 = image
    expect(isNativePrintMode({ pluginId: 'labelframe-transport-zebra', params: { printMode: 'garbage' }, mode: 'Log' })).toBe(false) // 非法值回退
    expect(isNativePrintMode(null)).toBe(false)
  })
})

describe('spec 解析（兼容后端两种序列化）', () => {
  it('specOptions：string[] 与 { value, label? }[] 均可解析', () => {
    expect(specOptions(ZEBRA_PLUGIN.parameters[0])).toEqual([
      { value: 'Tcp', label: 'Tcp' },
      { value: 'Usb', label: 'Usb' },
      { value: 'Driver', label: 'Driver' },
    ])
    expect(specOptions(VENDOR_PLUGIN.parameters[0])).toEqual([
      { value: 'fast', label: '快速' },
      { value: 'stable', label: '稳定' },
    ])
    expect(specOptions({ key: 'k', label: 'l', type: 'Select' })).toEqual([])
  })

  it('specDefaultValue：Bool / Int 字符串序列化防御解析，Select 回退第一项', () => {
    expect(specDefaultValue(VENDOR_PLUGIN.parameters[1])).toBe(true) // 'true' 字符串 → true
    expect(specDefaultValue(TCP9100_PLUGIN.parameters[1])).toBe(9100) // '9100' → 9100
    expect(specDefaultValue(TCP9100_PLUGIN.parameters[2])).toBe(10)
    expect(specDefaultValue(ZEBRA_PLUGIN.parameters[0])).toBe('Tcp')
    expect(specDefaultValue(VENDOR_PLUGIN.parameters[0])).toBe('fast')
    expect(specDefaultValue({ key: 'k', label: 'l', type: 'Int' })).toBe(0)
    expect(specDefaultValue({ key: 'k', label: 'l', type: 'Bool' })).toBe(false)
    expect(specDefaultValue({ key: 'k', label: 'l', type: 'String' })).toBe('')
    expect(specDefaultValue({ key: 'k', label: 'l', type: 'String', defaultValue: null })).toBe('') // 后端 defaultValue:null 不显示字面量 "null"
  })

  it('defaultPluginParams：全部参数按 spec 默认值生成', () => {
    expect(defaultPluginParams(TCP9100_PLUGIN)).toEqual({ host: '', port: 9100, timeoutSeconds: 10 })
    expect(defaultPluginParams(ZEBRA_PLUGIN)).toEqual({ kind: 'Tcp', printerName: '' })
  })

  it('pluginParamsFromConfig：从当前配置提取有效键（按 spec 类型转换），未命中用默认值', () => {
    const cfg: TransportConfig = {
      pluginId: 'tcp9100',
      params: { host: '192.168.1.50', port: '9100' }, // port 字符串 → Int
      mode: 'Tcp',
    }
    expect(pluginParamsFromConfig(TCP9100_PLUGIN, cfg)).toEqual({ host: '192.168.1.50', port: 9100, timeoutSeconds: 10 })
    // 旧后端平铺 params 也可提取（如 printerName）
    const oldCfg: TransportConfig = { mode: 'WindowsDriver', params: { printerName: 'ZDesigner' } }
    expect(pluginParamsFromConfig({ id: 'winspool', displayName: 'Windows 驱动', parameters: [{ key: 'printerName', label: '打印机名称', type: 'String', required: true }] }, oldCfg)).toEqual({
      printerName: 'ZDesigner',
    })
  })

  // ── isSpecVisible：条件可见性判定（与后端 spec.IsVisible 口径逐条对齐）──

  it('isSpecVisible：无声明恒可见（兼容旧后端 / 无条件插件）', () => {
    const spec = TCP9100_PLUGIN.parameters[0] // host，无 visibleWhenKey
    expect(isSpecVisible(spec, {})).toBe(true)
    expect(isSpecVisible(spec, { host: 'x' })).toBe(true)
  })

  it('isSpecVisible：条件键取值命中集合即可见（忽略大小写与首尾空白）', () => {
    const spec = { key: 'host', label: '主机', type: 'String' as const, visibleWhenKey: 'kind', visibleWhenValues: ['Tcp'] }
    expect(isSpecVisible(spec, { kind: 'Tcp' })).toBe(true)
    expect(isSpecVisible(spec, { kind: ' tcp ' })).toBe(true)
    expect(isSpecVisible(spec, { kind: 'Driver' })).toBe(false)
    expect(isSpecVisible(spec, { kind: true })).toBe(false) // 非字符串条件值不强转命中
  })

  it('isSpecVisible：条件键缺失 / 空值 / 集合空 = 不可见', () => {
    const spec = { key: 'host', label: '主机', type: 'String' as const, visibleWhenKey: 'kind', visibleWhenValues: ['Tcp'] }
    expect(isSpecVisible(spec, {})).toBe(false)
    expect(isSpecVisible(spec, { kind: '' })).toBe(false)
    expect(isSpecVisible(spec, { kind: '   ' })).toBe(false)
    expect(isSpecVisible({ ...spec, visibleWhenValues: null }, { kind: 'Tcp' })).toBe(false)
  })
})

// ── 非中文界面本地化组合：插件名（displayNameKey）＋ kind 选项 ＋ 关键参数 ＋ 原生指令后缀 ──

const LOG_PLUGIN: TransportPluginInfo = {
  id: 'log',
  displayName: '模拟打印',
  displayNameKey: 'plugin.log.displayName',
  parameters: [],
}

const ZEBRA_OFFICIAL_PLUGIN: TransportPluginInfo = {
  id: 'labelframe-transport-zebra',
  displayName: 'Zebra',
  displayNameKey: 'plugin.labelframe-transport-zebra.displayName',
  parameters: [
    {
      key: 'kind',
      label: '连接类型',
      labelKey: 'plugin.labelframe-transport-zebra.params.kind.label',
      type: 'Select',
      defaultValue: 'Tcp',
      options: [
        { value: 'Tcp', label: 'TCP' },
        { value: 'Usb', label: 'USB（自动发现）', labelKey: 'plugin.labelframe-transport-zebra.params.kind.opt.Usb' },
        { value: 'Driver', label: 'Windows 驱动', labelKey: 'plugin.labelframe-transport-zebra.params.kind.opt.Driver' },
      ],
    },
    { key: 'host', label: '打印机地址 / IP', type: 'String' },
    { key: 'port', label: '端口', type: 'Int', defaultValue: 9100 },
    { key: 'printerName', label: 'Windows 打印机名', type: 'String' },
    { key: 'usbName', label: 'Zebra USB 打印机名', type: 'String' },
    {
      key: 'printMode',
      label: '打印方式',
      type: 'Select',
      defaultValue: 'image',
      options: [
        { value: 'image', label: '图片（默认）', labelKey: 'plugin.labelframe-transport-zebra.params.printMode.opt.image' },
        { value: 'native', label: '原生指令', labelKey: 'plugin.labelframe-transport-zebra.params.printMode.opt.native' },
      ],
    },
  ],
}

const TSPL_OFFICIAL_PLUGIN: TransportPluginInfo = {
  id: 'labelframe-transport-tspl',
  displayName: 'TSPL（TSC 及兼容）',
  displayNameKey: 'plugin.labelframe-transport-tspl.displayName',
  parameters: [
    {
      key: 'kind',
      label: '连接类型',
      type: 'Select',
      defaultValue: 'Tcp',
      options: [
        { value: 'Tcp', label: 'TCP' },
        { value: 'Usb', label: 'USB（免驱动直连）', labelKey: 'plugin.labelframe-transport-tspl.params.kind.opt.Usb' },
      ],
    },
    { key: 'host', label: '打印机地址 / IP', type: 'String' },
    { key: 'port', label: '端口', type: 'Int', defaultValue: 9100 },
    { key: 'usbName', label: 'USB 打印机名', type: 'String' },
    {
      key: 'printMode',
      label: '打印方式',
      type: 'Select',
      defaultValue: 'image',
      options: [
        { value: 'image', label: '图片（默认）', labelKey: 'plugin.labelframe-transport-tspl.params.printMode.opt.image' },
        { value: 'native', label: '原生指令', labelKey: 'plugin.labelframe-transport-tspl.params.printMode.opt.native' },
      ],
    },
  ],
}

describe('formatTransport：非中文界面本地化组合', () => {
  afterEach(() => {
    i18next.changeLanguage('zh-CN')
  })

  it('en：模拟打印徽标走 pluginLabels 词条，不再直出后端中文 displayText', () => {
    i18next.changeLanguage('en')
    const cfg: TransportConfig = {
      pluginId: 'log',
      displayText: '模拟打印',
      params: {},
      mode: 'Log',
      availablePlugins: [LOG_PLUGIN],
    }
    expect(formatTransport(cfg)).toBe('Simulated printing')
  })

  it('en：Zebra TCP → 插件名 + TCP + host:port（语言中立段原样保留）', () => {
    i18next.changeLanguage('en')
    const cfg: TransportConfig = {
      pluginId: 'labelframe-transport-zebra',
      displayText: 'Zebra TCP 192.168.1.50:9100',
      params: { kind: 'Tcp', host: '192.168.1.50', port: 9100, printMode: 'image' },
      mode: 'Log',
      availablePlugins: [ZEBRA_OFFICIAL_PLUGIN],
    }
    expect(formatTransport(cfg)).toBe('Zebra TCP 192.168.1.50:9100')
  })

  it('en：Zebra USB 自动发现走选项词条、显式设备名为「USB {name}」（与后端口径一致）', () => {
    i18next.changeLanguage('en')
    const base = { pluginId: 'labelframe-transport-zebra', mode: 'Log' as const, availablePlugins: [ZEBRA_OFFICIAL_PLUGIN] }
    expect(formatTransport({ ...base, displayText: 'Zebra USB（自动发现）', params: { kind: 'Usb', usbName: '', printMode: 'image' } })).toBe('Zebra USB (auto-discovery)')
    expect(formatTransport({ ...base, displayText: 'Zebra USB DMJ261804448', params: { kind: 'Usb', usbName: 'DMJ261804448', printMode: 'image' } })).toBe('Zebra USB DMJ261804448')
  })

  it('en：Zebra 驱动 → 插件名 + 驱动选项词条 + 打印机名', () => {
    i18next.changeLanguage('en')
    const cfg: TransportConfig = {
      pluginId: 'labelframe-transport-zebra',
      displayText: 'Zebra 驱动 ZDesigner ZD421',
      params: { kind: 'Driver', printerName: 'ZDesigner ZD421', printMode: 'image' },
      mode: 'Log',
      availablePlugins: [ZEBRA_OFFICIAL_PLUGIN],
    }
    expect(formatTransport(cfg)).toBe('Zebra Windows driver ZDesigner ZD421')
  })

  it('en：TSPL USB 显式设备名 + 原生指令后缀（printMode 选项词条）', () => {
    i18next.changeLanguage('en')
    const cfg: TransportConfig = {
      pluginId: 'labelframe-transport-tspl',
      displayText: 'TSPL USB VID_1203（原生指令）',
      params: { kind: 'Usb', usbName: 'VID_1203', printMode: 'native' },
      mode: 'Log',
      availablePlugins: [TSPL_OFFICIAL_PLUGIN],
    }
    expect(formatTransport(cfg)).toBe('TSPL (TSC compatible) USB VID_1203 Native commands')
  })

  it('en：无 displayNameKey 的第三方插件维持后端 displayText（无本地化信息不做更薄组合）', () => {
    i18next.changeLanguage('en')
    const cfg: TransportConfig = {
      pluginId: 'vendor-ble',
      displayText: '厂商蓝牙（快速）',
      params: { mode: 'fast' },
      mode: 'Log',
      availablePlugins: [VENDOR_PLUGIN],
    }
    expect(formatTransport(cfg)).toBe('厂商蓝牙（快速）')
  })

  it('zh：中文界面仍以后端 displayText 为准（零漂移）', () => {
    i18next.changeLanguage('zh-CN')
    const cfg: TransportConfig = {
      pluginId: 'log',
      displayText: '模拟打印',
      params: {},
      mode: 'Log',
      availablePlugins: [LOG_PLUGIN],
    }
    expect(formatTransport(cfg)).toBe('模拟打印')
  })

  it('en：旧后端（无 availablePlugins）维持按 mode 本地格式化', () => {
    i18next.changeLanguage('en')
    expect(formatTransport({ mode: 'Tcp', params: { tcpHost: '192.168.1.50', tcpPort: 9100 } })).toBe('Network printer 192.168.1.50:9100')
  })
})
