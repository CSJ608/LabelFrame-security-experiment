// 设计器内部模型 ↔ 后端模板契约（layout.elements）双向转换。
// 后端元素 JSON 由 LabelElementJsonConverter 判别 type（text/barcode/qrcode/image/line/region），
// 字段 camelCase；写方向严格镜像 converter 的省略规则（padding/border 仅 >0 时写等）。

import type { DesignElement, ElementType } from './types'
import { uid } from './types'
import type { FieldInfo } from './fields'
import { r2 } from './geometry'

/** 后端版式元素 JSON（layout.elements 项）。 */
export interface BackendElement {
  type: 'text' | 'barcode' | 'qrcode' | 'image' | 'line' | 'region'
  xMm: number
  yMm: number
  paddingMm?: number
  /* * 双边内边距（替代单值 max 近似；旧模板无此字段时用 paddingMm 兜底） */
  paddingH?: number
  paddingV?: number
  borderMm?: number
  regionId?: string
  regionHAlign?: string
  regionVAlign?: string
  sourceKey?: string
  literal?: string
  /* * 字段填充模式的预览值（仅 field 模式且 key 非空写；固定值模式不写） */
  previewValue?: string
  fontName?: string
  fontHeightMm?: number
  fontWidthMm?: number
  widthMm?: number
  heightMm?: number
  textAlign?: string
  verticalAlign?: string
  /* * 前端画布字体（Skia 图片打印用；矢量 ZPL 仍由 fontName 决定） */
  fontFamily?: string
  /* * 加粗（true 才写；ZPL 用字体变体、Skia 用 fontStyle bold） */
  bold?: boolean
  /* * 自动换行（true 才写） */
  wrap?: boolean
  /* * 行距系数（!= 1.2 才写） */
  lineHeight?: number
  /* * 溢出处理（shrink 缩小 / overflow 隐藏裁剪；!= shrink 才写） */
  fitMode?: 'shrink' | 'overflow'
  moduleWidth?: number
  /* * 条码底部文字（false 才写，默认 true） */
  displayValue?: boolean
  sizeMm?: number
  /* * 二维码纠错级别（!= M 才写） */
  qrEcc?: 'L' | 'M' | 'Q' | 'H'
  /* * 二维码静区模块数（!= 2 才写） */
  qrMargin?: number
  x2Mm?: number
  y2Mm?: number
  thicknessMm?: number
  id?: string
}

/** 契约字段 JSON。 */
export interface BackendField {
  key: string
  displayName: string
  isRequired: boolean
  type: 'Text' | 'Number'
  pattern?: string
}

/** 契约 JSON。 */
export interface BackendContract {
  name: string
  version: string
  fields: BackendField[]
}

/** 版式 JSON。 */
export interface BackendLayout {
  name: string
  contractName: string
  contractVersion: string
  widthMm: number
  heightMm: number
  elements: BackendElement[]
}

const backType = (t: ElementType): BackendElement['type'] =>
  t === 'Text' ? 'text' : t === 'Barcode' ? 'barcode' : t === 'QrCode' ? 'qrcode' : t === 'Rect' ? 'region' : t === 'Image' ? 'image' : t === 'Line' ? 'line' : 'region'

