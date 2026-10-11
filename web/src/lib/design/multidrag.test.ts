// 多选拖动机制锚定与旧口径证伪（迭代 131 · #324）：
// 修复前多选拖动只提交主导元素模型坐标（跟随元素重渲染后跳回拖前位置）、且拖动期间与
// 松手时的参考线吸附位移只作用于主导元素（组合相对位置散开 ≤8px=2mm——用户陪验 #323 时
// 实测「拖拽期间对齐点失效、两元素发生偏移」）。本文件按 CanvasViewport 修复后的拖动链序
// （dragMove 吸附先行 + dragEnd multiDragCommitPatches 全选中元素提交）在 headless Konva
// 节点上复刻序列断言修复行为，并复刻旧链序把两个缺陷可执行化。
//
// 环境说明（循 renderbox.test.ts 先例）：jsdom 无 2d context，react-konva 画布不可挂载
// （Designer.test.tsx 桩化先例），组件 dragstart/move/end 真实事件流由浏览器走查＋陪验覆盖；
// 本文件在机制层锚定——用真实 Konva 节点坐标 + 真实 computeSnap / multiDragCommitPatches /
// containerRegionIdFor 复刻 CanvasViewport 的算式与调用序（指针 → 节点原点直移模拟 Konva
// 「每次 dragmove 以指针位置覆盖上一步吸附」的拖动语义）。
//
// 重渲染语义注记（react-konva makeUpdates.js:127-131 实读核实）：props 值未变的普通重渲染
// 不重置命令式位移，真实跳回路径是 x/y prop 值变化（预览切换 ox 变化 / 组件重挂 / 保存往返 /
// undo 重放）——本文件按「位置按模型 prop 重算」近似锚定（ElementNode 算式 x = ox + pxv(e.x)），
// 断言措辞与浏览器实测口径以此为准。

import { describe, expect, it } from 'vitest'
import Konva from 'konva'
import { PX, RULER, commitDragPosition, multiDragCommitPatches, pxv } from './geometry'
import { computeSnap, elementModelRect, SNAP_THRESHOLD } from './snapping'
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
  md: { targetId: string; lastX: number; lastY: number } | null
}

function setup(elements: DesignElement[], selected: string[], leaderId: string): DragState {
  const nodes = new Map<string, Konva.Node>()
  elements.forEach((e) => nodes.set(e.id, makeNode(e)))
  const leader = nodes.get(leaderId)!
  return { elements, selected, nodes, md: { targetId: leaderId, lastX: leader.x(), lastY: leader.y() } }
}

/** CanvasViewport.snapNode 复刻：候选 = 其它元素模型盒（多选跟随元素排除出候选——#180 ④）。 */
function snapNode(s: DragState, g: Konva.Node): { dx: number; dy: number } {
  const e = elementById(s.elements, g.id())!
  const followerIds = s.md ? s.selected : []
  const others = s.elements
    .filter((o) => o.id !== e.id && !followerIds.includes(o.id))
    .map((o) => elementModelRect(o, OX, OY))
  const m = elementModelRect(e, OX, OY)
  const snap = computeSnap({ x: g.x(), y: g.y(), w: m.w, h: m.h }, CANVAS_W, CANVAS_H, OX, OY, 100 * PX, 60 * PX, others)
  if (snap.dx !== 0) g.x(g.x() + snap.dx)
  if (snap.dy !== 0) g.y(g.y() + snap.dy)
  return { dx: snap.dx, dy: snap.dy }
}

const moveFollowerByPointerDelta = (s: DragState, dx: number, dy: number) => {
  const md = s.md!
  s.selected.forEach((id) => {
    if (id === md.targetId) return
    const n = s.nodes.get(id)
    if (n) {
      n.x(n.x() + dx)
      n.y(n.y() + dy)
    }
  })
}

/** 修复后 handleDragMove 复刻（迭代 131 · #324）：吸附先行，跟随元素获得含吸附修正的实际视觉位移。 */
function dragMoveNew(s: DragState, pointerX: number, pointerY: number) {
  const g = s.nodes.get(s.md!.targetId)!
  // Konva 拖动语义：每次 dragmove 以指针位置覆盖上一步吸附
  g.x(pointerX)
  g.y(pointerY)
  snapNode(s, g)
  const md = s.md!
  const dx = g.x() - md.lastX
  const dy = g.y() - md.lastY
  md.lastX = g.x()
  md.lastY = g.y()
  moveFollowerByPointerDelta(s, dx, dy)
}

