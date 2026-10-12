// 几何换算与常量（与原型一致）

/** 1mm = 4px（设计逻辑像素）。 */
export const PX = 4
/** 画布四周留白（mm）。 */
export const PAD_MM = 10
/** 标尺区宽度（逻辑 px，画进 Konva 与内容同坐标系）。 */
export const RULER = 20

/** px → mm。 */
export const mm = (px: number): number => px / PX
/** mm → px。 */
export const pxv = (v: number): number => v * PX
/** 保留两位小数。 */
export const r2 = (v: number): number => Math.round((Number(v) || 0) * 100) / 100
/** 数值夹取。 */
export const clamp = (v: number, min: number, max: number): number => Math.min(max, Math.max(min, v))

/** 打印 DPI 对应的每毫米点数（203dpi → 8，300dpi → 12）。 */
export function dotsPerMm(dpi: number): number {
  return Math.round(dpi / 25.4)
}

/** DPI 打印预览相对设计点（4px/mm）的放大倍数。 */
export function previewScale(dpi: number): number {
  return dotsPerMm(dpi) / PX
}

/** 设计态画布总尺寸（含标尺区 + 留白）。 */
export function designCanvasSize(paperW: number, paperH: number): { w: number; h: number } {
  return { w: RULER + (paperW + PAD_MM * 2) * PX, h: RULER + (paperH + PAD_MM * 2) * PX }
}

/** 预览态画布总尺寸（仅标签宽高范围）。 */
export function previewCanvasSize(paperW: number, paperH: number): { w: number; h: number } {
  return { w: paperW * PX, h: paperH * PX }
}

/** 内容区偏移：设计态 = 标尺 + 留白；预览态 = 0。 */
export function contentOffset(preview: boolean): { x: number; y: number } {
  return preview ? { x: 0, y: 0 } : { x: RULER + PAD_MM * PX, y: RULER + PAD_MM * PX }
}

/** 逻辑坐标（相对 stage 内容盒）→ 标签内容坐标（mm）。 */
export function logicToContentMm(x: number, y: number, preview: boolean): { x: number; y: number } {
  const off = contentOffset(preview)
  return { x: mm(x - off.x), y: mm(y - off.y) }
}

/**
 * 拖动落点提交（· 模型盒域）：Konva 节点原点（= 元素模型盒原点，逻辑 px）
 * → 内容 mm（两位舍入）。CanvasViewport.handleDragEnd 与单测共用本算式，保证测试执行真实代码路径；
 * 旧口径取渲染包围盒左上角，被居中描边半线宽外扩带偏（线 −0.25mm / 边框 −0.15mm）。
 */
export function commitDragPosition(nodeX: number, nodeY: number, preview: boolean): { x: number; y: number } {
  const pos = logicToContentMm(nodeX, nodeY, preview)
  return { x: r2(pos.x), y: r2(pos.y) }
}

/* * 多选拖动松手提交入参（返工 · ——用户拍板「整体刚性」）。 */
export interface MultiDragRigidArgs {
  /** 主导（被拖）元素 id（须在 items 中，缺失时 Δ 按 0 处理＝不动提交）。 */
  leaderId: string
  /** dragStart 快照：各选中元素初始节点坐标（逻辑 px，拖前模型换算）。 */
  items: ReadonlyArray<{ id: string; x: number; y: number }>
  /** 主导节点终态坐标（末次联合盒吸附后，逻辑 px）。 */
  leaderX: number
  leaderY: number
  /** 是否预览态坐标（与 commitDragPosition 同参）。 */
  preview: boolean
}

/** 多选拖动松手提交结果：刚体总位移 + 全选中元素落点补丁。 */
export interface MultiDragRigidResult {
  /** 刚体总位移（逻辑 px）＝主导终态 − 主导初始。 */
  dx: number
  dy: number
  /** 全选中元素的模型坐标补丁（mm 两位舍入）——各元素 = commitDragPosition(初始 + Δ)，同 Δ 刚性。 */
  patches: Map<string, { x: number; y: number }>
}

