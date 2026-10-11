// 多选拖动机制锚定与 v1 口径证伪（迭代 131 返工 · #324 方案 v2——用户拍板「整体刚性语义」）：
// 多选组合视为一个整体——吸附单位 = 整组联合包围盒（边缘 / 中心与画布 / 内容区 / 未选中元素参考线
// 对齐，网格兜底按联合盒左上角贴 1mm 网格），组内元素全部退出吸附（既不作被拖盒也不作候选），
// 拖动 / 吸附位移以同一平移量刚性施加于整组，松手末次联合盒吸附后同 Δ 一次提交全部选中元素
// （multiDragRigidPatches 复用 #323 commitDragPosition 口径）。v1（PR #326，决策 #181）以
// 「主导元素盒吸附＋跟随元素逐步差分」实现，机制测试 11/11 绿但用户实测分离更严重（AC-05 不通过）
// ——本文件按 v2 链序复刻断言，并复刻 v1 链序把两套口径的吸附落点分歧可执行化
// （联合盒贴参考线 vs 主导盒贴网格）。
//
// 环境说明（循 renderbox.test.ts 先例）：jsdom 无 2d context，react-konva 画布不可挂载
// （Designer.test.tsx 桩化先例），组件 dragstart/move/end 真实事件流由浏览器走查＋陪验覆盖；
// 本文件在机制层锚定——用真实 Konva 节点坐标 + 真实 computeSnap / elementModelRect /
// unionModelRect / othersModelRects / multiDragRigidPatches / containerRegionIdFor 复刻
// CanvasViewport 的算式与调用序（指针 → 节点原点直移模拟 Konva dragmove 置位；dragend 保持
// 末次 dragmove 位置——DragAndDrop._endDragBefore 实读：只置 dragStatus 不更新节点位置）。
//
// 重渲染语义注记（react-konva makeUpdates 实读核实）：props 值未变的普通重渲染不重置命令式位移，
// 真实跳回路径是 x/y prop 值变化（预览切换 ox 变化 / 组件重挂 / 保存往返 / undo 重放）——本文件按
// 「位置按模型 prop 重算」近似锚定（ElementNode 算式 x = ox + pxv(e.x)），断言措辞与浏览器实测
// 口径以此为准。

import { describe, expect, it } from 'vitest'
import Konva from 'konva'
import { PX, RULER, commitDragPosition, multiDragRigidPatches, pxv } from './geometry'
import { SNAP_THRESHOLD, computeSnap, elementModelRect, othersModelRects, unionModelRect } from './snapping'
import type { SnapRect } from './snapping'
import { containerRegionIdFor, elementById } from './model'
import { defaultElement, type DesignElement } from './types'

// 与 CanvasViewport 一致的画布常量：纸 100x60（1mm = 4px），内容区偏移 = 标尺 + 留白
const OX = RULER + 10 * PX
const OY = RULER + 10 * PX
const CANVAS_W = RULER + (100 + 20) * PX // 500
const CANVAS_H = RULER + (60 + 20) * PX // 340

/** 元素模型 + 对应 Konva 节点（ElementNode 同构：Group 定位于 ox+pxv(e.x)，盒 = 模型宽高）。 */
function makeRect(id: string, x: number, y: number, w = 10, h = 10): DesignElement {
  const e = defaultElement('Rect', id)
  e.x = x
  e.y = y
  e.w = w
  e.h = h
  e.border = 0
  return e
}

function makeNode(e: DesignElement): Konva.Group {
  const n = new Konva.Group({ id: e.id, x: OX + pxv(e.x), y: OY + pxv(e.y) })
  n.add(new Konva.Rect({ width: Math.max(2, pxv(e.w)), height: Math.max(2, pxv(e.h)) }))
  return n
}

/** 拖动序列状态：模型（拖前值，与组件一致——拖动期间不提交）+ 节点（命令式位移）。 */
interface DragState {
  elements: DesignElement[]
  selected: string[]
  nodes: Map<string, Konva.Node>
  md: { targetId: string; union: SnapRect; items: Array<{ id: string; x: number; y: number }> } | null
}

