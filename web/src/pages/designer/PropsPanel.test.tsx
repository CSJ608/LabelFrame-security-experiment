// @vitest-environment jsdom
// 迭代 32 P1-4：设计器属性面板组件测试——不同元素类型（文本 / 条码 / 二维码）选中时渲染对应属性控件，
// 改值经 onChange 回调驱动状态更新（画布重绘的数据源）；多选对齐 / 删除；预览锁定与空态。
// PropsPanel 为纯 props 驱动组件（不依赖 Konva 画布 / api client），直接以最小 props 真实渲染。

import { useState } from 'react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen, within } from '@testing-library/react'
import type { BarcodeElement, DesignElement, ImageAsset, QrCodeElement, TextElement } from '../../lib/design/types'
import { defaultElement } from '../../lib/design/types'
import { deriveFieldInfos } from '../../lib/design/fields'
import { PropsPanel } from './PropsPanel'
import { SidePanel } from './SidePanel'

// 迭代 126（#308）：「?」文档点用例为 client 构建语义断言（[data-help] 在场），显式注入 client 分支
// （VITE_UI_MODE=server 整仓测试时保持稳定）；迭代 129（#320）撤门后双构建同渲染，server 分支
// 断言面由 HelpDot.server.test.tsx 专项覆盖
vi.mock('../../lib/uiMode', () => ({ UI_MODE: 'client', isServerUi: false }))

// ---------- 测试夹具（defaultElement 与控件栏新增元素的默认值一致） ----------

const textEl: TextElement = { ...defaultElement('Text', 't1'), text: '库位 A-01' }
const fieldTextEl: TextElement = { ...defaultElement('Text', 't2'), mode: 'field', key: 'location', text: 'A-01' }
const barcodeEl: BarcodeElement = { ...defaultElement('Barcode', 'b1') }
const qrEl: QrCodeElement = { ...defaultElement('QrCode', 'q1'), mode: 'field', key: 'sku', text: 'SKU-9' }

function renderProps(elements: DesignElement[], selected: string[], viewMode: 'fit' | 'preview' = 'fit') {
  const onChange = vi.fn<(id: string, patch: Partial<DesignElement>) => void>()
  const onAlign = vi.fn<(align: 'left' | 'centerH' | 'right' | 'top' | 'centerV' | 'bottom') => void>()
  const onDelete = vi.fn<(ids: string[]) => void>()
  render(
    <PropsPanel elements={elements} selected={selected} viewMode={viewMode} onChange={onChange} onAlign={onAlign} onDelete={onDelete} />,
  )
  return { onChange, onAlign, onDelete }
}

/** NumField 受控输入以 blur / Enter 提交（输入过程中不触发重绘），用 change + blur 模拟完成一次编辑。 */
function setNum(label: string, value: string) {
  const input = screen.getByLabelText(label) as HTMLInputElement
  fireEvent.change(input, { target: { value } })
  fireEvent.blur(input)
}

afterEach(cleanup)

describe('空态与预览锁定', () => {
  it('未选中元素：显示空态提示，不渲染任何属性控件', () => {
    renderProps([textEl], [])
    expect(screen.getByText('在画布上选中元素后显示属性。')).toBeTruthy()
    expect(screen.queryByLabelText('X')).toBeNull()
    expect(screen.queryByLabelText('字体')).toBeNull()
  })

  it('预览模式：显示画布锁定提示，不渲染属性控件', () => {
    renderProps([textEl], ['t1'], 'preview')
    expect(screen.getByText(/预览中：画布已锁定/)).toBeTruthy()
    expect(screen.queryByLabelText('字体')).toBeNull()
  })
})

