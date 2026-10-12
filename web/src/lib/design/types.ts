// 设计器内部元素模型（labelframe-web-design 格式，与原型一致）
// 文案 key 化（designer 域）——默认元素文本「文本」属 UI 层数据性默认值
// 跟随当前界面语言（待决议-1 建议项：en 态新建文本元素默认占位英文，存量模板存储值不受影响）；
// typeLabel / layerLabel / elementContent 在调用点（组件渲染期）求值，语言切换后随重渲染更新。

import i18next from '../../i18n'

export type ElementType = 'Text' | 'Barcode' | 'QrCode' | 'Rect' | 'Image' | 'Line' | 'Region'

export type ContentMode = 'literal' | 'field'

interface ElementBase {
  id: string
  type: ElementType
  /** 左上角 X（mm，相对标签内容区） */
  x: number
  /** 左上角 Y（mm，相对标签内容区） */
  y: number
  /** 宽（mm） */
  w: number
  /** 高（mm） */
  h: number
  /** 边框线宽（mm，0 = 无边框） */
  border: number
  /** 锚定的容器 id（拖入容器自动建立） */
  regionId?: string
  /** 区域内对齐（旧模板保留，前端不做编辑 UI） */
  regionHAlign?: string
  regionVAlign?: string
}

export interface TextElement extends ElementBase {
  type: 'Text'
  fontH: number
  fontW: number
  fontFamily: string
  /* * 加粗（小字号打印不清晰，加粗提高可读性；默认 false 不写契约字段） */
  bold: boolean
  /** 自动换行 */
  wrap: boolean
  lineHeight: number
  /** 垂直对齐：顶端 / 居中 / 底部 */
  valign: 'top' | 'middle' | 'bottom'
  mode: ContentMode
  /** 字段填充的字段名（打印数据按此匹配） */
  key: string
  /* * 打印字段显示名（可选，· 决议 1：打印页字段标签回退 key） */
  displayName?: string
  /** 固定值内容 / 字段填充的预览值（仅画布显示） */
  text: string
  /** 水平对齐 */
  align: 'Left' | 'Center' | 'Right'
  paddingH: number
  paddingV: number
  /** 单行溢出：缩小适应 / 隐藏 */
  fitMode: 'shrink' | 'overflow'
}

export interface BarcodeElement extends ElementBase {
  type: 'Barcode'
  mode: ContentMode
  key: string
  /* * 打印字段显示名（可选，· 决议 1：打印页字段标签回退 key） */
  displayName?: string
  text: string
  paddingH: number
  paddingV: number
  barcodeFormat: string
  /** 底部显示文字 */
  displayValue: boolean
  moduleWidth: number
}

export interface QrCodeElement extends ElementBase {
  type: 'QrCode'
  mode: ContentMode
  key: string
  /* * 打印字段显示名（可选，· 决议 1：打印页字段标签回退 key） */
  displayName?: string
  text: string
  paddingH: number
  paddingV: number
  qrEcc: 'L' | 'M' | 'Q' | 'H'
  qrMargin: number
}

export interface RectElement extends ElementBase {
  type: 'Rect'
}

export interface ImageElement extends ElementBase {
  type: 'Image'
  /** 图片资源键（模板包 images 键，画布按资源表渲染）。 */
  key: string
}

/* * 图片资源（设计器内存态，·）：dataUrl 供画布解码；width / height 为入库图片像素尺寸（属性面板信息行）
 * sizeBytes 为入库（压缩后）字节量——上传预检总量上限与保存体积按此计（ 起超 1MB 走压缩管道
 *  入库字节 ≠ 用户原文件字节；模板回填按 base64 长度折算）。 */
export interface ImageAsset {
  dataUrl: string
  width: number
  height: number
  sizeBytes: number
}

export interface LineElement extends ElementBase {
  type: 'Line'
  thickness: number
}

export interface RegionElement extends ElementBase {
  type: 'Region'
  /** 容器标识（保存为后端 region.id） */
  containerId: string
}

export type DesignElement =
  | TextElement
  | BarcodeElement
  | QrCodeElement
  | RectElement
  | ImageElement
  | LineElement
  | RegionElement

let idCounter = 0

/** 生成唯一元素 id（与原型一致：随机短串）。 */
export function uid(): string {
  idCounter += 1
  return 'e' + Date.now().toString(36).slice(-6) + idCounter.toString(36)
}

