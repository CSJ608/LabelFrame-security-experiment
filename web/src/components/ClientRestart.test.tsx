// @vitest-environment jsdom
import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { ClientRestart } from './ClientRestart'
import '../i18n'

const mocks = vi.hoisted(() => ({ read: vi.fn(), restart: vi.fn() }))
vi.mock('../lib/hostLifecycle', () => ({ readHostLifecycle: mocks.read, restartHost: mocks.restart }))

afterEach(() => { cleanup(); vi.clearAllMocks() })

describe('客户端重启入口', () => {
  it('插件变更持续显示入口，忙态失败显示原因且允许重试', async () => {
    mocks.read.mockResolvedValue({ instanceId: 'one', restartRequired: true })
    mocks.restart.mockRejectedValueOnce(new Error('打印任务尚未完成')).mockResolvedValueOnce(undefined)
    render(<ClientRestart contextual />)
    const button = await screen.findByRole('button', { name: '立即重启' })
    fireEvent.click(button)
    expect((await screen.findByRole('alert')).textContent).toContain('打印任务尚未完成')
    fireEvent.click(screen.getByRole('button', { name: '立即重启' }))
    await waitFor(() => expect(mocks.restart).toHaveBeenCalledTimes(2))
    expect((screen.getByRole('button', { name: '正在重启客户端…' }) as HTMLButtonElement).disabled).toBe(true)
  })

  it('当前进程没有插件变更时隐藏上下文入口，常规设置保留入口', async () => {
    mocks.read.mockResolvedValue({ instanceId: 'two', restartRequired: false })
    const view = render(<ClientRestart contextual />)
    await waitFor(() => expect(mocks.read).toHaveBeenCalled())
    expect(screen.queryByRole('button')).toBeNull()
    view.unmount()
    render(<ClientRestart />)
    await waitFor(() => expect((screen.getByRole('button', { name: '重启客户端' }) as HTMLButtonElement).disabled).toBe(false))
  })

  it('旧客户端不提供生命周期接口时明确显示不支持', async () => {
    mocks.read.mockResolvedValue(null)
    render(<ClientRestart />)
    expect(screen.getByText('当前客户端不支持一键重启。')).toBeTruthy()
    expect((screen.getByRole('button', { name: '重启客户端' }) as HTMLButtonElement).disabled).toBe(true)
  })
})
