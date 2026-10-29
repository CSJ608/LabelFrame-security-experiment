// @vitest-environment jsdom
import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import i18n from '../i18n'
import { UpdatePublishing } from './UpdatePublishing'

beforeEach(async () => { await i18n.changeLanguage('zh-CN') })
afterEach(() => { cleanup(); vi.unstubAllGlobals(); vi.restoreAllMocks() })

it.each(['windows', 'android'] as const)('列表直接发布 %s，真实版本一次确认且无需口令', async platform => {
  const name = platform === 'android' ? 'draft.apk' : 'draft.msi'
  const pkg = { version: '1.6.0', versionName: '1.6.0', versionCode: 10600, architecture: 'x64', sha256: 'ABC' }
  let published = false
  let paused = false
  const fetchMock = vi.fn(async (url: string, init: RequestInit) => {
    if (url.endsWith('/preview')) return Response.json(pkg)
    if (url.endsWith('/publish')) { published = true; return Response.json({}) }
    if (url.endsWith('/pause')) { paused = (JSON.parse(init.body as string) as { paused: boolean }).paused; return Response.json({}) }
    if (url.endsWith('/drafts')) return Response.json([{ fileName: name, sha256: 'abc' }])
    if (url.endsWith('/status')) return Response.json([])
    return Response.json(published ? [{ id: 'release', package: pkg, devices: [], paused }] : [])
  })
  vi.stubGlobal('fetch', fetchMock)
  render(<UpdatePublishing platform={platform} revision="1">{actions => <div data-testid="row">{actions(name)}</div>}</UpdatePublishing>)
  const row = within(screen.getByTestId('row'))
  await waitFor(() => expect((row.getByRole('button', { name: '发布' }) as HTMLButtonElement).disabled).toBe(false))
  expect(screen.queryByRole('textbox')).toBeNull()
  expect(screen.queryByRole('combobox')).toBeNull()
  fireEvent.click(row.getByRole('button', { name: '发布' }))
  const dialog = await screen.findByRole('dialog')
  expect(within(dialog).getByText('1.6.0')).toBeTruthy()
  expect(published).toBe(false)
  await waitFor(() => expect((within(dialog).getByRole('button', { name: '确认发布' }) as HTMLButtonElement).disabled).toBe(false))
  fireEvent.click(within(dialog).getByRole('button', { name: '确认发布' }))
  await row.findByText('已发布')
  const call = fetchMock.mock.calls.find(([url]) => url.endsWith('/publish'))!
  expect(JSON.parse(call[1].body as string)).toMatchObject({ fileName: name, devices: [], expectedSha256: 'ABC' })
  expect(call[1].headers).not.toHaveProperty('Authorization')
  await waitFor(() => expect((row.getByRole('button', { name: '暂停' }) as HTMLButtonElement).disabled).toBe(false))
  fireEvent.click(row.getByRole('button', { name: '暂停' }))
  await row.findByText('已暂停')
})

it('校验失败不出现确认框且须刷新后重试', async () => {
  vi.stubGlobal('fetch', vi.fn(async (url: string) => url.endsWith('/preview')
    ? Response.json({ message: '签名不符' }, { status: 400 }) : Response.json([])))
  render(<UpdatePublishing platform="android" revision="1">{actions => actions('draft.apk')}</UpdatePublishing>)
  await waitFor(() => expect((screen.getByRole('button', { name: '发布' }) as HTMLButtonElement).disabled).toBe(false))
  fireEvent.click(screen.getByRole('button', { name: '发布' }))
  expect((await screen.findByRole('alert')).textContent).toContain('签名不符')
  expect(screen.queryByRole('dialog')).toBeNull()
  expect((screen.getByRole('button', { name: '发布' }) as HTMLButtonElement).disabled).toBe(true)
})

it('取消确认不发布，离页取消在途预览', async () => {
  let complete: ((value: Response) => void) | undefined
  let previewSignal: AbortSignal | undefined
  let previews = 0
  const fetchMock = vi.fn(async (url: string, init: RequestInit) => {
    if (url.endsWith('/preview')) {
      previews++
      if (previews === 1) return Response.json({ version: '1.6.0', sha256: 'hash' })
      previewSignal = init.signal as AbortSignal
      return new Promise<Response>(resolve => { complete = resolve })
    }
    return Response.json([])
  })
  vi.stubGlobal('fetch', fetchMock)
  const view = render(<UpdatePublishing platform="windows" revision="1">{actions => actions('draft.msi')}</UpdatePublishing>)
  await waitFor(() => expect((screen.getByRole('button', { name: '发布' }) as HTMLButtonElement).disabled).toBe(false))
  fireEvent.click(screen.getByRole('button', { name: '发布' }))
  const dialog = await screen.findByRole('dialog')
  await waitFor(() => expect((within(dialog).getByRole('button', { name: '取消' }) as HTMLButtonElement).disabled).toBe(false))
  fireEvent.click(within(dialog).getByRole('button', { name: '取消' }))
  expect(fetchMock.mock.calls.some(([url]) => url.endsWith('/publish'))).toBe(false)
  fireEvent.click(screen.getByRole('button', { name: '发布' }))
  await waitFor(() => expect(complete).toBeDefined())
  view.unmount()
  expect(previewSignal?.aborted).toBe(true)
  complete!(Response.json({ version: '1.7.0', sha256: 'hash' }))
})
