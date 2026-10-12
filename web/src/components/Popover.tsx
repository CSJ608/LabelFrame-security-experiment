// 轻量锚定弹出菜单。
// 关键实现约束：必须 createPortal 到 body ＋ position:fixed 锚定——
// 工作台卡片 .wb-card 为 overflow:hidden，普通绝对定位菜单会被卡片裁剪（实施已知坑）。
// 关闭语义：点击菜单外区域 / Esc / 页面滚动均关闭；菜单项点击由调用方关闭。

import { useEffect, useLayoutEffect, useRef, useState } from 'react'
import type { ReactNode } from 'react'
import { createPortal } from 'react-dom'

interface PopoverProps {
  /** 锚定元素（触发按钮）；null 时不渲染。 */
  anchor: HTMLElement | null
  onClose: () => void
  /** 无障碍标签（role=menu 的 aria-label）。 */
  ariaLabel: string
  children: ReactNode
}

/** 弹层与锚点的间距（px）。 */
const GAP = 4
/** 弹层与视口边缘的最小留白（px）。 */
const VIEWPORT_MARGIN = 8

/**
 * 锚定弹出菜单：以 anchor 的边界矩形定位（默认右对齐、下缘展开；下方放不下翻到上方），
 * fixed 定位脱离任何 overflow:hidden 祖先。定位完成前 visibility:hidden 防闪烁。
 */
export function Popover({ anchor, onClose, ariaLabel, children }: PopoverProps) {
  const menuRef = useRef<HTMLDivElement>(null)
  const [pos, setPos] = useState<{ left: number; top: number } | null>(null)

  // 定位：锚点右缘对齐 + 下缘展开；越出视口时翻转 / 贴边（jsdom 中矩形为 0，仍产出可用坐标）
  useLayoutEffect(() => {
    if (!anchor || !menuRef.current) return
    const place = () => {
      const menu = menuRef.current
      if (!menu) return
      const rect = anchor.getBoundingClientRect()
      const { offsetWidth: w, offsetHeight: h } = menu
      // 右缘对齐（菜单右缘 ≈ 锚点右缘）；越出左侧贴边，越出右侧也贴边
      let left = rect.right - w
      left = Math.max(VIEWPORT_MARGIN, Math.min(left, window.innerWidth - w - VIEWPORT_MARGIN))
      // 默认在锚点下方展开；下方放不下且上方放得下时翻转到上方
      let top = rect.bottom + GAP
      if (top + h > window.innerHeight - VIEWPORT_MARGIN) {
        top = Math.max(VIEWPORT_MARGIN, rect.top - h - GAP)
      }
      setPos({ left, top })
    }
    place()
    window.addEventListener('resize', place)
    return () => window.removeEventListener('resize', place)
  }, [anchor])

  // Esc 关闭
  useEffect(() => {
    const onKey = (ev: KeyboardEvent) => {
      if (ev.key === 'Escape') onClose()
    }
    document.addEventListener('keydown', onKey)
    return () => document.removeEventListener('keydown', onKey)
  }, [onClose])

  // 点击外部关闭：菜单与锚点（触发按钮）之外的区域。锚点本身不视为「外部」，
  // 让触发按钮自己的 onClick 可先于关闭消费这次点击（再点 ⋯ = 收起菜单的常规切换语义）。
  useEffect(() => {
    const onPointerDown = (ev: MouseEvent) => {
      const target = ev.target as Node
      if (menuRef.current?.contains(target)) return
      if (anchor?.contains(target)) return
      onClose()
    }
    document.addEventListener('mousedown', onPointerDown)
    return () => document.removeEventListener('mousedown', onPointerDown)
  }, [onClose, anchor])

  // 页面滚动即关闭（fixed 定位不随锚点滚动，挂着反而脱离卡片误导；轻量组件不做滚动跟随）
  useEffect(() => {
    const onScroll = () => onClose()
    document.addEventListener('scroll', onScroll, true)
    return () => document.removeEventListener('scroll', onScroll, true)
  }, [onClose])

  if (!anchor) return null

  return createPortal(
    <div
      ref={menuRef}
      className="popover"
      role="menu"
      aria-label={ariaLabel}
      style={{ position: 'fixed', left: pos?.left ?? 0, top: pos?.top ?? 0, visibility: pos ? 'visible' : 'hidden' }}
    >
      {children}
    </div>,
    document.body,
  )
}

/* * 弹出菜单内的菜单项（role=menuitem 的按钮形态；danger = 删除类警示样式）。 */
export function MenuItem({
  danger,
  children,
  ...rest
}: React.ButtonHTMLAttributes<HTMLButtonElement> & { danger?: boolean }) {
  return (
    <button type="button" role="menuitem" className={danger ? 'menu-item danger' : 'menu-item'} {...rest}>
      {children}
    </button>
  )
}