/** 旧 handleDragMove 复刻（#324 修复前）：位移记录先于吸附——每步吸附量对跟随元素永久丢失。 */
function dragMoveOld(s: DragState, pointerX: number, pointerY: number) {
  const g = s.nodes.get(s.md!.targetId)!
  g.x(pointerX)
  g.y(pointerY)
  const md = s.md!
  const dx = g.x() - md.lastX
  const dy = g.y() - md.lastY
  md.lastX = g.x()
  md.lastY = g.y()
  moveFollowerByPointerDelta(s, dx, dy)
  snapNode(s, g)
}

/** dragMoveNew 变体：返回本步吸附量（供阈值外断言）。 */
function dragMoveNewReturnSnap(s: DragState, pointerX: number, pointerY: number) {
  const g = s.nodes.get(s.md!.targetId)!
  g.x(pointerX)
  g.y(pointerY)
  const snap = snapNode(s, g)
  const md = s.md!
  const dx = g.x() - md.lastX
  const dy = g.y() - md.lastY
  md.lastX = g.x()
  md.lastY = g.y()
  moveFollowerByPointerDelta(s, dx, dy)
  return snap
}

/** dragMoveOld 变体：返回本步吸附量（供证伪断言）。 */
function dragMoveOldReturnSnap(s: DragState, pointerX: number, pointerY: number) {
  const g = s.nodes.get(s.md!.targetId)!
  g.x(pointerX)
  g.y(pointerY)
  const md = s.md!
  const dx = g.x() - md.lastX
  const dy = g.y() - md.lastY
  md.lastX = g.x()
  md.lastY = g.y()
  moveFollowerByPointerDelta(s, dx, dy)
  return snapNode(s, g)
}

