// @vitest-environment jsdom
import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import i18n from '../i18n'
import { AndroidUpdatePublishing } from './AndroidUpdatePublishing'

const token = 'secret'.repeat(8)
const draft = 'LabelFrame.apk'
const release = { id: 'batch-1', package: { versionName: '1.5.2', versionCode: 10502, abis: ['arm64-v8a'], sha256: 'digest' }, devices: ['pda-1'], paused: false, publishedAt: '2026-10-27T00:00:00Z' }
beforeEach(async () => { await i18n.changeLanguage('zh-CN') })
afterEach(() => { cleanup(); vi.unstubAllGlobals(); vi.restoreAllMocks() })

async function load() {
  fireEvent.change(screen.getByLabelText('升级管理口令（仅本页内存）'), { target: { value: token } })
  fireEvent.click(screen.getByRole('button', { name: '读取发布与设备状态' }))
  await screen.findByRole('combobox')
  fireEvent.change(screen.getByRole('combobox'), { target: { value: draft } })
}

it('只列APK草稿，默认拒绝空范围，去重设备且只发布确认策略，口令仅用于请求头', async () => {
  const fetchMock = vi.fn(async () => Response.json([]))
  vi.stubGlobal('fetch', fetchMock)
  const stored = localStorage.length
  render(<AndroidUpdatePublishing fileNames={[draft, 'client.msi']} />)
  await load()
  expect(screen.queryByRole('option', { name: 'client.msi' })).toBeNull()
  const publish = screen.getByRole('button', { name: '校验并发布升级' }) as HTMLButtonElement
  expect(publish.disabled).toBe(true)
  fireEvent.change(screen.getByRole('textbox'), { target: { value: ' pda-1，pda-2\npda-1 ' } })
  fireEvent.click(publish)
  await waitFor(() => expect(fetchMock).toHaveBeenCalledWith('api/android-updates/publish', expect.objectContaining({
    method: 'POST', redirect: 'error', headers: { Authorization: `Bearer ${token}`, 'Content-Type': 'application/json' },
    body: JSON.stringify({ fileName: draft, devices: ['pda-1', 'pda-2'], allDevicesConfirmed: false, mode: 'confirm' }),
  })))
  expect(localStorage.length).toBe(stored)
})

it('全量必须显式确认；暂停和恢复只修改批次状态', async () => {
  let paused = false
  const fetchMock = vi.fn(async (url: string, init: RequestInit) => {
    if (url.endsWith('/pause')) paused = (JSON.parse(init.body as string) as { paused: boolean }).paused
    return Response.json(url.endsWith('/status') ? [] : [{ ...release, paused }])
  })
  vi.stubGlobal('fetch', fetchMock)
  render(<AndroidUpdatePublishing fileNames={[draft]} />)
  await load()
  fireEvent.click(screen.getByRole('checkbox'))
  fireEvent.click(screen.getByRole('button', { name: '校验并发布升级' }))
  await waitFor(() => expect(fetchMock).toHaveBeenCalledWith('api/android-updates/publish', expect.objectContaining({ body: JSON.stringify({ fileName: draft, devices: [], allDevicesConfirmed: true, mode: 'confirm' }) })))
  await waitFor(() => expect((screen.getByRole('button', { name: '暂停' }) as HTMLButtonElement).disabled).toBe(false))
  fireEvent.click(screen.getByRole('button', { name: '暂停' }))
  fireEvent.click(await screen.findByRole('button', { name: '恢复' }))
  await screen.findByRole('button', { name: '暂停' })
  expect(fetchMock).toHaveBeenCalledWith('api/android-updates/batch-1/pause', expect.objectContaining({ method: 'PUT', body: '{"paused":false}' }))
})

it('拒绝权限并保留服务端原始诊断，不显示未经读取的表单', async () => {
  vi.stubGlobal('fetch', vi.fn(async () => new Response('', { status: 403 })))
  render(<AndroidUpdatePublishing fileNames={[draft]} />)
  fireEvent.change(screen.getByLabelText('升级管理口令（仅本页内存）'), { target: { value: token } })
  fireEvent.click(screen.getByRole('button', { name: '读取发布与设备状态' }))
  expect((await screen.findByRole('alert')).textContent).toContain('管理权限不足')
  expect(screen.queryByRole('combobox')).toBeNull()
})

it('切换口令后旧请求不能回填；离页取消请求', async () => {
  let finish!: (response: Response) => void
  const fetchMock = vi.fn(() => new Promise<Response>(resolve => { finish = resolve }))
  vi.stubGlobal('fetch', fetchMock)
  const { unmount } = render(<AndroidUpdatePublishing fileNames={[draft]} />)
  const input = screen.getByLabelText('升级管理口令（仅本页内存）')
  fireEvent.change(input, { target: { value: token } })
  fireEvent.click(screen.getByRole('button', { name: '读取发布与设备状态' }))
  const signal = (fetchMock.mock.calls[0] as unknown as [string, RequestInit])[1].signal!
  fireEvent.change(input, { target: { value: 'other'.repeat(8) } })
  expect(signal.aborted).toBe(true)
  finish(Response.json([]))
  await Promise.resolve()
  expect(screen.queryByRole('combobox')).toBeNull()
  fireEvent.click(screen.getByRole('button', { name: '读取发布与设备状态' }))
  const nextSignal = (fetchMock.mock.calls[2] as unknown as [string, RequestInit])[1].signal!
  unmount()
  expect(nextSignal.aborted).toBe(true)
})

it('英文状态、时间与陈旧标识明确区分自报，失败原因原文透传', async () => {
  await i18n.changeLanguage('en')
  vi.stubGlobal('fetch', vi.fn(async (url: string) => Response.json(url.endsWith('/status') ? [{
    status: { deviceId: 'pda-1', currentVersionCode: 10501, targetVersionCode: 10502, phase: 'unknown', reason: '原始诊断', transactionId: 'transaction-1' },
    receivedAt: '2020-01-01T00:00:00Z',
  }] : [release])))
  render(<AndroidUpdatePublishing fileNames={[draft]} />)
  fireEvent.change(screen.getByLabelText('Update administration token (memory only)'), { target: { value: token } })
  fireEvent.click(screen.getByRole('button', { name: 'Load releases and device status' }))
  expect(await screen.findByText('Outcome unknown; printing frozen')).toBeTruthy()
  expect(screen.getByText('Stale report: older than 5 minutes')).toBeTruthy()
  expect(screen.getByText('原始诊断')).toBeTruthy()
  expect(screen.getByText('transaction-1')).toBeTruthy()
})

it('发布校验错误展示原文且不重复提交', async () => {
  const fetchMock = vi.fn(async (url: string) => url.endsWith('/publish') ? Response.json({ message: 'APK 信任校验失败' }, { status: 400 }) : Response.json([]))
  vi.stubGlobal('fetch', fetchMock)
  render(<AndroidUpdatePublishing fileNames={[draft]} />)
  await load()
  fireEvent.change(screen.getByRole('textbox'), { target: { value: 'pda-1' } })
  const button = screen.getByRole('button', { name: '校验并发布升级' })
  fireEvent.click(button); fireEvent.click(button)
  expect((await screen.findByRole('alert')).textContent).toBe('APK 信任校验失败')
  expect(fetchMock.mock.calls.filter(([url]) => url.endsWith('/publish'))).toHaveLength(1)
  expect((button as HTMLButtonElement).disabled).toBe(true)
  fireEvent.click(screen.getByRole('button', { name: '读取发布与设备状态' }))
  await waitFor(() => expect((button as HTMLButtonElement).disabled).toBe(false))
})
