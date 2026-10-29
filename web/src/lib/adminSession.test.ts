// @vitest-environment jsdom
import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { invalidateAdmin, loginAdmin, logoutAdmin, readAdminSession, onAdminInvalidated } from './adminSession'
import { serverApi, serverRequest } from './api/client'
vi.mock('./uiMode', () => ({ isServerUi: true, UI_MODE: 'server' }))

const fetchMock = vi.fn()
const session = (authenticated = true) => new Response(JSON.stringify({ authenticated, configured: true, csrfToken: authenticated ? 'identity-token' : 'anonymous-token' }))
beforeEach(() => { invalidateAdmin(false); vi.stubGlobal('fetch', fetchMock); fetchMock.mockReset() })
afterEach(() => { invalidateAdmin(false); vi.unstubAllGlobals() })

it('登录先取匿名令牌，成功后重新读取身份绑定令牌', async () => {
  fetchMock.mockResolvedValueOnce(session(false)).mockResolvedValueOnce(new Response('{}')).mockResolvedValueOnce(session())
  await loginAdmin('synthetic-only')
  expect(fetchMock.mock.calls.map(call => call[0])).toEqual(['/api/auth/session', '/api/auth/login', '/api/auth/session'])
  expect(new Headers(fetchMock.mock.calls[1][1].headers).get('X-LabelFrame-CSRF')).toBe('anonymous-token')
  fetchMock.mockResolvedValue(new Response('{}'))
  await serverRequest('/api/client-updates/publish', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: '{}' })
  const init = fetchMock.mock.calls.at(-1)![1]
  expect(new Headers(init.headers).get('X-LabelFrame-CSRF')).toBe('identity-token')
  expect(init.credentials).toBe('same-origin')
  expect(init.cache).toBe('no-store')
  expect(init.redirect).toBe('error')
})

it('上传与 blob 写请求携带 CSRF，不覆盖 multipart 类型', async () => {
  fetchMock.mockResolvedValueOnce(session())
  await readAdminSession()
  fetchMock.mockResolvedValueOnce(new Response('[]'))
  await serverApi.uploadClientPackage(new File(['synthetic'], 'sample.msi'))
  const upload = fetchMock.mock.calls.at(-1)![1]
  expect(new Headers(upload.headers).get('X-LabelFrame-CSRF')).toBe('identity-token')
  expect(new Headers(upload.headers).has('Content-Type')).toBe(false)
  fetchMock.mockResolvedValueOnce(new Response('image'))
  await serverApi.previewTemplate('sample')
  expect(new Headers(fetchMock.mock.calls.at(-1)![1].headers).get('X-LabelFrame-CSRF')).toBe('identity-token')
})

it.each(['json', 'blob'] as const)('退出取消请求，%s 响应体晚到也不得返回数据', async kind => {
  fetchMock.mockResolvedValueOnce(session())
  await readAdminSession()
  let finish!: (value: unknown) => void
  const body = new Promise(resolve => { finish = resolve })
  fetchMock.mockResolvedValueOnce({ ok: true, status: 200, headers: new Headers(), [kind]: () => body })
  const pending = kind === 'json' ? serverApi.listTemplates() : serverApi.previewTemplate('sample')
  const rejected = expect(pending).rejects.toMatchObject({ name: 'AbortError' })
  await Promise.resolve()
  const signal = fetchMock.mock.calls.at(-1)![1].signal as AbortSignal
  invalidateAdmin(false)
  expect(signal.aborted).toBe(true)
  finish(kind === 'json' ? [{ name: 'sensitive' }] : new Blob(['sensitive']))
  await rejected
})

it('401 通知清理并取消其他在途请求', async () => {
  fetchMock.mockResolvedValueOnce(session())
  await readAdminSession()
  const invalidated = vi.fn()
  const stop = onAdminInvalidated(invalidated)
  fetchMock.mockResolvedValueOnce(new Response('{}', { status: 401 }))
  await expect(serverApi.listTemplates()).rejects.toMatchObject({ name: 'AbortError' })
  expect(invalidated).toHaveBeenCalledTimes(1)
  stop()
})

it('失效后迟到的登录响应不能继续读取会话或恢复身份', async () => {
  let finish!: (value: Response) => void
  fetchMock.mockResolvedValueOnce(session(false)).mockImplementationOnce(() => new Promise<Response>(resolve => { finish = resolve }))
  const pending = loginAdmin('synthetic-only')
  const rejected = expect(pending).rejects.toMatchObject({ name: 'AbortError' })
  await vi.waitFor(() => expect(fetchMock).toHaveBeenCalledTimes(2))
  invalidateAdmin(false)
  finish(new Response('{}'))
  await rejected
  expect(fetchMock).toHaveBeenCalledTimes(2)
})

it('注销先清本地，再请求撤销；失败保持失败结果且不存令牌', async () => {
  fetchMock.mockResolvedValueOnce(session())
  await readAdminSession()
  window.sessionStorage.setItem('labelframe.printDraft', 'sensitive')
  window.localStorage.setItem('labelframe.defaultTargetDeviceId', 'sensitive')
  const invalidated = vi.fn()
  const stop = onAdminInvalidated(invalidated)
  fetchMock.mockImplementationOnce(() => {
    expect(invalidated).toHaveBeenCalledTimes(1)
    expect(window.sessionStorage.getItem('labelframe.printDraft')).toBeNull()
    expect(window.localStorage.getItem('labelframe.defaultTargetDeviceId')).toBeNull()
    return Promise.resolve(new Response('{}', { status: 503 }))
  })
  await expect(logoutAdmin()).rejects.toThrow('503')
  expect(new Headers(fetchMock.mock.calls.at(-1)![1].headers).get('X-LabelFrame-CSRF')).toBe('identity-token')
  stop()
})
