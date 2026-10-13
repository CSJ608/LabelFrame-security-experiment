// 帮助深链与「看实时效果」演示编排 hook：深链锚点页内轮询、到达性保障（不可见时自动重建示例草稿 / 改选中）、
// 演示锚点状态与深链草稿一次性标记。效应体自 Designer.tsx 逐字搬移（纯重构零行为变化）。

import { useEffect, useRef, useState } from 'react'
import type { Dispatch, RefObject, SetStateAction } from 'react'
import { useTranslation } from 'react-i18next'
import type { DesignState } from './CanvasViewport'
import type { DesignerHistory } from './designState'
import { snap, parse } from './designState'
import { createHistory } from '../../lib/design/history'
import { useApp } from '../../state/AppContext'
import type { DesignerRequest } from '../../state/types'
import { HELP_DEMO_PRESETS, HELP_LINK_ELEMENTS, helpSampleDisplayName } from '../../lib/help'
import { useHelpDeepLink } from '../../lib/helpLink'
import { defaultElement } from '../../lib/design/types'
import type { DesignElement } from '../../lib/design/types'

/* * 深链示例元素构造：预设 spec → defaultElement + patch 合并——每次调用生成新 id、默认文案随当前语言。 */
export function buildHelpLinkElements(anchor: string): DesignElement[] {
  return (HELP_LINK_ELEMENTS[anchor] ?? []).map(({ type, patch }) => {
    const e = defaultElement(type as 'Text') as DesignElement
    const extra = 'mode' in (patch ?? {}) ? { displayName: helpSampleDisplayName() } : {}
    return { ...e, ...patch, ...extra } as DesignElement
  })
}

export interface UseHelpLinkDraftOptions {
  request: DesignerRequest
  stateRef: RefObject<DesignState | null>
  historyRef: RefObject<DesignerHistory | null>
  selectedRef: RefObject<string[]>
  setState: Dispatch<SetStateAction<DesignState | null>>
  setSelected: Dispatch<SetStateAction<string[]>>
}

export function useHelpLinkDraft(helpAnchor: string | null | undefined, { request, stateRef, historyRef, selectedRef, setState, setSelected }: UseHelpLinkDraftOptions) {
  const app = useApp()
  const { t } = useTranslation('designer')
  // 「看实时效果」演示——显示层 override（记录演示文档点锚点）：只喂画布与属性面板，
  // 不写 stateRef / historyRef（不进历史栈、不置 dirty，保存链零落库零写请求）；退出清锚点即还原。
  const [demoAnchor, setDemoAnchor] = useState<string | null>(null)
  // displayName 示例值随语言补入（预设不静态写死）
  const demoPatch = demoAnchor
    ? { ...HELP_DEMO_PRESETS[demoAnchor], displayName: helpSampleDisplayName() }
    : undefined
  const demoActive = demoPatch !== undefined
  // 深链自动弹气泡的内部信号（PropsPanel 处理后清信号防重复触发）
  const [autoOpenAnchor, setAutoOpenAnchor] = useState<string | null>(null)
  // 深链草稿一次性标记：深链自动开草稿时置位，首个 onFound 不清状态栏（draftOpened 为终态提示），消费后复位。
  const deepLinkDraftRef = useRef(false)

  // 深链锚点页内轮询——须声明于加载 effect 之前（自动草稿路径先写 pending、init 随即以 draftOpened 覆盖）；
  // 超时走通用降级词条（一切锚点不可见情形同此提示）。
  useHelpDeepLink(helpAnchor, {
    onFound: () => {
      if (deepLinkDraftRef.current) deepLinkDraftRef.current = false
      else app.setStatus('')
      setAutoOpenAnchor(helpAnchor ?? null)
    },
    onTimeout: () => app.setStatus(t('help:deepLink.timeout')),
    onPending: () => app.setStatus(t('help:deepLink.pending')),
  })

  // 深链到达性保障：锚点变更时若目标分组对当前选中不可见，主动带到可见状态（否则轮询只会超时降级）。
  // 两档：示例草稿未编辑（undoCount=0）→ 按新锚点重建示例集（不丢用户内容）；用户内容 → 只改选中
  // 不注入元素（对齐组选任意两个），无匹配元素则不动作（维持超时降级）。
  useEffect(() => {
    if (!helpAnchor) return
    const specs = Object.hasOwn(HELP_LINK_ELEMENTS, helpAnchor) ? HELP_LINK_ELEMENTS[helpAnchor] : null
    if (!specs) return // 非设计器文档点锚——锚点常在，走既有轮询
    const s = stateRef.current
    if (!s) return
    const selEls = s.elements.filter((e) => selectedRef.current.includes(e.id))
    const types = new Set(specs.map((sp) => sp.type))
    const reachable = helpAnchor === 'designer.align' ? selEls.length >= 2 : selEls.some((e) => types.has(e.type))
    if (reachable) return
    if (request.kind === 'new' && (historyRef.current?.undoCount ?? 0) === 0) {
      // 重建示例集：undoCount=0 = 无任何已提交更改，重建不丢工作；元素与历史基线整体换为目标锚点预设（不 dirty）
      const samples = buildHelpLinkElements(helpAnchor)
      const next = { ...s, elements: samples }
      stateRef.current = next
      historyRef.current = createHistory(next, snap, parse)
      setState(next)
      const ids = samples.map((e) => e.id)
      setSelected(ids)
      selectedRef.current = ids
      deepLinkDraftRef.current = true
      app.setStatus(t('help:deepLink.draftOpened'))
      return
    }
    const pick =
      helpAnchor === 'designer.align'
        ? s.elements.filter((e) => e.type !== 'Region').slice(0, 2).map((e) => e.id)
        : [s.elements.find((e) => types.has(e.type))?.id].filter((id): id is string => id !== undefined)
    if (pick.length === 0) return
    setSelected(pick)
    selectedRef.current = pick
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [helpAnchor])

  return { demoAnchor, setDemoAnchor, demoPatch, demoActive, autoOpenAnchor, setAutoOpenAnchor, deepLinkDraftRef }
}
