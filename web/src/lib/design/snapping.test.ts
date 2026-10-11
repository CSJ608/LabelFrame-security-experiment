import { describe, expect, it } from 'vitest'
import { commitDragPosition } from './geometry'
import { computeSnap, elementModelRect } from './snapping'
import { defaultElement } from './types'
import type { DesignElement } from './types'

// 画布：500x340；内容区起点 (60,60)，尺寸 400x240（纸 100x60，1mm=4px）
const CANVAS = { w: 500, h: 340 }
const CONTENT = { x: 60, y: 60, w: 400, h: 240 }

describe('snapping 智能参考线吸附', () => {
  it('无目标时网格吸附兜底（贴最近 1mm=4px 网格）', () => {
    const r = computeSnap({ x: 61, y: 62, w: 40, h: 20 }, CANVAS.w, CANVAS.h, CONTENT.x, CONTENT.y, CONTENT.w, CONTENT.h, [])
    expect(r.dx).toBe(-1) // 61 → 60
    expect(r.dy).toBe(-2) // 62 → 60
  })

  it('吸附到内容区边缘', () => {
    const r = computeSnap({ x: 56, y: 56, w: 40, h: 20 }, CANVAS.w, CANVAS.h, CONTENT.x, CONTENT.y, CONTENT.w, CONTENT.h, [])
    expect(r.dx).toBe(4) // 左缘 56 → 60
    expect(r.guideX).toBe(60)
    expect(r.dy).toBe(4)
    expect(r.guideY).toBe(60)
  })

  it('吸附到其它元素边缘', () => {
    const other = { x: 100, y: 100, w: 200, h: 30 } // 左缘 100（中心 200 / 右缘 300 远离）
    const r = computeSnap({ x: 106, y: 60, w: 40, h: 20 }, CANVAS.w, CANVAS.h, CONTENT.x, CONTENT.y, CONTENT.w, CONTENT.h, [other])
    // 元素左缘 106 与其它元素左缘 100 差 6（阈值内）→ dx = -6
    expect(r.dx).toBe(-6)
    expect(r.guideX).toBe(100)
  })

  it('多目标取最近（中心线比边缘更近时吸附中心）', () => {
    const other = { x: 100, y: 100, w: 50, h: 30 } // 中心 125
    const r = computeSnap({ x: 106, y: 60, w: 40, h: 20 }, CANVAS.w, CANVAS.h, CONTENT.x, CONTENT.y, CONTENT.w, CONTENT.h, [other])
    // 元素中心 126 与其它元素中心 125 差 1，比左缘差 6 更近
    expect(r.dx).toBe(-1)
    expect(r.guideX).toBe(125)
  })

  it('无其它目标时网格兜底对齐（贴最近 1mm 网格并显示参考线）', () => {
    const r = computeSnap({ x: 81, y: 82, w: 40, h: 20 }, CANVAS.w, CANVAS.h, CONTENT.x, CONTENT.y, CONTENT.w, CONTENT.h, [])
    expect(r.dx).toBe(-1) // 81 → 80（round(20.25)=20）
    expect(r.dy).toBe(2) // 82 → 84（round(20.5)=21）
    expect(r.guideX).toBe(80)
    expect(r.guideY).toBe(84)
  })
})