/** 内部元素 → 后端元素 JSON（与 LabelElementJsonConverter.Write 输出一致）。 */
export function toBackendElement(e: DesignElement): BackendElement {
  const base: BackendElement = { type: backType(e.type), xMm: r2(e.x), yMm: r2(e.y) }
  const padH = 'paddingH' in e ? (e.paddingH ?? 0) : 0
  const padV = 'paddingV' in e ? (e.paddingV ?? 0) : 0
  const pad = Math.max(padH, padV)
  // 双边内边距双值写出；paddingMm 保留兼容（旧模板/后端读回兜底）
  if (padH > 0) base.paddingH = r2(padH)
  if (padV > 0) base.paddingV = r2(padV)
  if (pad > 0) base.paddingMm = r2(pad)
  if ((e.border ?? 0) > 0) base.borderMm = r2(e.border ?? 0)
  if (e.regionId) base.regionId = e.regionId
  if (e.regionHAlign) base.regionHAlign = e.regionHAlign
  if (e.regionVAlign) base.regionVAlign = e.regionVAlign

  switch (e.type) {
    case 'Text':
      base.sourceKey = e.key
      if (e.mode === 'literal' && e.text) base.literal = e.text
      // 边界 A：字段填充且 key 非空才写预览值（未绑定字段的预览值无意义，避免读回时模式推断改变）
      if (e.mode === 'field' && e.key && e.text) base.previewValue = e.text
      base.fontName = '0' // ZPL 字体标识（前端字体仅影响画布预览，打印字体由宿主配置）
      base.fontHeightMm = r2(e.fontH)
      base.fontWidthMm = r2(e.fontW)
      if (e.w > 0) base.widthMm = r2(e.w)
      if (e.h > 0) base.heightMm = r2(e.h)
      if (e.align !== 'Left') base.textAlign = e.align
      if (e.valign && e.valign !== 'middle') base.verticalAlign = e.valign === 'top' ? 'Top' : 'Bottom'
      // 文本排版字段（非默认才写）
      if (e.fontFamily && e.fontFamily !== 'Microsoft YaHei') base.fontFamily = e.fontFamily
      if (e.bold) base.bold = true
      if (e.wrap) base.wrap = true
      if (e.lineHeight && e.lineHeight !== 1.2) base.lineHeight = r2(e.lineHeight)
      if (e.fitMode && e.fitMode !== 'shrink') base.fitMode = e.fitMode
      break
    case 'Barcode':
      base.sourceKey = e.key
      if (e.mode === 'literal' && e.text) base.literal = e.text
      if (e.mode === 'field' && e.key && e.text) base.previewValue = e.text
      base.heightMm = r2(e.h)
      base.moduleWidth = Math.max(1, Math.round(e.moduleWidth))
      if (e.displayValue === false) base.displayValue = false
      break
    case 'QrCode':
      base.sourceKey = e.key
      if (e.mode === 'literal' && e.text) base.literal = e.text
      if (e.mode === 'field' && e.key && e.text) base.previewValue = e.text
      base.sizeMm = r2(Math.max(e.w, e.h))
      if (e.qrEcc && e.qrEcc !== 'M') base.qrEcc = e.qrEcc
      if (e.qrMargin !== undefined && e.qrMargin !== 2) base.qrMargin = e.qrMargin
      break
    case 'Image':
      base.sourceKey = e.key
      base.widthMm = r2(e.w)
      base.heightMm = r2(e.h)
      break
    case 'Line':
      base.x2Mm = r2(e.x + e.w)
      base.y2Mm = r2(e.y + e.h)
      base.thicknessMm = r2(e.thickness)
      break
    case 'Rect':
    case 'Region':
      base.id = e.type === 'Region' ? e.containerId : 'r' + e.id.slice(1)
      base.widthMm = r2(e.w)
      base.heightMm = r2(e.h)
      break
  }
  return base
}

/**
 * 后端元素 JSON 数组 → 内部元素。
 * region 判别：有其它元素锚定（regionId 指向）→ 容器；否则 → 矩形（用户画的镂空矩形）。
 */
