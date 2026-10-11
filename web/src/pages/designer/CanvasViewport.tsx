// 设计器画布（react-konva）：毫米标尺 + 网格、智能参考线吸附、多选整体刚性拖动、
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
import { commitDragPosition, designCanvasSize, fitScale, logicToContentMm, multiDragRigidPatches, pointerToLogic, previewCanvasSize, previewScale, PX, pxv, RULER, viewportToLogic } from '../../lib/design/geometry'
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
  /** 画布只读锁（迭代 126 · #308 AC-05）：「看实时效果」演示期间 true——容器 pointer-events:none，
   *  选中 / 拖拽 / 缩放 / 滚轮 / 拖入全不响应，画布仍可见；不拦 Konva 事件本身，规避拖拽状态清理被破坏。 */
  locked?: boolean
  /** 图片资源表（key → 资源，迭代 132 · #329）：透传给各元素节点（图片元素按 key 解码渲染）。 */
  imageAssets?: Record<string, ImageAsset>
  onSelect: (ids: string[], toggle?: boolean) => void
  onAddElement: (type: string, xMm: number, yMm: number) => void
  /** dragend / transformend 提交（元素坐标 / 尺寸 / 锚定）。 */
  onUpdateElements: (updater: (els: DesignElement[]) => DesignElement[]) => void
  onCommit: () => void
  onZoomChange: (z: number) => void
}

/** 多选整体刚性快照（迭代 131 返工 · #324 方案 v2/v3——用户拍板「整体刚性」）：
 *  dragStart 一次快照 + 每步独立重算（v1 的 lastX/lastY 逐步差分已废除，差分链不复存在）；
 *  v3 增 lastApplied 末步终态备忘（仅作 dragend 提交来源，不参与逐步推导——非步间差分状态）。 */
interface MultiDragSnapshot {
  /** 主导（被拖）元素 id。 */
  targetId: string
  /** 选中元素联合包围盒（dragStart 拖前模型换算）——多选吸附的被拖盒基形。 */
  union: SnapRect
  /** 各选中元素初始节点坐标（逻辑 px，拖前模型换算）——跟随刚性设置与同 Δ 提交的基准。 */
  items: Array<{ id: string; x: number; y: number }>
  /** dragmove 末步写入的吸附后主导位置（#324 方案 v3）——dragend 提交的唯一终态来源，
   *  对松手瞬间任何节点扰动免疫（react-konva 事件后同步 flush、Transformer 重绑等组件层时序）。 */
  lastApplied: { x: number; y: number } | null
}

