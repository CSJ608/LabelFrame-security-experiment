// 组件级「?」就地文档点（· ，②③）：ghost 圆形问号按钮（带 data-help
// 语义锚点，帮助文章深链的定位目标）＋点击弹就地文档气泡。
// 气泡复用 Popover 的 portal + fixed 锚定算法（右缘对齐 / 下缘展开 / 放不下翻上方 / 贴边，
// ①：脱离 overflow 祖先裁剪）与 guide-bubble 视觉（拍板 2：加宽至 ~380px＋
// max-height 40vh 滚动，en 词条 1.5~2 倍 zh 长度弹性，#164⑦/ 前科）；语义为 dialog
// （Popover 本体 role=menu 不通用， 方案），不直接复用 Popover。
// 关闭语义：点外 / Esc 关闭（Popover 同法）；Esc 为 document 级监听，与 Modal / Popover /
// Guide 同键并存时同帧双响应——既有先例边界（Guide.tsx 风险记录），不做监听互斥。
// 演示插槽：hasDemo 时气泡 footer 渲染「看实时效果」/「退出演示」，注入 / 还原
// 行为由调用方（Designer 显示层 override）实现——本组件只出回调，不持有演示状态。
// （观感返工拍板①）：气泡不再渲染可见标题节点——点「?」时分组 / 卡片
// 标题就在按钮旁，重复显示即冗余（陪验否决项）；正文直出，dialog 无障碍名仍取
// title 词条（读屏不丢名，title 词条仍被帮助文章 h3 与 aria-label 消费——单源不破）。
// server 构建口径（·）：设计器挂点（PropsPanel 10 组）已撤门——server
// 构建可达路径照常渲染「?」（词条同源，help 域已进双产物）；Settings 挂点维持 !isServerUi 条件
// 渲染（client-only 页防御门——server 构建本不可达该页，门防手造 URL 等边缘），本组件仍不做
// 内部分支，两口径均在调用点显式可见。

import { useEffect, useLayoutEffect, useRef, useState } from 'react'
import type { CSSProperties } from 'react'
import { createPortal } from 'react-dom'
import { useTranslation } from 'react-i18next'
import { Icon } from './Icon'

interface HelpDotProps {
  /** data-help 语义锚点值（帮助文章「去界面查看」深链的定位目标）。 */
  anchor: string
  /** help 域词条前缀（如 `topic.designer.fill`，title / body 两槽）。 */
  topicKey: string
  /** 气泡开启状态（受控——由调用方持有，帮助页深链自动弹出复用同一状态）。 */
  open: boolean
  /** 「?」按钮点击（组件内 stopPropagation，防面板头折叠类点击连带触发——评审建议②）。 */
  onToggle: () => void
  /** 点外 / Esc 关闭。 */
  onClose: () => void
  /* * 文档点是否内嵌「看实时效果」演示（首波仅设计器填充组， 旗舰示例）。 */
  hasDemo?: boolean
  /** 演示进行中：按钮切「退出演示」并显示提示行。 */
  demoActive?: boolean
  onDemoStart?: () => void
  onDemoExit?: () => void
}

/** 气泡与锚点的间距 / 与视口边缘的最小留白（px，口径对齐 Popover.tsx）。 */
const GAP = 4
const VIEWPORT_MARGIN = 8

export function HelpDot({ anchor, topicKey, open, onToggle, onClose, hasDemo, demoActive, onDemoStart, onDemoExit }: HelpDotProps) {
  const { t } = useTranslation('help')
  const btnRef = useRef<HTMLButtonElement>(null)
  const bubbleRef = useRef<HTMLDivElement>(null)
  const [pos, setPos] = useState<{ left: number; top: number } | null>(null)

  // 定位：锚点右缘对齐 + 下缘展开；越出视口时翻转 / 贴边（jsdom 中矩形为 0，仍产出可用坐标）
  useLayoutEffect(() => {
    if (!open) return
    const place = () => {
      const btn = btnRef.current
      const bubble = bubbleRef.current
      if (!btn || !bubble) return
      const rect = btn.getBoundingClientRect()
      const { offsetWidth: w, offsetHeight: h } = bubble
      let left = rect.right - w
      left = Math.max(VIEWPORT_MARGIN, Math.min(left, window.innerWidth - w - VIEWPORT_MARGIN))
      let top = rect.bottom + GAP
      if (top + h > window.innerHeight - VIEWPORT_MARGIN) {
        top = Math.max(VIEWPORT_MARGIN, rect.top - h - GAP)
      }
      setPos({ left, top })
    }
    place()
    window.addEventListener('resize', place)
    return () => window.removeEventListener('resize', place)
  }, [open])

  // Esc 关闭
  useEffect(() => {
    if (!open) return
    const onKey = (ev: KeyboardEvent) => {
      if (ev.key === 'Escape') onClose()
    }
    document.addEventListener('keydown', onKey)
    return () => document.removeEventListener('keydown', onKey)
  }, [open, onClose])

  // 点击气泡与触发按钮之外关闭（锚点不视为「外部」，再点「?」= 收起的常规切换语义）
  useEffect(() => {
    if (!open) return
    const onPointerDown = (ev: MouseEvent) => {
      const target = ev.target as Node
      if (bubbleRef.current?.contains(target)) return
      if (btnRef.current?.contains(target)) return
      onClose()
    }
    document.addEventListener('mousedown', onPointerDown)
    return () => document.removeEventListener('mousedown', onPointerDown)
  }, [open, onClose])

  const bubbleStyle: CSSProperties = {
    position: 'fixed',
    left: pos?.left ?? 0,
    top: pos?.top ?? 0,
    visibility: pos ? 'visible' : 'hidden',
  }

  return (
    <>
      <button
        ref={btnRef}
        type="button"
        className="help-dot"
        data-help={anchor}
        title={t('ui.dotTitle')}
        aria-label={t('ui.dotTitle')}
        aria-haspopup="dialog"
        aria-expanded={open}
        onClick={(ev) => {
          ev.stopPropagation()
          onToggle()
        }}
      >
        <Icon name="help" size={13} />
      </button>
      {open &&
        createPortal(
          <div ref={bubbleRef} className="help-bubble" role="dialog" aria-label={t(`${topicKey}.title`)} style={bubbleStyle}>
            {/* 正文按换行分段（#316 陪验反馈 2）：词条 body 以 \n 划分段落，逐段渲染——单段词条照旧一节 */}
            {String(t(`${topicKey}.body`)).split('\n').map((para, i) => (
              <p key={i} className="help-bubble-body">{para}</p>
            ))}
            {hasDemo && (
              <div className="help-bubble-foot">
                {demoActive ? (
                  <>
                    <button type="button" className="btn sm" onClick={onDemoExit}>
                      {t('demo.exit')}
                    </button>
                    <span className="help-bubble-hint">{t('demo.active')}</span>
                  </>
                ) : (
                  <button type="button" className="btn sm" onClick={onDemoStart}>
                    {t('demo.start')}
                  </button>
                )}
              </div>
            )}
          </div>,
          document.body,
        )}
    </>
  )
}
