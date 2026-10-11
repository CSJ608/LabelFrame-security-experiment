// 元素查询工具

import type { DesignElement, RegionElement } from './types'

export function elementById(els: readonly DesignElement[], id: string): DesignElement | undefined {
  return els.find((e) => e.id === id)
}

export function elementsByIds(els: readonly DesignElement[], ids: readonly string[]): DesignElement[] {
  return els.filter((e) => ids.includes(e.id))
}

/**
 * 容器锚定重算（迭代 131 · #324 自 CanvasViewport.containerHit 原样提取，语义逐行不变）：
 * 元素（可带补丁坐标）中心点落在某 Region 内 → 该 Region 的 containerId，未命中 → undefined；
 * 排除以该元素为容器的候选（元素不锚定进自己充当的容器；谓词 c.containerId !== e.id）。组件与单测共用，保证锚定算式被真实代码路径执行。
 */
export function containerRegionIdFor(e: DesignElement, els: readonly DesignElement[]): string | undefined {
  const hit = els.find(
    (c): c is RegionElement =>
      c.type === 'Region' && c.containerId !== e.id && e.x + e.w / 2 >= c.x && e.x + e.w / 2 <= c.x + c.w && e.y + e.h / 2 >= c.y && e.y + e.h / 2 <= c.y + c.h,
  )
  return hit?.containerId
}
