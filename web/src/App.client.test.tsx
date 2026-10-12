// @vitest-environment jsdom
// client 构建菜单与状态栏——含 设置（不含 在线设备 / 设备日志）
// 「PDA 日志」页下线——导航入口移除（回传链路不存在前的界面收敛）
// 状态栏在本机打印服务运行中时显示本机 IP（/api/host/config.ips，多 IP 逗号分隔，title 给全量）。
// （决议 2「三名义」）：状态栏改呈现「本机打印服务：运行中 / 未运行」
// 不再用「服务端已连接」兼指本机后台服务可达（评审 B-9）。

import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { cleanup, configure, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import App from './App'
import { GUIDE_SEEN_KEY } from './lib/guide'

// App 挂载链（AppContext 启动链）为多段 promise + React 真实宏任务调度
// CI 高负载 runner 上偶发超过 findBy 默认 1000ms；统一放宽到 8000ms（与其余页面测试同口径），
// 单测总超时由 vitest.config testTimeout（20s）兜住。
configure({ asyncUtilTimeout: 8000 })

const mocks = vi.hoisted(() => ({
  server: {
    healthz: vi.fn(),
    listTemplates: vi.fn(),
    getTemplate: vi.fn(),
    saveTemplate: vi.fn(),
    deleteTemplate: vi.fn(),
    exportTemplate: vi.fn(),
    importTemplate: vi.fn(),
    importExcel: vi.fn(),
    submitJob: vi.fn(),
    getJob: vi.fn(),
    retryJobItem: vi.fn(),
    getJobs: vi.fn(),
    listDevices: vi.fn(),
    renderImage: vi.fn(),
    renderImages: vi.fn(),
    getLogs: vi.fn(),
  },
  local: {
    healthz: vi.fn(),
    listTemplates: vi.fn(),
    getTemplate: vi.fn(),
    saveTemplate: vi.fn(),
    deleteTemplate: vi.fn(),
    exportTemplate: vi.fn(),
    importTemplate: vi.fn(),
    importExcel: vi.fn(),
    submitJob: vi.fn(),
    getJob: vi.fn(),
    retryJobItem: vi.fn(),
    getJobs: vi.fn(),
    listDevices: vi.fn(),
    renderImage: vi.fn(),
    renderImages: vi.fn(),
    getLogs: vi.fn(),
    getTransport: vi.fn(),
    setTransport: vi.fn(),
    testTransport: vi.fn(),
    getPrinterStatus: vi.fn(),
    testPrinter: vi.fn(),
    getHostConfig: vi.fn(),
    setHostConfig: vi.fn(),
  },
  probeHealthz: vi.fn(),
}))

vi.mock('./lib/api/client', () => ({
  serverApi: mocks.server,
  localApi: mocks.local,
  setServerBaseUrl: vi.fn(),
  probeHealthz: mocks.probeHealthz,
}))

vi.mock('./lib/uiMode', () => ({ UI_MODE: 'client', isServerUi: false }))

beforeEach(() => {
  vi.clearAllMocks()
  window.localStorage.clear()
  window.sessionStorage.clear()
  // 本文件测壳层既有功能，以「已看过引导」用户运行——首见自动启动 / 切页行为归 Guide.test.tsx
  window.localStorage.setItem(GUIDE_SEEN_KEY, 'done')
  mocks.server.healthz.mockResolvedValue({ service: 'LabelFrame.Server', status: 'ok' })
  // 本机打印服务探测（localApi.healthz = 页面来源 WinHost）默认运行中
  mocks.local.healthz.mockResolvedValue({ service: 'LabelFrame.WinHost', status: 'ok' })
  mocks.local.getHostConfig.mockResolvedValue({
    serverUrl: 'http://127.0.0.1:53961',
    deviceId: 'PC-1',
    deviceName: 'PC-1',
    ips: ['192.168.1.5', '10.0.0.8'],
  })
  mocks.local.getTransport.mockResolvedValue({ mode: 'Log', params: {} })
  mocks.local.listTemplates.mockResolvedValue([])
  mocks.server.listTemplates.mockResolvedValue([])
})

afterEach(() => {
  cleanup()
})

describe('client 构建：菜单（迭代 20 裁剪守门）', () => {
  it('含 设置；不含 在线设备 / 设备日志 / PDA 日志（迭代 75 日志页下线）', async () => {
    render(<App />)
    expect(await screen.findByRole('button', { name: '设置' })).toBeTruthy()
    expect(screen.queryByRole('button', { name: '在线设备' })).toBeNull()
    expect(screen.queryByRole('button', { name: '设备日志' })).toBeNull()
    // 「PDA 日志」导航入口移除（/api/logs 端点与 logs.db 后端保留）
    expect(screen.queryByRole('button', { name: 'PDA 日志' })).toBeNull()
  })
})

describe('client 构建：状态栏本机 IP（迭代 20 G3）', () => {
  it('本机打印服务运行中时显示本机 IP（多 IP 逗号分隔全部）', async () => {
    render(<App />)
    // localApi.healthz 成功后 localServiceUp=true → IP 显示
    expect(await screen.findByText(/本机 IP：192\.168\.1\.5, 10\.0\.0\.8/)).toBeTruthy()
    // title 给全量
    const el = screen.getByText(/本机 IP：/)
    expect(el.getAttribute('title')).toBe('192.168.1.5, 10.0.0.8')
  })

  it('不显示「同源 · Server 管理界面」（server 构建专属）', async () => {
    render(<App />)
    expect(screen.queryByText(/同源（/)).toBeNull()
    expect(screen.queryByText(/Server 管理界面/)).toBeNull()
  })
})

describe('client 构建：状态栏本机设备名称（迭代 22 §2.1）', () => {
  it('本机打印服务运行中时显示「本机：{deviceName}」（与本机 IP 并列）', async () => {
    render(<App />)
    expect(await screen.findByText(/本机：PC-1/)).toBeTruthy()
    expect(screen.getByText(/本机 IP：192\.168\.1\.5, 10\.0\.0\.8/)).toBeTruthy()
  })

  it('无 deviceName（旧客户端）：不显示本机名称段，本机 IP 仍显示', async () => {
    mocks.local.getHostConfig.mockResolvedValue({ serverUrl: 'http://127.0.0.1:53961', deviceId: 'PC-1', ips: ['192.168.1.5'] })
    render(<App />)
    expect(await screen.findByText(/本机 IP：192\.168\.1\.5/)).toBeTruthy()
    expect(screen.queryByText(/本机：/)).toBeNull()
  })
})

describe('client 构建：状态栏「本机打印服务」（迭代 80 决议 2「三名义」①）', () => {
  it('本机打印服务可达：显示「本机打印服务：运行中」，不再出现「服务端已连接」', async () => {
    render(<App />)
    expect(await screen.findByText('本机打印服务：运行中')).toBeTruthy()
    // 三名义：状态栏不再用「服务端」一词描述本机后台服务
    expect(screen.queryByText('服务端已连接')).toBeNull()
    expect(screen.queryByText('服务端未连接（单机模式可用）')).toBeNull()
  })

  it('本机打印服务不可达：显示「本机打印服务：未运行」', async () => {
    mocks.local.healthz.mockRejectedValue(new Error('down'))
    render(<App />)
    expect(await screen.findByText('本机打印服务：未运行')).toBeTruthy()
  })

  it('服务端地址连通性变化不影响状态栏本机打印服务状态（各义独立探测）', async () => {
    // 远程服务端不可达（healthz 失败）但本机打印服务运行中——状态栏仍「运行中」
    mocks.server.healthz.mockRejectedValue(new Error('down'))
    render(<App />)
    expect(await screen.findByText('本机打印服务：运行中')).toBeTruthy()
  })
})

// 日志抽屉「清空」= 销毁类操作——实心红 danger＋点击先弹确认
// （原灰色 ghost 无确认直接执行）；Esc / 遮罩 / 取消均不清空，确认后才清空。
describe('client 构建：日志抽屉「清空」确认流（迭代 104 · #225）', () => {
  /** 日志抽屉容器（状态栏 msg 与抽屉同文——断言一律限定抽屉内，避免状态栏同名干扰）。 */
  const drawer = () => document.querySelector('.log-drawer') as HTMLElement

  /** 打开日志抽屉并等启动日志落盘（client 挂载链必写一条「已读取本机配置」）。 */
  async function openDrawerWithLogs() {
    render(<App />)
    expect(await screen.findByText('本机打印服务：运行中')).toBeTruthy()
    fireEvent.click(screen.getByRole('button', { name: '日志' }))
    expect(await waitFor(() => expect(within(drawer()).getAllByText(/已读取本机配置/).length).toBeGreaterThan(0))).toBeTruthy()
  }

  it('「清空」为实心红 danger 按钮；点击先弹确认（不可恢复文案），确认后才清空', async () => {
    await openDrawerWithLogs()
    const clearBtn = within(drawer()).getByRole('button', { name: '清空' }) as HTMLButtonElement
    expect(clearBtn.className).toContain('danger')

    // 点击 → 确认弹窗（含不可恢复），日志尚未清空
    fireEvent.click(clearBtn)
    expect(screen.getByRole('dialog')).toBeTruthy()
    expect(screen.getByText(/不可恢复/)).toBeTruthy()
    const confirmBtn = screen.getByRole('button', { name: /确认清空/ }) as HTMLButtonElement
    expect(confirmBtn.className).toContain('danger')
    expect(within(drawer()).getAllByText(/已读取本机配置/).length).toBeGreaterThan(0)

    // 确认 → 弹窗关闭、日志清空（抽屉内不再有该行）
    fireEvent.click(confirmBtn)
    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull())
    expect(within(drawer()).queryByText(/已读取本机配置/)).toBeNull()
  })

  it('确认框点「取消」不清空日志（Esc / 遮罩同默认取消语义）', async () => {
    await openDrawerWithLogs()
    fireEvent.click(within(drawer()).getByRole('button', { name: '清空' }))
    fireEvent.click(screen.getByRole('button', { name: '取消' }))
    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull())
    expect(within(drawer()).getAllByText(/已读取本机配置/).length).toBeGreaterThan(0)
  })
})