describe('文本元素属性', () => {
  it('渲染文本专属控件（字体 / 水平垂直对齐 / 自动换行），不渲染条码 / 二维码参数', () => {
    renderProps([textEl], ['t1'])
    expect(screen.getByText('文本')).toBeTruthy()
    expect((screen.getByLabelText('字体') as HTMLSelectElement).value).toBe('Microsoft YaHei')
    expect((screen.getByLabelText('水平对齐') as HTMLSelectElement).value).toBe('Left')
    expect((screen.getByLabelText('垂直对齐') as HTMLSelectElement).value).toBe('middle')
    expect(screen.getByLabelText('自动换行')).toBeTruthy()
    expect(screen.getByLabelText('加粗')).toBeTruthy()
    // 通用组（位置 / 填充）也在
    expect(screen.getByLabelText('X')).toBeTruthy()
    expect(screen.getByLabelText('固定值')).toBeTruthy()
    // 不出现条码 / 二维码参数组
    expect(screen.queryByLabelText('码制')).toBeNull()
    expect(screen.queryByLabelText('纠错级别')).toBeNull()
  })

  it('固定值输入改值触发 onChange（属性变更驱动画布重绘）', () => {
    const { onChange } = renderProps([textEl], ['t1'])
    fireEvent.change(screen.getByLabelText('固定值'), { target: { value: '库位 B-02' } })
    expect(onChange).toHaveBeenCalledWith('t1', { text: '库位 B-02' })
  })

  it('字体 / 对齐 / 换行改值触发 onChange', () => {
    const { onChange } = renderProps([textEl], ['t1'])
    fireEvent.change(screen.getByLabelText('字体'), { target: { value: 'SimSun' } })
    expect(onChange).toHaveBeenCalledWith('t1', { fontFamily: 'SimSun' })
    fireEvent.change(screen.getByLabelText('水平对齐'), { target: { value: 'Center' } })
    expect(onChange).toHaveBeenCalledWith('t1', { align: 'Center' })
    fireEvent.change(screen.getByLabelText('垂直对齐'), { target: { value: 'bottom' } })
    expect(onChange).toHaveBeenCalledWith('t1', { valign: 'bottom' })
    fireEvent.click(screen.getByLabelText('自动换行'))
    expect(onChange).toHaveBeenCalledWith('t1', { wrap: true })
  })

  it('位置数字字段 blur 提交；字高超过元素高度时联动放大高度', () => {
    const { onChange } = renderProps([textEl], ['t1'])
    setNum('X', '12.5')
    expect(onChange).toHaveBeenCalledWith('t1', { x: 12.5 })
    setNum('字高', '12') // 默认 h=10 → 12 > 10 联动 { fontH: 12, h: 12 }
    expect(onChange).toHaveBeenCalledWith('t1', { fontH: 12, h: 12 })
  })

  it('来源切换到字段填充：onChange({ mode: "field" })', () => {
    const { onChange } = renderProps([textEl], ['t1'])
    fireEvent.change(screen.getByLabelText('来源'), { target: { value: 'field' } })
    expect(onChange).toHaveBeenCalledWith('t1', { mode: 'field' })
  })
})

describe('条码元素属性', () => {
  it('渲染条码参数（码制 / 底部显示文字 displayValue / 模块宽），不渲染字体与二维码参数', () => {
    renderProps([barcodeEl], ['b1'])
    expect(screen.getByText('条码')).toBeTruthy()
    expect((screen.getByLabelText('码制') as HTMLSelectElement).value).toBe('CODE128')
    expect((screen.getByLabelText('底部显示文字') as HTMLInputElement).checked).toBe(true)
    expect(screen.getByLabelText('模块宽')).toBeTruthy()
    expect(screen.queryByLabelText('字体')).toBeNull()
    expect(screen.queryByLabelText('纠错级别')).toBeNull()
  })

  it('displayValue / 码制 / 模块宽改值触发 onChange', () => {
    const { onChange } = renderProps([barcodeEl], ['b1'])
    fireEvent.click(screen.getByLabelText('底部显示文字'))
    expect(onChange).toHaveBeenCalledWith('b1', { displayValue: false })
    fireEvent.change(screen.getByLabelText('码制'), { target: { value: 'EAN13' } })
    expect(onChange).toHaveBeenCalledWith('b1', { barcodeFormat: 'EAN13' })
    setNum('模块宽', '0.8')
    expect(onChange).toHaveBeenCalledWith('b1', { moduleWidth: 0.8 })
  })
})

