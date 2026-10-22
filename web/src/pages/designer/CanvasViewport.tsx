// 设计器画布（react-konva）：毫米标尺 + 网格、智能参考线吸附、空白框选多选、多选整体刚性拖动、
// 8 手柄缩放、中键平移、Ctrl+滚轮缩放、DPI 打印预览。
// 拖动过程中的高频节点操作直接走 Konva 实例（不触发 React 渲染），
// dragend / transformend 才提交状态（与原型一致）。

import { useCallback, useEffect, useLayoutEffect, useMemo, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Layer, Rect as KRect, Line as KLine, Stage, Text as KText, Transformer } from 'react-konva'
import Konva from 'konva'
import type { KonvaEventObject } from 'konva/lib/Node'
import { ElementNode } from './ElementNode'
import type { DesignElement, ImageAsset } from '../../lib/design/types'
import { containerRegionIdFor, elementById } from '../../lib/design/model'
import { commitDragPosition, contentOffset, designCanvasSize, fitScale, logicToContentMm, multiDragRigidPatches, pointerToLogic, previewCanvasSize, previewScale, PX, pxv, RULER, viewportToLogic } from '../../lib/design/geometry'
import { elementsIntersecting } from '../../lib/design/marquee'
import { computeSnap, elementModelRect, othersModelRects, unionModelRect } from '../../lib/design/snapping'
import type { SnapRect } from '../../lib/design/snapping'
import { coreShortcuts } from './shortcuts'

export interface DesignState {
  paperW: number
  paperH: number
  elements: DesignElement[]
}

export type ViewMode = 'fit' | 'preview'

interface CanvasViewportProps {
  state: DesignState
  selected: string[]
  viewMode: ViewMode
  dpi: number
  zoom: number
  gridOn: boolean
  pendingType: string | null
  /* * 画布只读锁：「看实时效果」演示期间 true——容器 pointer-events:none 全不响应，画布仍可见；
   *  不拦 Konva 事件本身，规避拖拽状态清理被破坏。 */
  locked?: boolean
  /* * 图片资源表（key → 资源）：透传给各元素节点（图片元素按 key 解码渲染）。 */
  imageAssets?: Record<string, ImageAsset>
  onSelect: (ids: string[], toggle?: boolean) => void
  onAddElement: (type: string, xMm: number, yMm: number) => void
  /* * 空占位图片元素（key 空）被无修饰键单击：选中后直接打开文件选择
   *  Shift / Ctrl 修饰点击仅多选不触发（修饰语义优先，防误弹）。 */
  onRequestImageUpload?: (id: string) => void
  /** dragend / transformend 提交（元素坐标 / 尺寸 / 锚定）。 */
  onUpdateElements: (updater: (els: DesignElement[]) => DesignElement[]) => void
  onCommit: () => void
  onZoomChange: (z: number) => void
}

/* * 多选整体刚性快照：dragStart 一次快照 + 每步独立重算（无步间差分状态）；
 *  lastApplied 为末步终态备忘（仅作 dragend 提交来源，不参与逐步推导）。 */
interface MultiDragSnapshot {
  /** 主导（被拖）元素 id。 */
  targetId: string
  /** 选中元素联合包围盒（dragStart 拖前模型换算）——多选吸附的被拖盒基形。 */
  union: SnapRect
  /** 各选中元素初始节点坐标（逻辑 px，拖前模型换算）——跟随刚性设置与同 Δ 提交的基准。 */
  items: Array<{ id: string; x: number; y: number }>
  /* * dragmove 末步写入的吸附后主导位置——dragend 提交的唯一终态来源，
   *  对松手瞬间任何节点扰动免疫（react-konva 事件 flush、Transformer 重绑等组件层时序）。 */
  lastApplied: { x: number; y: number } | null
}

/** 框选位移阈值（stage px）：松手位移低于此值不提交框选，回落为普通空白点击（清空选择）。 */
const MARQUEE_CLICK_PX = 3

/* * 框选进行态：起点同时记 stage 系（阈值判定）与逻辑系（选区矩形）双坐标。 */
interface MarqueeState {
  startStage: { x: number; y: number }
  startLogic: { x: number; y: number }
}

