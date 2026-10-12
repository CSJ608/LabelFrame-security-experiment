import type { DesignElement } from './types'
import { uid } from './types'
import i18next from '../../i18n'

/** labelframe-web-design 交换格式（与原型导出格式一致）。 */
export interface DesignFile {
  format: 'labelframe-web-design'
  version: number
  paperW: number
  paperH: number
  elements: DesignElement[]
}

export const DESIGN_FORMAT = 'labelframe-web-design'

/** 导出设计 JSON（pretty）。 */
export function exportDesign(paperW: number, paperH: number, elements: readonly DesignElement[]): string {
  const payload: DesignFile = { format: DESIGN_FORMAT, version: 1, paperW, paperH, elements: [...elements] }
  return JSON.stringify(payload, null, 2)
}

/**
 * 解析设计 JSON（Ctrl+Shift+V 导入）。
 * 校验格式与 elements 结构；元素重新生成 id 避免与现有冲突。
 */
export function parseDesign(text: string): { paperW: number; paperH: number; elements: DesignElement[] } {
  let data: unknown
  try {
    data = JSON.parse(text)
  } catch {
    // 错误消息 key 化（抛出期即当时界面语言）
    throw new Error(i18next.t('designer:importError.jsonParse'))
  }
  const d = data as Partial<DesignFile>
  if (d.format !== DESIGN_FORMAT || !Array.isArray(d.elements)) {
    throw new Error(i18next.t('designer:importError.badFormat'))
  }
  const paperW = typeof d.paperW === 'number' && d.paperW > 0 ? d.paperW : 100
  const paperH = typeof d.paperH === 'number' && d.paperH > 0 ? d.paperH : 60
  const elements = d.elements.map((e) => ({ ...e, id: uid() }))
  if (!elements.every(isDesignElement)) {
    throw new Error(i18next.t('designer:importError.badElements'))
  }
  return { paperW, paperH, elements }
}

function isDesignElement(e: unknown): e is DesignElement {
  if (typeof e !== 'object' || e === null) return false
  const o = e as { type?: unknown; x?: unknown; y?: unknown; w?: unknown; h?: unknown }
  const types = ['Text', 'Barcode', 'QrCode', 'Rect', 'Image', 'Line', 'Region']
  return (
    typeof o.type === 'string' &&
    types.includes(o.type) &&
    typeof o.x === 'number' &&
    typeof o.y === 'number' &&
    typeof o.w === 'number' &&
    typeof o.h === 'number'
  )
}
