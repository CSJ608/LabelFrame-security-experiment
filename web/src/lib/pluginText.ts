import i18next from 'i18next'

/**
 * 插件自描述文案展示端翻译（决策 #192，#366）：有键且 pluginLabels 域有词条 → 当前语言词条；
 * 无键 / 无词条（第三方插件、语言包未覆盖）→ 原文回退。渲染期调用，语言切换随宿主组件
 * useTranslation 订阅重渲染刷新。
 */
export function pluginText(key: string | null | undefined, fallback: string | null | undefined): string {
  if (key && i18next.exists(key, { ns: 'pluginLabels' })) {
    return i18next.t(key, { ns: 'pluginLabels' })
  }
  return fallback ?? ''
}

/** 插件级展示名（连接区单选 displayName）映射；官方封闭集合外回退原文。 */
export function pluginDisplayName(pluginId: string, displayName: string): string {
  return pluginText(`plugin.${pluginId}.displayName`, displayName)
}

/** 插件级描述映射；官方封闭集合外回退原文。 */
export function pluginDescription(pluginId: string, description: string | null | undefined): string {
  return pluginText(`plugin.${pluginId}.desc`, description)
}

/** 插件管理页包名（manifest name）映射；官方封闭集合外回退原文。 */
export function pluginPackageName(pluginId: string | null | undefined, name: string | null | undefined): string {
  if (!pluginId) return name ?? ''
  return pluginText(`plugin.${pluginId}.name`, name)
}
