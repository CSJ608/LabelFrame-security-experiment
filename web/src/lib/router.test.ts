// @vitest-environment jsdom
// 自研 hash 路由（迭代 127 · #312，决策 #177）：parseHash / formatHash 纯函数全分支 +
// useHashRoute hook 行为（入栈时序、sub 不入栈、hashchange 页面跟随、守卫取消 pre-revert 按戳回退、
// 挂载规范化）。jsdom 的 history back/forward/go 触发 hashchange 且逐条目保留 state——pre-revert 可测
// （#312 设计段勘察实证，本文件复验）。

import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { cleanup, renderHook, waitFor } from '@testing-library/react'
import type { TabId } from '../state/types'
import { formatHash, parseHash, useHashRoute } from './router'

const CLIENT_ALLOWED: readonly TabId[] = ['workbench', 'designer', 'data', 'jobs', 'settings', 'help']
const SERVER_ALLOWED: readonly TabId[] = [
  'workbench',
  'designer',
  'data',
  'devices',
  'jobs',
  'packages',
  'plugin-packages',
]

beforeEach(() => {
  window.location.hash = ''
})

afterEach(() => {
  cleanup()
})

describe('parseHash · 纯函数全分支', () => {
  it('空形态（"" / "#" / "#/"）→ 默认 workbench 无 sub', () => {
    for (const h of ['', '#', '#/']) {
      expect(parseHash(h, CLIENT_ALLOWED)).toEqual({ page: 'workbench', sub: '' })
    }
  })

  it('client 六页合法单段：page 原样、sub 空', () => {
    for (const p of CLIENT_ALLOWED) {
      expect(parseHash(`#/${p}`, CLIENT_ALLOWED)).toEqual({ page: p, sub: '' })
    }
  })

  it('两级形 sub 段：词法 [A-Za-z0-9._-]+ 通过（含 V3.1 锚点串 designer.fill 形）', () => {
    expect(parseHash('#/help/designer.fill', CLIENT_ALLOWED)).toEqual({ page: 'help', sub: 'designer.fill' })
    expect(parseHash('#/packages/windows', SERVER_ALLOWED)).toEqual({ page: 'packages', sub: 'windows' })
    expect(parseHash('#/data/a_b-c.d-e', CLIENT_ALLOWED)).toEqual({ page: 'data', sub: 'a_b-c.d-e' })
  })

  it('sub 词法不符 → 丢弃 sub 保留 page（页面仍可达，URL 挂载期会被规范化）', () => {
    expect(parseHash('#/data/好', CLIENT_ALLOWED)).toEqual({ page: 'data', sub: '' })
    expect(parseHash('#/data/好呀', CLIENT_ALLOWED)).toEqual({ page: 'data', sub: '' })
  })

  it('构建白名单裁剪：client 解析 #/packages（server 页）→ 回退 workbench；server 解析 #/help / #/settings 同理', () => {
    expect(parseHash('#/packages', CLIENT_ALLOWED)).toEqual({ page: 'workbench', sub: '' })
    expect(parseHash('#/packages/windows', CLIENT_ALLOWED)).toEqual({ page: 'workbench', sub: '' })
    expect(parseHash('#/help', SERVER_ALLOWED)).toEqual({ page: 'workbench', sub: '' })
    expect(parseHash('#/settings', SERVER_ALLOWED)).toEqual({ page: 'workbench', sub: '' })
  })

  it('未知 page / 整体非法形态 → 回退 workbench', () => {
    expect(parseHash('#/whatever', CLIENT_ALLOWED)).toEqual({ page: 'workbench', sub: '' })
    expect(parseHash('#foo', CLIENT_ALLOWED)).toEqual({ page: 'workbench', sub: '' })
    // 三级形不支持：第二段之后的路径并作 sub 串，词法不符（含 /）→ 丢弃 sub 保留 page（挂载期规范化）
    expect(parseHash('#/data/extra/more', CLIENT_ALLOWED)).toEqual({ page: 'data', sub: '' })
  })

  it('旧 #dc= 链接（迭代 118 分享形态）server 构建兼容映射 → packages 页 sub 段', () => {
    expect(parseHash('#dc=quick', SERVER_ALLOWED)).toEqual({ page: 'packages', sub: 'quick' })
    expect(parseHash('#dc=windows', SERVER_ALLOWED)).toEqual({ page: 'packages', sub: 'windows' })
    expect(parseHash('#dc=android', SERVER_ALLOWED)).toEqual({ page: 'packages', sub: 'android' })
    // 值词法外（旧机制本就回退默认）也归 packages 页——消费侧 tabFromSub 未知 sub 回退 quick
    expect(parseHash('#dc=whatever', SERVER_ALLOWED)).toEqual({ page: 'packages', sub: 'whatever' })
  })

  it('旧 #dc= 与正典形同一白名单裁剪：client 构建（packages 越权）→ 回退 workbench（PR #313 修复轮回归）', () => {
    // 与 #/packages/windows 在 client 下回退 workbench 完全同口径——旧形态不得成为越权旁路
    expect(parseHash('#dc=windows', CLIENT_ALLOWED)).toEqual({ page: 'workbench', sub: '' })
    expect(parseHash('#dc=quick', CLIENT_ALLOWED)).toEqual({ page: 'workbench', sub: '' })
    expect(parseHash('#dc=android', CLIENT_ALLOWED)).toEqual({ page: 'workbench', sub: '' })
  })
})

