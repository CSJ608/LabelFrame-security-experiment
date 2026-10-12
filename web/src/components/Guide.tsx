// 分步气泡引导（· ，①②：spotlight 形态、自研实现，复用 Popover portal 经验）。
// 结构：全屏遮罩层（拦截其余区域交互）+ box-shadow 巨影法 spotlight 挖洞（无需 SVG mask）+ 定位气泡；
// createPortal 到 body（①：fixed 锚定脱离 overflow 祖先裁剪）。步骤定义为 lib/guide.ts 纯数据。
// 切页必须经调用方注入的 switchTab（设计器离开守卫统一入口），组件内不做裸 setTab。
// 退出语义（⑧⑨）：「跳过」/ Esc / 「完成」共用 onFinish——首见标记是否写入由调用方决定
// （首见与重放均写标记，setItem 幂等，重放不改变已看状态）。
// 已知边界（风险记录）：Esc 为 document 级监听，与 Modal / Popover 同键并存时同帧双响应
// ——引导激活期间遮罩已拦截打开 Modal 的常规路径，仅「重放 + 设计器 dirty」边缘场景可达，不做监听互斥；
// 引导期间不做滚动跟随（锚点全选不滚动区，遮罩拦截滚动），窄窗口气泡翻转 / 贴边沿用 Popover 视口约束。

import { useEffect, useLayoutEffect, useRef, useState } from 'react'
import type { CSSProperties, ReactElement } from 'react'
import { createPortal } from 'react-dom'
import { useTranslation } from 'react-i18next'
import type { TabId } from '../state/types'
import { GUIDE_STEPS, guideAnchorSelector } from '../lib/guide'

/** 气泡与视口边缘的最小留白 / 与锚点的间距（px，口径对齐 Popover.tsx）。 */
const VIEWPORT_MARGIN = 8
const GAP = 10
/** 锚点高亮矩形外扩留白（px）。 */
const SPOT_PAD = 6
/** 切页后等待锚点元素渲染的轮询间隔与上限（页面数据异步加载；超时降级为全屏暗幕 + 居中气泡，不阻塞引导）。 */
const ANCHOR_POLL_MS = 80
const ANCHOR_POLL_MAX_MS = 4000

interface SpotRect {
  left: number
  top: number
  width: number
  height: number
}

interface Placement {
  spot: SpotRect | null
  bubble: { left: number; top: number } | null
}

interface GuideProps {
  /** 是否展示引导（首次自动启动与「使用引导」重放均由调用方控制）。 */
  open: boolean
  /** 当前激活 tab（App Shell 状态；用于步骤切页判等）。 */
  tab: TabId
  /* * 切页回调——调用方必须传 App 的 switchTab（设计器离开守卫统一入口）。 */
  onSwitchTab: (id: TabId) => void
  /** 结束回调（跳过 / Esc / 完成共用）。 */
  onFinish: () => void
}

