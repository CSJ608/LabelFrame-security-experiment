// @vitest-environment jsdom
// 「?」文档点组件测试——按钮渲染 / 气泡开关 / 词条渲染 / 演示按钮插槽。

import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen, within } from '@testing-library/react'
import { HelpDot } from './HelpDot'
// 组件级单测不经 App 引用链，显式 import 初始化 i18next 单例（语言包资源就位，词条断言可用）
import '../i18n'

function renderDot(overrides: Partial<Parameters<typeof HelpDot>[0]> = {}) {
  const props = {
    anchor: 'designer.fill',
    topicKey: 'topic.designer.fill',
    open: false,
    onToggle: vi.fn(),
    onClose: vi.fn(),
    ...overrides,
  }
  render(<HelpDot {...props} />)
  return props
}

afterEach(cleanup)

describe('HelpDot 文档点', () => {
  it('渲染「?」ghost 按钮：data-help 语义锚点＋无障碍名（dotAria 定稿词条）＋aria-expanded', () => {
    renderDot()
    const btn = document.querySelector('[data-help="designer.fill"]') as HTMLButtonElement
    expect(btn).toBeTruthy()
    expect(btn.getAttribute('aria-label')).toBe('查看说明')
    expect(btn.getAttribute('title')).toBe('查看说明')
    expect(btn.getAttribute('aria-haspopup')).toBe('dialog')
    expect(btn.getAttribute('aria-expanded')).toBe('false')
  })

  it('点击触发 onToggle，且 stopPropagation（防面板头折叠类容器连带点击——评审建议②）', () => {
    const { onToggle } = renderDot()
    // body 级监听探针：点击冒泡若未被组件 stopPropagation 拦截，body 必收到
    let bubbled = false
    const onBodyClick = () => {
      bubbled = true
    }
    document.body.addEventListener('click', onBodyClick)
    fireEvent.click(document.querySelector('[data-help="designer.fill"]') as HTMLButtonElement)
    document.body.removeEventListener('click', onBodyClick)
    expect(onToggle).toHaveBeenCalledTimes(1)
    expect(bubbled).toBe(false)
  })

  it('open：portal 弹出 dialog 气泡，不再渲染可见标题、body 直出（#316 AC-01 去标题冗余）', () => {
    renderDot({ open: true })
    const dialog = screen.getByRole('dialog')
    // 无障碍名保留 title 词条（读屏不丢名）；可见标题节点已删——分组标题就在「?」旁，不重复
    expect(dialog.getAttribute('aria-label')).toBe('填充：固定值或字段填充')
    expect(within(dialog).queryByText('填充：固定值或字段填充')).toBeNull()
    expect(within(dialog).getByText(/「来源」决定打印内容：固定值立即按所填内容渲染/)).toBeTruthy()
  })

  it('Esc 关闭（document 级监听，Popover 同法）', () => {
    const { onClose } = renderDot({ open: true })
    fireEvent.keyDown(document, { key: 'Escape' })
    expect(onClose).toHaveBeenCalledTimes(1)
  })

  it('点外关闭；气泡与触发按钮内部点击不关闭（再点「?」= 收起切换语义）', () => {
    const { onClose } = renderDot({ open: true })
    const dialog = screen.getByRole('dialog')
    fireEvent.mouseDown(dialog)
    expect(onClose).not.toHaveBeenCalled()
    fireEvent.mouseDown(document.querySelector('[data-help="designer.fill"]') as HTMLButtonElement)
    expect(onClose).not.toHaveBeenCalled()
    fireEvent.mouseDown(document.body)
    expect(onClose).toHaveBeenCalledTimes(1)
  })

  it('hasDemo：气泡 footer 渲染「看实时效果」；demoActive 切「退出演示」＋演示态提示行', () => {
    const onDemoStart = vi.fn()
    renderDot({ open: true, hasDemo: true, onDemoStart })
    fireEvent.click(screen.getByRole('button', { name: '看实时效果' }))
    expect(onDemoStart).toHaveBeenCalledTimes(1)

    cleanup()
    const onDemoExit = vi.fn()
    renderDot({ open: true, hasDemo: true, demoActive: true, onDemoStart, onDemoExit })
    expect(screen.getByText(/当前为演示效果（示例数据），退出后自动还原，不会保存到模板。/)).toBeTruthy()
    fireEvent.click(screen.getByRole('button', { name: '退出演示' }))
    expect(onDemoExit).toHaveBeenCalledTimes(1)
  })

  it('无 hasDemo：不渲染演示按钮（hasDemo 由主题数据声明，首波仅填充组）', () => {
    renderDot({ open: true })
    expect(screen.queryByRole('button', { name: '看实时效果' })).toBeNull()
  })
})
