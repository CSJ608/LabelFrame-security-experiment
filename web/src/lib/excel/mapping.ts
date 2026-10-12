// Excel 列 → 契约字段键 的映射建议：自动按列名匹配（忽略大小写 / 空白 / 下划线）。

/** 归一化列名 / 键名：小写、去空白与下划线。 */
export function normalizeName(s: string): string {
  return s.trim().toLowerCase().replace(/[\s_-]+/g, '')
}

/* * 参与列映射建议的字段：可直接给键，或带显示名（表头为显示名时可自动匹配）。 */
export type MappingField = string | { key: string; displayName?: string }

/**
 * 为每个 Excel 表头列建议字段键。
 * @param headers Excel 表头（原样）
 * @param fields 契约字段（键 + 可选显示名）；表头为显示名（下载的 Excel 模板第一行）时按显示名匹配
 * @returns 与 headers 等长的数组：匹配到的键或 ''（需手工映射）
 */
export function suggestMapping(headers: readonly string[], fields: readonly MappingField[]): string[] {
  const keyMap = new Map<string, string>()
  for (const f of fields) {
    const key = typeof f === 'string' ? f : f.key
    if (!key) continue
    keyMap.set(normalizeName(key), key)
    if (typeof f !== 'string' && f.displayName) keyMap.set(normalizeName(f.displayName), key)
  }
  return headers.map((h) => keyMap.get(normalizeName(h)) ?? '')
}

/** 是否可提交：至少有一列完成映射（未映射列不参与打印）。 */
export function isMappingComplete(mapping: readonly string[]): boolean {
  return mapping.some((k) => k.length > 0)
}

/** 是否同一键被多列映射（重复映射警告）。 */
export function findDuplicateKeys(mapping: readonly string[]): string[] {
  const seen = new Map<string, number>()
  for (const k of mapping) {
    if (!k) continue
    seen.set(k, (seen.get(k) ?? 0) + 1)
  }
  return [...seen.entries()].filter(([, n]) => n > 1).map(([k]) => k)
}

/** 按映射把一行单元格拼成标签数据（未映射的列忽略）。 */
export function rowToData(headers: readonly string[], row: readonly string[], mapping: readonly string[]): Record<string, string> {
  const data: Record<string, string> = {}
  mapping.forEach((key, i) => {
    if (key && i < headers.length) data[key] = row[i] ?? ''
  })
  return data
}