describe('二维码元素属性', () => {
  it('渲染二维码参数（纠错级别 ECC / 边距静区），不渲染字体与码制', () => {
    renderProps([qrEl], ['q1'])
    expect(screen.getByText('二维码')).toBeTruthy()
    expect((screen.getByLabelText('纠错级别') as HTMLSelectElement).value).toBe('M')
    expect(screen.getByLabelText('边距')).toBeTruthy()
    expect(screen.queryByLabelText('字体')).toBeNull()
    expect(screen.queryByLabelText('码制')).toBeNull()
  })

  it('纠错级别 / 边距改值触发 onChange', () => {
    const { onChange } = renderProps([qrEl], ['q1'])
    fireEvent.change(screen.getByLabelText('纠错级别'), { target: { value: 'H' } })
    expect(onChange).toHaveBeenCalledWith('q1', { qrEcc: 'H' })
    setNum('边距', '4')
    expect(onChange).toHaveBeenCalledWith('q1', { qrMargin: 4 })
  })

  it('宽高联动：改宽同时提交 w / h（二维码保持正方形）', () => {
    const { onChange } = renderProps([qrEl], ['q1'])
    setNum('宽', '25')
    expect(onChange).toHaveBeenCalledWith('q1', { w: 25, h: 25 })
  })
})

describe('字段填充（打印字段）', () => {
  it('字段模式：标题显示字段名徽标，字段名 / 预览值改值触发 onChange', () => {
    const { onChange } = renderProps([fieldTextEl], ['t2'])
    expect(screen.getByText('location')).toBeTruthy()
    const key = screen.getByLabelText('字段名') as HTMLInputElement
    expect(key.value).toBe('location')
    fireEvent.change(key, { target: { value: 'sku' } })
    expect(onChange).toHaveBeenCalledWith('t2', { key: 'sku' })
    fireEvent.change(screen.getByLabelText('预览值'), { target: { value: 'B-02' } })
    expect(onChange).toHaveBeenCalledWith('t2', { text: 'B-02' })
  })

  it('显示名输入（迭代 83 · #131 决议 1）：字段模式渲染可选显示名，改值触发 onChange；未填时输入框为空', () => {
    const named: TextElement = { ...defaultElement('Text', 't3'), mode: 'field', key: 'location', displayName: '库位', text: 'A-01' }
    const { onChange } = renderProps([named], ['t3'])
    const input = screen.getByLabelText('显示名') as HTMLInputElement
    expect(input.value).toBe('库位')
    fireEvent.change(input, { target: { value: '货架位置' } })
    expect(onChange).toHaveBeenCalledWith('t3', { displayName: '货架位置' })

    cleanup()
    renderProps([fieldTextEl], ['t2'])
    expect((screen.getByLabelText('显示名') as HTMLInputElement).value).toBe('')
  })

  it('固定值模式：不渲染字段名 / 显示名 / 预览值输入，也不显示字段填充提示（AC-03）', () => {
    renderProps([textEl], ['t1'])
    expect(screen.queryByLabelText('字段名')).toBeNull()
    expect(screen.queryByLabelText('显示名')).toBeNull()
    expect(screen.queryByLabelText('预览值')).toBeNull()
    expect(screen.queryByText(/打印时从外界数据取/)).toBeNull()
  })

  it('教学提示已迁文档点（迭代 126 · #308 AC-06）：字段填充模式不再渲染界面内 fillHint 行', () => {
    renderProps([fieldTextEl], ['t2'])
    expect(screen.queryByText(/打印时从外界数据取「字段名」对应字段填充/)).toBeNull()
  })

  it('A 档迁移负断言（AC-06）：旧括号教学提示串不再出现在面板任何位置', () => {
    renderProps([fieldTextEl], ['t2'])
    for (const legacy of ['（打印时用数据填充）', '（打印页显示，可选）', '（仅画布显示）', '（立即渲染）', '（打印更清晰）', '（mm，相对标签内容区）', '（通用）', '（以包围框为基准）']) {
      expect(document.body.textContent).not.toContain(legacy)
    }
  })

  it('措辞用户化（AC-02）：属性面板无「契约」字样直出，字段绑定控件为「字段名」措辞', () => {
    renderProps([fieldTextEl], ['t2'])
    expect(document.body.textContent).not.toContain('契约')
    expect(screen.getByLabelText('字段名')).toBeTruthy()
  })

  it('切回固定值：清空字段名与显示名（key: ""、displayName: undefined）', () => {
    const { onChange } = renderProps([fieldTextEl], ['t2'])
    fireEvent.change(screen.getByLabelText('来源'), { target: { value: 'literal' } })
    expect(onChange).toHaveBeenCalledWith('t2', { mode: 'literal', key: '', displayName: undefined })
  })
})