export function CanvasViewport(props: CanvasViewportProps) {
  const { t } = useTranslation('designer')
  const { state, selected, viewMode, dpi, zoom, gridOn, locked, imageAssets } = props
  const { paperW, paperH, elements } = state
  const preview = viewMode === 'preview'

  const containerRef = useRef<HTMLDivElement>(null)
  const stageRef = useRef<Konva.Stage>(null)
  const layerRef = useRef<Konva.Layer>(null)
  const trRef = useRef<Konva.Transformer>(null)
  const boxRef = useRef<HTMLDivElement>(null)
  const guidesRef = useRef<Konva.Line[]>([])
  const panRef = useRef({ panning: false, startX: 0, startY: 0, stageX: 0, stageY: 0 })
  const multiDragRef = useRef<MultiDragSnapshot | null>(null)
  const marqueeRef = useRef<MarqueeState | null>(null)
  const marqueeNodeRef = useRef<Konva.Rect | null>(null)
  /* * 框选一次性标记：框选提交 onSelect 后置位，handleClick 空白分支消费并复位——
   *  防紧随 mouseup 的 Konva click 把刚框选的组清空；每次 Stage mousedown 先复位清残留。 */
  const marqueeSelectedRef = useRef(false)
  /* * Transformer._proxyDrag 触发的「假拖动」节点 id 集——其 dragstart / dragmove / dragend 一律忽略：
   *  位置由主导侧每步刚性设置，dragend 吞掉防走单选提交（否则多选拖动产生第二步历史，破坏撤销单步）。 */
  const fakeDragRef = useRef<Set<string>>(new Set())

  // 闭包镜像：高频回调里读取最新状态
  const stateRef = useRef(props)
  stateRef.current = props

  const [viewport, setViewport] = useState({ w: 0, h: 0 })
  const [total, setTotal] = useState(1)

  // 画布逻辑尺寸
  const canvasSize = useMemo(
    () => (preview ? previewCanvasSize(paperW, paperH) : designCanvasSize(paperW, paperH)),
    [preview, paperW, paperH],
  )
  const ox = preview ? 0 : RULER + 10 * PX
  const oy = preview ? 0 : RULER + 10 * PX

  // 视口尺寸跟踪
  useLayoutEffect(() => {
    const el = containerRef.current
    if (!el) return
    const update = () => setViewport({ w: el.clientWidth, h: el.clientHeight })
    update()
    const ro = new ResizeObserver(update)
    ro.observe(el)
    return () => ro.disconnect()
  }, [])

  // 总缩放 = 适应窗口（或 DPI 预览）× 内容缩放
  useLayoutEffect(() => {
    const base = preview ? previewScale(dpi) : fitScale(viewport.w, viewport.h, canvasSize.w, canvasSize.h)
    setTotal(base * zoom)
  }, [viewport, canvasSize, preview, dpi, zoom])

  /** 平移不越界：画布至少一块区域在视口内。 */
  const clampStage = useCallback(() => {
    const stage = stageRef.current
    if (!stage) return
    const cw = canvasSize.w * total
    const ch = canvasSize.h * total
    const vw = viewport.w
    const vh = viewport.h
    let x = stage.x()
    let y = stage.y()
    if (cw > vw) x = Math.min(0, Math.max(vw - cw, x))
    else x = 0
    if (ch > vh) y = Math.min(0, Math.max(vh - ch, y))
    else y = 0
    if (x !== stage.x()) stage.x(x)
    if (y !== stage.y()) stage.y(y)
  }, [canvasSize, total, viewport])

  // stageBox 尺寸 / 位置（直接操作 DOM，避免重渲染循环）
  useLayoutEffect(() => {
    const box = boxRef.current
    const stage = stageRef.current
    if (!box || !stage || viewport.w === 0) return
    box.style.width = canvasSize.w * total + 'px'
    box.style.height = canvasSize.h * total + 'px'
    box.style.left = Math.max(0, (viewport.w - canvasSize.w * total) / 2) + 'px'
    box.style.top = Math.max(0, (viewport.h - canvasSize.h * total - 20) / 2) + 'px'
    stage.width(canvasSize.w * total)
    stage.height(canvasSize.h * total)
    stage.scale({ x: total, y: total })
    clampStage()
  }, [canvasSize, total, viewport, clampStage])

  // 参考线
  const clearGuides = useCallback(() => {
    guidesRef.current.forEach((n) => n.destroy())
    guidesRef.current = []
  }, [])
  const addGuide = useCallback((vertical: boolean, pos: number) => {
    const layer = layerRef.current
    if (!layer) return
    const n = new Konva.Line({
      points: vertical ? [pos, 0, pos, canvasSize.h] : [0, pos, canvasSize.w, pos],
      stroke: '#ff2d55',
      strokeWidth: 1.5,
      dash: [6, 3],
      listening: false,
      strokeScaleEnabled: false,
    })
    guidesRef.current.push(n)
    layer.add(n)
  }, [canvasSize])

  /** 拖动吸附（纯单选路径）：目标 = 画布 / 内容区 / 其它元素边缘中心，网格兜底。
   * 被拖盒与候选统一切模型盒域——getClientRect 渲染盒默认含居中描边半线宽外扩（线 +0.25mm /
   * 边框 +0.15mm 每侧），旧口径把外扩传染给吸附落点与提交坐标。多选吸附走 snapUnion 不经本函数；
   * 候选 = 仅排除被拖元素自身（othersModelRects）。 */
  const snapNode = useCallback(
    (g: Konva.Node) => {
      const p = stateRef.current
      const e = elementById(p.state.elements, g.id())
      if (!e) return
      const others = othersModelRects(p.state.elements, [e.id], ox, oy)
      const m = elementModelRect(e, ox, oy)
      const snap = computeSnap({ x: g.x(), y: g.y(), w: m.w, h: m.h }, canvasSize.w, canvasSize.h, ox, oy, paperW * PX, paperH * PX, others)
      clearGuides()
      if (snap.dx !== 0 || snap.guideX !== null) {
        g.x(g.x() + snap.dx)
        if (snap.guideX !== null) addGuide(true, snap.guideX)
      }
      if (snap.dy !== 0 || snap.guideY !== null) {
        g.y(g.y() + snap.dy)
        if (snap.guideY !== null) addGuide(false, snap.guideY)
      }
    },
    [addGuide, canvasSize, clearGuides, ox, oy, paperH, paperW],
  )

  /* * 多选联合盒吸附：被拖盒 = dragStart 联合盒 + 主导总位移；候选 = 未选中元素（组内元素零参与）；
   *  吸附量施加于主导节点，跟随元素由「初始快照 + 主导总位移」另行刚性设置。与 snapNode 共用
   *  computeSnap（含按盒左上角贴 1mm 网格的兜底）。 */
  const snapUnion = useCallback(
    (el: Konva.Node, md: MultiDragSnapshot) => {
      const p = stateRef.current
      const leader = md.items.find((i) => i.id === md.targetId)
      if (!leader) return
      const rect: SnapRect = { x: md.union.x + (el.x() - leader.x), y: md.union.y + (el.y() - leader.y), w: md.union.w, h: md.union.h }
      const others = othersModelRects(p.state.elements, md.items.map((i) => i.id), ox, oy)
      const snap = computeSnap(rect, canvasSize.w, canvasSize.h, ox, oy, paperW * PX, paperH * PX, others)
      clearGuides()
      if (snap.dx !== 0 || snap.guideX !== null) {
        el.x(el.x() + snap.dx)
        if (snap.guideX !== null) addGuide(true, snap.guideX)
      }
      if (snap.dy !== 0 || snap.guideY !== null) {
        el.y(el.y() + snap.dy)
        if (snap.guideY !== null) addGuide(false, snap.guideY)
      }
    },
    [addGuide, canvasSize, clearGuides, ox, oy, paperH, paperW],
  )

  const elementFromTarget = (target: Konva.Node | null): Konva.Node | null => {
    if (!target) return null
    if (target.hasName('element')) return target
    return target.findAncestor('.element')
  }

  // ---------- 画布交互 ----------

  const handleClick = (ev: KonvaEventObject<MouseEvent>) => {
    const p = stateRef.current
    if (p.viewMode === 'preview') return
    if (ev.evt.button !== 0 || panRef.current.panning) return
    const el = elementFromTarget(ev.target)
    if (!el) {
      if (p.pendingType) {
        const ptr = ev.target.getStage()?.getPointerPosition()
        const stage = stageRef.current
        if (ptr && stage) {
          // 指针 → 逻辑：Konva 指针为 canvas CSS 像素（已内置页面缩放修正），须除 stage.scale 才是逻辑坐标
          const logic = pointerToLogic(ptr, stage.x(), stage.y(), stage.scaleX(), stage.scaleY())
          const pos = logicToContentMm(logic.x, logic.y, false)
          p.onAddElement(p.pendingType, pos.x, pos.y)
        }
        return
      }
      if (marqueeSelectedRef.current) {
        marqueeSelectedRef.current = false
        return
      }
      p.onSelect([], false)
      return
    }
    const modifier = ev.evt.shiftKey || ev.evt.ctrlKey
    p.onSelect([el.id()], modifier)
    // 空占位图片元素无修饰键单击：选中后直接打开文件选择上传（Konva 抑制拖拽后的 click，
    // 拖动空占位不会误弹）；修饰键点击只多选，不走上传
    if (!modifier && p.onRequestImageUpload) {
      const target = elementById(p.state.elements, el.id())
      if (target?.type === 'Image' && !target.key) p.onRequestImageUpload(el.id())
    }
  }

  // ---------- 空白框选多选 ----------

  /** client 坐标 → stage 坐标（canvas CSS 像素）：含页面缩放修正，与 Konva 指针 / viewportToLogic 同口径。 */
  const clientToStagePoint = useCallback((clientX: number, clientY: number): { x: number; y: number } | null => {
    const dom = stageRef.current?.container()
    if (!dom) return null
    const rect = dom.getBoundingClientRect()
    const zx = dom.clientWidth > 0 ? rect.width / dom.clientWidth : 1
    const zy = dom.clientHeight > 0 ? rect.height / dom.clientHeight : 1
    return { x: (clientX - rect.left) / zx, y: (clientY - rect.top) / zy }
  }, [])

  /** 框选虚线矩形节点（仿参考线：直接建 Konva 实例，listening:false 不参与命中）。 */
  const ensureMarqueeNode = useCallback(() => {
    const layer = layerRef.current
    if (!layer) return null
    if (marqueeNodeRef.current) return marqueeNodeRef.current
    const n = new Konva.Rect({
      x: 0,
      y: 0,
      width: 0,
      height: 0,
      stroke: '#1a5fd0',
      strokeWidth: 1,
      dash: [4, 3],
      listening: false,
      strokeScaleEnabled: false,
    })
    marqueeNodeRef.current = n
    layer.add(n)
    return n
  }, [])

  const destroyMarqueeNode = useCallback(() => {
    const n = marqueeNodeRef.current
    marqueeNodeRef.current = null
    n?.destroy()
  }, [])

  /** 框选拖拽（document mousemove）：位移过阈值才建节点，之后每步直改 Konva 实例（不触发 React 渲染）。 */
  const onMarqueeMove = useCallback(
    (ev: MouseEvent) => {
      const m = marqueeRef.current
      const layer = layerRef.current
      const stage = stageRef.current
      if (!m || !layer || !stage) return
      const sp = clientToStagePoint(ev.clientX, ev.clientY)
      if (!sp) return
      if (!marqueeNodeRef.current && Math.hypot(sp.x - m.startStage.x, sp.y - m.startStage.y) < MARQUEE_CLICK_PX) return
      const n = ensureMarqueeNode()
      if (!n) return
      const logic = pointerToLogic(sp, stage.x(), stage.y(), stage.scaleX(), stage.scaleY())
      n.x(Math.min(m.startLogic.x, logic.x))
      n.y(Math.min(m.startLogic.y, logic.y))
      n.width(Math.abs(logic.x - m.startLogic.x))
      n.height(Math.abs(logic.y - m.startLogic.y))
      layer.batchDraw()
    },
    [clientToStagePoint, ensureMarqueeNode],
  )

  /* * 框选收尾（document mouseup，capture 阶段）：必须先于 Konva 容器 mouseup→click 触发，
   *  一次性标记才能赶在 handleClick 前置位（bubble 阶段监听晚于 click，刚框选的组会先被清空）。 */
  const onMarqueeUp = useCallback(
    (ev: MouseEvent) => {
      const m = marqueeRef.current
      marqueeRef.current = null
      document.removeEventListener('mousemove', onMarqueeMove)
      document.removeEventListener('mouseup', onMarqueeUp, true)
      destroyMarqueeNode()
      layerRef.current?.batchDraw()
      const stage = stageRef.current
      const sp = clientToStagePoint(ev.clientX, ev.clientY)
      if (!m || !stage || !sp) return
      if (Math.hypot(sp.x - m.startStage.x, sp.y - m.startStage.y) < MARQUEE_CLICK_PX) return
      const p = stateRef.current
      const logic = pointerToLogic(sp, stage.x(), stage.y(), stage.scaleX(), stage.scaleY())
      const rect = {
        x: Math.min(m.startLogic.x, logic.x),
        y: Math.min(m.startLogic.y, logic.y),
        w: Math.abs(logic.x - m.startLogic.x),
        h: Math.abs(logic.y - m.startLogic.y),
      }
      const off = contentOffset(p.viewMode === 'preview')
      marqueeSelectedRef.current = true
      p.onSelect(elementsIntersecting(rect, p.state.elements, off.x, off.y), false)
    },
    [clientToStagePoint, destroyMarqueeNode, onMarqueeMove],
  )

  /* * Stage 左键空白按下启动框选；Transformer 内部（手柄 / 背板）不属空白——命中即缩放或整组拖动，不得混入框选。 */
  const handleStageMouseDown = (ev: KonvaEventObject<MouseEvent>) => {
    marqueeSelectedRef.current = false
    const p = stateRef.current
    if (ev.evt.button !== 0 || p.viewMode === 'preview' || p.locked || p.pendingType) return
    if (elementFromTarget(ev.target)) return
    let n: Konva.Node | null = ev.target
    while (n) {
      if (n === trRef.current) return
      n = n.getParent()
    }
    const stage = ev.target.getStage()
    const ptr = stage?.getPointerPosition()
    if (!stage || !ptr) return
    marqueeRef.current = {
      startStage: { x: ptr.x, y: ptr.y },
      startLogic: pointerToLogic(ptr, stage.x(), stage.y(), stage.scaleX(), stage.scaleY()),
    }
    document.addEventListener('mousemove', onMarqueeMove)
    document.addEventListener('mouseup', onMarqueeUp, true)
  }

  // 框选手势兜底清理：组件卸载时移除 document 监听与选区节点
  useEffect(() => {
    return () => {
      document.removeEventListener('mousemove', onMarqueeMove)
      document.removeEventListener('mouseup', onMarqueeUp, true)
      destroyMarqueeNode()
    }
  }, [onMarqueeMove, onMarqueeUp, destroyMarqueeNode])

  const handleWheel = (ev: KonvaEventObject<WheelEvent>) => {
    ev.evt.preventDefault()
    if (!ev.evt.ctrlKey) return
    const p = stateRef.current
    const oldZoom = p.zoom
    const newZoom = Math.max(0.1, Math.min(8, oldZoom * (ev.evt.deltaY < 0 ? 1.1 : 1 / 1.1)))
    const stage = stageRef.current
    if (stage) {
      const base = p.viewMode === 'preview' ? previewScale(p.dpi) : fitScale(viewport.w, viewport.h, canvasSize.w, canvasSize.h)
      const oldTotal = base * oldZoom
      const newTotal = base * newZoom
      const ptr = stage.getPointerPosition()
      if (ptr) {
        stage.x(ptr.x - ((ptr.x - stage.x()) * newTotal) / oldTotal)
        stage.y(ptr.y - ((ptr.y - stage.y()) * newTotal) / oldTotal)
      }
    }
    p.onZoomChange(newZoom)
  }

  // 中键平移（DOM 级，document 监听保证复位）
  useEffect(() => {
    const dom = stageRef.current?.container()
    if (!dom) return
    const onDown = (ev: MouseEvent) => {
      if (ev.button !== 1) return
      ev.preventDefault()
      const stage = stageRef.current!
      panRef.current = { panning: true, startX: ev.clientX, startY: ev.clientY, stageX: stage.x(), stageY: stage.y() }
      document.addEventListener('mousemove', onMove)
      document.addEventListener('mouseup', onUp)
    }
    const onMove = (ev: MouseEvent) => {
      const pan = panRef.current
      if (!pan.panning) return
      const stage = stageRef.current!
      stage.x(pan.stageX + (ev.clientX - pan.startX))
      stage.y(pan.stageY + (ev.clientY - pan.startY))
      clampStage()
      stage.batchDraw()
    }
    const onUp = () => {
      panRef.current.panning = false
      document.removeEventListener('mousemove', onMove)
      document.removeEventListener('mouseup', onUp)
    }
    dom.addEventListener('mousedown', onDown)
    return () => {
      dom.removeEventListener('mousedown', onDown)
      document.removeEventListener('mousemove', onMove)
      document.removeEventListener('mouseup', onUp)
    }
  }, [clampStage])

  // 拖入画布（HTML5 DnD，用 clientX/Y 几何换算，不依赖 Konva 指针）
  const handleDrop = (ev: React.DragEvent) => {
    ev.preventDefault()
    const p = stateRef.current
    const type = ev.dataTransfer.getData('text/plain')
    if (!type || p.viewMode === 'preview') return
    const dom = stageRef.current?.container()
    const stage = stageRef.current
    if (!dom || !stage) return
    // client → 逻辑：含页面缩放修正（rect.width/clientWidth），DnD 事件不经过 Konva 指针
    const logic = viewportToLogic({
      clientX: ev.clientX,
      clientY: ev.clientY,
      rect: dom.getBoundingClientRect(),
      clientWidth: dom.clientWidth,
      clientHeight: dom.clientHeight,
      stageX: stage.x(),
      stageY: stage.y(),
      scaleX: stage.scaleX(),
      scaleY: stage.scaleY(),
    })
    const pos = logicToContentMm(logic.x, logic.y, false)
    p.onAddElement(type, pos.x, pos.y)
  }

  // ---------- 拖动（单选吸附 + 多选整体刚性） ----------
  const handleDragStart = (ev: KonvaEventObject<DragEvent>) => {
    const p = stateRef.current
    const el = elementFromTarget(ev.target)
    if (el && p.selected.length > 1 && p.selected.includes(el.id())) {
      // 主导权防御：Konva Transformer 的 _proxyDrag 会在主导首次 dragmove 时把其余绑定节点强制
      // startDrag——跟随元素由此产生「假 dragstart」，不拦截会顶替快照（松手瞬移根因）。
      // md 在场且来者非主导 → 登记假拖动集并忽略；不能调 stopDrag()——它会终止整个手势的
      // 全部拖动元素（主导一并置 stopped 即刻提交）。
      if (multiDragRef.current && el.id() !== multiDragRef.current.targetId) {
        fakeDragRef.current.add(el.id())
        return
      }
      fakeDragRef.current.clear()
      // 整体刚性快照：联合盒与各元素初始坐标一律从拖前模型换算（不依赖节点存在，拖动中途删除的元素自然跳过）
      const sel = p.selected.flatMap((id) => {
        const m = elementById(p.state.elements, id)
        return m ? [m] : []
      })
      multiDragRef.current = {
        targetId: el.id(),
        union: unionModelRect(sel, ox, oy),
        items: sel.map((m) => ({ id: m.id, x: ox + pxv(m.x), y: oy + pxv(m.y) })),
        lastApplied: null,
      }
    } else {
      multiDragRef.current = null
      fakeDragRef.current.clear()
    }
  }

  const handleDragMove = (ev: KonvaEventObject<DragEvent>) => {
    const layer = layerRef.current
    const el = elementFromTarget(ev.target)
    if (!el || !layer) return
    const md = multiDragRef.current
    // 单选路径（md null）：snapNode 吸附
    if (!md) {
      snapNode(el)
      layer.batchDraw()
      return
    }
    // 多选整体刚性：吸附单位 = 整组联合盒，每步独立重算（dragStart 快照 + 主导总位移），无步间状态
    // ——不得引入逐步差分链（差分累积误差致元素分离，已废除）。跟随节点每步设「初始快照 + 主导总位移
    //（含吸附）」，组内相对位置刚性不变；非主导（假拖动）的 dragmove 一律忽略——主导侧 fire 序在先，随后覆盖。
    if (el.id() !== md.targetId) return
    snapUnion(el, md)
    const leader = md.items.find((i) => i.id === md.targetId)
    if (leader) {
      const dx = el.x() - leader.x
      const dy = el.y() - leader.y
      md.items.forEach((item) => {
        if (item.id === md.targetId) return
        const n = layer.findOne('#' + item.id)
        if (n) {
          n.x(item.x + dx)
          n.y(item.y + dy)
        }
      })
    }
    // 末步吸附后主导位置写入快照——dragend 提交的唯一终态来源（松手段纯提交不读节点现值）。
    md.lastApplied = { x: el.x(), y: el.y() }
    layer.batchDraw()
  }

  const handleDragEnd = (ev: KonvaEventObject<DragEvent>) => {
    const p = stateRef.current
    const layer = layerRef.current
    const el = elementFromTarget(ev.target)
    const e = el ? elementById(p.state.elements, el.id()) : undefined
    if (!el || !e) {
      multiDragRef.current = null
      fakeDragRef.current.clear()
      clearGuides()
      return
    }
    const md = multiDragRef.current
    // 假拖动节点的 dragend 一律吞掉——不提交（防走单选提交产生第二步历史）、不清快照；
    // 多选提交权唯一归属主导元素。
    if (fakeDragRef.current.has(el.id())) {
      fakeDragRef.current.delete(el.id())
      return
    }
    if (md && el.id() !== md.targetId) return
    multiDragRef.current = null
    // 单选路径（md 为 null）：仅被拖元素提交
    if (!md) {
      // Konva 松手时节点保持末次 dragmove 位置（只置状态不更新位置），松手再吸一次幂等（已贴线 dx=0）
      snapNode(el)
      // 从节点原点（= 模型盒原点）提交——落点 mm 与参考线严格一致（残差仅两位舍入）；
      // 取渲染盒左上角会被描边外扩带偏 −0.15/−0.25mm（旧口径踩过）。
      const pos = commitDragPosition(el.x(), el.y(), false)
      const regionId = p.viewMode === 'preview' ? e.regionId : containerRegionIdFor({ ...e, x: pos.x, y: pos.y }, p.state.elements)
      clearGuides()
      p.onUpdateElements((els) =>
        els.map((o) => {
          if (o.id !== e.id) return o
          const next = { ...o, x: pos.x, y: pos.y }
          if (regionId) next.regionId = regionId
          else delete next.regionId
          return next
        }),
      )
      p.onCommit()
      return
    }
    // 多选路径：松手段纯提交——终态统一取 dragmove 末步写入的 lastApplied，不读节点现值、不跑二次吸附
    //（Konva 松手只置状态不更新位置，末次 dragmove 的吸附就是终态；lastApplied 对松手瞬间组件层时序扰动免疫）。
    // 一次 onUpdateElements + 一次 onCommit = 一步撤销（Ctrl+Z 整组回退）。
    // lastApplied 兜底分支不可达（startDrag 必伴随至少一次 dragmove）——防御性回退节点现值防 undefined 入算式。
    const leaderTerminal = md.lastApplied ?? { x: el.x(), y: el.y() }
    const { patches } = multiDragRigidPatches({ leaderId: md.targetId, items: md.items, leaderX: leaderTerminal.x, leaderY: leaderTerminal.y, preview: false })
    // 跟随节点直接按补丁设视觉（视觉 = 模型）；findOne 未命中 / 补丁缺失跳过（边缘路径不崩溃）
    md.items.forEach((item) => {
      if (item.id === md.targetId) return
      const patch = patches.get(item.id)
      const n = layer ? layer.findOne('#' + item.id) : null
      if (n && patch) {
        n.x(ox + pxv(patch.x))
        n.y(oy + pxv(patch.y))
      }
    })
    clearGuides()
    p.onUpdateElements((els) =>
      els.map((o): DesignElement => {
        const patch = patches.get(o.id)
        if (!patch) return o
        const next = { ...o, x: patch.x, y: patch.y }
        // 容器锚定与单选同语义重算；预览态防御性保留原值（拖动只可能发生在设计态）
        const regionId = p.viewMode === 'preview' ? o.regionId : containerRegionIdFor({ ...o, x: patch.x, y: patch.y }, els)
        if (regionId) next.regionId = regionId
        else delete next.regionId
        return next
      }),
    )
    p.onCommit()
  }

  // ---------- 8 手柄缩放 ----------
  const handleTransformEnd = () => {
    const p = stateRef.current
    const layer = layerRef.current
    const tr = trRef.current
    if (!layer || !tr) return
    const updates = new Map<string, Partial<DesignElement>>()
    tr.nodes().forEach((g) => {
      const e = elementById(p.state.elements, g.id())
      if (!e) return
      // skipStroke 剔除居中描边外扩（盒 = 模型盒）——否则反复缩放每轮 x/y 偏 −半线宽、
      // w/h 虚增 +线宽，逐次累计漂移（踩过）。
      const r = g.getClientRect({ relativeTo: layer, skipStroke: true })
      let w = r.width / PX
      let h = r.height / PX
      if (e.type === 'QrCode') w = h = Math.max(w, h)
      updates.set(e.id, {
        x: Math.round((r.x - ox) / PX * 100) / 100,
        y: Math.round((r.y - oy) / PX * 100) / 100,
        w: Math.round(w * 100) / 100,
        h: Math.round(h * 100) / 100,
      })
      // 重置 Transformer 产生的 scale，避免残留拉伸（文本只改遮罩区域，字高保持独立）
      g.scaleX(1)
      g.scaleY(1)
    })
    p.onUpdateElements((els) =>
      els.map((o): DesignElement => {
        const patch = updates.get(o.id)
        return patch ? ({ ...o, ...patch } as DesignElement) : o
      }),
    )
    p.onCommit()
  }

  // Transformer 绑定选中节点
  useEffect(() => {
    const tr = trRef.current
    const layer = layerRef.current
    if (!tr || !layer) return
    const nodes = selected
      .map((id) => layer.findOne('#' + id))
      .filter((n): n is Konva.Node => n !== null && n !== undefined)
    tr.nodes(preview ? [] : nodes)
    tr.getLayer()?.batchDraw()
  }, [selected, elements, preview])

  // 画布网格
  const gridNodes = useMemo(() => {
    if (preview || !gridOn) return []
    const step = 5 * PX
    const gw = (paperW + 10 * 2) * PX
    const gh = (paperH + 10 * 2) * PX
    const nodes: React.ReactNode[] = []
    for (let x = 0; x <= gw; x += step) {
      nodes.push(
        <KLine key={'v' + x} points={[RULER + x, RULER, RULER + x, RULER + gh]} stroke={(x / step) % 2 === 0 ? '#e3e9f0' : '#eef1f5'} strokeWidth={1} listening={false} strokeScaleEnabled={false} />,
      )
    }
    for (let y = 0; y <= gh; y += step) {
      nodes.push(
        <KLine key={'h' + y} points={[RULER, RULER + y, RULER + gw, RULER + y]} stroke={(y / step) % 2 === 0 ? '#e3e9f0' : '#eef1f5'} strokeWidth={1} listening={false} strokeScaleEnabled={false} />,
      )
    }
    return nodes
  }, [preview, gridOn, paperH, paperW])

  // 毫米标尺（画进 Konva 与内容同坐标系；标签边缘用主色粗刻度）
  const rulerNodes = useMemo(() => {
    if (preview) return []
    const nodes: React.ReactNode[] = []
    const wMm = paperW + 20
    const hMm = paperH + 20
    const tick = (m: number, vertical: boolean) => {
      const isEdge = m === 10 || m === 10 + (vertical ? paperH : paperW)
      const len = m % 10 === 0 || isEdge ? 14 : m % 5 === 0 ? 9 : 4
      const color = isEdge ? '#1a5fd0' : '#9aa6b4'
      const width = isEdge ? 2 : 1
      if (vertical) {
        const y = RULER + m * PX
        nodes.push(<KLine key={'vy' + m} points={[RULER - len, y, RULER, y]} stroke={color} strokeWidth={width} listening={false} strokeScaleEnabled={false} />)
        if (m % 10 === 0 || isEdge) {
          nodes.push(<KText key={'vt' + m} x={RULER - 13} y={y + 2} text={String(m)} fontSize={9} fontFamily="Consolas, Microsoft YaHei" fill={isEdge ? '#1a5fd0' : '#667'} listening={false} />)
        }
      } else {
        const x = RULER + m * PX
        nodes.push(<KLine key={'hx' + m} points={[x, RULER - len, x, RULER]} stroke={color} strokeWidth={width} listening={false} strokeScaleEnabled={false} />)
        if (m % 10 === 0 || isEdge) {
          nodes.push(<KText key={'ht' + m} x={x + 2} y={RULER - 12} text={String(m)} fontSize={9} fontFamily="Consolas, Microsoft YaHei" fill={isEdge ? '#1a5fd0' : '#667'} listening={false} />)
        }
      }
    }
    for (let m = 0; m <= wMm; m++) tick(m, false)
    for (let m = 0; m <= hMm; m++) tick(m, true)
    return nodes
  }, [paperH, paperW, preview])

  return (
    <div
      ref={containerRef}
      className={'canvas-viewport' + (locked ? ' canvas-locked' : '')}
      style={{ flex: 1, overflow: 'hidden', background: 'var(--bg-deep)', position: 'relative', minHeight: 0 }}
      onDragOver={(ev) => {
        ev.preventDefault()
        ev.dataTransfer.dropEffect = 'copy'
      }}
      onDrop={handleDrop}
    >
      <div ref={boxRef} style={{ position: 'absolute', background: preview ? '#fff' : '#fff', border: '1px solid #9aa6b4', boxShadow: '0 2px 8px rgba(0,0,0,.08)', left: 0, top: 0 }}>
        <Stage ref={stageRef} width={canvasSize.w * total || 1} height={canvasSize.h * total || 1} scaleX={total} scaleY={total} onMouseDown={handleStageMouseDown} onClick={handleClick} onWheel={handleWheel}>
          <Layer ref={layerRef}>
            {!preview && <KRect x={0} y={0} width={RULER} height={RULER} fill="#f7f8fa" stroke="#d8dee6" strokeWidth={1} listening={false} strokeScaleEnabled={false} />}
            {gridNodes}
            {!preview && (
              <KRect
                x={ox}
                y={oy}
                width={paperW * PX}
                height={paperH * PX}
                stroke="#b0b8c4"
                strokeWidth={1}
                dash={[8, 4]}
                listening={false}
                strokeScaleEnabled={false}
              />
            )}
            {rulerNodes}
            {elements.map((e) => (
              <ElementNode
                key={e.id}
                e={e}
                editable={!preview}
                ox={ox}
                oy={oy}
                imageAssets={imageAssets}
                onDragStart={handleDragStart}
                onDragMove={handleDragMove}
                onDragEnd={handleDragEnd}
              />
            ))}
            <Transformer
              ref={trRef}
              rotateEnabled={false}
              keepRatio={false}
              anchorSize={8}
              anchorStroke="#1a5fd0"
              anchorFill="#fff"
              borderStroke="#1a5fd0"
              borderDash={[4, 2]}
              onTransformEnd={handleTransformEnd}
            />
          </Layer>
        </Stage>
      </div>
      {!preview && (
        <div
          style={{
            position: 'absolute',
            top: 8,
            left: '50%',
            transform: 'translateX(-50%)',
            background: 'rgba(29,38,51,.78)',
            color: '#fff',
            fontSize: 12,
            padding: '4px 12px',
            borderRadius: 999,
            pointerEvents: 'none',
            fontFamily: 'var(--font-mono)',
            whiteSpace: 'nowrap',
            zIndex: 2,
          }}
        >
          {coreShortcuts()}
        </div>
      )}
      {preview && (
        <div style={{ position: 'absolute', top: 8, left: '50%', transform: 'translateX(-50%)', background: 'rgba(29,38,51,.78)', color: '#fff', fontSize: 12, padding: '4px 12px', borderRadius: 999, pointerEvents: 'none', fontFamily: 'var(--font-mono)' }}>
          {t('canvas.previewHint', { dpi })}
        </div>
      )}
    </div>
  )
}
