// @vitest-environment jsdom
// 锚定 Popover 组件行为回归——portal 到 body（脱离 overflow:hidden 祖先）
// Esc / 点击菜单外区域关闭、菜单内点击不关闭；anchor 为 null 时不渲染。
// 视觉裁剪（.wb-card overflow:hidden）以「菜单不在卡片内 + fixed 定位」结构断言自证（Workbench.test.tsx 同口径），
// 浏览器实测留待 验收走查。

import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import { useRef, useState } from 'react'
import { MenuItem, Popover } from './Popover'

/** 最小宿主：按钮锚点 + 受控开关（贴近工作台 ⋯ 菜单的真实用法——初始关，点触发按钮开）。 */
function Host() {
  const anchorRef = useRef<HTMLButtonElement>(null)
  const [open, setOpen] = useState(false)
  return (
    <div className="wb-card" style={{ overflow: 'hidden' }}>
      <button ref={anchorRef} onClick={() => setOpen((v) => !v)}>
        触发
      </button>
      {open && (
        <Popover anchor={anchorRef.current} onClose={() => setOpen(false)} ariaLabel="更多操作">
          <MenuItem onClick={() => {}}>导出</MenuItem>
          <MenuItem danger onClick={() => {}}>
            删除
          </MenuItem>
        </Popover>
      )}
    </div>
  )
}

afterEach(cleanup)

describe('Popover（锚定弹出菜单）', () => {
  it('点触发按钮打开：portal 到 body 直下（不在 overflow:hidden 卡片内）、fixed 定位、菜单项可达', () => {
    render(<Host />)
    fireEvent.click(screen.getByRole('button', { name: '触发' }))
    const menu = screen.getByRole('menu')
    // 防裁剪两要素（实施已知坑）：菜单不挂在 .wb-card 子树内 + fixed 定位
    expect(menu.closest('.wb-card')).toBeNull()
    expect(menu.parentElement).toBe(document.body)
    expect(menu.style.position).toBe('fixed')
    expect(menu.getAttribute('aria-label')).toBe('更多操作')
    expect(screen.getByRole('menuitem', { name: '导出' })).toBeTruthy()
    expect(screen.getByRole('menuitem', { name: '删除' }).className).toContain('danger')
  })

  it('按 Esc 关闭', () => {
    render(<Host />)
    fireEvent.click(screen.getByRole('button', { name: '触发' }))
    expect(screen.getByRole('menu')).toBeTruthy()
    fireEvent.keyDown(document, { key: 'Escape' })
    expect(screen.queryByRole('menu')).toBeNull()
  })

  it('点菜单外区域关闭；菜单内按下不关闭', () => {
    render(<Host />)
    fireEvent.click(screen.getByRole('button', { name: '触发' }))
    // 菜单内 mousedown：不关闭（菜单项点击由调用方决定后续）
    fireEvent.mouseDown(screen.getByRole('menuitem', { name: '导出' }))
    expect(screen.getByRole('menu')).toBeTruthy()
    // 菜单外 mousedown：关闭
    fireEvent.mouseDown(document.body)
    expect(screen.queryByRole('menu')).toBeNull()
  })

  it('点触发按钮本身不触发「外部关闭」（再点 ⋯ = 切换收起的常规语义由调用方状态机承担）', () => {
    render(<Host />)
    const trigger = screen.getByRole('button', { name: '触发' })
    fireEvent.click(trigger)
    expect(screen.getByRole('menu')).toBeTruthy()
    // mousedown 落在锚点上：Popover 不代为关闭（留给触发按钮自己的 click 切换）
    fireEvent.mouseDown(trigger)
    expect(screen.getByRole('menu')).toBeTruthy()
    fireEvent.click(trigger)
    expect(screen.queryByRole('menu')).toBeNull()
  })

  it('anchor 为 null 时不渲染任何菜单节点', () => {
    const onClose = vi.fn()
    const { container } = render(
      <Popover anchor={null} onClose={onClose} ariaLabel="空锚点">
        <MenuItem>项</MenuItem>
      </Popover>,
    )
    expect(container.firstChild).toBeNull()
    expect(document.querySelector('[role="menu"]')).toBeNull()
  })
})