describe('组件级「?」文档点（迭代 126 · #308 AC-03）', () => {
  it('文本元素：位置 / 填充 / 边框内边距 / 文本字体四组标题挂「?」（data-help 语义锚点）', () => {
    renderProps([fieldTextEl], ['t2'])
    expect(document.querySelector('[data-help="designer.position"]')).toBeTruthy()
    expect(document.querySelector('[data-help="designer.fill"]')).toBeTruthy()
    expect(document.querySelector('[data-help="designer.box"]')).toBeTruthy()
    expect(document.querySelector('[data-help="designer.text"]')).toBeTruthy()
  })

  it('条码 / 二维码 / 矩形 / 线 / 图片：各自类型分组挂对应「?」', () => {
    renderProps([barcodeEl], ['b1'])
    expect(document.querySelector('[data-help="designer.barcode"]')).toBeTruthy()
    cleanup()
    renderProps([qrEl], ['q1'])
    expect(document.querySelector('[data-help="designer.qrcode"]')).toBeTruthy()
    cleanup()
    renderProps([{ ...defaultElement('Rect', 'r1') }], ['r1'])
    expect(document.querySelector('[data-help="designer.rect"]')).toBeTruthy()
    cleanup()
    renderProps([{ ...defaultElement('Line', 'l1') }], ['l1'])
    expect(document.querySelector('[data-help="designer.line"]')).toBeTruthy()
    cleanup()
    // 迭代 132（#329）：图片元素改挂专属 designer.image 锚点（compat 组收敛为仅容器）
    renderProps([{ ...defaultElement('Image', 'i1') }], ['i1'])
    expect(document.querySelector('[data-help="designer.image"]')).toBeTruthy()
  })

  it('多选：对齐组挂「?」；点「?」弹出就地文档气泡（词条与帮助文章同源）', () => {
    renderProps([textEl, barcodeEl], ['t1', 'b1'])
    const alignDot = document.querySelector('[data-help="designer.align"]') as HTMLButtonElement
    expect(alignDot).toBeTruthy()
    fireEvent.click(alignDot)
    const dialog = screen.getByRole('dialog')
    expect(dialog).toBeTruthy()
    // #316 AC-01：气泡不再渲染可见标题（无障碍名保留 title 词条）、正文直出
    expect(dialog.getAttribute('aria-label')).toBe('对齐')
    expect(within(dialog).queryByText('对齐')).toBeNull()
    expect(within(dialog).getByText(/多选至少 2 个元素时出现/)).toBeTruthy()
    // Esc 关闭（与 Popover 同法）
    fireEvent.keyDown(document, { key: 'Escape' })
    expect(screen.queryByRole('dialog')).toBeNull()
  })
})

