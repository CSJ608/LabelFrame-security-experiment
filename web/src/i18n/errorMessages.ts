// LF_* 错误码展示端翻译（· ， ③ /）：
// 后端吐结构化错误（码 + 模板参数 params + 中文兜底 message），展示端持码表（errorCodes 命名空间）
// 翻译——已知码用码表模板渲染（{{key}} 插值，键名与后端 params 一致），未知码回退后端中文 message。
//
// 渲染规则（详见 DESIGN §5 /）：
// - zh-CN：直接采用后端 message——中文权威在后端（码表 zh 份是注册表镜像，供覆盖断言与后续端参考），
//   避免码表镜像在多消息变体同码时失真；
// - en：已知码用 en 码表模板插值；模板所需参数不全（后端未带 params 的消息变体）或未知码
//   回退后端中文 message——保证任何输入都不出现裸 {{key}} 插值残缺。
// 前端自造码（TIMEOUT / NETWORK_ERROR / HTTP_xxx）不在码表内，自然走回退（维持现状中文文案，
//   归后续页面迁移迭代处理）。

import i18next, { currentLocale } from './index'

/** 错误码码表命名空间（locales/<locale>/errorCodes.json，码 → 文案模板）。 */
export const ERROR_CODES_NS = 'errorCodes'

/** i18next 插值槽：{{key}}（键名与后端 params 的 {key} 占位符同名对接）。 */
const PLACEHOLDER = /\{\{\s*([A-Za-z0-9_]+)\s*\}\}/g

function rawTemplate(code: string, lng: 'en' | 'zh-CN'): string | undefined {
  const direct = i18next.getResource(lng, ERROR_CODES_NS, code)
  if (typeof direct === 'string') return direct
  const fallback = i18next.getResource('zh-CN', ERROR_CODES_NS, code)
  return typeof fallback === 'string' ? fallback : undefined
}

/**
 * 解析错误展示文案：已知码按码表渲染（含 params 插值），否则回退后端 message。
 * @param code 后端 ErrorView.code（或前端自造码）
 * @param params 后端 ErrorView.params（扁平字符串键值对象）
 * @param backendMessage 后端中文 message（或前端生成的回退文案）
 */
export function resolveApiErrorMessage(
  code: string | null | undefined,
  params: Record<string, string> | null | undefined,
  backendMessage: string,
): string {
  if (!code || currentLocale() === 'zh-CN') return backendMessage
  const template = rawTemplate(code, 'en')
  if (template === undefined) return backendMessage

  const needed = [...template.matchAll(PLACEHOLDER)].map((m) => m[1])
  if (needed.length > 0 && (!params || needed.some((key) => typeof params[key] !== 'string'))) {
    // 模板带参但后端未提供（多消息变体同码共用代表性模板）：回退后端原文，不输出插值残缺
    return backendMessage
  }
  return template.replace(PLACEHOLDER, (_, key: string) => params![key])
}
