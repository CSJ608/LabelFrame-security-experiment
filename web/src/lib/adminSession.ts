import { isServerUi } from './uiMode'
import { DRAFT_KEY } from '../state/draft'

export interface AdminSession { authenticated: boolean; configured: boolean; csrfToken: string }
const listeners = new Set<() => void>()
const requests = new Set<AbortController>()
let generation = 0
let csrfToken = ''
let authenticated = false
let boundaryActive = false
const invalidationKey = 'labelframe.admin.invalidated'

/** 启用页面请求门禁，公开读取仍可使用。 */
export function activateAdminBoundary() { boundaryActive = true }

/** 防止卸载前的旧回调在匿名态重新发出管理请求。 */
export function assertAdminRequestAllowed(path: string, method = 'GET') {
  if (!isServerUi || !boundaryActive || authenticated) return
  const normalized = '/' + path.replace(/^\/+/, '').split('?')[0]
  if (method.toUpperCase() === 'GET' && ['/healthz', '/api/server/info', '/api/client-packages', '/api/pda-packages'].includes(normalized)) return
  throw new DOMException('管理身份已失效', 'AbortError')
}

/** 订阅管理身份失效，不传递任何凭据。 */
export function onAdminInvalidated(listener: () => void) {
  listeners.add(listener)
  return () => { listeners.delete(listener) }
}

/** 清除旧版敏感持久化数据。 */
export function clearAdminStorage() {
  try { window.sessionStorage.removeItem(DRAFT_KEY) } catch { /* 隐私模式不影响内存清理。 */ }
  try { window.localStorage.removeItem('labelframe.defaultTargetDeviceId') } catch { /* 同上。 */ }
}

/** 作废当前请求域并通知管理树退出。 */
export function invalidateAdmin(broadcast = true) {
  generation++
  csrfToken = ''
  authenticated = false
  for (const controller of requests) controller.abort()
  requests.clear()
  clearAdminStorage()
  for (const listener of listeners) listener()
  if (broadcast) {
    try { window.localStorage.setItem(invalidationKey, String(Date.now()) + ':' + Math.random()) } catch { /* 广播通道仍可用。 */ }
    try {
      const channel = new BroadcastChannel(invalidationKey)
      channel.postMessage('invalidate')
      channel.close()
    } catch { /* 不支持时使用 storage 事件。 */ }
  }
}

/** 监听其他标签页的失效通知。 */
export function watchAdminTabs() {
  const receive = () => invalidateAdmin(false)
  const storage = (event: StorageEvent) => { if (event.key === invalidationKey) receive() }
  window.addEventListener('storage', storage)
  let channel: BroadcastChannel | undefined
  try { channel = new BroadcastChannel(invalidationKey); channel.onmessage = receive } catch { /* storage 兜底。 */ }
  return () => { window.removeEventListener('storage', storage); channel?.close() }
}

/** 为 Server 请求附加内存防伪令牌并绑定身份代数。 */
export function beginAdminRequest(init: RequestInit = {}, enabled = isServerUi) {
  const epoch = generation
  const controller = new AbortController()
  if (enabled) requests.add(controller)
  const signals = [controller.signal, ...(init.signal ? [init.signal] : [])]
  const headers = new Headers(init.headers)
  if (enabled && csrfToken && !['GET', 'HEAD'].includes((init.method ?? 'GET').toUpperCase())) headers.set('X-LabelFrame-CSRF', csrfToken)
  const assertCurrent = () => {
    if (enabled && (epoch !== generation || controller.signal.aborted)) throw new DOMException('请求所属会话已失效', 'AbortError')
  }
  return {
    init: enabled ? { ...init, headers, credentials: 'same-origin' as const, cache: 'no-store' as const, redirect: 'error' as const, signal: AbortSignal.any(signals) } : init,
    assertCurrent,
    response: (status: number) => {
      assertCurrent()
      if (enabled && authenticated && status === 401) invalidateAdmin()
      assertCurrent()
    },
    dispose: () => requests.delete(controller),
  }
}

async function authRequest(path: string, init?: RequestInit): Promise<AdminSession> {
  const scope = beginAdminRequest({ ...init, signal: AbortSignal.timeout(30000) }, true)
  try {
    const response = await fetch('/api/auth/' + path, scope.init)
    scope.assertCurrent()
    if (!response.ok) throw new Error('HTTP ' + response.status)
    const result = await response.json() as AdminSession
    scope.assertCurrent()
    return result
  } finally { scope.dispose() }
}

/** 读取当前 Cookie 身份及其绑定的防伪令牌。 */
export async function readAdminSession(): Promise<AdminSession> {
  const epoch = generation
  const session = await authRequest('session')
  if (epoch !== generation) throw new DOMException('请求所属会话已失效', 'AbortError')
  if (typeof session.authenticated !== 'boolean' || typeof session.configured !== 'boolean' || !session.csrfToken) throw new Error('无效会话响应')
  csrfToken = session.csrfToken
  authenticated = session.authenticated
  return session
}

/** 登录后重新获取身份绑定令牌。 */
export async function loginAdmin(password: string): Promise<AdminSession> {
  const epoch = generation
  const current = await readAdminSession()
  if (epoch !== generation) throw new DOMException('请求所属会话已失效', 'AbortError')
  if (current.authenticated) return current
  await authRequest('login', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ password }) })
  if (epoch !== generation) throw new DOMException('请求所属会话已失效', 'AbortError')
  return readAdminSession()
}

/** 本地先失效，再使用原身份令牌请求服务端撤销。 */
export async function logoutAdmin(): Promise<void> {
  const token = csrfToken
  invalidateAdmin()
  await Promise.resolve()
  const response = await fetch('/api/auth/logout', {
    method: 'POST', credentials: 'same-origin', cache: 'no-store', redirect: 'error',
    headers: { 'X-LabelFrame-CSRF': token }, signal: AbortSignal.timeout(30000),
  })
  if (!response.ok) throw new Error('HTTP ' + response.status)
}
