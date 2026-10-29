// @vitest-environment jsdom
import { useEffect } from 'react'
import { act, cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import App from './App'
import { serverApi } from './lib/api/client'
import { invalidateAdmin } from './lib/adminSession'
import i18n from './i18n'
import { useApp } from './state/AppContext'
vi.mock('./lib/uiMode', () => ({ UI_MODE: 'server', isServerUi: true }))
vi.mock('./pages/Workbench', () => ({
  Workbench: function Workbench() {
    const app = useApp()
    useEffect(() => {
      void serverApi.listTemplates().catch(() => {})
      const timer = setInterval(() => { void serverApi.listTemplates().catch(() => {}) }, 500)
      return () => clearInterval(timer)
    }, [])
    return <div>management-probe
      <span data-testid="draft-name">{app.printDraft.selectedName}</span>
      <span data-testid="log-count">{app.logs.length}</span>
      <button onClick={() => { app.setDraftSelected('sensitive-template'); app.log('sensitive-log') }}>seed-sensitive</button>
    </div>
  },
}))
const fetchMock = vi.fn()
let signedIn = false
beforeEach(async () => {
  await i18n.changeLanguage('zh-CN')
  invalidateAdmin(false)
  window.history.replaceState(null, '', '/#/workbench')
  signedIn = false
  vi.stubGlobal('fetch', fetchMock)
  fetchMock.mockReset().mockImplementation(async (url: string) => {
    if (url === '/api/auth/session') return new Response(JSON.stringify({ authenticated: signedIn, configured: true, csrfToken: signedIn ? 'identity-token' : 'anonymous-token' }))
    if (url === '/api/auth/login') { signedIn = true; return new Response('{}') }
    if (url === '/api/auth/logout') { signedIn = false; return new Response('{}') }
    if (url === '/api/server/info') return new Response(JSON.stringify({ publicBaseUrl: 'https://public.example.test' }))
    if (url === '/healthz') return new Response('{}')
    return new Response('[]')
  })
})
afterEach(() => { cleanup(); vi.unstubAllGlobals(); vi.useRealTimers() })
async function login() {
  await waitFor(() => expect((screen.getByLabelText('管理员密码') as HTMLInputElement).disabled).toBe(false))
  fireEvent.change(screen.getByLabelText('管理员密码'), { target: { value: 'synthetic-only' } })
  fireEvent.submit(screen.getByLabelText('管理员密码').closest('form')!)
  await screen.findByText('management-probe')
}

it.each(['workbench', 'designer/text', 'devices', 'jobs', 'plugin-packages', 'packages/windows', 'packages/android'])('匿名深链 %s 不挂管理树也不发管理请求', async path => {
  window.history.replaceState(null, '', '/#/' + path)
  render(<App />)
  await waitFor(() => expect((screen.getByLabelText('管理员密码') as HTMLInputElement).disabled).toBe(false))
  expect(screen.queryByText('management-probe')).toBeNull()
  expect(fetchMock.mock.calls.map(call => call[0])).toEqual(['/api/auth/session'])
})

it('公开下载只请求最小信息与包列表，无网卡或发布管理请求', async () => {
  window.history.replaceState(null, '', '/#/packages')
  render(<App />)
  await screen.findByText('https://public.example.test')
  expect(new Set(fetchMock.mock.calls.map(call => call[0]))).toEqual(new Set(['/api/auth/session', '/api/server/info', '/api/client-packages', '/api/pda-packages']))
  expect(document.querySelector('input[type=file]')).toBeNull()
})

it('静态帮助不请求业务数据', async () => {
  window.history.replaceState(null, '', '/#/help')
  render(<App />)
  await waitFor(() => expect(fetchMock).toHaveBeenCalledTimes(1))
  expect(fetchMock.mock.calls[0][0]).toBe('/api/auth/session')
})

it('登录挂新管理树；退出先卸载并停止轮询，失败提示撤销未确认', async () => {
  render(<App />)
  await login()
  let removedBeforeRequest = false
  fetchMock.mockImplementationOnce(async () => {
    removedBeforeRequest = screen.queryByText('management-probe') === null
    return new Response('{}', { status: 503 })
  })
  fireEvent.click(screen.getByText('退出管理'))
  await screen.findByRole('alert')
  expect(screen.getByRole('alert').textContent).toContain('撤销尚未确认')
  expect(removedBeforeRequest).toBe(true)
  expect(screen.queryByText('management-probe')).toBeNull()
  const count = fetchMock.mock.calls.filter(call => call[0] === '/api/templates').length
  await act(async () => { await new Promise(resolve => setTimeout(resolve, 650)) })
  expect(fetchMock.mock.calls.filter(call => call[0] === '/api/templates')).toHaveLength(count)
})

it('管理 API 返回 401 后销毁敏感树并清空草稿存储', async () => {
  render(<App />)
  await login()
  window.sessionStorage.setItem('labelframe.printDraft', 'sensitive')
  fetchMock.mockResolvedValueOnce(new Response('{}', { status: 401 }))
  await act(async () => { await expect(serverApi.getLogs()).rejects.toMatchObject({ name: 'AbortError' }) })
  expect(screen.queryByText('management-probe')).toBeNull()
  expect(window.sessionStorage.getItem('labelframe.printDraft')).toBeNull()
})

it('再次登录创建空草稿与空日志，不复活退出前的管理状态', async () => {
  render(<App />)
  await login()
  fireEvent.click(screen.getByText('seed-sensitive'))
  expect(screen.getByTestId('draft-name').textContent).toBe('sensitive-template')
  expect(screen.getByTestId('log-count').textContent).toBe('1')
  expect(window.sessionStorage.getItem('labelframe.printDraft')).toBeNull()
  fireEvent.click(screen.getByText('退出管理'))
  await login()
  expect(screen.getByTestId('draft-name').textContent).toBe('')
  expect(screen.getByTestId('log-count').textContent).toBe('0')
})

it('其他标签退出立即清理管理树，旧回调不能再发管理请求', async () => {
  render(<App />)
  await login()
  act(() => window.dispatchEvent(new StorageEvent('storage', { key: 'labelframe.admin.invalidated', newValue: 'change' })))
  expect(screen.queryByText('management-probe')).toBeNull()
  const count = fetchMock.mock.calls.length
  await expect(serverApi.listTemplates()).rejects.toMatchObject({ name: 'AbortError' })
  expect(fetchMock.mock.calls).toHaveLength(count)
})

it('注销在途收到重复跨标签失效仍禁止登录，直到撤销结果确定', async () => {
  render(<App />)
  await login()
  let finish!: (value: Response) => void
  fetchMock.mockImplementationOnce(() => new Promise<Response>(resolve => { finish = resolve }))
  fireEvent.click(screen.getByText('退出管理'))
  await waitFor(() => expect(finish).toBeTypeOf('function'))
  act(() => window.dispatchEvent(new StorageEvent('storage', { key: 'labelframe.admin.invalidated', newValue: 'duplicate' })))
  const input = screen.getByLabelText('管理员密码') as HTMLInputElement
  expect(input.disabled).toBe(true)
  const count = fetchMock.mock.calls.length
  fireEvent.change(input, { target: { value: 'synthetic-only' } })
  fireEvent.submit(input.closest('form')!)
  expect(fetchMock.mock.calls).toHaveLength(count)
  await act(async () => { finish(new Response('{}', { status: 503 })) })
  expect(screen.getByRole('alert').textContent).toContain('撤销尚未确认')
  expect(input.disabled).toBe(false)
  expect(screen.queryByText('management-probe')).toBeNull()
})

it('重复焦点核验只发一次请求，失效后的迟到身份不能恢复管理树', async () => {
  render(<App />)
  await login()
  let finish!: (value: Response) => void
  fetchMock.mockImplementationOnce(() => new Promise<Response>(resolve => { finish = resolve }))
  const count = fetchMock.mock.calls.filter(call => call[0] === '/api/auth/session').length
  act(() => { window.dispatchEvent(new Event('focus')); window.dispatchEvent(new Event('focus')) })
  expect(fetchMock.mock.calls.filter(call => call[0] === '/api/auth/session')).toHaveLength(count + 1)
  act(() => invalidateAdmin(false))
  await act(async () => { finish(new Response(JSON.stringify({ authenticated: true, configured: true, csrfToken: 'late-token' }))) })
  expect(screen.queryByText('management-probe')).toBeNull()
  await expect(serverApi.listTemplates()).rejects.toMatchObject({ name: 'AbortError' })
})

it('恢复焦点时网络失败会清理身份和敏感树，不继续管理轮询', async () => {
  render(<App />)
  await login()
  fireEvent.click(screen.getByText('seed-sensitive'))
  fetchMock.mockRejectedValueOnce(new TypeError('synthetic offline'))
  await act(async () => { window.dispatchEvent(new Event('focus')) })
  expect(screen.queryByText('management-probe')).toBeNull()
  const count = fetchMock.mock.calls.length
  await expect(serverApi.listTemplates()).rejects.toMatchObject({ name: 'AbortError' })
  expect(fetchMock.mock.calls).toHaveLength(count)
})

it('注销在途切换语言后，失败提示使用最新语言且不恢复管理树', async () => {
  render(<App />)
  await login()
  let finish!: (value: Response) => void
  fetchMock.mockImplementationOnce(() => new Promise<Response>(resolve => { finish = resolve }))
  fireEvent.click(screen.getByText('退出管理'))
  await waitFor(() => expect(finish).toBeTypeOf('function'))
  await act(async () => { await i18n.changeLanguage('en') })
  await act(async () => { finish(new Response('{}', { status: 503 })) })
  expect(screen.getByRole('alert').textContent).toBe(i18n.t('shell:auth.logoutUnconfirmed'))
  expect(screen.queryByText('management-probe')).toBeNull()
})
