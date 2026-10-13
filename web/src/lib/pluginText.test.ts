// @vitest-environment jsdom
// 插件自描述文案展示端翻译防线（决策 #192，#366/#368）：
// ① zh 词条 = 官方插件 spec 原文逐字一致（AC 词条层锚点——fixture 镜像 dotnet 插件源码的键与中文原文，
//    漂移即红；#368 起插件级键由 ITransportPlugin DIM 声明，spec 键与插件级键统一由插件侧维护）；
// ② pluginText 词条命中 / 回退语义与包名映射（pluginPackageName）回退语义。

import { describe, expect, it } from 'vitest'
import i18next from '../i18n'
import { pluginPackageName, pluginText } from './pluginText'
import zhCNPluginLabels from '../i18n/locales/zh-CN/pluginLabels.json'
import enPluginLabels from '../i18n/locales/en/pluginLabels.json'

/** 官方插件 spec 镜像（dotnet 插件源码的 LabelKey → 中文原文；键值漂移此处即红）。 */
const OFFICIAL_SPEC_ZH: Record<string, string> = {
  'plugin.log.displayName': '模拟打印',
  'plugin.log.desc': '模拟打印：不连接真实打印机，作业按打印成功处理；图片模式的标签图片会保存到本机，便于先确认打印效果。',
  'plugin.tcp9100.displayName': '网口打印机（TCP 9100）',
  'plugin.tcp9100.desc': 'TCP 9100 网络打印机（Zebra 等）：连接打印机 IP 的 9100 端口发送指令，状态查询用 ~HS。',
  'plugin.tcp9100.params.host.label': '打印机地址 / IP',
  'plugin.tcp9100.params.host.hint': '如 192.168.1.50',
  'plugin.tcp9100.params.port.label': '端口',
  'plugin.tcp9100.params.port.hint': '默认 9100',
  'plugin.tcp9100.params.timeoutSeconds.label': '超时（秒）',
  'plugin.winspool.displayName': 'Windows 驱动',
  'plugin.winspool.desc': 'Windows 驱动（USB / 已安装打印机）：通过 winspool 以 RAW 方式发送指令，无需打印机 IP。',
  'plugin.winspool.params.printerName.label': 'Windows 打印机名',
  'plugin.winspool.params.printerName.hint': '如 ZDesigner ZD421-203dpi ZPL',
  'plugin.labelframe-transport-zebra.displayName': 'Zebra',
  'plugin.labelframe-transport-zebra.desc': 'Zebra 官方 Link-OS SDK：TCP / USB（自动发现）/ Windows 驱动统一连接与打印机状态（Win10+，官方插件）。原生指令模式下二维码（QR）数据不支持中文（仅 ASCII），中文请用文本框（图片模式）。',
  'plugin.labelframe-transport-zebra.name': 'Zebra 品牌传输（官方）',
  'plugin.labelframe-transport-zebra.params.kind.label': '连接类型',
  'plugin.labelframe-transport-zebra.params.kind.opt.Usb': 'USB（自动发现）',
  'plugin.labelframe-transport-zebra.params.kind.opt.Driver': 'Windows 驱动',
  'plugin.labelframe-transport-zebra.params.host.label': '打印机地址 / IP',
  'plugin.labelframe-transport-zebra.params.host.hint': '如 192.168.1.50',
  'plugin.labelframe-transport-zebra.params.port.label': '端口',
  'plugin.labelframe-transport-zebra.params.printerName.label': 'Windows 打印机名',
  'plugin.labelframe-transport-zebra.params.printerName.hint': '如 ZDesigner ZD421-203dpi ZPL',
  'plugin.labelframe-transport-zebra.params.usbName.label': 'Zebra USB 打印机名',
  'plugin.labelframe-transport-zebra.params.usbName.hint': '留空 = 自动发现第一台',
  'plugin.labelframe-transport-zebra.params.printMode.label': '打印方式',
  'plugin.labelframe-transport-zebra.params.printMode.opt.image': '图片（默认）',
  'plugin.labelframe-transport-zebra.params.printMode.opt.native': '原生指令',
  'plugin.labelframe-transport-zebra.params.printMode.hint': '原生指令模式无预览，效果以真机为准；该模式下二维码（QR）数据不支持中文（仅 ASCII），中文请用文本框（图片模式）',
}

/** 展平嵌套 JSON 为 `a.b.c` 全量 key 路径映射。 */
function flatten(obj: unknown, prefix = '', out: Record<string, string> = {}): Record<string, string> {
  if (obj === null || typeof obj !== 'object') return out
  for (const [k, v] of Object.entries(obj as Record<string, unknown>)) {
    const path = prefix ? `${prefix}.${k}` : k
    if (v !== null && typeof v === 'object') flatten(v, path, out)
    else out[path] = String(v)
  }
  return out
}

describe('pluginLabels zh 词条与官方插件 spec 原文逐字一致（AC-02 词条层锚点）', () => {
  it('官方插件全部键的 zh 词条 = dotnet 侧中文原文', () => {
    const zhFlat = flatten(zhCNPluginLabels)
    for (const [key, expected] of Object.entries(OFFICIAL_SPEC_ZH)) {
      expect(zhFlat[key], `zh 词条 ${key}`).toBe(expected)
    }
  })

  it('zh / en 词条键集一致（官方集合全覆盖，无死词条）', () => {
    const zhKeys = Object.keys(flatten(zhCNPluginLabels)).sort()
    const enKeys = Object.keys(flatten(enPluginLabels)).sort()
    expect(zhKeys).toEqual(enKeys)
    expect(zhKeys.length).toBeGreaterThanOrEqual(Object.keys(OFFICIAL_SPEC_ZH).length)
  })
})

describe('pluginText 翻译与回退语义', () => {
  it('zh 态：带键词条命中返回 zh 词条（= 原文，行为与直出等价）', () => {
    expect(pluginText('plugin.tcp9100.params.port.label', '端口')).toBe('端口')
  })

  it('EN 态：带键词条命中返回英文；无键 / 无词条回退原文', async () => {
    await i18next.changeLanguage('en')
    expect(pluginText('plugin.tcp9100.params.port.label', '端口')).toBe('Port')
    expect(pluginText(undefined, '厂商原文')).toBe('厂商原文')
    expect(pluginText('plugin.unknown-plugin.params.x.label', '未知键原文')).toBe('未知键原文')
    await i18next.changeLanguage('zh-CN')
  })

  it('插件级词条（接口 DIM 键）与包名映射：官方命中、第三方回退', async () => {
    expect(pluginText('plugin.tcp9100.displayName', '网口打印机（TCP 9100）')).toBe('网口打印机（TCP 9100）')
    await i18next.changeLanguage('en')
    expect(pluginText('plugin.tcp9100.displayName', '网口打印机（TCP 9100）')).toBe('Network printer (TCP 9100)')
    expect(pluginText('plugin.tcp9100.desc', 'TCP 9100 网络打印机…')).toContain('TCP 9100 network printer')
    expect(pluginText('plugin.third-party.displayName', '第三方名')).toBe('第三方名')
    expect(pluginPackageName('labelframe-transport-zebra', 'Zebra 品牌传输（官方）')).toBe('Zebra Brand Transport (official)')
    expect(pluginPackageName(undefined, '任意包名')).toBe('任意包名')
    await i18next.changeLanguage('zh-CN')
  })
})
