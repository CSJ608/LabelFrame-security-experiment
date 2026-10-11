import { describe, expect, it } from 'vitest'
import { applyContractDisplayNames, fromBackendElements, toBackendElement, toContract, toLayout } from './convert'
import type { BackendLayout } from './convert'
import { defaultElement } from './types'
import type { BarcodeElement, DesignElement, LineElement, QrCodeElement, RectElement, RegionElement, TextElement } from './types'

/** 取转换结果并按具体类型收窄（fromBackendElements 返回联合类型）。 */
function first<T extends DesignElement>(list: readonly DesignElement[]): T {
  return list[0] as T
}

describe('convert 设计器模型 ↔ 后端模板契约', () => {
  describe('toBackendElement 写方向（镜像 LabelElementJsonConverter）', () => {
    it('文本：sourceKey 总是写；literal 仅固定值非空写；fontName 固定 ZPL 标识 "0"；textAlign 非 Left 写', () => {
      const t = defaultElement('Text')
      t.x = 10
      t.y = 5.5
      t.w = 40
      t.h = 8
      t.fontH = 4
      t.fontW = 4
      t.mode = 'literal'
      t.text = '库位 A'
      t.paddingH = 1
      t.paddingV = 0.5
      t.border = 0.2
      t.align = 'Center'
      const j = toBackendElement(t)
      expect(j.type).toBe('text')
      expect(j.xMm).toBe(10)
      expect(j.yMm).toBe(5.5)
      expect(j.sourceKey).toBe('')
      expect(j.literal).toBe('库位 A')
      expect(j.fontName).toBe('0')
      expect(j.fontHeightMm).toBe(4)
      expect(j.widthMm).toBe(40)
      expect(j.textAlign).toBe('Center')
      expect(j.paddingMm).toBe(1) // max(paddingH, paddingV)
      expect(j.borderMm).toBe(0.2)
    })

    it('文本字段填充：literal 不写，sourceKey 写键名', () => {
      const t = defaultElement('Text')
      t.mode = 'field'
      t.key = 'location'
      t.text = 'A-01'
      const j = toBackendElement(t)
      expect(j.sourceKey).toBe('location')
      expect(j.literal).toBeUndefined()
    })

    it('条码：heightMm 与 int 化 moduleWidth', () => {
      const b = defaultElement('Barcode')
      b.h = 15
      b.moduleWidth = 1.6
      const j = toBackendElement(b)
      expect(j.type).toBe('barcode')
      expect(j.heightMm).toBe(15)
      expect(j.moduleWidth).toBe(2)
    })

    it('二维码：sizeMm = 边长', () => {
      const q = defaultElement('QrCode')
      q.w = 22
      q.h = 20
      const j = toBackendElement(q)
      expect(j.type).toBe('qrcode')
      expect(j.sizeMm).toBe(22)
    })

    it('矩形 → region（后端无矩形类型）', () => {
      const r = defaultElement('Rect')
      const j = toBackendElement(r)
      expect(j.type).toBe('region')
      expect(j.id).toBeDefined()
      expect(j.widthMm).toBe(40)
      expect(j.heightMm).toBe(20)
    })

    it('线：x2/y2 为绝对坐标（端点 = 起点 + 长度）', () => {
      const l = defaultElement('Line')
      l.x = 3
      l.y = 4
      l.w = 60
      l.h = 0
      l.thickness = 0.5
      const j = toBackendElement(l)
      expect(j.type).toBe('line')
      expect(j.x2Mm).toBe(63)
      expect(j.y2Mm).toBe(4)
      expect(j.thicknessMm).toBe(0.5)
    })

    it('容器：id 与宽高', () => {
      const rg = defaultElement('Region')
      const j = toBackendElement(rg)
      expect(j.type).toBe('region')
      expect(j.id).toBe(rg.containerId)
      expect(j.widthMm).toBe(60)
    })

    it('padding/border 为 0 时省略（与后端省略规则一致）', () => {
      const t = defaultElement('Text')
      t.paddingH = 0
      t.paddingV = 0
      t.border = 0
      const j = toBackendElement(t)
      expect(j.paddingMm).toBeUndefined()
      expect(j.borderMm).toBeUndefined()
    })

    it('previewValue：字段填充且 key 非空 → 写预览值（text/barcode/qrcode）', () => {
      const t = defaultElement('Text')
      t.mode = 'field'
      t.key = 'location'
      t.text = 'A-01-02'
      expect(toBackendElement(t).previewValue).toBe('A-01-02')

      const b = defaultElement('Barcode')
      b.mode = 'field'
      b.key = 'sku'
      b.text = 'SKU-2026'
      expect(toBackendElement(b).previewValue).toBe('SKU-2026')

      const q = defaultElement('QrCode')
      q.mode = 'field'
      q.key = 'sn'
      q.text = 'SN-001'
      expect(toBackendElement(q).previewValue).toBe('SN-001')
    })

    it('边界 A：字段填充但 key 为空（未绑定字段）→ 不写 previewValue', () => {
      const t = defaultElement('Text')
      t.mode = 'field'
      t.key = ''
      t.text = '未绑定的预览值'
      expect(toBackendElement(t).previewValue).toBeUndefined()
    })

    it('固定值模式 → 不写 previewValue（防回归，只写 literal）', () => {
      const t = defaultElement('Text')
      t.mode = 'literal'
      t.text = '标题'
      const j = toBackendElement(t)
      expect(j.previewValue).toBeUndefined()
      expect(j.literal).toBe('标题')
    })
  })

  describe('fromBackendElements 读方向', () => {
    it('文本：literal 非空 → 固定值；sourceKey 非空 → 字段填充', () => {
      const lit = first<TextElement>(fromBackendElements([
        { type: 'text', xMm: 1, yMm: 2, sourceKey: '', literal: '标题', fontHeightMm: 5, widthMm: 30, borderMm: 0.2 },
      ]))
      expect(lit.type).toBe('Text')
      expect(lit.mode).toBe('literal')
      expect(lit.text).toBe('标题')
      expect(lit.border).toBe(0.2)
      expect(lit.w).toBe(30)

      const f = first<TextElement>(fromBackendElements([{ type: 'text', xMm: 0, yMm: 0, sourceKey: 'location', fontHeightMm: 4 }]))
      expect(f.mode).toBe('field')
      expect(f.key).toBe('location')
    })

    it('文本高度回退：字高 + 2×内边距，下限 10mm（后端无高度字段）', () => {
      const t = first<TextElement>(fromBackendElements([{ type: 'text', xMm: 0, yMm: 0, sourceKey: '', fontHeightMm: 6, paddingMm: 1 }]))
      expect(t.h).toBe(10)
    })

    it('region 判别：被锚定 → 容器；独立 → 矩形', () => {
      const [container, , rect] = fromBackendElements([
        { type: 'region', xMm: 0, yMm: 0, id: 'c1', widthMm: 50, heightMm: 30 },
        { type: 'text', xMm: 5, yMm: 5, sourceKey: 'k', regionId: 'c1' },
        { type: 'region', xMm: 0, yMm: 40, id: 'r2', widthMm: 20, heightMm: 10 },
      ])
      expect(container.type).toBe('Region')
      expect((container as RegionElement).containerId).toBe('c1')
      expect(rect.type).toBe('Rect')

    })

    it('线读回为长度', () => {
      const l = first<LineElement>(fromBackendElements([{ type: 'line', xMm: 3, yMm: 4, x2Mm: 63, y2Mm: 4, thicknessMm: 0.5 }]))
      expect(l.type).toBe('Line')
      expect(l.w).toBe(60)
      expect(l.h).toBe(0)
      expect(l.thickness).toBe(0.5)
    })

    it('未知 / 缺省字段容错', () => {
      const t = first<TextElement>(fromBackendElements([{ type: 'text', xMm: 0, yMm: 0, sourceKey: '' }]))
      expect(t.type).toBe('Text')
      expect(t.fontH).toBe(5)
      expect(t.w).toBe(40)
      expect(t.h).toBeGreaterThanOrEqual(10)
    })

    it('previewValue 读回：字段填充模式 text 取预览值', () => {
      const t = first<TextElement>(fromBackendElements([
        { type: 'text', xMm: 0, yMm: 0, sourceKey: 'location', previewValue: 'A-01-02-03' },
      ]))
      expect(t.mode).toBe('field')
      expect(t.text).toBe('A-01-02-03')

      const b = first<BarcodeElement>(fromBackendElements([
        { type: 'barcode', xMm: 0, yMm: 0, sourceKey: 'sku', previewValue: 'SKU-X' },
      ]))
      expect(b.text).toBe('SKU-X')
    })

    it('旧模板无 previewValue → 字段填充 text 为空（向后兼容）', () => {
      const t = first<TextElement>(fromBackendElements([{ type: 'text', xMm: 0, yMm: 0, sourceKey: 'location' }]))
      expect(t.mode).toBe('field')
      expect(t.text).toBe('')
    })
  })

  describe('toContract / toLayout 模板顶层', () => {
    it('契约字段 = 推导结果（displayName 空则回退键，非必填，类型 Text；迭代 83 · #131 决议 1）', () => {
      const c = toContract('库位标签', '1', [
        { key: 'location', displayName: '库位' },
        { key: 'sku' },
        { key: 'batch', displayName: '   ' },
      ])
      expect(c.name).toBe('库位标签')
      expect(c.version).toBe('1')
      expect(c.fields).toEqual([
        { key: 'location', displayName: '库位', isRequired: false, type: 'Text' },
        { key: 'sku', displayName: 'sku', isRequired: false, type: 'Text' },
        { key: 'batch', displayName: 'batch', isRequired: false, type: 'Text' },
      ])
    })

    it('版式包含纸张与元素数组', () => {
      const t = defaultElement('Text')
      const l: BackendLayout = toLayout('库位标签', '库位标签', '1', 100, 60, [t])
      expect(l.widthMm).toBe(100)
      expect(l.heightMm).toBe(60)
      expect(l.elements).toHaveLength(1)
      expect(l.elements[0].type).toBe('text')
    })
  })

  describe('显示名契约往返（迭代 83 · #131 决议 1：模板包导出 / 导入不丢显示名）', () => {
    it('保存 → 读取：契约 fields 携带显示名，applyContractDisplayNames 按字段名回填到字段填充元素', () => {
      const t = defaultElement('Text')
      t.mode = 'field'
      t.key = 'location'
      t.displayName = '库位'
      t.text = 'A-01'
      const b = defaultElement('Barcode')
      b.mode = 'field'
      b.key = 'sku'
      // 保存：契约 fields 写显示名；版式元素 JSON 无显示名字段（契约格式不变）
      const contract = toContract('库位标签', '1', [{ key: 'location', displayName: '库位' }, { key: 'sku' }])
      const layout = toLayout('库位标签', '库位标签', '1', 100, 60, [t, b])
      expect(contract.fields[0]).toMatchObject({ key: 'location', displayName: '库位' })
      expect(layout.elements[0]).not.toHaveProperty('displayName')
      // 读取：fromBackendElements 重建元素 → 契约显示名回填
      const restored = applyContractDisplayNames(fromBackendElements(layout.elements), contract.fields)
      const rt = restored[0] as TextElement
      const rb = restored[1] as BarcodeElement
      expect(rt.mode).toBe('field')
      expect(rt.key).toBe('location')
      expect(rt.displayName).toBe('库位')
      expect(rb.key).toBe('sku')
      expect(rb.displayName).toBeUndefined() // 契约未提供 → 元素无显示名（显示处回退键名）
    })

    it('存量模板（旧口径 displayName = 键）：不回填显示名，显示处回退键名不报错', () => {
      const t = defaultElement('Text')
      t.mode = 'field'
      t.key = 'location'
      const legacyFields = [{ key: 'location', displayName: 'location', isRequired: false, type: 'Text' as const }]
      const layout = toLayout('库位标签', '库位标签', '1', 100, 60, [t])
      const restored = applyContractDisplayNames(fromBackendElements(layout.elements), legacyFields)
      const rt = restored[0] as TextElement
      expect(rt.key).toBe('location')
      expect(rt.displayName).toBeUndefined()
    })

    it('applyContractDisplayNames：固定值元素与未匹配键不受影响；契约无有效显示名时原样返回', () => {
      const lit = defaultElement('Text')
      lit.mode = 'literal'
      lit.text = '固定内容'
      const fld = defaultElement('QrCode')
      fld.mode = 'field'
      fld.key = 'url'
      const elements = applyContractDisplayNames([lit, fld], [{ key: 'other', displayName: '其他', isRequired: false, type: 'Text' }])
      expect((elements[0] as TextElement).displayName).toBeUndefined()
      expect((elements[1] as QrCodeElement).displayName).toBeUndefined()
    })
  })

  describe('往返一致性（保存 → 读取 → 关键字段不丢）', () => {
    it('文本往返：位置 / 填充 / 边框 / 对齐', () => {
      const t = defaultElement('Text')
      t.x = 12.5
      t.y = 7
      t.w = 45
      t.h = 10
      t.fontH = 5
      t.mode = 'field'
      t.key = 'location'
      t.text = 'A-01'
      t.align = 'Right'
      t.border = 0.3
      t.paddingH = 1.5
      const back = first<TextElement>(fromBackendElements([toBackendElement(t)]))
      expect(back.type).toBe('Text')
      expect(back.x).toBe(12.5)
      expect(back.y).toBe(7)
      expect(back.mode).toBe('field')
      expect(back.key).toBe('location')
      expect(back.align).toBe('Right')
      expect(back.border).toBe(0.3)
      expect(back.w).toBe(45)
      expect(back.text).toBe('A-01') // 字段填充预览值往返保留
    })

    it('条码 / 二维码字段填充预览值往返', () => {
      const b = defaultElement('Barcode')
      b.mode = 'field'
      b.key = 'sku'
      b.text = 'SKU-2026'
      const b2 = first<BarcodeElement>(fromBackendElements([toBackendElement(b)]))
      expect(b2.mode).toBe('field')
      expect(b2.text).toBe('SKU-2026')

      const q = defaultElement('QrCode')
      q.mode = 'field'
      q.key = 'sn'
      q.text = 'SN-001'
      const q2 = first<QrCodeElement>(fromBackendElements([toBackendElement(q)]))
      expect(q2.text).toBe('SN-001')
    })

    it('条码 / 二维码 / 容器 / 线往返', () => {
      const b = defaultElement('Barcode')
      b.h = 18
      b.mode = 'literal'
      b.text = 'SKU-001'
      const b2 = first<BarcodeElement>(fromBackendElements([toBackendElement(b)]))
      expect(b2.type).toBe('Barcode')
      expect(b2.h).toBe(18)
      expect(b2.mode).toBe('literal')
      expect(b2.text).toBe('SKU-001')
      const q = defaultElement('QrCode')
      q.w = 16
      q.h = 16
      const q2 = first<QrCodeElement>(fromBackendElements([toBackendElement(q)]))
      expect(q2.type).toBe('QrCode')
      expect(q2.w).toBe(16)
      expect(q2.h).toBe(16)

      const rg = defaultElement('Region')
      rg.x = 10
      rg.w = 55
      const rg2 = first<RectElement>(fromBackendElements([toBackendElement(rg)]))
      expect(rg2.type).toBe('Rect') // 无锚定 → 读回为矩形
      expect(rg2.w).toBe(55)
    })

    it('小数 mm 坐标往返不漂移（迭代 130 · #323：吸附落点两位小数口径，保存 → 重载一致）', () => {
      // 选无契约损耗的字段钉住（避坑：Barcode 的 w 读回固定 50、QrCode 尺寸取 max(w,h) 非全保持）
      const l = defaultElement('Line')
      l.x = 4.75
      l.y = 9.13
      const l2 = first<LineElement>(fromBackendElements([toBackendElement(l)]))
      expect(l2.x).toBe(4.75)
      expect(l2.y).toBe(9.13)

      const t = defaultElement('Text')
      t.x = 11.25
      t.y = 3.05
      t.w = 40.5
      t.h = 10.25
      const t2 = first<TextElement>(fromBackendElements([toBackendElement(t)]))
      expect(t2.x).toBe(11.25)
      expect(t2.y).toBe(3.05)
      expect(t2.w).toBe(40.5)
      expect(t2.h).toBe(10.25)
    })
  })

  describe('迭代 13 契约字段（wrap/lineHeight/fitMode/fontFamily/qrEcc/qrMargin/displayValue/paddingH-V）', () => {
    it('写方向：默认值省略（与现有省略规则一致）', () => {
      const t = defaultElement('Text') // fontFamily=YaHei / bold=false / wrap=false / lineHeight=1.2 / fitMode=shrink
      const tj = toBackendElement(t)
      expect(tj.fontFamily).toBeUndefined()
      expect(tj.bold).toBeUndefined()
      expect(tj.wrap).toBeUndefined()
      expect(tj.lineHeight).toBeUndefined()
      expect(tj.fitMode).toBeUndefined()

      const q = defaultElement('QrCode') // qrEcc=M / qrMargin=2
      const qj = toBackendElement(q)
      expect(qj.qrEcc).toBeUndefined()
      expect(qj.qrMargin).toBeUndefined()

      const b = defaultElement('Barcode') // displayValue=true
      expect(toBackendElement(b).displayValue).toBeUndefined()
    })

    it('写方向：非默认值写出（text 全字段）', () => {
      const t = defaultElement('Text')
      t.fontFamily = 'SimSun'
      t.bold = true
      t.wrap = true
      t.lineHeight = 1.3
      t.fitMode = 'overflow'
      const j = toBackendElement(t)
      expect(j.fontFamily).toBe('SimSun')
      expect(j.bold).toBe(true)
      expect(j.wrap).toBe(true)
      expect(j.lineHeight).toBe(1.3)
      expect(j.fitMode).toBe('overflow')
    })

    it('写方向：qrEcc/qrMargin/displayValue 非默认写出', () => {
      const q = defaultElement('QrCode')
      q.qrEcc = 'H'
      q.qrMargin = 4
      const qj = toBackendElement(q)
      expect(qj.qrEcc).toBe('H')
      expect(qj.qrMargin).toBe(4)

      const b = defaultElement('Barcode')
      b.displayValue = false
      expect(toBackendElement(b).displayValue).toBe(false)
    })

    it('写方向：双边内边距双值写出 + paddingMm 兼容保留', () => {
      const t = defaultElement('Text')
      t.paddingH = 2
      t.paddingV = 1
      const j = toBackendElement(t)
      expect(j.paddingH).toBe(2)
      expect(j.paddingV).toBe(1)
      expect(j.paddingMm).toBe(2) // max 兼容
    })

    it('读回：新字段还原（text/qrcode/barcode）', () => {
      const t = first<TextElement>(fromBackendElements([
        { type: 'text', xMm: 0, yMm: 0, sourceKey: 'k', fontHeightMm: 4, fontFamily: 'SimSun', bold: true, wrap: true, lineHeight: 1.3, fitMode: 'overflow' },
      ]))
      expect(t.fontFamily).toBe('SimSun')
      expect(t.bold).toBe(true)
      expect(t.wrap).toBe(true)
      expect(t.lineHeight).toBe(1.3)
      expect(t.fitMode).toBe('overflow')

      const q = first<QrCodeElement>(fromBackendElements([
        { type: 'qrcode', xMm: 0, yMm: 0, sourceKey: 'k', sizeMm: 20, qrEcc: 'H', qrMargin: 4 },
      ]))
      expect(q.qrEcc).toBe('H')
      expect(q.qrMargin).toBe(4)

      const b = first<BarcodeElement>(fromBackendElements([
        { type: 'barcode', xMm: 0, yMm: 0, sourceKey: 'k', heightMm: 10, displayValue: false },
      ]))
      expect(b.displayValue).toBe(false)
    })

    it('读回：旧模板（无新字段 / 仅 paddingMm）→ 前端默认 + paddingMm 兜底', () => {
      const t = first<TextElement>(fromBackendElements([
        { type: 'text', xMm: 0, yMm: 0, sourceKey: 'k', paddingMm: 1.5 },
      ]))
      expect(t.fontFamily).toBe('Microsoft YaHei')
      expect(t.bold).toBe(false)
      expect(t.wrap).toBe(false)
      expect(t.lineHeight).toBe(1.2)
      expect(t.fitMode).toBe('shrink')
      expect(t.paddingH).toBe(1.5)
      expect(t.paddingV).toBe(1.5)
    })

    it('往返：全字段一致（含不对称内边距与 QR 参数）', () => {
      const t = defaultElement('Text')
      t.bold = true
      t.wrap = true
      t.lineHeight = 1.3
      t.fitMode = 'overflow'
      t.fontFamily = 'SimSun'
      t.paddingH = 2
      t.paddingV = 1
      const t2 = first<TextElement>(fromBackendElements([toBackendElement(t)]))
      expect(t2.bold).toBe(true)
      expect(t2.wrap).toBe(true)
      expect(t2.lineHeight).toBe(1.3)
      expect(t2.fitMode).toBe('overflow')
      expect(t2.fontFamily).toBe('SimSun')
      expect(t2.paddingH).toBe(2)
      expect(t2.paddingV).toBe(1)

      const q = defaultElement('QrCode')
      q.qrEcc = 'H'
      q.qrMargin = 4
      const q2 = first<QrCodeElement>(fromBackendElements([toBackendElement(q)]))
      expect(q2.qrEcc).toBe('H')
      expect(q2.qrMargin).toBe(4)

      const b = defaultElement('Barcode')
      b.displayValue = false
      const b2 = first<BarcodeElement>(fromBackendElements([toBackendElement(b)]))
      expect(b2.displayValue).toBe(false)
    })
  })
})