/** CanvasViewport.handleDragStart v2 复刻：快照（联合盒 + 各元素初始坐标）一律从拖前模型换算，不依赖节点存在。 */
function setup(elements: DesignElement[], selected: string[], leaderId: string): DragState {
  const nodes = new Map<string, Konva.Node>()
  elements.forEach((e) => nodes.set(e.id, makeNode(e)))
  const sel = selected.map((id) => elementById(elements, id)).filter((e): e is DesignElement => e !== undefined)
  return {
    elements,
    selected,
    nodes,
    md: {
      targetId: leaderId,
      union: unionModelRect(sel, OX, OY),
      items: sel.map((e) => ({ id: e.id, x: OX + pxv(e.x), y: OY + pxv(e.y) })),
    },
  }
}

/** CanvasViewport v2 联合盒吸附复刻：被拖盒 = 快照联合盒 + 主导指针位移；候选 = 未选中元素（组内零参与）；
 *  吸附量施加于主导节点（参考线绘制不在机制断言范围）。 */
function snapUnion(s: DragState, g: Konva.Node): { dx: number; dy: number } {
  const md = s.md!
  const leader = md.items.find((i) => i.id === md.targetId)!
  const rect: SnapRect = { x: md.union.x + (g.x() - leader.x), y: md.union.y + (g.y() - leader.y), w: md.union.w, h: md.union.h }
  const others = othersModelRects(s.elements, md.items.map((i) => i.id), OX, OY)
  const snap = computeSnap(rect, CANVAS_W, CANVAS_H, OX, OY, 100 * PX, 60 * PX, others)
  if (snap.dx !== 0) g.x(g.x() + snap.dx)
  if (snap.dy !== 0) g.y(g.y() + snap.dy)
  return { dx: snap.dx, dy: snap.dy }
}

/** 修复后（v2）handleDragMove 复刻：联合盒吸附 → 跟随节点每步直接设「初始快照 + 主导总位移（含吸附）」。 */
function dragMoveV2(s: DragState, pointerX: number, pointerY: number) {
  const md = s.md!
  const g = s.nodes.get(md.targetId)!
  // Konva 拖动语义：每次 dragmove 节点被置为本步指针位置（DragAndDrop._drag 实读）
  g.x(pointerX)
  g.y(pointerY)
  snapUnion(s, g)
  const leader = md.items.find((i) => i.id === md.targetId)!
  const dx = g.x() - leader.x
  const dy = g.y() - leader.y
  md.items.forEach((item) => {
    if (item.id === md.targetId) return
    const n = s.nodes.get(item.id)
    if (n) {
      n.x(item.x + dx)
      n.y(item.y + dy)
    }
  })
}

/** 修复后（v2）handleDragEnd 复刻：末次联合盒吸附（已贴线幂等）→ multiDragRigidPatches 同 Δ 补丁
 *  → 跟随节点按补丁设视觉（ElementNode 算式）→ 一次应用（含容器锚定重算，与单选同语义）。 */
function dragEndV2(s: DragState) {
  const md = s.md!
  const g = s.nodes.get(md.targetId)!
  const snap = snapUnion(s, g)
  s.md = null
  const { patches } = multiDragRigidPatches({ leaderId: md.targetId, items: md.items, leaderX: g.x(), leaderY: g.y(), preview: false })
  md.items.forEach((item) => {
    if (item.id === md.targetId) return
    const patch = patches.get(item.id)
    const n = s.nodes.get(item.id)
    if (n && patch) {
      n.x(OX + pxv(patch.x))
      n.y(OY + pxv(patch.y))
    }
  })
  s.elements = s.elements.map((o): DesignElement => {
    const patch = patches.get(o.id)
    if (!patch) return o
    const next = { ...o, x: patch.x, y: patch.y }
    const regionId = containerRegionIdFor({ ...o, x: patch.x, y: patch.y }, s.elements)
    if (regionId) next.regionId = regionId
    else delete next.regionId
    return next
  })
  return { snap, patches }
}

/** v1 snapNode 复刻（PR #326）：被拖盒 = 主导元素模型盒；候选排除全部选中元素（#180 ④）。 */
function snapNodeV1(s: DragState, g: Konva.Node): { dx: number; dy: number } {
  const e = elementById(s.elements, g.id())!
  const followerIds = s.selected
  const others = s.elements
    .filter((o) => o.id !== e.id && !followerIds.includes(o.id))
    .map((o) => elementModelRect(o, OX, OY))
  const m = elementModelRect(e, OX, OY)
  const snap = computeSnap({ x: g.x(), y: g.y(), w: m.w, h: m.h }, CANVAS_W, CANVAS_H, OX, OY, 100 * PX, 60 * PX, others)
  if (snap.dx !== 0) g.x(g.x() + snap.dx)
  if (snap.dy !== 0) g.y(g.y() + snap.dy)
  return { dx: snap.dx, dy: snap.dy }
}