export function fromBackendElements(list: readonly BackendElement[]): DesignElement[] {
  const regionRefs = new Set<string>()
  for (const j of list) if (j.regionId) regionRefs.add(j.regionId)
  return list.map((j) => {
    const base = {
      id: uid(),
      x: j.xMm ?? 0,
      y: j.yMm ?? 0,
      border: j.borderMm ?? 0,
      paddingH: j.paddingH ?? j.paddingMm ?? 0,
      paddingV: j.paddingV ?? j.paddingMm ?? 0,
      regionId: j.regionId,
      regionHAlign: j.regionHAlign,
      regionVAlign: j.regionVAlign,
    }
    switch (j.type) {
      case 'text': {
        const literal = j.literal ?? ''
        const key = j.sourceKey ?? ''
        const mode = literal ? 'literal' : key ? 'field' : 'literal'
        const fontH = j.fontHeightMm ?? 5
        const pad = j.paddingMm ?? 0
        return {
          ...base,
          type: 'Text',
          w: j.widthMm && j.widthMm > 0 ? j.widthMm : 40,
          h: j.heightMm && j.heightMm > 0 ? j.heightMm : Math.max(fontH + pad * 2, 10),
          fontH,
          fontW: j.fontWidthMm ?? fontH,
          fontFamily: j.fontFamily ?? 'Microsoft YaHei',
          bold: j.bold ?? false,
          wrap: j.wrap ?? false,
          lineHeight: j.lineHeight ?? 1.2,
          valign: (j.verticalAlign?.toLowerCase() as 'top' | 'middle' | 'bottom') || 'middle',
          mode,
          key: mode === 'field' ? key : '',
          text: literal || (j.previewValue ?? ''), // field 模式读回预览值
          align: (j.textAlign as 'Left' | 'Center' | 'Right') || 'Left',
          fitMode: (j.fitMode as 'shrink' | 'overflow') ?? 'shrink',
        }
      }
      case 'barcode': {
        const literal = j.literal ?? ''
        const key = j.sourceKey ?? ''
        const mode = literal ? 'literal' : key ? 'field' : 'literal'
        return {
          ...base,
          type: 'Barcode',
          w: 50,
          h: j.heightMm ?? 20,
          mode,
          key: mode === 'field' ? key : '',
          text: literal || (j.previewValue ?? ''),
          barcodeFormat: 'CODE128',
          displayValue: j.displayValue ?? true,
          moduleWidth: j.moduleWidth ?? 2,
        }
      }
      case 'qrcode': {
        const literal = j.literal ?? ''
        const key = j.sourceKey ?? ''
        const mode = literal ? 'literal' : key ? 'field' : 'literal'
        const size = j.sizeMm ?? 20
        return {
          ...base,
          type: 'QrCode',
          w: size,
          h: size,
          mode,
          key: mode === 'field' ? key : '',
          text: literal || (j.previewValue ?? ''),
          qrEcc: (j.qrEcc as 'L' | 'M' | 'Q' | 'H') ?? 'M',
          qrMargin: j.qrMargin ?? 2,
        }
      }
      case 'image':
        return { ...base, type: 'Image', w: j.widthMm ?? 20, h: j.heightMm ?? 20, key: j.sourceKey ?? '' }
      case 'line':
        return {
          ...base,
          type: 'Line',
          w: Math.abs((j.x2Mm ?? j.xMm ?? 0) - (j.xMm ?? 0)),
          h: Math.abs((j.y2Mm ?? j.yMm ?? 0) - (j.yMm ?? 0)),
          thickness: j.thicknessMm ?? 0.5,
        }
      case 'region': {
        const isContainer = !!j.id && regionRefs.has(j.id)
        return isContainer
          ? { ...base, type: 'Region', w: j.widthMm ?? 60, h: j.heightMm ?? 30, containerId: j.id || 'c' + Math.random().toString(36).slice(2, 8) }
          : { ...base, type: 'Rect', w: j.widthMm ?? 40, h: j.heightMm ?? 20 }
      }
    }
  })
}

/**
 * 字段推导结果 → 契约 fields（非必填，类型 Text）。
 * 显示名（· 决议 1）：可选，空则回退键（displayName 字段契约本就存在，非格式变更）。
 */
export function toContractFields(fields: readonly FieldInfo[]): BackendField[] {
  return fields.map((f) => ({ key: f.key, displayName: f.displayName?.trim() || f.key, isRequired: false, type: 'Text' as const }))
}

/** 内部状态 → 契约（POST /api/templates 用）。 */
export function toContract(name: string, version: string, fields: readonly FieldInfo[]): BackendContract {
  return { name, version, fields: toContractFields(fields) }
}

/**
 * 契约 fields 显示名 → 内部元素回填（按字段名匹配；· 决议 1）。
 * 版式元素 JSON 无显示名字段（契约格式不变），保存路径显示名只进契约 fields——
 * 加载模板时按 sourceKey 匹配回填到元素，保证设计器往返（导出 / 导入）不丢显示名。
 * 存量模板 displayName = key（旧口径）时不回填（元素保持无显示名，显示处回退键，行为不变）。
 */
export function applyContractDisplayNames(elements: readonly DesignElement[], fields: readonly BackendField[]): DesignElement[] {
  const names = new Map<string, string>()
  for (const f of fields) {
    const dn = f.displayName?.trim()
    // 与键相同 = 存量旧口径（displayName 取 Key），视为未填
    if (f.key && dn && dn !== f.key) names.set(f.key, dn)
  }
  if (names.size === 0) return [...elements]
  return elements.map((e) => {
    if (!('mode' in e) || e.mode !== 'field' || !e.key) return e
    const dn = names.get(e.key)
    return dn && dn !== e.displayName ? ({ ...e, displayName: dn } as DesignElement) : e
  })
}

/** 内部状态 → 版式（POST /api/templates 用）。 */
export function toLayout(name: string, contractName: string, contractVersion: string, paperW: number, paperH: number, elements: readonly DesignElement[]): BackendLayout {
  return { name, contractName, contractVersion, widthMm: r2(paperW), heightMm: r2(paperH), elements: elements.map(toBackendElement) }
}