/** 新建元素的默认值（与原型 defaultElement 一致）。 */
export function defaultElement(type: 'Text', id?: string): TextElement
export function defaultElement(type: 'Barcode', id?: string): BarcodeElement
export function defaultElement(type: 'QrCode', id?: string): QrCodeElement
export function defaultElement(type: 'Rect', id?: string): RectElement
export function defaultElement(type: 'Image', id?: string): ImageElement
export function defaultElement(type: 'Line', id?: string): LineElement
export function defaultElement(type: 'Region', id?: string): RegionElement
/* * 控件栏入口（文本 / 条码 / 二维码 / 矩形 / 图片——· 起图片入控件栏）。 */
export function defaultElement(type: 'Text' | 'Barcode' | 'QrCode' | 'Rect' | 'Image', id?: string): TextElement | BarcodeElement | QrCodeElement | RectElement | ImageElement
export function defaultElement(type: ElementType, id = uid()): DesignElement {
  const base = { id, x: 5, y: 5, w: 40, h: 10, border: 0 }
  switch (type) {
    case 'Text':
      return { ...base, type, fontH: 5, fontW: 5, fontFamily: 'Microsoft YaHei', bold: false, wrap: false, lineHeight: 1.2, valign: 'middle', mode: 'literal', key: '', text: i18next.t('designer:elementDefaultText'), align: 'Left', paddingH: 1, paddingV: 1, fitMode: 'shrink' }
    case 'Barcode':
      return { ...base, y: 20, w: 50, h: 20, type, mode: 'literal', key: '', text: 'ABC-123', paddingH: 1, paddingV: 1, barcodeFormat: 'CODE128', displayValue: true, moduleWidth: 1 }
    case 'QrCode':
      return { ...base, y: 20, w: 20, h: 20, type, mode: 'literal', key: '', text: 'ABC-123', paddingH: 1, paddingV: 1, qrEcc: 'M', qrMargin: 2 }
    case 'Rect':
      return { ...base, h: 20, type, border: 0.3 }
    case 'Image':
      return { ...base, y: 20, w: 20, h: 20, type, key: '' }
    case 'Line':
      return { ...base, y: 5, w: 60, h: 0, type, thickness: 0.5 }
    case 'Region':
      return { ...base, y: 5, w: 60, h: 30, type, border: 0.3, containerId: 'c' + Math.random().toString(36).slice(2, 8) }
  }
}

/** 元素类型显示名（随界面语言）。 */
export function typeLabel(e: DesignElement): string {
  switch (e.type) {
    case 'Text': return i18next.t('designer:type.text')
    case 'Barcode': return i18next.t('designer:type.barcode')
    case 'QrCode': return i18next.t('designer:type.qrcode')
    case 'Rect': return i18next.t('designer:type.rect')
    case 'Image': return i18next.t('designer:type.image')
    case 'Line': return i18next.t('designer:type.line')
    case 'Region': return i18next.t('designer:type.region')
  }
}

/** 图层显示名称：固定值显示内容；字段填充显示「(键名) 预览值」；条码 / 二维码带类型前缀（随界面语言）。 */
export function layerLabel(e: DesignElement): string {
  switch (e.type) {
    case 'Text':
      if (e.mode === 'literal') return e.text || i18next.t('designer:elementDefaultText')
      return i18next.t('designer:layer.textField', { key: e.key || i18next.t('designer:layer.unbound'), text: e.text || '' })
    case 'Barcode':
    case 'QrCode': {
      const t = e.type === 'Barcode' ? i18next.t('designer:type.barcode') : i18next.t('designer:type.qrcode')
      if (e.mode === 'literal') return i18next.t('designer:layer.typedLiteral', { type: t, text: e.text || i18next.t('designer:layer.literalFallback') })
      return i18next.t('designer:layer.typedField', { type: t, key: e.key || i18next.t('designer:layer.unbound'), text: e.text || '' })
    }
    case 'Rect': return i18next.t('designer:type.rect')
    // 图片：key 空＝未指定（两步式空占位）——图层标签提升辨识
    case 'Image': return e.key ? i18next.t('designer:layer.imageWithKey', { type: i18next.t('designer:type.image'), key: e.key }) : i18next.t('designer:layer.imageEmpty')
    case 'Line': return i18next.t('designer:type.line')
    case 'Region': return i18next.t('designer:type.region')
  }
}

/** 元素内容占位态（供渲染层判别「固定值空文本 / 字段未绑定」占位，不依赖本地化字符串比较）。 */
export type ElementContentState = 'none' | 'literal-empty' | 'unbound-field' | 'content'

/** 元素内容态判别：none = 非内容元素；literal-empty = 固定值模式无文本；unbound-field = 字段模式无预览值无字段名。 */
export function elementContentState(e: DesignElement): ElementContentState {
  if (e.type === 'Image' || e.type === 'Line' || e.type === 'Rect' || e.type === 'Region') return 'none'
  if (e.mode === 'literal') return e.text ? 'content' : 'literal-empty'
  if (e.text) return 'content'
  return e.key ? 'content' : 'unbound-field'
}

/** 画布显示内容：固定值原样；字段填充取预览值（仅画布显示，打印以外界数据为准；占位文案随界面语言）。 */
export function elementContent(e: DesignElement): string {
  switch (elementContentState(e)) {
    case 'none':
      return ''
    case 'literal-empty':
      return i18next.t('designer:content.literalEmpty')
    case 'unbound-field':
      return i18next.t('designer:content.unboundField')
    case 'content': {
      // elementContentState 已过滤非内容元素；此处仅 Text / Barcode / QrCode 三型到达
      const el = e as TextElement | BarcodeElement | QrCodeElement
      return el.mode === 'literal' ? el.text : el.text || el.key
    }
  }
}

/** 是否可填充内容（文本 / 条码 / 二维码）。 */
export function supportsContent(e: DesignElement): boolean {
  return e.type === 'Text' || e.type === 'Barcode' || e.type === 'QrCode'
}

/** 复制元素（生成新 id）。 */
export function cloneElement<T extends DesignElement>(e: T, newId = uid()): T {
  const copy = JSON.parse(JSON.stringify(e)) as T
  copy.id = newId
  return copy
}