/** v1 链序复刻（PR #326「主导盒吸附 + 逐步差分」）走同一指针步序，返回 v1 主导松手落点（逻辑 px）：
 *  v1 主导补丁 = commitDragPosition(主导末次吸附后坐标)——落点由主导盒吸附决定（本证伪的目标）；
 *  逐步差分对跟随的累积 = 主导末次吸附后位置 − 初始（理想事件流下与 v2 同构，用户实测分离症状
 *  在 headless 复刻不可复现——方案 v2 风险第 1 条如实记录，落点分歧是可执行锚定点）。 */
function runV1Landing(elements: DesignElement[], selected: string[], leaderId: string, steps: Array<[number, number]>): number {
  const s = setup(elements, selected, leaderId)
  const g = s.nodes.get(leaderId)!
  let lastX = g.x()
  let lastY = g.y()
  steps.forEach(([px, py]) => {
    g.x(px)
    g.y(py)
    snapNodeV1(s, g)
    const dx = g.x() - lastX
    const dy = g.y() - lastY
    lastX = g.x()
    lastY = g.y()
    selected.forEach((id) => {
      if (id === leaderId) return
      const n = s.nodes.get(id)
      if (n) {
        n.x(n.x() + dx)
        n.y(n.y() + dy)
      }
    })
  })
  snapNodeV1(s, g) // v1 dragEnd 末次吸附（主导盒）
  return g.x()
}

/** ElementNode 位置算式：重渲染时节点位置按模型 prop 重算（x = ox + pxv(e.x)）。 */
const renderedX = (e: DesignElement) => OX + pxv(e.x)

// 布局（场景 U）：A（主导，模型 10,10）与 B（跟随，模型 30,10）多选；C（未选中，模型 47.5,40）为参考线锚。
// 联合盒 x 100..220；指针步序（主导节点 x）：
//   112（网格对齐，无吸附）→ 143（联合盒右缘 263 距内容区中心线 260 为 3px ≤ 阈值，吸附 −3 → 右缘贴线 260）
//   → 151（越过参考线，联合盒右缘 271 距 C 中心线 270 为 1px，回吸 −1）→ 143（回扫，再吸 −3）。
const scenarioU = () => setup([makeRect('A', 10, 10), makeRect('B', 30, 10), makeRect('C', 47.5, 40)], ['A', 'B'], 'A')
const stepsU: Array<[number, number]> = [
  [112, 100],
  [143, 100],
  [151, 100],
  [143, 100],
]

describe('AC-01 多选拖动落库——联合盒吸附 + 同 Δ 刚性提交', () => {
  it('松手补丁覆盖全部选中元素：联合盒右缘贴内容区中心线（260）→ 主导 20、跟随同 Δ 40', () => {
    const s = scenarioU()
    stepsU.forEach(([px, py]) => dragMoveV2(s, px, py))
    const { patches } = dragEndV2(s)
    expect(patches.size).toBe(2)
    expect(patches.get('A')).toEqual({ x: 20, y: 10 })
    expect(patches.get('B')).toEqual({ x: 40, y: 10 })
    expect(s.elements.find((e) => e.id === 'B')).toMatchObject({ x: 40, y: 10 })
  })

  it('同 Δ 刚性：组内任意两元素模型坐标差与拖前逐对相等（10−30 = 20−40 = −20）', () => {
    const s = scenarioU()
    const before = s.elements.find((e) => e.id === 'A')!.x - s.elements.find((e) => e.id === 'B')!.x
    stepsU.forEach(([px, py]) => dragMoveV2(s, px, py))
    dragEndV2(s)
    const a = s.elements.find((e) => e.id === 'A')!
    const b = s.elements.find((e) => e.id === 'B')!
    expect(a.x - b.x).toBeCloseTo(before, 10)
    expect(a.y - b.y).toBeCloseTo(0, 10)
  })

  it('落库后跟随元素模型 = 视觉位置：按 ElementNode 算式重算不再跳回', () => {
    const s = scenarioU()
    stepsU.forEach(([px, py]) => dragMoveV2(s, px, py))
    dragEndV2(s)
    const b = s.elements.find((e) => e.id === 'B')!
    expect(renderedX(b)).toBe(s.nodes.get('B')!.x()) // 220 = 60 + 4×40
  })

  it('multiDragRigidPatches 直测：Δ = 主导终态 − 主导初始，补丁 = commitDragPosition(初始 + Δ) 同参', () => {
    const { dx, dy, patches } = multiDragRigidPatches({
      leaderId: 'A',
      items: [
        { id: 'A', x: 100, y: 100 },
        { id: 'B', x: 180, y: 100 },
      ],
      leaderX: 141,
      leaderY: 100,
      preview: false,
    })
    expect(dx).toBeCloseTo(41, 10)
    expect(dy).toBe(0)
    expect(patches.size).toBe(2)
    // 非整 mm 落点（20.25 / 40.25）——补丁 = commitDragPosition(初始 + Δ) 逐元素同参
    expect(patches.get('A')).toEqual(commitDragPosition(141, 100, false))
    expect(patches.get('B')).toEqual(commitDragPosition(180 + 41, 100, false))
  })
})

