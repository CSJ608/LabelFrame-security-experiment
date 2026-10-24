// @vitest-environment jsdom
import { afterEach, expect, it, vi } from 'vitest'

vi.mock('./uiMode', () => ({ isServerUi: false }))
afterEach(() => { vi.unstubAllGlobals(); vi.resetModules() })

it('清除编辑后立即重启，必须等待旧报告并发送最新状态', async () => {
  const calls: { path: string; dirty?: boolean }[] = []
  let release!: (value: Response) => void
  const oldReport = new Promise<Response>(resolve => { release = resolve })
  const ok = () => new Response(JSON.stringify({ preparationId: null }), { status: 200 })
  vi.stubGlobal('fetch', vi.fn((path: string, init?: RequestInit) => {
    calls.push({ path, dirty: init?.body ? JSON.parse(String(init.body)).dirty : undefined })
    return calls.length === 1 ? oldReport : Promise.resolve(ok())
  }))
  const lifecycle = await import('./hostLifecycle')
  lifecycle.setHostDirty('settings', true)
  const stop = lifecycle.startHostActivity()
  try {
    lifecycle.setHostDirty('settings', false)
    const restart = lifecycle.restartHost()
    await Promise.resolve()
    await Promise.resolve()
    expect(calls.some(call => call.path === '/api/host/restart')).toBe(false)
    release(ok())
    await restart
    expect(calls.at(-1)?.path).toBe('/api/host/restart')
    expect(calls.filter(call => call.path === '/api/host/ui-activity').at(-1)?.dirty).toBe(false)
  } finally { release(ok()); stop() }
})
