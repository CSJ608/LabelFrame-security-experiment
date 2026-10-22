// 画布框选命中（纯函数）：选区矩形与元素模型盒的「有面积重叠」相交判定。

import { elementModelRect } from './snapping'
import type { DesignElement } from './types'

/** 返回与选区有面积重叠（边缘相切不算）的元素 id；w/h 负值（反向拖拽）自动按 min/max 归一。 */
export function elementsIntersecting(
  rect: { x: number; y: number; w: number; h: number },
  elements: readonly DesignElement[],
  ox: number,
  oy: number,
): string[] {
  const l = Math.min(rect.x, rect.x + rect.w)
  const r = Math.max(rect.x, rect.x + rect.w)
  const t = Math.min(rect.y, rect.y + rect.h)
  const b = Math.max(rect.y, rect.y + rect.h)
  const ids: string[] = []
  for (const e of elements) {
    const m = elementModelRect(e, ox, oy)
    // 开区间相交：交集需有正的宽与高——共享边界（相切）不算命中
    if (r > m.x && l < m.x + m.w && b > m.y && t < m.y + m.h) ids.push(e.id)
  }
  return ids
}