export function Guide({ open, tab, onSwitchTab, onFinish }: GuideProps) {
  const { t } = useTranslation('guide')
  const [stepIndex, setStepIndex] = useState(0)
  const [anchorEl, setAnchorEl] = useState<HTMLElement | null>(null)
  const [placement, setPlacement] = useState<Placement>({ spot: null, bubble: null })
  const bubbleRef = useRef<HTMLDivElement>(null)
  const step = GUIDE_STEPS[stepIndex]
  const isLast = stepIndex === GUIDE_STEPS.length - 1

  // 每次打开（首见或重放）都从第一步开始
  useEffect(() => {
    if (open) setStepIndex(0)
  }, [open])

  // 步骤驱动（仅在步骤变更时执行一次）：目标 tab 与当前不一致时经 switchTab 切页（离开守卫统一入口）。
  // tab / onSwitchTab 经 ref 取最新值而不入依赖——引导打开期间用户自行导航（键盘 Tab 聚焦导航等遮罩外路径）
  // 不被拽回步骤页，切页只由「上一步 / 下一步」驱动。
  const tabRef = useRef(tab)
  tabRef.current = tab
  const switchTabRef = useRef(onSwitchTab)
  switchTabRef.current = onSwitchTab
  useEffect(() => {
    if (!open) return
    const target = GUIDE_STEPS[stepIndex].tab
    if (target !== tabRef.current) switchTabRef.current(target)
  }, [open, stepIndex])

  // 锚点就位：切页渲染异步（数据加载），轮询等锚点出现；超时降级为无锚点形态
  useEffect(() => {
    setAnchorEl(null)
    if (!open) return
    const selector = guideAnchorSelector(GUIDE_STEPS[stepIndex])
    const startedAt = Date.now()
    let timer: ReturnType<typeof setTimeout> | undefined
    const poll = () => {
      const el = document.querySelector<HTMLElement>(selector)
      if (el) {
        setAnchorEl(el)
        return
      }
      if (Date.now() - startedAt < ANCHOR_POLL_MAX_MS) timer = setTimeout(poll, ANCHOR_POLL_MS)
    }
    poll()
    return () => clearTimeout(timer)
  }, [open, stepIndex])

  // 定位：spotlight 套锚点矩形（外扩 SPOT_PAD），气泡默认锚点下方、放不下翻到上方、水平贴边；
  // 无可用矩形（超时 / 零矩形）时全屏暗幕 + 气泡居中。定位完成前 visibility:hidden 防闪烁（Popover 同法）。
  useLayoutEffect(() => {
    if (!open) {
      setPlacement({ spot: null, bubble: null })
      return
    }
    const compute = () => {
      const bubble = bubbleRef.current
      const bw = bubble?.offsetWidth ?? 0
      const bh = bubble?.offsetHeight ?? 0
      const rect = anchorEl?.getBoundingClientRect()
      if (!rect || rect.width <= 0 || rect.height <= 0) {
        setPlacement({
          spot: null,
          bubble: {
            left: Math.max(VIEWPORT_MARGIN, (window.innerWidth - bw) / 2),
            top: Math.max(VIEWPORT_MARGIN, (window.innerHeight - bh) / 2),
          },
        })
        return
      }
      const spot: SpotRect = {
        left: rect.left - SPOT_PAD,
        top: rect.top - SPOT_PAD,
        width: rect.width + SPOT_PAD * 2,
        height: rect.height + SPOT_PAD * 2,
      }
      let left = spot.left + spot.width / 2 - bw / 2
      left = Math.max(VIEWPORT_MARGIN, Math.min(left, window.innerWidth - bw - VIEWPORT_MARGIN))
      let top = spot.top + spot.height + GAP
      if (top + bh > window.innerHeight - VIEWPORT_MARGIN) {
        top = Math.max(VIEWPORT_MARGIN, spot.top - GAP - bh)
      }
      setPlacement({ spot, bubble: { left, top } })
    }
    compute()
    window.addEventListener('resize', compute)
    return () => window.removeEventListener('resize', compute)
  }, [open, anchorEl, stepIndex])

  // Esc = 跳过本轮并写标记（⑨，与 Modal「Esc=取消」语义对齐；document 级监听同 Modal/Popover 先例）
  useEffect(() => {
    if (!open) return
    const onKey = (ev: KeyboardEvent) => {
      if (ev.key === 'Escape') onFinish()
    }
    document.addEventListener('keydown', onKey)
    return () => document.removeEventListener('keydown', onKey)
  }, [open, onFinish])

  if (!open) return null

  const spotStyle: CSSProperties | undefined = placement.spot
    ? {
        left: placement.spot.left,
        top: placement.spot.top,
        width: placement.spot.width,
        height: placement.spot.height,
      }
    : undefined

  const bubble: ReactElement = (
    <div
      ref={bubbleRef}
      className="guide-bubble"
      role="dialog"
      aria-label={t(`${step.key}.title`)}
      style={{
        left: placement.bubble?.left ?? 0,
        top: placement.bubble?.top ?? 0,
        visibility: placement.bubble ? 'visible' : 'hidden',
      }}
    >
      <div className="guide-bubble-title">{t(`${step.key}.title`)}</div>
      <p className="guide-bubble-body">{t(`${step.key}.body`)}</p>
      <div className="guide-bubble-foot">
        <button type="button" className="btn sm" onClick={onFinish}>
          {t('btn.skip')}
        </button>
        <span className="guide-flex" />
        {stepIndex > 0 && (
          <button type="button" className="btn sm" onClick={() => setStepIndex(stepIndex - 1)}>
            {t('btn.prev')}
          </button>
        )}
        <button
          type="button"
          className="btn sm primary"
          onClick={() => (isLast ? onFinish() : setStepIndex(stepIndex + 1))}
        >
          {isLast ? t('btn.done') : t('btn.next')}
        </button>
      </div>
    </div>
  )

  return createPortal(
    <div className="guide-layer">
      {placement.spot ? <div className="guide-spot" style={spotStyle} /> : <div className="guide-veil" />}
      {bubble}
    </div>,
    document.body,
  )
}
