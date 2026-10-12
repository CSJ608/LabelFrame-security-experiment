// 智能参考线吸附（纯函数）：候选线 = 画布边缘/中心 + 内容区边缘/中心 + 其它元素边缘/中心；
// 无目标时网格吸附兜底（贴最近 1mm 网格）。与原型 snapNode 逻辑一致。

import { pxv } from './geometry'
import type { DesignElement } from './types'

export interface SnapRect {
  x: number
  y: number
  w: number
  h: number
}

export interface SnapResult {
  /** 需要施加的位移（逻辑 px）。 */
  dx: number
  dy: number
  /** 吸附到的参考线位置（逻辑 px），null = 未吸附。 */
  guideX: number | null
  guideY: number | null
}

/** 吸附阈值（逻辑 px）。 */
export const SNAP_THRESHOLD = 8

/**
 * 元素模型盒（逻辑 px，相对 layer）：按模型 mm 坐标纯函数计算，脱离 Konva 渲染包围盒。
 *
 * 根因（·）：getClientRect 默认把居中描边半线宽计入盒外——线（默认 0.5mm）
 * 每侧外扩 0.25mm、矩形 / 容器边框（默认 0.3mm）每侧 0.15mm、图片占位描边每侧 0.125mm——
 * 吸附候选与落点提交取渲染盒会把外扩传染给 mm 坐标（用户报告「自动对齐后偏差约 0.2」）。
 * 宽高镜像 ElementNode 的渲染下限钳制（Math.max(2, pxv(w))），保证候选盒与画布视觉宽度一致。
 */
export function elementModelRect(e: DesignElement, ox: number, oy: number): SnapRect {
  return {
    x: ox + pxv(e.x),
    y: oy + pxv(e.y),
    w: Math.max(2, pxv(e.w)),
    h: Math.max(2, pxv(e.h)),
  }
}

/**
 * 选中元素联合包围盒（返工 · ——用户拍板「整体刚性」）：多选拖动的吸附单位。
 * 各元素 elementModelRect 的 min/max 包络；空集返回 {0,0,0,0}（调用方保证非空，防御性退化）。
 */
export function unionModelRect(els: readonly DesignElement[], ox: number, oy: number): SnapRect {
  let x = Infinity
  let y = Infinity
  let r = -Infinity
  let b = -Infinity
  for (const e of els) {
    const m = elementModelRect(e, ox, oy)
    x = Math.min(x, m.x)
    y = Math.min(y, m.y)
    r = Math.max(r, m.x + m.w)
    b = Math.max(b, m.y + m.h)
  }
  if (!Number.isFinite(x)) return { x: 0, y: 0, w: 0, h: 0 }
  return { x, y, w: r - x, h: b - y }
}

/**
 * 参考线候选盒构造（返工 ·）：排除 excludeIds 后的元素模型盒列表。
 * 单选 = 仅排除被拖元素自身；多选 = 排除全部选中元素（组内零参与——不作被拖盒也不作候选）。
 * 单选 snapNode 与多选联合盒吸附共用本算式，组件与单测共用真实代码路径。
 */
export function othersModelRects(elements: readonly DesignElement[], excludeIds: readonly string[], ox: number, oy: number): SnapRect[] {
  return elements.filter((o) => !excludeIds.includes(o.id)).map((o) => elementModelRect(o, ox, oy))
}

/**
 * 计算吸附结果。
 * @param rect 被拖动元素的包围盒（逻辑 px，相对 layer）
 * @param canvasW / canvasH 画布总尺寸（逻辑 px）
 * @param contentX / contentY 内容区偏移（逻辑 px）
 * @param contentW / contentH 标签内容区尺寸（逻辑 px）
 * @param others 其它元素包围盒（逻辑 px）
 */
export function computeSnap(
  rect: SnapRect,
  canvasW: number,
  canvasH: number,
  contentX: number,
  contentY: number,
  contentW: number,
  contentH: number,
  others: readonly SnapRect[],
): SnapResult {
  const xs = [rect.x, rect.x + rect.w / 2, rect.x + rect.w]
  const ys = [rect.y, rect.y + rect.h / 2, rect.y + rect.h]
  const cx = [0, canvasW / 2, canvasW, contentX, contentX + contentW / 2, contentX + contentW]
  const cy = [0, canvasH / 2, canvasH, contentY, contentY + contentH / 2, contentY + contentH]
  for (const o of others) {
    cx.push(o.x, o.x + o.w / 2, o.x + o.w)
    cy.push(o.y, o.y + o.h / 2, o.y + o.h)
  }

  let bestDx: { d: number; c: number } | null = null
  let bestDy: { d: number; c: number } | null = null
  for (const x of xs) {
    for (const c of cx) {
      const d = c - x
      if (Math.abs(d) <= SNAP_THRESHOLD && (bestDx === null || Math.abs(d) < Math.abs(bestDx.d))) bestDx = { d, c }
    }
  }
  for (const y of ys) {
    for (const c of cy) {
      const d = c - y
      if (Math.abs(d) <= SNAP_THRESHOLD && (bestDy === null || Math.abs(d) < Math.abs(bestDy.d))) bestDy = { d, c }
    }
  }

  // 网格吸附兜底：左上角贴最近 1mm 网格（4px）
  if (bestDx === null) {
    const gridX = Math.round(rect.x / 4) * 4
    const d = gridX - rect.x
    if (Math.abs(d) <= SNAP_THRESHOLD) bestDx = { d, c: gridX }
  }
  if (bestDy === null) {
    const gridY = Math.round(rect.y / 4) * 4
    const d = gridY - rect.y
    if (Math.abs(d) <= SNAP_THRESHOLD) bestDy = { d, c: gridY }
  }

  return {
    dx: bestDx ? bestDx.d : 0,
    dy: bestDy ? bestDy.d : 0,
    guideX: bestDx ? bestDx.c : null,
    guideY: bestDy ? bestDy.c : null,
  }
}
