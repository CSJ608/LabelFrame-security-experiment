// 设计器状态序列化与历史类型：快照式历史的 snap / parse（仅 paperW/paperH/elements，图片资源不入栈）
// 与 DesignerHistory 类型别名——页面壳与各编排 hook 共用。

import type { DesignState } from './CanvasViewport'
import { createHistory } from '../../lib/design/history'

export type DesignerHistory = ReturnType<typeof createHistory<DesignState>>

export const snap = (s: DesignState) => JSON.stringify({ paperW: s.paperW, paperH: s.paperH, elements: s.elements })
export const parse = (s: string): DesignState => JSON.parse(s) as DesignState