describe('多选对齐与删除', () => {
  it('多选两个元素：显示对齐操作组，点击触发 onAlign（以包围框为基准）', () => {
    const { onAlign } = renderProps([textEl, barcodeEl], ['t1', 'b1'])
    expect(screen.getByText('已选 2 个元素')).toBeTruthy()
    fireEvent.click(screen.getByRole('button', { name: '左对齐' }))
    expect(onAlign).toHaveBeenCalledWith('left')
    fireEvent.click(screen.getByRole('button', { name: '垂直居中' }))
    expect(onAlign).toHaveBeenCalledWith('centerV')
  })

  it('多选删除选中 / 单选删除元素触发 onDelete', () => {
    const multi = renderProps([textEl, barcodeEl], ['t1', 'b1'])
    fireEvent.click(screen.getByRole('button', { name: '删除选中' }))
    expect(multi.onDelete).toHaveBeenCalledWith(['t1', 'b1'])
    cleanup()
    const single = renderProps([textEl], ['t1'])
    fireEvent.click(screen.getByRole('button', { name: '删除元素' }))
    expect(single.onDelete).toHaveBeenCalledWith(['t1'])
  })
})

// ---------- 迭代 132（#329，决策 #184）：图片元素属性组（查看 / 更换 / 移除 / 缺资源提示） ----------

const imageEl = { ...defaultElement('Image', 'i1'), key: 'logo.png' } as DesignElement
const IMAGE_ASSETS: Record<string, ImageAsset> = {
  'logo.png': { dataUrl: 'data:image/png;base64,iVBORw0', width: 320, height: 200, sizeBytes: 1024 },
}

function renderImageProps(elements: DesignElement[], selected: string[], imageAssets?: Record<string, ImageAsset>) {
  const onChange = vi.fn<(id: string, patch: Partial<DesignElement>) => void>()
  const onAlign = vi.fn<(align: 'left' | 'centerH' | 'right' | 'top' | 'centerV' | 'bottom') => void>()
  const onDelete = vi.fn<(ids: string[]) => void>()
  const onReplaceImage = vi.fn<(id: string) => void>()
  render(
    <PropsPanel
      elements={elements}
      selected={selected}
      viewMode="fit"
      onChange={onChange}
      onAlign={onAlign}
      onDelete={onDelete}
      imageAssets={imageAssets}
      onReplaceImage={onReplaceImage}
    />,
  )
  return { onChange, onDelete, onReplaceImage }
}

describe('图片元素属性组（迭代 132 · #329）', () => {
  it('有资源：显示图片组（信息行含键名与原图尺寸）＋更换 / 移除按钮；不再出现容器兼容组', () => {
    renderImageProps([imageEl], ['i1'], IMAGE_ASSETS)
    expect(screen.getByText('图片资源：logo.png · 320×200 px')).toBeTruthy()
    expect(screen.getByRole('button', { name: '更换图片' })).toBeTruthy()
    expect(screen.getByRole('button', { name: '移除图片' })).toBeTruthy()
    // compat 组收敛为仅容器（#329）：图片元素不挂 designer.compat 锚点，改挂 designer.image
    expect(document.querySelector('[data-help="designer.image"]')).toBeTruthy()
    expect(document.querySelector('[data-help="designer.compat"]')).toBeNull()
  })

  it('缺资源：提示图片资源缺失，「更换图片」仍在（重新上传修复通道）；「移除图片」删除该元素', () => {
    const { onReplaceImage, onDelete } = renderImageProps([imageEl], ['i1'], {})
    expect(screen.getByText('图片资源缺失，请点「更换图片」重新上传。')).toBeTruthy()
    fireEvent.click(screen.getByRole('button', { name: '更换图片' }))
    expect(onReplaceImage).toHaveBeenCalledWith('i1')
    fireEvent.click(screen.getByRole('button', { name: '移除图片' }))
    expect(onDelete).toHaveBeenCalledWith(['i1'])
  })

  it('key 空（未指定，三态拆分 · 方案 v2）：提示尚未指定图片＋「上传图片」入口；「移除图片」删除该空占位元素', () => {
    // key 空与缺资源是不同态：提示不出现「资源缺失」字样（未指定 ≠ 缺资源）
    const emptyEl = { ...defaultElement('Image', 'i1'), key: '' } as DesignElement
    const { onReplaceImage, onDelete } = renderImageProps([emptyEl], ['i1'])
    expect(screen.getByText('尚未指定图片；点占位元素或「上传图片」均可。')).toBeTruthy()
    expect(screen.queryByText(/图片资源缺失/)).toBeNull()
    expect(screen.getByRole('button', { name: '上传图片' })).toBeTruthy()
    expect(screen.queryByRole('button', { name: '更换图片' })).toBeNull()
    fireEvent.click(screen.getByRole('button', { name: '上传图片' }))
    expect(onReplaceImage).toHaveBeenCalledWith('i1')
    fireEvent.click(screen.getByRole('button', { name: '移除图片' }))
    expect(onDelete).toHaveBeenCalledWith(['i1'])
  })

  it('容器元素仍挂 compat 组（收敛为仅容器）；边框字段保留', () => {
    renderImageProps([{ ...defaultElement('Region', 'r1') }], ['r1'])
    expect(document.querySelector('[data-help="designer.compat"]')).toBeTruthy()
    expect(screen.getByLabelText('边框')).toBeTruthy()
  })
})

