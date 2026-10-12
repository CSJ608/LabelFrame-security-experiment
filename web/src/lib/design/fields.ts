import type { DesignElement } from './types'

/* * 打印字段信息：字段名（键）+ 可选显示名（· 决议 1：显示处一律 `displayName || key` 回退）。 */
export interface FieldInfo {
  key: string
  displayName?: string
}

/**
 * 打印字段自动推导：字段集合 = 「字段填充」元素的字段名按元素顺序去重
 * （图片 / 线 / 容器不算字段）。
 * 显示名（· 决议 1）：同键多元素时取**首个非空**显示名（与去重的首现语义一致，确定性回填）。
 */
export function deriveFieldInfos(elements: readonly DesignElement[]): FieldInfo[] {
  const infos: FieldInfo[] = []
  for (const e of elements) {
    // 图片 / 线 / 容器无填充概念（'mode' in e 收窄）
    if (!('mode' in e)) continue
    if (e.mode !== 'field' || !e.key) continue
    const found = infos.find((f) => f.key === e.key)
    const displayName = e.displayName?.trim()
    if (!found) {
      infos.push(displayName ? { key: e.key, displayName } : { key: e.key })
    } else if (displayName && !found.displayName) {
      found.displayName = displayName
    }
  }
  return infos
}

/** 字段名列表推导（仅键；显示名场景用 deriveFieldInfos）。 */
export function deriveFields(elements: readonly DesignElement[]): string[] {
  return deriveFieldInfos(elements).map((f) => f.key)
}