describe('AC-02 吸附跟随——联合盒吸附 + 刚性相对位置恒定（用户复现场景）', () => {
  it('逐步拖动（网格对齐步 / 联合盒贴线吸附步 / 越线步 / 回扫步）主导 − 跟随相对位置恒定', () => {
    const s = scenarioU()
    const relX = s.nodes.get('A')!.x() - s.nodes.get('B')!.x() // −80
    const relY = s.nodes.get('A')!.y() - s.nodes.get('B')!.y()
    stepsU.forEach(([px, py]) => {
      dragMoveV2(s, px, py)
      expect(s.nodes.get('A')!.x() - s.nodes.get('B')!.x()).toBeCloseTo(relX, 10)
      expect(s.nodes.get('A')!.y() - s.nodes.get('B')!.y()).toBeCloseTo(relY, 10)
    })
    dragEndV2(s)
    expect(s.nodes.get('A')!.x() - s.nodes.get('B')!.x()).toBeCloseTo(relX, 10)
  })

  it('吸附生效步：联合盒右缘贴线（−3px ≤ SNAP_THRESHOLD）同量施加于整组——主导与跟随同吃吸附量', () => {
    const s = scenarioU()
    dragMoveV2(s, 112, 100) // 无吸附基准步：A=112、B=192
    dragMoveV2(s, 143, 100) // 吸附 −3：主导 140（Δ=40）、跟随 220（初始 180 + Δ40）
    expect(s.nodes.get('A')!.x()).toBe(140)
    expect(s.nodes.get('B')!.x()).toBe(220)
  })

  it('网格兜底步按联合盒左上角贴 1mm 网格：联合盒左上 113 → 112，整组 −1', () => {
    const s = scenarioU()
    dragMoveV2(s, 113, 100)
    expect(s.nodes.get('A')!.x()).toBe(112) // 联合盒左上角 = 主导节点原点，贴 4px 网格
    expect(s.nodes.get('B')!.x()).toBe(192) // 初始 180 + Δ12
  })

  it('松手末次联合盒吸附幂等：已贴线（260）时 dx=0，落点不漂移', () => {
    const s = scenarioU()
    stepsU.forEach(([px, py]) => dragMoveV2(s, px, py))
    const before = s.nodes.get('A')!.x() // 140
    const { snap } = dragEndV2(s)
    expect(snap.dx).toBe(0)
    expect(s.nodes.get('A')!.x()).toBe(before)
  })

  it('v1 口径复算证伪：主导盒吸附落点（贴 1mm 网格 144 → 模型 21）与 v2 联合盒落点（贴内容区中心线 → 模型 20）存在可执行差值 4px', () => {
    const elements = [makeRect('A', 10, 10), makeRect('B', 30, 10), makeRect('C', 47.5, 40)]
    const v1LeaderX = runV1Landing(elements, ['A', 'B'], 'A', stepsU)
    const v2 = scenarioU()
    stepsU.forEach(([px, py]) => dragMoveV2(v2, px, py))
    const { patches } = dragEndV2(v2)
    const v2LeaderX = OX + pxv(patches.get('A')!.x)
    expect(v1LeaderX).toBe(144) // v1：A 盒无可命中参考线 → 网格兜底（143 → 144）
    expect(v2LeaderX).toBe(140) // v2：联合盒右缘 263 → 贴内容区中心线 260
    expect(v2LeaderX - v1LeaderX).toBe(-4) // 两口径落点分歧 4px = 1mm：组边界贴线 vs 主导元素盒贴网格
    expect(SNAP_THRESHOLD).toBe(8) // 吸附量上限口径锚定（= 2mm）
  })

  it('用户复现场景（A/B 对齐对）机制复刻：多选拖动全程接触点保持（B.left − A.right === 0）', () => {
    // A（10,10）与 B（20,10）相邻对齐（A 右缘 = B 左缘 = 模型 20），联合盒 x 100..180：
    // 吸附步 D=76 → 联合盒右缘 256 → 贴内容区中心线 260（+4）；越线步 D=84 → 右缘 264 → 回吸 −4。
    const s = setup([makeRect('A', 10, 10), makeRect('B', 20, 10), makeRect('C', 47.5, 40)], ['A', 'B'], 'A')
    const steps: Array<[number, number]> = [
      [112, 100],
      [176, 100],
      [184, 100],
      [176, 100],
    ]
    steps.forEach(([px, py]) => {
      dragMoveV2(s, px, py)
      expect(s.nodes.get('B')!.x() - (s.nodes.get('A')!.x() + 40)).toBe(0) // 接触点（px）
    })
    dragEndV2(s)
    const a = s.elements.find((e) => e.id === 'A')!
    const b = s.elements.find((e) => e.id === 'B')!
    expect(b.x - (a.x + a.w)).toBe(0) // 模型层接触点保持（30 + 10 = 40）
  })
})