/** 修复后 handleDragEnd 复刻：末次吸附 → 先读 multiDragRef 再清零 → 全选中元素补丁一次提交。 */
function dragEndNew(s: DragState) {
  const g = s.nodes.get(s.md!.targetId)!
  snapNode(s, g)
  const md = s.md!
  s.md = null
  const followers = s.selected
    .filter((id) => id !== md.targetId)
    .map((id) => ({ id, x: s.nodes.get(id)!.x(), y: s.nodes.get(id)!.y() }))
  const { snapDx, snapDy, patches } = multiDragCommitPatches({ leaderId: md.targetId, leaderX: g.x(), leaderY: g.y(), lastX: md.lastX, lastY: md.lastY, followers, preview: false })
  followers.forEach((f) => {
    const n = s.nodes.get(f.id)!
    n.x(n.x() + snapDx)
    n.y(n.y() + snapDy)
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
  return patches
}

/** 旧 handleDragEnd 复刻（#324 修复前）：只提交主导元素，跟随元素模型坐标停留拖前位置。 */
function dragEndOld(s: DragState) {
  const g = s.nodes.get(s.md!.targetId)!
  snapNode(s, g)
  const leaderId = s.md!.targetId
  s.md = null
  const e = elementById(s.elements, leaderId)!
  const pos = commitDragPosition(g.x(), g.y(), false)
  const regionId = containerRegionIdFor({ ...e, x: pos.x, y: pos.y }, s.elements)
  s.elements = s.elements.map((o) => {
    if (o.id !== leaderId) return o
    const next = { ...o, x: pos.x, y: pos.y }
    if (regionId) next.regionId = regionId
    else delete next.regionId
    return next
  })
}

/** ElementNode 位置算式：重渲染时节点位置按模型 prop 重算（x = ox + pxv(e.x)）。 */
const renderedX = (e: DesignElement) => OX + pxv(e.x)

describe('AC-01 多选拖动落库——跟随元素与主导同位移提交（含末次吸附量）', () => {
  // 布局：A（主导，模型 10,10）与 B（跟随，模型 30,10）多选；C（未选中，模型 50,10）为吸附锚。
  // 指针步序：200（无吸附）→ 256.5（左缘距 C 左缘 / 内容区中心线 3.5px ≤ SNAP_THRESHOLD，吸附 +3.5）。
  const scenario = () => {
    const s = setup([makeRect('A', 10, 10), makeRect('B', 30, 10), makeRect('C', 50, 10)], ['A', 'B'], 'A')
    dragMoveNew(s, 200, 100)
    dragMoveNew(s, 256.5, 100)
    return s
  }

  it('松手补丁覆盖全部选中元素：主导贴参考线（50mm），跟随同位移（30 → 70 = 主导 10 → 50）', () => {
    const s = scenario()
    const patches = dragEndNew(s)
    expect(patches.size).toBe(2)
    expect(patches.get('A')).toEqual({ x: 50, y: 10 })
    expect(patches.get('B')).toEqual({ x: 70, y: 10 })
    expect(s.elements.find((e) => e.id === 'B')).toMatchObject({ x: 70, y: 10 })
  })

  it('落库后跟随元素模型 = 视觉位置：按 ElementNode 算式重算不再跳回', () => {
    const s = scenario()
    dragEndNew(s)
    const b = s.elements.find((e) => e.id === 'B')!
    const nodeB = s.nodes.get('B')!
    expect(renderedX(b)).toBe(nodeB.x())
  })

  it('旧口径复算证伪：只提交主导 → 跟随模型停留拖前值，重算位置跳回拖前（156.5px 可执行断言）', () => {
    const s = setup([makeRect('A', 10, 10), makeRect('B', 30, 10), makeRect('C', 50, 10)], ['A', 'B'], 'A')
    dragMoveOld(s, 200, 100)
    dragMoveOld(s, 256.5, 100)
    dragEndOld(s)
    const b = s.elements.find((e) => e.id === 'B')!
    const nodeB = s.nodes.get('B')!
    expect(b).toMatchObject({ x: 30, y: 10 }) // 模型停留拖前值
    expect(renderedX(b)).toBe(OX + pxv(30)) // 重算回到拖前视觉位置（180）
    expect(renderedX(b)).not.toBe(nodeB.x()) // 与拖后视觉位置（336.5）不符 = 跳回
  })

  it('末次吸附残差（snapDx/Dy）同步施加于跟随节点与补丁：lastX 落后于末次吸附时补齐', () => {
    // dragEnd 再吸附与 dragmove 末步吸附不一致的残差路径：lastX = 吸附前指针位置（旧序遗留），
    // 末次吸附后主导 260，snapDx = +3.5 —— 跟随补丁 = 节点坐标 + snapDx（视觉 = 模型）。
    const { snapDx, snapDy, patches } = multiDragCommitPatches({
      leaderId: 'A',
      leaderX: 260,
      leaderY: 100,
      lastX: 256.5,
      lastY: 100,
      followers: [{ id: 'B', x: 336.5, y: 100 }],
      preview: false,
    })
    expect(snapDx).toBeCloseTo(3.5, 10)
    expect(snapDy).toBe(0)
    expect(patches.get('B')).toEqual({ x: 70, y: 10 }) // 336.5 + 3.5 = 340 → (340−60)/4 = 70
  })
})

describe('AC-02 吸附跟随——拖动期间组合相对位置恒定（用户复现场景）', () => {
  const initialRel = () => {
    const s = setup([makeRect('A', 10, 10), makeRect('B', 30, 10), makeRect('C', 50, 10)], ['A', 'B'], 'A')
    return { relX: s.nodes.get('A')!.x() - s.nodes.get('B')!.x(), relY: s.nodes.get('A')!.y() - s.nodes.get('B')!.y() }
  }

  it('阈值内（3.5px）：逐步拖动（含吸附生效步与松手末次吸附步）主导 − 跟随相对位置恒定', () => {
    const { relX, relY } = initialRel()
    const s = setup([makeRect('A', 10, 10), makeRect('B', 30, 10), makeRect('C', 50, 10)], ['A', 'B'], 'A')
    const steps: Array<[number, number]> = [
      [200, 100],
      [256.5, 100], // 吸附生效步：+3.5px 修正
      [262.5, 100], // 越过参考线再回，Konva 以指针覆盖后重新吸附
      [256.5, 100],
    ]
    steps.forEach(([px, py]) => {
      dragMoveNew(s, px, py)
      expect(s.nodes.get('A')!.x() - s.nodes.get('B')!.x()).toBeCloseTo(relX, 10)
      expect(s.nodes.get('A')!.y() - s.nodes.get('B')!.y()).toBeCloseTo(relY, 10)
    })
    dragEndNew(s)
    expect(s.nodes.get('A')!.x() - s.nodes.get('B')!.x()).toBeCloseTo(relX, 10)
  })

  it('旧口径复算证伪：md.lastX 先记 + 吸附只施加主导 → 相对位置偏移吸附量 3.5px（≤ SNAP_THRESHOLD）', () => {
    const { relX } = initialRel()
    const s = setup([makeRect('A', 10, 10), makeRect('B', 30, 10), makeRect('C', 50, 10)], ['A', 'B'], 'A')
    dragMoveOld(s, 200, 100)
    const snap = dragMoveOldReturnSnap(s, 256.5, 100)
    expect(Math.abs(snap.dx)).toBeGreaterThan(0)
    expect(Math.abs(snap.dx)).toBeLessThanOrEqual(SNAP_THRESHOLD)
    const off = s.nodes.get('A')!.x() - s.nodes.get('B')!.x() - relX
    expect(Math.abs(off)).toBeCloseTo(Math.abs(snap.dx), 10) // 偏移量恰为丢失的吸附量
  })

  it('阈值外（距参考线 10px > SNAP_THRESHOLD）：无吸附位移，相对位置天然恒定', () => {
    const { relX, relY } = initialRel()
    const s = setup([makeRect('A', 10, 10), makeRect('B', 30, 10), makeRect('C', 50, 10)], ['A', 'B'], 'A')
    const snap = dragMoveNewReturnSnap(s, 200, 100) // 主导右缘 240 距最近候选 250 为 10px，网格 200 恰对齐
    expect(snap.dx).toBe(0)
    expect(snap.dy).toBe(0)
    expect(s.nodes.get('A')!.x() - s.nodes.get('B')!.x()).toBe(relX)
    expect(s.nodes.get('A')!.y() - s.nodes.get('B')!.y()).toBe(relY)
  })
})

describe('AC-03 跟随元素容器锚定——containerRegionIdFor 与单选同语义', () => {
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
    // A（主导 10,10）B（跟随 12,12）拖动 (+35,+25)mm：A → (45,35)、B → (47,37)，两中心均入 R
    const s = setup([makeRect('A', 10, 10), makeRect('B', 12, 12), region], ['A', 'B'], 'A')
    dragMoveNew(s, OX + pxv(45), OY + pxv(35))
    dragEndNew(s)
    expect(s.elements.find((e) => e.id === 'A')!.regionId).toBe('cR')
    expect(s.elements.find((e) => e.id === 'B')!.regionId).toBe('cR')

    // 单选对照：B 单独从 (12,12) 拖到 (47,37)（md null 走单选链——commitDragPosition + containerRegionIdFor）
    const pos = commitDragPosition(OX + pxv(47), OY + pxv(37), false)
    const single = containerRegionIdFor({ ...makeRect('B', 12, 12), x: pos.x, y: pos.y }, [makeRect('B', 12, 12), region])
    expect(single).toBe(s.elements.find((e) => e.id === 'B')!.regionId)
  })

  it('多选整组拖出容器：既有 regionId 被清除（跟随元素与单选同语义）', () => {
    const region = makeRegion('R', 40, 30, 30, 20)
    const a = makeRect('A', 45, 35)
    a.regionId = 'cR'
    const b = makeRect('B', 47, 37)
    b.regionId = 'cR'
    // 拖动 (−40,−30)mm：A → (5,5)、B → (7,7)，中心均出 R
    const s = setup([a, b, region], ['A', 'B'], 'A')
    dragMoveNew(s, OX + pxv(5), OY + pxv(5))
    dragEndNew(s)
    expect(s.elements.find((e) => e.id === 'A')!.regionId).toBeUndefined()
    expect(s.elements.find((e) => e.id === 'B')!.regionId).toBeUndefined()
    expect('regionId' in s.elements.find((e) => e.id === 'B')!).toBe(false) // delete 而非置空
  })
})

describe('AC-04 单选路径锚定（md null → 补丁仅主导，提交值与 #323 口径一致）', () => {
  it('followers 为空时补丁只含主导元素，值 = commitDragPosition 同参结果', () => {
    const { patches } = multiDragCommitPatches({ leaderId: 'A', leaderX: 260, leaderY: 100, lastX: 260, lastY: 100, followers: [], preview: false })
    expect(patches.size).toBe(1)
    expect(patches.get('A')).toEqual(commitDragPosition(260, 100, false))
  })
})

// 撤销单步性（一次多选拖动 = 一步撤销）在本层不可挂载组件验证（jsdom 无 2d context，
// Designer.test.tsx 桩化先例）：由代码结构保证——handleDragEnd 单一 onUpdateElements +
// 单一 onCommit（Designer.tsx applyElements/commitNow 既有链路不变），history.test.ts 既有
// 用例回归 + 浏览器走查 / 陪验实测 Ctrl+Z 整组回退与单选零回归（#180 先例：覆盖空档如实记录）。
