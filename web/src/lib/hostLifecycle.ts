import { isServerUi } from './uiMode'
import { v4 as uuidv4 } from 'uuid'

const dirtySources = new Set<string>()
let operations = 0
let reporting = false
let sessionId = ''
let sequence = 0
let preparationId: string | null = null
let inFlight: Promise<void> | null = null
let interactionUnixMs = Date.now()

function freezeUi(frozen: boolean): void {
  if (frozen && document.activeElement instanceof HTMLElement) document.activeElement.blur()
  const root = document.getElementById('root')
  if (root) root.inert = frozen
  document.body.style.cursor = frozen ? 'wait' : ''
}

function getSessionId(): string {
  if (sessionId) return sessionId
  try {
    sessionId = sessionStorage.getItem('labelframe-ui-session') ?? uuidv4()
    sessionStorage.setItem('labelframe-ui-session', sessionId)
  } catch { sessionId = uuidv4() }
  return sessionId
}

/** 将当前界面活动同步给本机宿主。 */
export async function reportHostActivity(closed = false): Promise<void> {
  if (isServerUi || !reporting) return
  if (closed) { await sendHostActivity(true); return }
  while (inFlight) await inFlight
  if (!reporting) return
  const report = sendHostActivity(closed)
  inFlight = report
  try { await report }
  finally { if (inFlight === report) inFlight = null }
}

async function sendHostActivity(closed: boolean): Promise<void> {
  sequence = Math.max(sequence + 1, Date.now() * 1000)
  try {
    const response = await fetch('/api/host/ui-activity', {
      method: 'POST', headers: { 'Content-Type': 'application/json' }, keepalive: closed,
      body: JSON.stringify({ sessionId: getSessionId(), dirty: dirtySources.size > 0, busy: operations > 0, closed, sequence, preparationId, interactionUnixMs }),
    })
    if (response.status === 200 || response.status === 409) {
      const state = await response.json() as { preparationId?: string | null }
      const next = state.preparationId ?? null
      if (next !== preparationId) {
        preparationId = next
        freezeUi(!!next)
        if (next && !closed) setTimeout(() => void reportHostActivity(), 0)
      }
    }
  } catch { /* 失联不清除宿主已记录的未保存状态。 */ }
}

/** 登记需要保留的界面更改。 */
export function setHostDirty(source: string, dirty: boolean): void {
  if (dirtySources.has(source) === dirty) return
  if (dirty) dirtySources.add(source)
  else dirtySources.delete(source)
  void reportHostActivity()
}

/** 保护一个正在进行的写操作。 */
export function beginHostOperation(): () => void {
  operations++
  void reportHostActivity()
  return () => { operations--; void reportHostActivity() }
}

/** 启动当前页面的活动报告。 */
export function startHostActivity(): () => void {
  if (isServerUi) return () => {}
  reporting = true
  void reportHostActivity()
  const timer = setInterval(() => void reportHostActivity(), 1000)
  const close = () => void reportHostActivity(true)
  const interact = () => { interactionUnixMs = Date.now() }
  window.addEventListener('pointerdown', interact, true)
  window.addEventListener('keydown', interact, true)
  window.addEventListener('pagehide', close)
  return () => {
    close()
    reporting = false
    clearInterval(timer)
    window.removeEventListener('pagehide', close)
    window.removeEventListener('pointerdown', interact, true)
    window.removeEventListener('keydown', interact, true)
  }
}

export interface HostLifecycle {
  instanceId: string
  restartRequired: boolean
  restarting: boolean
}

/** 读取本机进程与插件待重启状态。 */
export async function readHostLifecycle(): Promise<HostLifecycle | null> {
  try {
    const response = await fetch('/api/host/lifecycle')
    if (!response.ok) return null
    const state = await response.json() as HostLifecycle
    return typeof state.instanceId === 'string' ? state : null
  } catch { return null }
}

/** 请求本机客户端安全重启。 */
export async function restartHost(): Promise<void> {
  await reportHostActivity()
  const response = await fetch('/api/host/restart', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: '{}' })
  if (!response.ok) {
    const error = await response.json() as { message?: string }
    throw new Error(error.message ?? `HTTP ${response.status}`)
  }
}