export function CanvasViewport(props: CanvasViewportProps) {
  // 迭代 112（#246）：常驻提示条与打印预览提示随界面语言（订阅重渲染）
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
  /** Transformer._proxyDrag 触发的「假拖动」节点 id 集（#324 方案 v3）——这些节点的
   *  dragstart / dragmove / dragend 一律忽略：位置由主导侧每步刚性设置，dragend 吞掉
   *  防走单选提交（否则同一次多选拖动产生第二步历史，破坏撤销单步）。 */
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
    // 迭代 93（#151 F-06）：clampStage 为 useCallback（依赖同三项），入依赖消除 exhaustive-deps 告警
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

  /** 拖动吸附（原型 snapNode，纯单选路径）：目标 = 画布/内容区/其它元素边缘中心，网格兜底。
   *  迭代 130（#323）：被拖盒与候选统一切模型盒域——getClientRect 渲染盒默认含居中描边半线宽外扩
   *  （线 +0.25mm / 边框 +0.15mm / 图片占位描边 +0.125mm 每侧），旧口径把外扩传染给吸附落点与提交坐标。
   *  被拖盒 = 节点原点（= 模型盒原点）+ 模型宽高；候选 = 其它元素模型盒（纯模型计算，去逐元素 findOne）。
   *  迭代 131 返工（#324 方案 v2）：多选吸附改走联合盒路径（snapUnion），不经本函数——本函数不再耦合
   *  multiDragRef（v1 的跟随元素候选排除在 v2 中不可达：多选路径不调用本函数），候选 = 仅排除被拖元素
   *  自身（othersModelRects 构造，与 #323 口径逐行等价）。 */
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

  /** 多选联合盒吸附（迭代 131 返工 · #324 方案 v2，用户拍板「整体刚性」）：被拖盒 = dragStart 联合盒
   *  + 主导总位移（Konva dragmove fire 时节点已为本步指针位置——DragAndDrop._drag 实读）；候选 = 未选中
   *  元素（组内元素零参与——既不作被拖盒也不作候选，#180 ④「残影盒」防御的语义超集）；吸附量施加于
   *  主导节点，跟随元素由「初始快照 + 主导总位移」另行刚性设置。与单选 snapNode 共用 computeSnap
   *  （含按盒左上角贴 1mm 网格的兜底——此处即联合盒左上角）。 */
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
      p.onSelect([], false)
      return
    }
    p.onSelect([el.id()], ev.evt.shiftKey || ev.evt.ctrlKey)
  }

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
      // 主导权防御（#324 方案 v3，真实浏览器插桩定位）：Konva Transformer 的 _proxyDrag 会在主导元素
      // 首次 dragmove 时把其余绑定节点 setAbsolutePosition(+dx) 后强制 startDrag——跟随元素由此产生
      // 「假 dragstart」，若不拦截会覆盖本快照（v2 松手瞬移根因：单例 multiDragRef 被跟随元素的快照
      // 顶替，dragend 以「主导节点位置 × 跟随快照」错配算补丁）。桌面单指针下一个手势只有一个主导；
      // md 已在场且来者非主导 → 登记进假拖动集并忽略（不覆盖快照）。不能调 stopDrag()——它会终止
      // 整个手势的全部拖动元素（DragAndDrop._endDragBefore 全局循环，主导一并置 stopped 即刻提交）。
      if (multiDragRef.current && el.id() !== multiDragRef.current.targetId) {
        fakeDragRef.current.add(el.id())
        return
      }
      fakeDragRef.current.clear()
      // 整体刚性快照（迭代 131 返工 · #324 方案 v2，用户拍板）：联合盒与各元素初始坐标一律从拖前
      // 模型换算（不依赖节点存在——拖动中途删除元素时快照按 elementById 过滤、findOne 未命中自然跳过）
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
    // 单选路径（md null）：snapNode 吸附与 #323 口径逐行一致
    if (!md) {
      snapNode(el)
      layer.batchDraw()
      return
    }
    // 多选整体刚性（迭代 131 返工 · #324 方案 v2，用户拍板「整体刚性语义」）：吸附单位 = 整组联合盒，
    // 每步独立重算（dragStart 快照 + 主导总位移），无步间状态——v1 的 lastX/lastY 逐步差分链
    // （用户实测「分离更严重」的头号可疑结构）已废除，差分累积误差结构性不可能。
    // 跟随节点每步直接设「初始快照 + 主导总位移（含吸附）」，组内相对位置刚性不变。
    // v3（#324）：非主导节点（Transformer._proxyDrag 假拖动）的 dragmove 一律忽略——其节点位置
    // 由 Konva _setDragPosition 拉到指针位，但主导侧的 dragmove 随后每步刚性覆盖（fire 序主导在先）。
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
    // v3（#324）：末步吸附后主导位置写入快照——dragend 提交的唯一终态来源（松手段纯提交，
    // 不再读节点现值，对松手瞬间任何组件层时序扰动免疫）。
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
    // v3（#324）：Transformer._proxyDrag 假拖动节点的 dragend 一律吞掉——不提交（防走单选提交
    // 产生第二步历史破坏撤销单步）、不清快照；多选提交权唯一归属主导元素。
    if (fakeDragRef.current.has(el.id())) {
      fakeDragRef.current.delete(el.id())
      return
    }
    if (md && el.id() !== md.targetId) return
    multiDragRef.current = null
    // 单选路径（md 为 null）零变化：仅主导元素提交，regionId 与提交链与 #323 口径逐行等价
    if (!md) {
      // Konva 拖拽松手时节点保持末次 dragmove 位置（DragAndDrop._endDragBefore 实读：只置状态不更新
      // 位置），松手再吸一次幂等（已贴线 dx=0），保证落点精确
      snapNode(el)
      // 迭代 130（#323）：从节点原点（= 模型盒原点）提交——落点 mm 与参考线严格一致，
      // 残差仅剩两位舍入（≤0.005mm）；旧口径取渲染盒左上角，被描边外扩带偏 −0.15/−0.25mm。
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
    // 多选路径（迭代 131 返工 · #324 方案 v3）：松手段纯提交——不再跑二次联合盒吸附，终态也不读节点
    // 现值，二者统一取 dragmove 末步写入快照的 lastApplied（吸附后主导位置）。依据（Konva 源码实读
    // ＋浏览器插桩定位）：DragAndDrop._endDragBefore 松手只置 dragStatus 不更新节点位置，末次 dragmove
    // 的吸附就是终态，「松手再吸一次防指针覆盖」的存在前提在多选路径不成立（指针覆盖只发生在 dragmove
    // 序列内）；lastApplied 口径则对松手瞬间任何节点扰动（react-konva 事件处理器返回点同步 flush、
    // Transformer 重绑等组件层时序）免疫——修复的承重件在此，删二次吸附只是去掉扰动值的另一个消费点。
    // 主导权防御（假 dragstart/dragmove/dragend 忽略）见 handleDragStart / handleDragMove。
    // 一次 onUpdateElements + 一次 onCommit = 一步撤销（Ctrl+Z 整组回退）。
    // lastApplied 兜底（dragstart 后从未 dragmove 即 dragend）：Konva _drag 实读 startDrag 必伴随至少
    // 一次 dragmove，该分支不可达——防御性回退节点现值防 undefined 入算式。
    const leaderTerminal = md.lastApplied ?? { x: el.x(), y: el.y() }
    const { patches } = multiDragRigidPatches({ leaderId: md.targetId, items: md.items, leaderX: leaderTerminal.x, leaderY: leaderTerminal.y, preview: false })
    // 跟随节点直接按补丁设视觉（ElementNode 算式 x = ox + pxv(e.x)，视觉 = 模型）；findOne 未命中 /
    // 补丁缺失跳过（拖动中途被删除等边缘路径不提交不崩溃）
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
        // 容器锚定与单选同语义重算（主导 + 跟随统一；预览态防御性保留原值——拖动只可能发生在设计态）
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
      // 迭代 130（#323）：skipStroke 剔除居中描边外扩（盒 = 模型盒），保留 Konva 变换 / 翻折归一化数学——
      // 旧口径每轮提交 x/y 各偏 −半线宽、w/h 各虚增 +线宽，反复缩放逐次累计漂移（线 −0.25/+0.5、边框 −0.15/+0.3 每轮）。
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
      // 重置 Transformer 拖拽产生的 scale，避免残留拉伸（与原型一致：文本只改遮罩区域，字高保持独立）
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
        <Stage ref={stageRef} width={canvasSize.w * total || 1} height={canvasSize.h * total || 1} scaleX={total} scaleY={total} onClick={handleClick} onWheel={handleWheel}>
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
