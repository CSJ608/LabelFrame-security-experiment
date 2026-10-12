// @vitest-environment jsdom
// ElementNode 图片内容结构级断言：jsdom 无 2d context、react-konva
// 画布不可挂载（Designer.test.tsx 桩化先例； / 「画布 Konva Image 断言只能结构级」）
// 以 DOM 桩替换 react-konva 组件（props 序列化进 data-props），锚定承重结构：
// 成功分支 KImage 后跟整盒 fill=transparent 兜底命中 Rect（阻断根因——无此 Rect 则画布点击穿透
// 仅能经图层面板选中）； 空占位分支（key 空）保留整盒 fill 命中＋canvas.imageEmpty 文案（评审建议：
// 拆两态若只画虚线 stroke 会复现阻断 bug）； key 非空缺资源维持 imageNode 文案； 预览态且 key 空
// 整元素不渲染。真实 Konva 命中 / 点击交互归 复陪验。

import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, render, waitFor } from '@testing-library/react'
import type { DesignElement, ImageAsset } from '../../lib/design/types'
import { defaultElement } from '../../lib/design/types'
import { ElementNode } from './ElementNode'

// react-konva 桩：组件 → 记录 props 的 div（data-konva = 组件名；可序列化 props 进 data-props，
// 函数与 undefined 剔除——断言只关心呈现属性 fill / stroke / dash / listening / text）
vi.mock('react-konva', () => {
  const stub = (tag: string) =>
    function KonvaStub(props: Record<string, unknown>) {
      const { children, ...rest } = props
      const plain = Object.fromEntries(Object.entries(rest).filter(([, v]) => v !== undefined && typeof v !== 'function'))
      return (
        <div data-konva={tag} data-props={JSON.stringify(plain)}>
          {children as React.ReactNode}
        </div>
      )
    }
  return { Group: stub('Group'), Image: stub('Image'), Line: stub('Line'), Rect: stub('Rect'), Text: stub('Text') }
})

interface KonvaProps {
  fill?: string
  stroke?: string
  strokeWidth?: number
  dash?: number[]
  listening?: boolean
  width?: number
  height?: number
  text?: string
  [key: string]: unknown
}

/** 容器内全部指定 Konva 组件的 props 列表。 */
function konvaNodes(container: HTMLElement, tag: string): KonvaProps[] {
  return [...container.querySelectorAll<HTMLElement>(`[data-konva="${tag}"]`)].map((n) => JSON.parse(n.dataset.props ?? '{}') as KonvaProps)
}

/** jsdom 不解码图片位：桩全局 Image，src 赋值即异步 onload（ElementImageContent 解码通道）。 */
function stubImageElement() {
  class FakeImage {
    onload: (() => void) | null = null
    onerror: (() => void) | null = null
    naturalWidth = 8
    naturalHeight = 4
    private _src = ''
    get src() {
      return this._src
    }
    set src(value: string) {
      this._src = value
      queueMicrotask(() => this.onload?.())
    }
  }
  vi.stubGlobal('Image', FakeImage as unknown as typeof Image)
}

function renderNode(e: DesignElement, opts: { editable?: boolean; assets?: Record<string, ImageAsset> } = {}) {
  const { container } = render(<ElementNode e={e} editable={opts.editable ?? true} ox={40} oy={40} imageAssets={opts.assets} />)
  return container
}

afterEach(() => {
  cleanup()
  vi.unstubAllGlobals()
})

describe('图片元素内容三态与兜底命中区（#329 方案 v2）', () => {
  it('加载成功：KImage（listening=false）后跟整盒 fill=transparent 的 Rect——成功分支的兜底命中区', async () => {
    stubImageElement()
    const e = { ...defaultElement('Image', 'i1'), key: 'logo.png' } as DesignElement
    const assets: Record<string, ImageAsset> = { 'logo.png': { dataUrl: 'data:image/png;base64,iVBORw0', width: 8, height: 4, sizeBytes: 10 } }
    const container = renderNode(e, { assets })

    // 解码 onload 异步 → 等成功分支渲染
    await waitFor(() => expect(konvaNodes(container, 'Image')).toHaveLength(1))
    expect(konvaNodes(container, 'Image')[0].listening).toBe(false)
    // 兜底命中 Rect：整盒、fill=transparent 常在；border=0 时无 stroke（边框与命中同一 Rect 承担）
    const rects = konvaNodes(container, 'Rect')
    expect(rects).toHaveLength(1)
    expect(rects[0].fill).toBe('transparent')
    expect(rects[0].stroke).toBeUndefined()
  })

  it('加载成功且 border>0：同一 Rect 兼任边框（stroke #000）与命中（fill=transparent）', async () => {
    stubImageElement()
    const e = { ...defaultElement('Image', 'i1'), key: 'logo.png', border: 0.3 } as DesignElement
    const assets: Record<string, ImageAsset> = { 'logo.png': { dataUrl: 'data:image/png;base64,iVBORw0', width: 8, height: 4, sizeBytes: 10 } }
    const container = renderNode(e, { assets })

    await waitFor(() => expect(konvaNodes(container, 'Image')).toHaveLength(1))
    const rects = konvaNodes(container, 'Rect')
    expect(rects).toHaveLength(1)
    expect(rects[0].fill).toBe('transparent')
    expect(rects[0].stroke).toBe('#000')
  })

  it('key 空（未指定）：虚线空占位保留整盒 fill 命中＋「点击上传图片」文案', () => {
    const e = { ...defaultElement('Image', 'i1'), key: '' } as DesignElement
    const container = renderNode(e)

    const rects = konvaNodes(container, 'Rect')
    expect(rects).toHaveLength(1)
    // 评审建议承重项：空占位分支必须保留整盒 fill（否则点击穿透复现点击穿透 bug）
    expect(rects[0].fill).toBe('#f5f6f8')
    expect(rects[0].dash).toEqual([4, 3])
    const texts = konvaNodes(container, 'Text')
    expect(texts[0].text).toBe('点击上传图片')
  })

  it('key 非空缺资源：维持缺资源占位（imageNode 文案含键名）', () => {
    const e = { ...defaultElement('Image', 'i1'), key: 'gone.png' } as DesignElement
    const container = renderNode(e, { assets: {} })

    const rects = konvaNodes(container, 'Rect')
    expect(rects[0].fill).toBe('#f5f6f8')
    expect(konvaNodes(container, 'Text')[0].text).toBe('图片: gone.png')
  })

  it('预览态（editable=false）且 key 空：整元素不渲染（「未指定＝不存在于打印面」）', () => {
    const e = { ...defaultElement('Image', 'i1'), key: '' } as DesignElement
    const container = renderNode(e, { editable: false })
    expect(container.innerHTML).toBe('')
  })

  it('预览态且 key 非空：仍渲染（缺资源占位口径不变，可辨认）', () => {
    const e = { ...defaultElement('Image', 'i1'), key: 'gone.png' } as DesignElement
    const container = renderNode(e, { editable: false, assets: {} })
    expect(konvaNodes(container, 'Text')[0].text).toBe('图片: gone.png')
  })
})
