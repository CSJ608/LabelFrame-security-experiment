// 自研 hash 路由（迭代 127 · #312，决策 #177）：URL hash（#/<page>/<sub> 两级形态）成为页面位置的
// 权威载体——page 段取既有 TabId 按构建白名单裁剪（旧 #dc= 链接先折算为正典形走同一裁剪，无旁路），
// sub 段为页内次级不透明短串（V3.1 帮助深链 designer.fill 形锚点串预留，本轮由下载中心页内 tab 首用）。
// 历史栈语义：切页 pushState 入栈（state 写序号戳且戳与栈位置严格对齐——入栈戳＝来点条目戳＋1，
// back 截断前向历史后重推不跳号，戳差恒等于步数差，供守卫取消 pre-revert 按戳差精确回退）；
// sub 变更 replaceState 不入栈；守卫挂起（requestPage 返回 false）时页面与 hash 均不前进、不入栈；
// 无戳条目（手动改地址栏等外部来源）算不出步数差，降级为 URL 重写回当前页（多一条历史记录，已知边缘）。

import { useCallback, useEffect, useRef, useState } from 'react'
import type { TabId } from '../state/types'

/** 路由解析结果：page = 主导航页（回退 workbench）；sub = 页内次级段（缺省空串）。 */
export interface RouteLocation {
  page: TabId
  sub: string
}

/** sub 段词法：字母 / 数字 / 点 / 下划线 / 连字符（V3.1 锚点串含点，收窄会挡深链）。 */
const SUB_PATTERN = /^[A-Za-z0-9._-]+$/

/** history.state 内的路由序号戳键名（pre-revert 按戳差算步数）。 */
const ROUTE_SEQ_KEY = 'rseq'

/** 读条目 state 内的路由序号戳（无 state / 无戳 / 非数 → null）。 */
function readStamp(state: unknown): number | null {
  if (state && typeof state === 'object' && ROUTE_SEQ_KEY in state) {
    const v = (state as Record<string, unknown>)[ROUTE_SEQ_KEY]
    if (typeof v === 'number') return v
  }
  return null
}

/**
 * 解析 hash → 路由位置（纯函数，双构建共用）：
 * 空 hash（'' / '#' / '#/'）→ 默认 workbench；旧 #dc=<v>（迭代 118 分享链接）先折算为正典两级形
 * #/packages/<v> 再解析——与正典形同一构建白名单裁剪口径（决策 #177：client 构建 #dc= 回退
 * workbench，server 保持兼容直达）；#/<page> 或 #/<page>/<sub>——page 不在白名单（构建裁剪）或
 * 词法不符 → 回退 workbench（sub 一并丢弃）；sub 词法不符 → 丢弃 sub 保留 page。
 */
