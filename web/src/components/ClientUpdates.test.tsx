// @vitest-environment jsdom
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import i18n from '../i18n'
import { ClientUpdates } from './ClientUpdates'

const lifecycle = vi.hoisted(() => ({ dirty: vi.fn(), report: vi.fn(), begin: vi.fn(() => () => {}) }))
vi.mock('../lib/hostLifecycle', () => ({ setHostDirty: lifecycle.dirty, reportHostActivity: lifecycle.report, beginHostOperation: lifecycle.begin }))
const settings = { mode: 'download', windowStart: '02:00', windowEnd: '04:00' }
const status = { phase: 'ready', currentVersion: '1.4.0', targetVersion: '1.5.0', downloadedBytes: 0, totalBytes: 0 }
const fetchMock = vi.fn()
beforeEach(async () => {
  await i18n.changeLanguage('zh-CN')
  vi.stubGlobal('fetch', fetchMock)
  fetchMock.mockImplementation(async () => Response.json({ settings, status }))
})
afterEach(() => { cleanup(); vi.unstubAllGlobals(); vi.clearAllMocks() })

describe('客户端自动升级体验', () => {
  it('默认自动下载且安装失败保留页面与明确原因', async () => {
    fetchMock.mockImplementation(async (url: string) => url.endsWith('/install')
      ? Response.json({ message: '打印任务尚未完成' }, { status: 409 }) : Response.json({ settings, status }))
    render(<ClientUpdates />)
    const policy = await screen.findByRole('combobox')
    expect((policy as HTMLSelectElement).value).toBe('download')
    fireEvent.click(screen.getByRole('button', { name: '安装并重启客户端' }))
    expect((await screen.findByRole('alert')).textContent).toContain('打印任务尚未完成')
    expect(lifecycle.report).toHaveBeenCalled()
    expect(lifecycle.begin).not.toHaveBeenCalled()
  })

  it('显式保存维护时间并保护尚未保存的策略编辑', async () => {
    let saved = settings
    fetchMock.mockImplementation(async (_url: string, init: RequestInit) => {
      if (init?.method === 'PUT') saved = JSON.parse(init.body as string)
      return Response.json({ settings: saved, status })
    })
    render(<ClientUpdates />)
    fireEvent.change(await screen.findByRole('combobox'), { target: { value: 'maintenance' } })
    expect(screen.getByText(/仅在维护时间内/)).toBeTruthy()
    expect(lifecycle.dirty).toHaveBeenCalledWith('client-update-settings', true)
    expect((screen.getByRole('button', { name: '安装并重启客户端' }) as HTMLButtonElement).disabled).toBe(true)
    fireEvent.click(screen.getByRole('button', { name: '保存更新设置' }))
    await waitFor(() => expect(saved.mode).toBe('maintenance'))
    await waitFor(() => expect(lifecycle.dirty).toHaveBeenLastCalledWith('client-update-settings', false))
  })

  it('英文界面说明静默条件，旧接口缺失时保留手工升级说明', async () => {
    await i18n.changeLanguage('en')
    fetchMock.mockResolvedValue(Response.json({ settings: { ...settings, mode: 'maintenance' }, status }))
    const view = render(<ClientUpdates />)
    expect(await screen.findByText('Automatic client updates')).toBeTruthy()
    expect(screen.getByText(/A missed window waits until the next day/)).toBeTruthy()
    view.unmount()
    fetchMock.mockImplementation(async () => new Response('', { status: 404 }))
    render(<ClientUpdates />)
    expect(await screen.findByText(/Install a newer client manually first/)).toBeTruthy()
  })
})
