// 设计器画布（react-konva）：毫米标尺 + 网格、智能参考线吸附、多选拖动、
// 8 手柄缩放、中键平移、Ctrl+滚轮缩放、DPI 打印预览。
// 拖动过程中的高频节点操作直接走 Konva 实例（不触发 React 渲染），
// dragend / transformend 才提交状态（与原型一致）。

import { useCallback, useEffect, useLayoutEffect, useMemo, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Layer, Rect as KRect, Line as KLine, Stage, Text as KText, Transformer } from 'react-konva'
import Konva from 'konva'
import type { KonvaEventObject } from 'konva/lib/Node'
import { ElementNode } from './ElementNode'
import type { DesignElement } from '../../lib/design/types'
import { containerRegionIdFor, elementById } from '../../lib/design/model'
import { commitDragPosition, designCanvasSize, fitScale, logicToContentMm, multiDragCommitPatches, pointerToLogic, previewCanvasSize, previewScale, PX, RULER, viewportToLogic } from '../../lib/design/geometry'
import { computeSnap, elementModelRect } from '../../lib/design/snapping'
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
  onSelect: (ids: string[], toggle?: boolean) => void
  onAddElement: (type: string, xMm: number, yMm: number) => void
  /** dragend / transformend 提交（元素坐标 / 尺寸 / 锚定）。 */
  onUpdateElements: (updater: (els: DesignElement[]) => DesignElement[]) => void
  onCommit: () => void
  onZoomChange: (z: number) => void
}

export function CanvasViewport(props: CanvasViewportProps) {
  // 迭代 112（#246）：常驻提示条与打印预览提示随界面语言（订阅重渲染）
  const { t } = useTranslation('designer')
  const { state, selected, viewMode, dpi, zoom, gridOn, locked } = props
  const { paperW, paperH, elements } = state
  const preview = viewMode === 'preview'

  const containerRef = useRef<HTMLDivElement>(null)
  const stageRef = useRef<Konva.Stage>(null)
  const layerRef = useRef<Konva.Layer>(null)
  const trRef = useRef<Konva.Transformer>(null)
  const boxRef = useRef<HTMLDivElement>(null)
  const guidesRef = useRef<Konva.Line[]>([])
  const panRef = useRef({ panning: false, startX: 0, startY: 0, stageX: 0, stageY: 0 })
  const multiDragRef = useRef<{ targetId: string; lastX: number; lastY: number } | null>(null)

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

  /** 拖动吸附（原型 snapNode）：目标 = 画布/内容区/其它元素边缘中心，网格兜底。
   *  迭代 130（#323）：被拖盒与候选统一切模型盒域——getClientRect 渲染盒默认含居中描边半线宽外扩
   *  （线 +0.25mm / 边框 +0.15mm / 图片占位描边 +0.125mm 每侧），旧口径把外扩传染给吸附落点与提交坐标。
   *  被拖盒 = 节点原点（= 模型盒原点）+ 模型宽高；候选 = 其它元素模型盒（纯模型计算，去逐元素 findOne）。 */
  const snapNode = useCallback(
    (g: Konva.Node) => {
      const p = stateRef.current
      const e = elementById(p.state.elements, g.id())
      if (!e) return
      // 多选拖动跟随元素排除出候选：跟随元素的模型坐标停留在拖前位置，纳入会把主导元素吸到「残影盒」
      const followerIds = multiDragRef.current ? p.selected : []
      const others = p.state.elements
        .filter((o) => o.id !== e.id && !followerIds.includes(o.id))
        .map((o) => elementModelRect(o, ox, oy))
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

  // ---------- 拖动（多选跟随 + 吸附） ----------
  const handleDragStart = (ev: KonvaEventObject<DragEvent>) => {
    const p = stateRef.current
    const el = elementFromTarget(ev.target)
    if (el && p.selected.length > 1 && p.selected.includes(el.id())) {
      multiDragRef.current = { targetId: el.id(), lastX: el.x(), lastY: el.y() }
    } else {
      multiDragRef.current = null
    }
  }

  const handleDragMove = (ev: KonvaEventObject<DragEvent>) => {
    const p = stateRef.current
    const layer = layerRef.current
    const el = elementFromTarget(ev.target)
    if (!el || !layer) return
    const md = multiDragRef.current
    // 迭代 131（#324）：吸附先行。Konva 每次 dragmove 以指针位置覆盖上一步吸附（dragEnd「松手再吸一次」
    // 同据），故 md.lastX/lastY 推进到吸附后的主导位置，跟随元素每步获得含吸附修正的实际视觉位移——
    // 吸附生效期间组合相对位置不散开。若还原为先记后吸的旧序，每步吸附量对跟随元素永久丢失
    // （#324 缺陷 2：拖拽期间对齐点失效、元素偏移 ≤8px=2mm）。
    snapNode(el)
    if (md) {
      const dx = el.x() - md.lastX
      const dy = el.y() - md.lastY
      md.lastX = el.x()
      md.lastY = el.y()
      p.selected.forEach((id) => {
        if (id === md.targetId) return
        const n = layer.findOne('#' + id)
        if (n) {
          n.x(n.x() + dx)
          n.y(n.y() + dy)
        }
      })
    }
    layer.batchDraw()
  }

  const handleDragEnd = (ev: KonvaEventObject<DragEvent>) => {
    const p = stateRef.current
    const layer = layerRef.current
    const el = elementFromTarget(ev.target)
    const e = el ? elementById(p.state.elements, el.id()) : undefined
    if (!el || !e) {
      multiDragRef.current = null
      clearGuides()
      return
    }
    // Konva 拖拽会用指针位置覆盖 dragmove 里的吸附，松手再吸一次保证落点精确
    // （multiDragRef 在吸附后才清零：末次吸附同样把多选跟随元素排除出候选）
    snapNode(el)
    // 迭代 131（#324）：先读 multiDragRef 再清零——末次吸附量 snapDx/Dy 经 multiDragCommitPatches
    // 同步施加于跟随元素（覆盖 dragEnd 再吸附与 dragmove 末步吸附不一致的残差）。
    const md = multiDragRef.current
    multiDragRef.current = null
    // 单选路径（md 为 null）零变化：仅主导元素提交，regionId 与提交链与 #323 口径逐行等价
    if (!md) {
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
    // 多选路径（迭代 131 · #324）：跟随元素与主导同位移落库——先收集跟随节点坐标（findOne 未命中
    // 跳过该元素：拖动中途被删除等边缘路径不提交不崩溃），补丁一次应用全部选中元素、单一 onCommit
    // （一次多选拖动 = 一步撤销，Ctrl+Z 整组回退）。
    const followerNodes = p.selected
      .filter((id) => id !== md.targetId)
      .map((id) => ({ id, node: layer ? layer.findOne('#' + id) : null }))
      .filter((f): f is { id: string; node: Konva.Node } => f.node !== null && f.node !== undefined)
    const followers = followerNodes.map(({ id, node }) => ({ id, x: node.x(), y: node.y() }))
    const { snapDx, snapDy, patches } = multiDragCommitPatches({ leaderId: e.id, leaderX: el.x(), leaderY: el.y(), lastX: md.lastX, lastY: md.lastY, followers, preview: false })
    // 先按末次吸附量命令式修跟随节点（视觉 = 模型），再一次提交
    followerNodes.forEach(({ node }) => {
      node.x(node.x() + snapDx)
      node.y(node.y() + snapDy)
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