export function parseHash(hash: string, allowed: readonly TabId[]): RouteLocation {
  if (hash === '' || hash === '#' || hash === '#/') return { page: 'workbench', sub: '' }
  const legacy = hash.match(/^#dc=([A-Za-z0-9._-]+)/)
  const canonical = legacy ? `#/packages/${legacy[1]}` : hash
  const m = canonical.match(/^#\/([A-Za-z0-9_-]+)(?:\/(.*))?$/)
  if (m) {
    const inAllowed = (allowed as readonly string[]).includes(m[1])
    if (!inAllowed) return { page: 'workbench', sub: '' }
    const sub = m[2] !== undefined && SUB_PATTERN.test(m[2]) ? m[2] : ''
    return { page: m[1] as TabId, sub }
  }
  return { page: 'workbench', sub: '' }
}

/** 组装规范形 hash：sub 非空为两级、空为单级（缺省 sub 不写段）。 */
export function formatHash(page: TabId, sub = ''): string {
  return sub ? `#/${page}/${sub}` : `#/${page}`
}

/** useHashRoute 入参：白名单 + 当前页 + 切页请求（复用守卫统一入口，返回 false = 被守卫挂起）。 */
export interface HashRouteOptions {
  allowed: readonly TabId[]
  page: TabId
  requestPage: (id: TabId) => boolean
}

/** useHashRoute 产物：sub 段状态与写入器（replaceState 不入栈）。 */
export interface HashRoute {
  sub: string
  setSub: (next: string) => void
}

/**
 * hash 路由 hook（App 壳层唯一接线点）：
 * - tab → hash 同步：page 变化且 hash 页段不一致时 pushState 入栈（守卫放行后才发生）——
 *   一致则不写（back/forward 已到位，不重复入栈）；新入栈条目 sub 清空（主导航重进 = 页内次级回默认）。
 * - hashchange（后退 / 前进 / 手动改 URL）：解析落点——页不同则经 requestPage 切页（继承离开守卫）；
 *   守卫挂起时按「当前条目戳 − 落点条目戳」go(反向步数) 精确回退（pre-revert），无戳落点降级重写 URL。
 * - 挂载规范化：非空 hash 非法 / 越权 / 旧形态 → replaceState 规范化（空 hash 不动，保持 URL 干净）。
 */
export function useHashRoute({ allowed, page, requestPage }: HashRouteOptions): HashRoute {
  const [sub, setSubState] = useState(() => parseHash(window.location.hash, allowed).sub)
  // refs：单一 hashchange 监听不随渲染重建；seq 自增戳只由本 hook 写入 history.state
  const seqRef = useRef(0)
  const homeSeqRef = useRef<number | null>(null)
  const pageRef = useRef(page)
  const subRef = useRef(sub)
  const requestPageRef = useRef(requestPage)
  const allowedRef = useRef(allowed)

  useEffect(() => {
    pageRef.current = page
  }, [page])
  useEffect(() => {
    subRef.current = sub
  }, [sub])
  useEffect(() => {
    requestPageRef.current = requestPage
  }, [requestPage])
  useEffect(() => {
    allowedRef.current = allowed
  }, [allowed])

  // 挂载规范化 + 给当前条目补戳（挂载前条目无戳，补戳使首次守卫取消即可算步数）；
  // 刷新场景 state 跨刷新保留——已有戳则沿用（栈下方旧链不重排，戳差仍等于栈位置差）
  useEffect(() => {
    const parsed = parseHash(window.location.hash, allowedRef.current)
    const seq = readStamp(window.history.state) ?? seqRef.current + 1
    seqRef.current = Math.max(seqRef.current, seq)
    const stamp = { [ROUTE_SEQ_KEY]: seq }
    if (window.location.hash === '') {
      // 空 hash 不动 URL（保持干净），只补戳
      window.history.replaceState(stamp, '', '')
    } else {
      window.history.replaceState(stamp, '', formatHash(parsed.page, parsed.sub))
    }
    homeSeqRef.current = seq
  }, [])

  // tab → hash 同步：仅守卫放行后的真实切页入栈（switchTab 挂起时不 setTab，本 effect 不跑）。
  // 入栈戳＝来点（当前条目）戳＋1——与栈位置严格对齐（back 截断前向历史后重推不跳号，戳差恒等于
  // 步数差）；来点无戳（手动改地址栏等外部来源）先就地补戳再入栈，守卫取消回退到它也能算步数
  useEffect(() => {
    const parsed = parseHash(window.location.hash, allowedRef.current)
    if (parsed.page === page) return
    let base = readStamp(window.history.state)
    if (base === null) {
      seqRef.current += 1
      window.history.replaceState({ [ROUTE_SEQ_KEY]: seqRef.current }, '')
      base = seqRef.current
    }
    const next = base + 1
    seqRef.current = Math.max(seqRef.current, next)
    window.history.pushState({ [ROUTE_SEQ_KEY]: next }, '', formatHash(page))
    homeSeqRef.current = next
    setSubState('')
  }, [page])

  // hashchange：外部变化（后退 / 前进 / 手动改 URL）——pushState / replaceState 不触发本事件
  useEffect(() => {
    const onHashChange = () => {
      const landingSeq = readStamp(window.history.state)
      const parsed = parseHash(window.location.hash, allowedRef.current)
      if (parsed.page !== pageRef.current) {
        if (!requestPageRef.current(parsed.page)) {
          // 守卫挂起：pre-revert 精确回退（戳差 = 步数，仅当落点有戳且来点已知）；无戳落点降级重写 URL
          if (landingSeq !== null && homeSeqRef.current !== null) {
            window.history.go(homeSeqRef.current - landingSeq)
          } else {
            seqRef.current += 1
            window.history.replaceState(
              { [ROUTE_SEQ_KEY]: seqRef.current },
              '',
              formatHash(pageRef.current, subRef.current),
            )
            homeSeqRef.current = seqRef.current
          }
          return
        }
      }
      setSubState(parsed.sub)
      homeSeqRef.current = landingSeq
    }
    window.addEventListener('hashchange', onHashChange)
    return () => window.removeEventListener('hashchange', onHashChange)
  }, [])

  // sub 写入：replaceState 不入栈（循迭代 118 下载中心 switchTo 先例）；保留既有 state（戳不丢）
  const setSub = useCallback((next: string) => {
    setSubState(next)
    window.history.replaceState(window.history.state, '', formatHash(pageRef.current, next))
  }, [])

  return { sub, setSub }
}
