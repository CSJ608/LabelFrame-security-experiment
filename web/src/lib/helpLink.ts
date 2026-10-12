// 帮助深链页内轮询 hook：深链信号由路由 sub 段（#/designer/<anchor> 形）承载，「等锚点
// 入 DOM」的页内等待收敛为本 hook——Designer / Settings 共用：锚点出现即回调 onFound（调用方
// 经既有 openDot / autoOpenAnchor 链自动弹出文档点气泡），80ms/4s 轮询超时回调 onTimeout
//（目标锚点运行时不可见的一切触发——未开模板 / 未选中元素 / 类型不含目标分组 / 对齐组需多选 /
// 预览锁定——统一走 deepLink.timeout 词条降级）；等待期 onPending 可选（状态栏提示）。
// 轮询口径复用 lib/help.ts 的 HELP_ANCHOR_POLL_*（Guide.tsx 同源）；本文件引 react（hook），
// help.ts 保持纯数据。

import { useEffect, useRef } from 'react'
import { HELP_ANCHOR_POLL_MAX_MS, HELP_ANCHOR_POLL_MS, helpAnchorSelector } from './help'

export interface UseHelpDeepLinkOptions {
  /** 锚点已入 DOM：自动弹出对应文档点气泡。 */
  onFound: () => void
  /** 轮询超时（4s）：状态栏通用降级提示。 */
  onTimeout: () => void
  /** 等待期开始（每次锚点到达时一次）：状态栏等待提示，可选。 */
  onPending?: () => void
}

/**
 * 深链锚点页内轮询：anchor 非空即开始轮询 [data-help] 锚点（锚点值变更重启轮询）。
 * sub 段驻留 URL——锚点值不变期间不重复触发，重挂载（刷新 / 后退还原）重走一轮。
 */
export function useHelpDeepLink(anchor: string | null | undefined, { onFound, onTimeout, onPending }: UseHelpDeepLinkOptions) {
  // 回调经 ref 取最新（回调随渲染换引用不重启轮询）
  const cbRef = useRef({ onFound, onTimeout, onPending })
  useEffect(() => {
    cbRef.current = { onFound, onTimeout, onPending }
  })

  useEffect(() => {
    if (!anchor) return
    cbRef.current.onPending?.()
    const selector = helpAnchorSelector(anchor)
    const startedAt = Date.now()
    let timer: ReturnType<typeof setTimeout> | undefined
    const poll = () => {
      if (document.querySelector(selector)) {
        cbRef.current.onFound()
        return
      }
      if (Date.now() - startedAt < HELP_ANCHOR_POLL_MAX_MS) timer = setTimeout(poll, HELP_ANCHOR_POLL_MS)
      else cbRef.current.onTimeout()
    }
    poll()
    return () => clearTimeout(timer)
  }, [anchor])
}