describe('AC-03 跟随元素容器锚定——v2 提交链与单选同语义', () => {
  const makeRegion = (id: string, x: number, y: number, w: number, h: number): DesignElement => {
    const r = defaultElement('Region', id)
    r.x = x
    r.y = y
    r.w = w
    r.h = h
    r.containerId = 'cR'
    return r
  }

  it('中心点在 Region 内 → containerId；边界命中（>= / <=）；拖出未命中 → undefined；排除自身容器', () => {
    const region = makeRegion('R', 40, 30, 30, 20)
    const els = [region, makeRect('B', 45, 35)]
    expect(containerRegionIdFor(makeRect('B', 45, 35), els)).toBe('cR') // 中心 (50,40) 在内
    expect(containerRegionIdFor(makeRect('B', 65, 45), els)).toBe('cR') // 中心 (70,50) 恰在右下边界
    expect(containerRegionIdFor(makeRect('B', 70, 35), els)).toBeUndefined() // 中心 (75,40) 拖出
    // 排除自身容器 = 排除「以该元素 id 为 containerId」的候选（元素自身是容器时不锚定自己代表的容器）
    const selfContainer = makeRect('cR', 45, 35)
    expect(containerRegionIdFor(selfContainer, els)).toBeUndefined()
  })

  it('多选整组拖入容器：跟随元素补丁位置的 regionId 与单选拖到同位置一致', () => {
    const region = makeRegion('R', 40, 30, 30, 20)
    // A（主导 10,10）B（跟随 12,12）拖动 (+40,+30)mm：A → (50,40)、B → (52,42)——联合盒参考线
    // 命中但位移为 0（左缘/上缘恰落内容区中心线与 R 中心线上，dx=dy=0 纯平移，snapUnion 仍绘制参考线），
    // 两中心均入 R。注意：微扰指针终点即产生真实吸附位移，regionId 断言随之失稳——调参须重算落点
    const s = setup([makeRect('A', 10, 10), makeRect('B', 12, 12), region], ['A', 'B'], 'A')
    dragMoveV2(s, OX + pxv(50), OY + pxv(40))
    dragEndV2(s)
    expect(s.elements.find((e) => e.id === 'A')!).toMatchObject({ x: 50, y: 40, regionId: 'cR' })
    expect(s.elements.find((e) => e.id === 'B')!).toMatchObject({ x: 52, y: 42, regionId: 'cR' })

    // 单选对照：B 单独拖到同位置（md null 走单选链——commitDragPosition + containerRegionIdFor）
    const pos = commitDragPosition(OX + pxv(52), OY + pxv(42), false)
    const single = containerRegionIdFor({ ...makeRect('B', 12, 12), x: pos.x, y: pos.y }, [makeRect('B', 12, 12), region])
    expect(single).toBe(s.elements.find((e) => e.id === 'B')!.regionId)
  })

  it('多选整组拖出容器：既有 regionId 被清除（跟随元素与单选同语义）', () => {
    const region = makeRegion('R', 40, 30, 30, 20)
    const a = makeRect('A', 50, 40)
    a.regionId = 'cR'
    const b = makeRect('B', 52, 42)
    b.regionId = 'cR'
    // 拖动 (−45,−35)mm：A → (5,5)、B → (7,7)，中心均出 R（联合盒参考线恰无吸附命中，纯平移）
    const s = setup([a, b, region], ['A', 'B'], 'A')
    dragMoveV2(s, OX + pxv(5), OY + pxv(5))
    dragEndV2(s)
    expect(s.elements.find((e) => e.id === 'A')!).toMatchObject({ x: 5, y: 5 })
    expect(s.elements.find((e) => e.id === 'B')!).toMatchObject({ x: 7, y: 7 })
    expect(s.elements.find((e) => e.id === 'B')!.regionId).toBeUndefined()
    expect('regionId' in s.elements.find((e) => e.id === 'B')!).toBe(false) // delete 而非置空
  })
})

