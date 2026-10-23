// @vitest-environment jsdom
import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import i18n from '../i18n'
import { ClientUpdatePublishing } from './ClientUpdatePublishing'

beforeEach(async () => { await i18n.changeLanguage('zh-CN') })
afterEach(() => { cleanup(); vi.unstubAllGlobals(); vi.clearAllMocks() })

it('管理口令仅在请求头使用，显式发布指定设备后可暂停', async () => {
  const release = { id: 'release', package: { version: '1.5.0', architecture: 'x64', sha256: 'hash' }, devices: ['device-a'], paused: false }
  const fetchMock = vi.fn(async (url: string) => {
    if (url.endsWith('/publish')) return Response.json(release)
    if (url.endsWith('/pause')) { release.paused = true; return Response.json(release) }
    return Response.json(url.endsWith('/status') ? [] : [release])
  })
  vi.stubGlobal('fetch', fetchMock)
  const stored = localStorage.length
  render(<ClientUpdatePublishing fileNames={['LabelFrame-Client-1.5.0.msi']} />)
  const token = 'x'.repeat(40)
  fireEvent.change(screen.getByLabelText('升级管理口令（仅本页内存）'), { target: { value: token } })
  fireEvent.click(screen.getByRole('button', { name: '读取发布与设备状态' }))
  fireEvent.change(await screen.findByRole('combobox'), { target: { value: 'LabelFrame-Client-1.5.0.msi' } })
  fireEvent.change(screen.getByRole('textbox'), { target: { value: 'device-a' } })
  fireEvent.click(screen.getByRole('button', { name: '校验并发布升级' }))
  await waitFor(() => expect(fetchMock).toHaveBeenCalledWith('/api/client-updates/publish', expect.objectContaining({
    headers: expect.objectContaining({ Authorization: `Bearer ${token}` }), body: JSON.stringify({ fileName: 'LabelFrame-Client-1.5.0.msi', devices: ['device-a'] }),
  })))
  await waitFor(() => expect((screen.getByRole('button', { name: '暂停' }) as HTMLButtonElement).disabled).toBe(false))
  fireEvent.click(screen.getByRole('button', { name: '暂停' }))
  expect(await screen.findByRole('button', { name: '恢复' })).toBeTruthy()
  expect(localStorage.length).toBe(stored)
})
