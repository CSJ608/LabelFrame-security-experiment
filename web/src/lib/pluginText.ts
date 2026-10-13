import i18next from 'i18next'

/**
 * 插件自描述文案展示端翻译（决策 #192，#366/#368）：有键且 pluginLabels 域有词条 → 当前语言词条；
 * 无键 / 无词条（第三方插件、语言包未覆盖）→ 原文回退。渲染期调用，语言切换随宿主组件
 * useTranslation 订阅重渲染刷新。键统一由插件侧声明（spec labelKey/hintKey 与接口 DIM
 * displayNameKey/descriptionKey），本模块不拼键。
 */
export function pluginText(key: string | null | undefined, fallback: string | null | undefined): string {
  if (key && i18next.exists(key, { ns: 'pluginLabels' })) {
    return i18next.t(key, { ns: 'pluginLabels' })
  }
  return fallback ?? ''
}

/**
 * 插件管理页包名（manifest name）映射。包元数据层暂无键声明通道（manifest nameKey
 * 随未来「插件包内嵌词条」机制整体设计，见 #368 范围外），此函数按 pluginId 查词条、
 * 未命中回退原文。
 */
export function pluginPackageName(pluginId: string | null | undefined, name: string | null | undefined): string {
  if (!pluginId) return name ?? ''
  return pluginText(`plugin.${pluginId}.name`, name)
}