describe('AC-04 纯函数与单选路径锚定（md null → 单选与 #323 口径一致）', () => {
  it('unionModelRect：并集包络 / 单元素退化 / 空集防御', () => {
    const a = makeRect('A', 10, 10) // 100..140 × 100..140
    const b = makeRect('B', 30, 5) // 180..220 × 80..120
    expect(unionModelRect([a, b], OX, OY)).toEqual({ x: 100, y: 80, w: 120, h: 60 })
    expect(unionModelRect([a], OX, OY)).toEqual(elementModelRect(a, OX, OY))
    expect(unionModelRect([], OX, OY)).toEqual({ x: 0, y: 0, w: 0, h: 0 })
  })

  it('othersModelRects：排除集参数（单选仅排自身 / 多选排除全部选中 / 全排除空集）', () => {
    const a = makeRect('A', 10, 10)
    const b = makeRect('B', 30, 10)
    const c = makeRect('C', 47.5, 40)
    const els = [a, b, c]
    expect(othersModelRects(els, ['A'], OX, OY)).toEqual([elementModelRect(b, OX, OY), elementModelRect(c, OX, OY)])
    expect(othersModelRects(els, ['A', 'B'], OX, OY)).toEqual([elementModelRect(c, OX, OY)])
    expect(othersModelRects(els, ['A', 'B', 'C'], OX, OY)).toEqual([])
  })

  it('单选路径：snapNode 候选仅排自身（未选中同伴可作锚），提交 = commitDragPosition 同参', () => {
    // CanvasViewport v2 snapNode 复刻：候选经 othersModelRects(elements, [被拖元素 id]) 构造
    // （v2 删除 followerIds 排除后单选行为逐行等价——候选集与 #323 口径一致）
    const snapNodeSingle = (elements: DesignElement[], g: Konva.Node) => {
      const e = elementById(elements, g.id())!
      const others = othersModelRects(elements, [e.id], OX, OY)
      const m = elementModelRect(e, OX, OY)
      const snap = computeSnap({ x: g.x(), y: g.y(), w: m.w, h: m.h }, CANVAS_W, CANVAS_H, OX, OY, 100 * PX, 60 * PX, others)
      if (snap.dx !== 0) g.x(g.x() + snap.dx)
      if (snap.dy !== 0) g.y(g.y() + snap.dy)
      return snap
    }
    const elements = [makeRect('A', 10, 10), makeRect('B', 30, 10), makeRect('C', 47.5, 40)]
    const g = makeNode(elements[0])
    g.x(143) // A 右缘 183 距 B 左缘 180 为 3px ≤ 阈值 → 吸附 −3（B 未被拖 → 仍是候选）
    const snap = snapNodeSingle(elements, g)
    expect(snap.dx).toBeCloseTo(-3, 10)
    expect(g.x()).toBe(140)
    expect(commitDragPosition(g.x(), g.y(), false)).toEqual(commitDragPosition(140, 100, false))
  })
})

// 撤销单步性（一次多选拖动 = 一步撤销）在本层不可挂载组件验证（jsdom 无 2d context，
// Designer.test.tsx 桩化先例）：由代码结构保证——v2 多选路径仍是单一 onUpdateElements + 单一
// onCommit（Designer.tsx applyElements/commitNow 既有链路不动），history.test.ts 既有用例随全量
// 回归 + 浏览器走查 / 陪验实测 Ctrl+Z 整组回退与单选零回归（#180/#181 先例：覆盖空档如实记录）。
// 已知边界（#326 评审建议 2 留观察，本轮维持不动）：多选整组含 Region 且组位移使元素中心跨过该
// Region 拖前盒边界时 regionId 可能误删 / 误设——本文件 AC-03 场景 Region 均不在同组，不覆盖。