describe('formatHash · 纯函数', () => {
  it('sub 空 → 单段；非空 → 两级', () => {
    expect(formatHash('workbench')).toBe('#/workbench')
    expect(formatHash('workbench', '')).toBe('#/workbench')
    expect(formatHash('packages', 'windows')).toBe('#/packages/windows')
    expect(formatHash('help', 'designer.fill')).toBe('#/help/designer.fill')
  })

  it('与 parseHash 互逆（合法输入往返一致）', () => {
    const loc = parseHash('#/packages/windows', SERVER_ALLOWED)
    expect(parseHash(formatHash(loc.page, loc.sub), SERVER_ALLOWED)).toEqual(loc)
  })
})

describe('useHashRoute · hook 行为（jsdom history）', () => {
  it('tab→hash 同步入栈：page 变化 pushState（length +1）；同页重渲染不重复入栈', async () => {
    const { rerender } = renderHook(({ page }) => useHashRoute({ allowed: CLIENT_ALLOWED, page, requestPage: () => true }), {
      initialProps: { page: 'workbench' as TabId },
    })
    await waitFor(() => expect(window.location.hash).toBe(''))
    const len0 = window.history.length

    rerender({ page: 'data' })
    await waitFor(() => expect(window.location.hash).toBe('#/data'))
    expect(window.history.length).toBe(len0 + 1)

    rerender({ page: 'data' })
    await new Promise((r) => setTimeout(r, 20))
    expect(window.location.hash).toBe('#/data')
    expect(window.history.length).toBe(len0 + 1)
  })

  it('sub 写入不入栈：setSub 更新 hash（replaceState），length 不变', async () => {
    const { result, rerender } = renderHook(
      ({ page }) => useHashRoute({ allowed: SERVER_ALLOWED, page, requestPage: () => true }),
      { initialProps: { page: 'packages' as TabId } },
    )
    await waitFor(() => expect(window.location.hash).toBe('#/packages'))
    const len0 = window.history.length

    rerender({ page: 'packages' })
    result.current.setSub('windows')
    await waitFor(() => expect(window.location.hash).toBe('#/packages/windows'))
    expect(window.history.length).toBe(len0)

    // quick 缺省写空段（下载中心口径由调用方传入 ''）
    result.current.setSub('')
    await waitFor(() => expect(window.location.hash).toBe('#/packages'))
    expect(window.history.length).toBe(len0)
  })

  it('hashchange 外部变化页面跟随：手动改 hash → 经 requestPage 切页，放行后不重复入栈', async () => {
    const requestPage = vi.fn((id: TabId) => id === 'jobs')
    const { rerender } = renderHook(({ page }) => useHashRoute({ allowed: CLIENT_ALLOWED, page, requestPage }), {
      initialProps: { page: 'workbench' as TabId },
    })
    await waitFor(() => expect(window.location.hash).toBe(''))

    window.location.hash = '#/jobs'
    window.dispatchEvent(new HashChangeEvent('hashchange'))
    await waitFor(() => expect(requestPage).toHaveBeenCalledWith('jobs'))
    // 放行：调用方 setTab → page prop 更新（模拟 App 接线）
    rerender({ page: 'jobs' })
    await waitFor(() => expect(window.location.hash).toBe('#/jobs'))
  })

  it('守卫取消 pre-revert：back 到别页被拒 → 按戳差 go 回原条目，hash 与栈均不动', async () => {
    // 模拟 App：requestPage 仅在无守卫时放行并推进 page prop；designer 视为守卫挂起（返回 false）
    let page: TabId = 'workbench'
    const requestPage = vi.fn((id: TabId) => {
      if (page === 'designer') return false // dirty 设计器守卫挂起
      page = id
      return true
    })
    const { rerender } = renderHook(({ p }) => useHashRoute({ allowed: CLIENT_ALLOWED, page: p, requestPage }), {
      initialProps: { p: 'workbench' as TabId },
    })
    await waitFor(() => expect(window.location.hash).toBe(''))

    // workbench → designer 入栈
    page = 'designer'
    rerender({ p: 'designer' })
    await waitFor(() => expect(window.location.hash).toBe('#/designer'))
    const len0 = window.history.length

    // back → 落 workbench 条目 → 守卫挂起 → pre-revert 前进回 designer
    //（back 为异步导航：先等守卫收到落点页请求，再断言回退到位——防「hash 尚未离开」假绿）
    window.history.back()
    await waitFor(() => expect(requestPage).toHaveBeenCalledWith('workbench'))
    await waitFor(() => expect(window.location.hash).toBe('#/designer'))
    expect(window.history.length).toBe(len0)
    // page prop 从未推进（App 侧 setTab 未发生）
    expect(page).toBe('designer')
  })

  it('守卫取消 pre-revert（back 截断序列回归）：back 放行后再切页截断前向历史，dirty 守卫 back 仍精确回退', async () => {
    // PR #313 评审阻断项 2 复现序列：wb→data→jobs→back 放行回 data→切 designer（pushState 截断 jobs，
    // 栈 [戳1,2,4] 戳位错位）→ dirty 守卫 back → 落 data(戳2) → 旧实现 go(4−2) 超栈尾不动、
    // hash 停 '#/data' 而页面仍是设计器。修复后戳位对齐（栈 [戳1,2,3]），go(1) 精确回 designer。
    let page: TabId = 'workbench'
    const requestPage = vi.fn((id: TabId) => {
      if (page === 'designer') return false // dirty 设计器守卫挂起
      page = id
      return true
    })
    const { rerender } = renderHook(({ p }) => useHashRoute({ allowed: CLIENT_ALLOWED, page: p, requestPage }), {
      initialProps: { p: 'workbench' as TabId },
    })
    await waitFor(() => expect(window.location.hash).toBe(''))

    // wb(戳1) → data(戳2) → jobs(戳3) 线性入栈
    page = 'data'
    rerender({ p: 'data' })
    await waitFor(() => expect(window.location.hash).toBe('#/data'))
    page = 'jobs'
    rerender({ p: 'jobs' })
    await waitFor(() => expect(window.location.hash).toBe('#/jobs'))
    const lenLinear = window.history.length

    // back 放行回 data（模拟 App：requestPage 放行后 setTab → page prop 更新，不重复入栈）
    window.history.back()
    await waitFor(() => expect(requestPage).toHaveBeenCalledWith('data'))
    rerender({ p: 'data' })
    await waitFor(() => expect(page).toBe('data'))
    expect(window.history.length).toBe(lenLinear)

    // 切 designer：pushState 截断 jobs 前向条目（此后相邻条目戳差必须仍为 1）
    page = 'designer'
    rerender({ p: 'designer' })
    await waitFor(() => expect(window.location.hash).toBe('#/designer'))
    const lenTruncated = window.history.length

    // dirty 守卫下 back → 落 data 条目 → 挂起 → pre-revert 精确回 designer：hash / 栈 / page 三不动
    window.history.back()
    await waitFor(() => expect(requestPage).toHaveBeenCalledTimes(2))
    await waitFor(() => expect(window.location.hash).toBe('#/designer'))
    expect(window.history.length).toBe(lenTruncated)
    expect(page).toBe('designer')
  })

  it('守卫挂起无戳落点降级：手动改 hash（新条目无戳）→ URL 重写回当前页（多一条目，已知边缘）', async () => {
    let page: TabId = 'designer'
    const requestPage = vi.fn((id: TabId) => {
      if (page === 'designer') return false
      page = id
      return true
    })
    renderHook(({ p }) => useHashRoute({ allowed: CLIENT_ALLOWED, page: p, requestPage }), {
      initialProps: { p: 'designer' as TabId },
    })
    await waitFor(() => expect(window.location.hash).toBe('#/designer'))
    const len0 = window.history.length

    // 手动改地址栏（jsdom 等价）：压入无戳条目并触发 hashchange
    window.location.hash = '#/data'
    window.dispatchEvent(new HashChangeEvent('hashchange'))
    await waitFor(() => expect(window.location.hash).toBe('#/designer'))
    expect(requestPage).toHaveBeenCalledWith('data')
    // 降级语义：手动条目本身已 +1，重写不再增条
    expect(window.history.length).toBe(len0 + 1)
    expect(page).toBe('designer')
  })

  it('挂载规范化：非法 / 越权 / 旧形态 hash → replaceState 规范化；空 hash 不动', async () => {
    // 越权（server 白名单下 #/help）→ 规范化为 #/workbench
    window.location.hash = '#/help'
    renderHook(() => useHashRoute({ allowed: SERVER_ALLOWED, page: 'workbench', requestPage: () => true }))
    await waitFor(() => expect(window.location.hash).toBe('#/workbench'))
    cleanup()

    // 旧 #dc=windows（server 页合法）→ 规范化为 #/packages/windows
    window.location.hash = '#dc=windows'
    renderHook(() => useHashRoute({ allowed: SERVER_ALLOWED, page: 'packages', requestPage: () => true }))
    await waitFor(() => expect(window.location.hash).toBe('#/packages/windows'))
    cleanup()

    // 旧 #dc=windows 在 client 构建（packages 越权）→ 与正典形同口径回退并规范化为 #/workbench
    window.location.hash = '#dc=windows'
    renderHook(() => useHashRoute({ allowed: CLIENT_ALLOWED, page: 'workbench', requestPage: () => true }))
    await waitFor(() => expect(window.location.hash).toBe('#/workbench'))
    cleanup()

    // 空 hash 不动（保持 URL 干净）
    window.location.hash = ''
    renderHook(() => useHashRoute({ allowed: CLIENT_ALLOWED, page: 'workbench', requestPage: () => true }))
    await new Promise((r) => setTimeout(r, 20))
    expect(window.location.hash).toBe('')
  })
})