/**
 * 多选拖动松手提交（返工 · ，用户拍板「整体刚性」，复用 commitDragPosition 口径）：
 * Δ = 主导终态 − 主导初始（dragStart 快照基准的总位移口径，对 dragend「保持末次 dragmove 位置」与
 * 「被指针覆盖」两种时序均自洽）；补丁 = 各元素 commitDragPosition(初始节点坐标 + Δ)——组内任意两
 * 元素模型坐标差与拖前逐对相等（同 Δ 刚性，两位舍入残差 ≤0.01mm）。CanvasViewport.handleDragEnd
 * 与单测共用本算式，保证测试执行真实代码路径。
 */
export function multiDragRigidPatches(args: MultiDragRigidArgs): MultiDragRigidResult {
  const leader = args.items.find((i) => i.id === args.leaderId)
  const dx = leader ? args.leaderX - leader.x : 0
  const dy = leader ? args.leaderY - leader.y : 0
  const patches = new Map<string, { x: number; y: number }>()
  for (const item of args.items) {
    patches.set(item.id, commitDragPosition(item.x + dx, item.y + dy, args.preview))
  }
  return { dx, dy, patches }
}

/** 适应窗口缩放：画布略小于视口。 */
export function fitScale(viewportW: number, viewportH: number, canvasW: number, canvasH: number): number {
  return Math.max(0.05, Math.min((viewportW - 32) / canvasW, (viewportH - 32) / canvasH))
}

/**
 * 视口 client 坐标 → 画布逻辑坐标（含页面缩放修正）。
 *
 * 背景：canvas 的 `getBoundingClientRect()` 会随浏览器页面缩放（Windows 显示缩放 125% 等）
 * 同比放大，而 `clientWidth` 不变——两者比值即页面缩放因子，必须先除掉，
 * 否则 `clientX - rect.left` 得到的偏移被放大，换算出的元素落点偏出（实测 125% 下偏差 ~35%）。
 * Konva 内部 `_getContentPosition` 正是用 `rect.width / clientWidth` 做此修正（getPointerPosition 已内置）；
 * 本函数把同一修正暴露给不走 Konva 指针的事件路径（HTML5 DnD 拖放、原生 click）。
 */
export interface ViewportToLogicArgs {
  clientX: number
  clientY: number
  /** canvas 的 getBoundingClientRect()（随页面缩放）。 */
  rect: { left: number; top: number; width: number; height: number }
  /** canvas 的 clientWidth / clientHeight（不随页面缩放）。 */
  clientWidth: number
  clientHeight: number
  /** stage 平移。 */
  stageX: number
  stageY: number
  /** stage 缩放。 */
  scaleX: number
  scaleY: number
}

/** 视口 client 坐标 → 画布逻辑坐标。 */
export function viewportToLogic(args: ViewportToLogicArgs): { x: number; y: number } {
  // clientWidth 为 0（如未布局）时无法测量缩放，按 1 处理；不能用 `|| 1`——rect.width/0 是 Infinity（truthy）不会兜住
  const zoomX = args.clientWidth > 0 ? args.rect.width / args.clientWidth : 1
  const zoomY = args.clientHeight > 0 ? args.rect.height / args.clientHeight : 1
  return {
    x: ((args.clientX - args.rect.left) / zoomX - args.stageX) / args.scaleX,
    y: ((args.clientY - args.rect.top) / zoomY - args.stageY) / args.scaleY,
  }
}

/**
 * Konva `getPointerPosition()` → 画布逻辑坐标。
 * Konva 指针已内置页面缩放修正（返回 canvas CSS 像素系），只需扣除 stage 平移并除以 stage 缩放。
 */
export function pointerToLogic(ptr: { x: number; y: number }, stageX: number, stageY: number, scaleX: number, scaleY: number): { x: number; y: number } {
  return {
    x: (ptr.x - stageX) / scaleX,
    y: (ptr.y - stageY) / scaleY,
  }
}
