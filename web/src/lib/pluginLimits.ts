// 插件包大小上限（与后端 PluginPackageLimits.MaxBytes=64MB 一致
// 预检按 `> 上限` 阻止，恰好 64MB 的包由后端 Kestrel 413 兜底，前端展示现有 HTTP_413 文案）。
// 抽成纯函数便于单测（jsdom 构造 64MB+ 的 File 成本高，组件测试用 mock 小文件 + 本函数覆盖阈值）。

import i18next from '../i18n'

export const PLUGIN_PACKAGE_MAX_BYTES = 64 * 1024 * 1024

/* * 超出上限返回提示文案（key 化，调用期即当时界面语言），未超出返回 null。 */
export function pluginPackageTooLarge(sizeBytes: number): string | null {
  if (!Number.isFinite(sizeBytes) || sizeBytes > PLUGIN_PACKAGE_MAX_BYTES) {
    return i18next.t('api.pluginTooLarge')
  }
  return null
}