// 迭代 130（#323）：模型盒域吸附链路——被拖盒 / 参考线候选 / 落点提交统一切模型 mm 域，
// 落点与参考线 / 网格 mm 值一致（残差 ≤ 0.005mm，即两位舍入级别）。
describe('模型盒域吸附（elementModelRect + commitDragPosition）', () => {
  /** 复刻 CanvasViewport 拖动链：节点原点拖到 nodeX → computeSnap（模型盒）→ 落点提交。 */
  const dragAndCommit = (e: DesignElement, nodeX: number, nodeY: number, others: readonly DesignElement[]) => {
    const m = elementModelRect(e, CONTENT.x, CONTENT.y)
    const snap = computeSnap(
      { x: nodeX, y: nodeY, w: m.w, h: m.h },
      CANVAS.w,
      CANVAS.h,
      CONTENT.x,
      CONTENT.y,
      CONTENT.w,
      CONTENT.h,
      others.map((o) => elementModelRect(o, CONTENT.x, CONTENT.y)),
    )
    return { snap, landed: commitDragPosition(nodeX + snap.dx, nodeY + snap.dy, false) }
  }

  it('elementModelRect：模型 mm → 逻辑 px 盒，宽高镜像 ElementNode 的 ≥2px 下限钳制', () => {
    const line = defaultElement('Line') // x5 y5 w60 h0（thickness 0.5 不参与盒）
    expect(elementModelRect(line, CONTENT.x, CONTENT.y)).toEqual({ x: 80, y: 80, w: 240, h: 2 })
    const rect = defaultElement('Rect') // x5 y5 w40 h20（border 0.3 不参与盒）
    expect(elementModelRect(rect, CONTENT.x, CONTENT.y)).toEqual({ x: 80, y: 80, w: 160, h: 80 })
  })

  it('拖 Line 贴内容区左缘：落点 mm = 0（参考线值），残差 0（旧渲染盒口径偏 −0.25mm，见 renderbox.test）', () => {
    const line = defaultElement('Line')
    const { snap, landed } = dragAndCommit(line, 63, 80, [])
    expect(snap.guideX).toBe(CONTENT.x)
    expect(landed.x).toBe(0)
    expect(Math.abs(landed.x - 0)).toBeLessThanOrEqual(0.005)
    expect(landed.y).toBe(5)
  })

  it('拖 Rect 贴描边锚元素（border 0.3）边缘：落点 = 锚模型坐标 10mm（锚外扩盒不再传染）', () => {
    const anchor = defaultElement('Rect')
    anchor.x = 10
    const dragged = defaultElement('Rect')
    dragged.border = 0
    const { snap, landed } = dragAndCommit(dragged, 106, 60, [anchor])
    expect(snap.guideX).toBe(100) // ox + pxv(10)
    expect(landed.x).toBe(anchor.x)
  })

  it('贴其它元素中心：落点后中心 mm 严格相等', () => {
    const anchor = defaultElement('Rect')
    anchor.x = 10
    anchor.w = 12.5 // 中心 16.25mm
    const dragged = defaultElement('Rect')
    dragged.border = 0
    dragged.w = 10
    // 拖动元素中心 = 106 + 20 = 126，距锚中心 125 为 1px（比左缘差 6px 更近）→ 吸中心
    const { snap, landed } = dragAndCommit(dragged, 106, 60, [anchor])
    expect(snap.guideX).toBe(125) // 中心参考线
    expect(landed.x + dragged.w / 2).toBe(anchor.x + anchor.w / 2)
  })

  it('1mm 网格兜底：落点整 mm', () => {
    const rect = defaultElement('Rect')
    rect.border = 0
    // y=81：竖向无参考线命中（画布竖中心 170 / 内容缘 60 均超阈值）→ 网格兜底 80
    const { snap, landed } = dragAndCommit(rect, 61, 81, [])
    expect(snap.guideX).toBe(60)
    expect(snap.guideY).toBe(80)
    expect(landed.x).toBe(0)
    expect(landed.y).toBe(5)
  })

  it('网格兜底恒命中 pin：x=0~400 步进 0.37 扫描 guideX 全非空（自由拖动无漏吸，落点恒由参考线/网格决定）', () => {
    const rect = defaultElement('Rect')
    const m = elementModelRect(rect, CONTENT.x, CONTENT.y)
    for (let x = 0; x <= 400; x += 0.37) {
      const snap = computeSnap({ x, y: 80, w: m.w, h: m.h }, CANVAS.w, CANVAS.h, CONTENT.x, CONTENT.y, CONTENT.w, CONTENT.h, [])
      expect(snap.guideX, `x=${x}`).not.toBeNull()
    }
  })
})