describe('设计器主链：图层选择 → 属性面板字段（SidePanel + PropsPanel 联动）', () => {
  /** 模拟 Designer 的选中 / 属性编辑状态流（不引入 Konva 画布），验证两面板经回调协作的主链。 */
  function Panels() {
    const [elements, setElements] = useState<DesignElement[]>([textEl, barcodeEl, qrEl])
    const [selected, setSelected] = useState<string[]>([])
    const change = (id: string, patch: Partial<DesignElement>) =>
      setElements((prev) => prev.map((e) => (e.id === id ? ({ ...e, ...patch } as DesignElement) : e)))
    const remove = (ids: string[]) => setElements((prev) => prev.filter((e) => !ids.includes(e.id)))
    return (
      <div>
        <SidePanel
          elements={elements}
          selected={selected}
          viewMode="fit"
          pendingType={null}
          fields={deriveFieldInfos(elements)}
          onPickType={() => {}}
          onSelect={(id, toggle) => setSelected((prev) => (toggle ? prev.filter((x) => x !== id).concat(prev.includes(id) ? [] : [id]) : [id]))}
          onMoveLayer={() => {}}
          onLayerTop={() => {}}
          onLayerBottom={() => {}}
          onDelete={remove}
        />
        <PropsPanel elements={elements} selected={selected} viewMode="fit" onChange={change} onAlign={() => {}} onDelete={remove} />
      </div>
    )
  }

  it('点击图层选中元素 → 属性面板出现该类型字段；改值经 onChange 回流后控件刷新', () => {
    render(<Panels />)
    // 初始未选中 → 空态；契约字段由字段填充元素推导（sku）
    expect(screen.getByText('在画布上选中元素后显示属性。')).toBeTruthy()
    expect(screen.getByText('sku')).toBeTruthy()
    // 点击二维码图层 → 属性面板出现二维码字段
    fireEvent.click(screen.getByText('(二维码) (sku) SKU-9'))
    const ecc = screen.getByLabelText('纠错级别') as HTMLSelectElement
    expect(ecc.value).toBe('M')
    // 改纠错级别 → onChange 驱动状态更新 → 控件值刷新（与画布重绘同一数据源）
    fireEvent.change(ecc, { target: { value: 'H' } })
    expect((screen.getByLabelText('纠错级别') as HTMLSelectElement).value).toBe('H')
    // 切换选中条码图层 → 面板切换为条码字段
    fireEvent.click(screen.getByText('(条码) ABC-123'))
    expect(screen.getByLabelText('码制')).toBeTruthy()
    expect(screen.queryByLabelText('纠错级别')).toBeNull()
  })
})
