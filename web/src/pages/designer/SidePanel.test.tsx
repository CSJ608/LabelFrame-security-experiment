// @vitest-environment jsdom
// 设计器侧栏组件测试——控件栏（放置类型 / 武装态）、打印字段（推导列表）
// 图层列表（类型前缀标签、点击选中同步、选中高亮、层级操作）与预览锁定。
// SidePanel 为纯 props 驱动组件（不依赖 Konva 画布 / api client），直接以最小 props 真实渲染。

import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen, within } from '@testing-library/react'
import type { BarcodeElement, DesignElement, QrCodeElement, TextElement } from '../../lib/design/types'
import { defaultElement } from '../../lib/design/types'
import { SidePanel } from './SidePanel'
import type { SidePanelProps } from './SidePanel'

// ---------- 测试夹具 ----------

const textEl: TextElement = { ...defaultElement('Text', 't1'), text: '库位 A-01' }
const fieldTextEl: TextElement = { ...defaultElement('Text', 't2'), mode: 'field', key: 'location', text: 'A-01' }
const barcodeEl: BarcodeElement = { ...defaultElement('Barcode', 'b1') }
const qrEl: QrCodeElement = { ...defaultElement('QrCode', 'q1'), mode: 'field', key: 'sku', text: 'SKU-9' }

const ELEMENTS: DesignElement[] = [textEl, barcodeEl, qrEl, fieldTextEl]
const FIELDS: SidePanelProps['fields'] = [
  { key: 'location', displayName: '库位' },
  { key: 'sku' },
]

function renderSide(overrides: Partial<SidePanelProps> = {}) {
  const props: SidePanelProps = {
    elements: ELEMENTS,
    selected: [],
    viewMode: 'fit',
    pendingType: null,
    fields: FIELDS,
    onPickType: vi.fn<(type: string) => void>(),
    onSelect: vi.fn<(id: string, toggle?: boolean) => void>(),
    onMoveLayer: vi.fn<(delta: number) => void>(),
    onLayerTop: vi.fn<() => void>(),
    onLayerBottom: vi.fn<() => void>(),
    onDelete: vi.fn<(ids: string[]) => void>(),
    ...overrides,
  }
  render(<SidePanel {...props} />)
  return props
}

/** 按图层标签文本取图层 li 元素。 */
function layerItem(label: string): HTMLElement {
  return screen.getByText(label).closest('li') as HTMLElement
}

afterEach(cleanup)

