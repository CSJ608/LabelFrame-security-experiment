// 打印机状态消息展示端翻译：后端 GetStatusAsync 消息为中文（后端口径），非中文界面按已知
// 消息映射为当前语言词条；未命中（第三方插件 / 后端新增文案）回退原文，不阻塞状态展示。
// 模板正则与后端消息文案逐字对齐——后端改文案须同步此处与两语言词条（防漂移测试锚定）。

import i18next from '../i18n'

/** 静态消息精确映射（后端全量静态文案）。 */
const EXACT_KEYS: Record<string, string> = {
  '日志模拟在线。': 'printerStatus.logOnline',
  '打印头未闭合，请合上打印头。': 'printerStatus.headOpen',
  '卡纸，请开盖清理后重试。': 'printerStatus.paperJam',
  '缺纸，请装纸。': 'printerStatus.paperOut',
  '色带已用尽，请更换色带。': 'printerStatus.ribbonOut',
  '打印机报其他错误（0x80），请检查打印机状态。': 'printerStatus.otherError',
  '已暂停，按打印机的暂停键恢复。': 'printerStatus.paused',
  '驱动模式无法读回打印机状态（默认在线）。': 'printerStatus.driverNoStatus',
  '当前传输不支持状态查询。': 'printerStatus.unsupported',
  '已连上打印机，但未返回状态响应（缺纸 / 耗材用尽 / 打印头打开等状态下打印机可能不响应状态查询）。': 'printerStatus.emptyResponse',
}

/** 参数化消息模板（命名捕获组 → 词条插值变量）。 */
const TEMPLATE_RULES: { pattern: RegExp; key: string }[] = [
  { pattern: /^状态查询超时（(?<sec>[^）]+?) 秒无响应，(?<target>.+)）——打印机可能离线、缺纸或打印头打开。$/, key: 'printerStatus.timeout' },
  { pattern: /^连接失败：(?<reason>.+)$/, key: 'printerStatus.connectFailed' },
  { pattern: /^与打印机通讯失败：(?<reason>.+)$/, key: 'printerStatus.ioFailed' },
  { pattern: /^TSPL 打印机状态无应答（(?<target>.+)）。$/, key: 'printerStatus.tsplNoResponse' },
  { pattern: /^读取 TSPL 打印机状态失败（(?<target>.+)）：(?<reason>.+)$/, key: 'printerStatus.tsplReadFailed' },
  { pattern: /^读取 Zebra 打印机状态失败（(?<target>.+)）：(?<reason>.+)$/, key: 'printerStatus.zebraReadFailed' },
  { pattern: /^状态响应不完整，无法解析（首行仅 (?<fields>\d+) 个字段，期望至少 3 个）。$/, key: 'printerStatus.truncated' },
]

/** 状态消息按当前语言展示：精确映射 → 模板匹配 → 原文回退；空值透传（调用方走「无补充信息」词条）。 */
export function printerStatusMessage(raw: string | null | undefined): string | null {
  if (!raw) return raw ?? null
  const text = raw.trim()
  if (text === '') return raw
  const exact = EXACT_KEYS[text]
  if (exact && i18next.exists(exact)) return i18next.t(exact)
  for (const rule of TEMPLATE_RULES) {
    const m = text.match(rule.pattern)
    if (m?.groups && i18next.exists(rule.key)) {
      return i18next.t(rule.key, { ...m.groups, interpolation: { escapeValue: false } })
    }
  }
  return raw
}