describe('图层列表', () => {
  it('按元素顺序渲染：固定值显示内容，条码 / 二维码带类型前缀，字段填充显示 (键名) 预览值', () => {
    renderSide()
    const t = layerItem('库位 A-01')
    const b = layerItem('(条码) ABC-123')
    const q = layerItem('(二维码) (sku) SKU-9')
    const f = layerItem('(location) A-01')
    // 渲染顺序 = elements 顺序（图层顺序）
    expect(t.compareDocumentPosition(b) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy()
    expect(b.compareDocumentPosition(q) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy()
    expect(q.compareDocumentPosition(f) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy()
    // 序号从 1 开始
    expect(within(t).getByText('1')).toBeTruthy()
    expect(within(b).getByText('2')).toBeTruthy()
  })

  it('点击图层项触发 onSelect：普通点击单选，Shift / Ctrl 点击以 toggle 多选', () => {
    const p = renderSide()
    fireEvent.click(layerItem('(条码) ABC-123'))
    expect(p.onSelect).toHaveBeenCalledWith('b1', false)
    fireEvent.click(layerItem('(二维码) (sku) SKU-9'), { shiftKey: true })
    expect(p.onSelect).toHaveBeenCalledWith('q1', true)
    fireEvent.click(layerItem('库位 A-01'), { ctrlKey: true })
    expect(p.onSelect).toHaveBeenCalledWith('t1', true)
  })

  it('选中高亮：selected 中的元素图层项加 active 类，其余不加', () => {
    renderSide({ selected: ['b1'] })
    expect(layerItem('(条码) ABC-123').className).toContain('active')
    expect(layerItem('库位 A-01').className).not.toContain('active')
    expect(layerItem('(二维码) (sku) SKU-9').className).not.toContain('active')
  })

  it('层级操作：未选中或多选时四个层级按钮禁用', () => {
    renderSide()
    for (const name of ['置顶', '上移', '下移', '置底']) {
      expect((screen.getByRole('button', { name }) as HTMLButtonElement).disabled).toBe(true)
    }
    cleanup()
    renderSide({ selected: ['t1', 'b1'] })
    for (const name of ['置顶', '上移', '下移', '置底']) {
      expect((screen.getByRole('button', { name }) as HTMLButtonElement).disabled).toBe(true)
    }
  })

  it('层级操作：单选启用，点击触发置顶 / 上移(-1) / 下移(+1) / 置底', () => {
    const p = renderSide({ selected: ['b1'] })
    for (const name of ['置顶', '上移', '下移', '置底']) {
      expect((screen.getByRole('button', { name }) as HTMLButtonElement).disabled).toBe(false)
    }
    fireEvent.click(screen.getByRole('button', { name: '置顶' }))
    expect(p.onLayerTop).toHaveBeenCalledTimes(1)
    fireEvent.click(screen.getByRole('button', { name: '上移' }))
    expect(p.onMoveLayer).toHaveBeenCalledWith(-1)
    fireEvent.click(screen.getByRole('button', { name: '下移' }))
    expect(p.onMoveLayer).toHaveBeenCalledWith(1)
    fireEvent.click(screen.getByRole('button', { name: '置底' }))
    expect(p.onLayerBottom).toHaveBeenCalledTimes(1)
  })
})

describe('打印字段（自动推导）', () => {
  it('渲染字段列表：显示名优先（displayName || key），未填显示名回退字段名（迭代 83 · #131 决议 1）', () => {
    renderSide()
    expect(screen.getByText('库位')).toBeTruthy()
    expect(screen.getByText('sku')).toBeTruthy()
    // 有显示名的项悬停提示字段名（键）
    expect(screen.getByText('库位').closest('li')?.getAttribute('title')).toBe('字段名：location')
  })

  it('措辞用户化（AC-02）：区块标题为「打印字段」，界面无「契约」字样直出', () => {
    renderSide()
    expect(screen.getByText('打印字段')).toBeTruthy()
    expect(screen.getByText('自动推导')).toBeTruthy()
    expect(document.body.textContent).not.toContain('契约')
  })

  it('无字段时显示空态', () => {
    renderSide({ fields: [] })
    expect(screen.getByText('暂无字段')).toBeTruthy()
  })
})

describe('控件栏', () => {
  it('五类控件按钮：点击进入放置模式（onPickType）——图片与其他控件同模式（两步式，#329 方案 v2）', () => {
    const p = renderSide()
    for (const label of ['文本', '条码', '二维码', '矩形', '图片']) {
      expect(screen.getByRole('button', { name: label })).toBeTruthy()
    }
    fireEvent.click(screen.getByRole('button', { name: '条码' }))
    expect(p.onPickType).toHaveBeenCalledWith('Barcode')
    fireEvent.click(screen.getByRole('button', { name: '图片' }))
    expect(p.onPickType).toHaveBeenCalledWith('Image')
  })

  it('图片入口与文本同模式：draggable 解禁（可拖入画布，非锁死上传特例）', () => {
    renderSide()
    const image = screen.getByRole('button', { name: '图片' }) as HTMLButtonElement
    const text = screen.getByRole('button', { name: '文本' }) as HTMLButtonElement
    expect(image.draggable).toBe(true)
    expect(image.draggable).toBe(text.draggable)
    // 预览锁定时统一禁拖
    cleanup()
    renderSide({ viewMode: 'preview' })
    expect((screen.getByRole('button', { name: '图片' }) as HTMLButtonElement).draggable).toBe(false)
  })

  it('pendingType 武装态：对应按钮高亮（armed）+ 放置提示；再次点击取消（onPickType("")）——含图片', () => {
    const p = renderSide({ pendingType: 'Barcode' })
    const btn = screen.getByRole('button', { name: '条码' })
    expect(btn.className).toContain('armed')
    expect(screen.getByText(/点击画布放置「条码」/)).toBeTruthy()
    fireEvent.click(btn)
    expect(p.onPickType).toHaveBeenCalledWith('')

    // 图片武装态同款（放置提示文案随类型）
    cleanup()
    renderSide({ pendingType: 'Image' })
    expect(screen.getByRole('button', { name: '图片' }).className).toContain('armed')
    expect(screen.getByText(/点击画布放置「图片」/)).toBeTruthy()
  })
})

describe('预览锁定', () => {
  it('预览模式：图层 / 字段区显示锁定提示，控件栏点击不进入放置模式', () => {
    const p = renderSide({ viewMode: 'preview' })
    expect(screen.getByText('预览中（退出后可编辑）')).toBeTruthy()
    expect(screen.getByText('预览中')).toBeTruthy()
    fireEvent.click(screen.getByRole('button', { name: '条码' }))
    expect(p.onPickType).not.toHaveBeenCalled()
  })
})
