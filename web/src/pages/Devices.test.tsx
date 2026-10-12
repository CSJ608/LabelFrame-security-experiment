// @vitest-environment jsdom
// 在线设备页（Server UI 专用）——设备列表（deviceId / 名称 / lastIp / 在线状态 / 最近心跳）
// 每 5s 自动刷新；点击在线设备设为数据与打印默认目标（localStorage labelframe.defaultTargetDeviceId，
// AppContext 共享跨页联动，选中高亮 + 状态提示）；离线设备不可设为默认。

import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { act, cleanup, configure, fireEvent, render, screen } from '@testing-library/react'
import type { DeviceView } from '../lib/api/types'
import { AppProvider } from '../state/AppContext'
import { Devices } from './Devices'

// 挂载链（server 构建为 healthz → serverMode → 设备列表加载）为多段 promise +
// React 真实宏任务调度，CI 高负载 runner 上偶发超过 findBy 默认 1000ms；统一放宽到 8000ms
//（与 JobHistory / DataPrint / Settings 同口径），单测总超时由 vitest.config testTimeout（20s）兜住。
configure({ asyncUtilTimeout: 8000 })

const mocks = vi.hoisted(() => ({
  server: {
    healthz: vi.fn(),
    listDevices: vi.fn(),
  },
  local: {
    getHostConfig: vi.fn(),
    getTransport: vi.fn(),
  },
}))

vi.mock('../lib/api/client', () => ({
  serverApi: mocks.server,
  localApi: mocks.local,
  setServerBaseUrl: vi.fn(),
  probeHealthz: vi.fn(),
}))

vi.mock('../lib/uiMode', () => ({ UI_MODE: 'server', isServerUi: true }))

const DEVICES: DeviceView[] = [
  { deviceId: 'device-1', name: '仓库-1 打印电脑', registeredAt: '2026-08-11T00:00:00Z', lastSeenAt: '2026-08-11T01:00:00Z', status: 'Online', lastIp: '192.168.1.5' },
  { deviceId: 'device-2', name: '仓库-2 打印电脑', registeredAt: '2026-08-11T00:00:00Z', lastSeenAt: '2026-08-10T23:00:00Z', status: 'Offline', lastIp: '192.168.1.6' },
]

const DEFAULT_TARGET_KEY = 'labelframe.defaultTargetDeviceId'

function Harness() {
  return (
    <AppProvider>
      <Devices />
    </AppProvider>
  )
}

beforeEach(() => {
  vi.clearAllMocks()
  window.localStorage.clear()
  window.sessionStorage.clear()
  mocks.server.healthz.mockResolvedValue({ service: 'LabelFrame.Server', status: 'ok' })
  mocks.server.listDevices.mockResolvedValue(DEVICES)
})

afterEach(() => {
  vi.useRealTimers()
  cleanup()
})

describe('在线设备页：列表渲染', () => {
  it('显示 deviceId / 名称 / lastIp / 在线状态 / 最近心跳', async () => {
    render(<Harness />)
    expect(await screen.findByText('device-1')).toBeTruthy()
    expect(screen.getByText('仓库-1 打印电脑')).toBeTruthy()
    expect(screen.getByText('192.168.1.5')).toBeTruthy()
    expect(screen.getByText('在线')).toBeTruthy()
    // 最近心跳 = 本地时区 MM-dd HH:mm:ss（2026-08-11T01:00:00Z 按本地时区展示；两台设备可能同一天）
    // 最近心跳 = 本地时区 MM-dd HH:mm:ss；两台设备分钟均为 00，任意时区（UTC / UTC+8）都各自匹配一条
    expect(screen.getAllByText(/\d{2}-\d{2} \d{2}:00:00/).length).toBeGreaterThanOrEqual(2)
    // 离线设备
    expect(screen.getByText('仓库-2 打印电脑')).toBeTruthy()
    expect(screen.getByText('离线')).toBeTruthy()
  })

  it('首次拉取完成前显示加载态而非「暂无设备」（迭代 93 · #151 F-16 决议 a 案）', async () => {
    // 首次请求挂起：进页只见「正在加载设备列表…」，不闪现空态误导
    let resolveList: (list: DeviceView[]) => void = () => {}
    mocks.server.listDevices.mockReturnValue(
      new Promise<DeviceView[]>((resolve) => {
        resolveList = resolve
      }),
    )
    render(<Harness />)
    expect(screen.getByText('正在加载设备列表…')).toBeTruthy()
    expect(screen.queryByText('暂无设备')).toBeNull()

    // 拉取完成（空列表）：才呈现真正的空态
    await act(async () => {
      resolveList([])
    })
    expect(await screen.findByText('暂无设备')).toBeTruthy()
    expect(screen.queryByText('正在加载设备列表…')).toBeNull()
  })
})

describe('在线设备页：点击设为默认（Y2）', () => {
  it('点击在线设备：写入 localStorage + 高亮「默认」徽标 + 状态提示', async () => {
    render(<Harness />)
    fireEvent.click(await screen.findByText('仓库-1 打印电脑'))
    expect(window.localStorage.getItem(DEFAULT_TARGET_KEY)).toBe('device-1')
    expect(await screen.findByText('默认')).toBeTruthy()
    expect(screen.getByText('已将「仓库-1 打印电脑」设为数据与打印默认目标。')).toBeTruthy()
  })

  it('点击离线设备：不写入 localStorage + 提示不可设', async () => {
    render(<Harness />)
    fireEvent.click(await screen.findByText('仓库-2 打印电脑'))
    expect(window.localStorage.getItem(DEFAULT_TARGET_KEY)).toBeNull()
    expect(screen.getByText('设备「仓库-2 打印电脑」当前离线，无法设为默认目标。')).toBeTruthy()
    expect(screen.queryByText('默认')).toBeNull()
  })

  it('localStorage 已有默认目标：对应设备行高亮「默认」', async () => {
    window.localStorage.setItem(DEFAULT_TARGET_KEY, 'device-2')
    render(<Harness />)
    // device-2 虽离线，仍是用户上次点选记录——高亮保留（数据与打印侧对离线默认值会回退到第一台在线）
    expect(await screen.findByText('默认')).toBeTruthy()
  })
})

describe('在线设备页：5s 轮询（G2，在线状态翻转时效最坏约 37s，不要求即时）', () => {
  it('每 5s 重新拉取设备列表并更新', async () => {
    vi.useFakeTimers()
    render(<Harness />)
    // 首次拉取立即执行（无定时器延迟）；挂载链（healthz → serverMode → 列表加载）有界多轮冲洗至落定
    // （React 调度走真实宏任务，固定单轮冲洗在高负载 runner 上可能残留未落定代际—— 同类时序敏感）
    for (let i = 0; i < 15 && mocks.server.listDevices.mock.calls.length < 1; i++) {
      await act(async () => {
        await vi.advanceTimersByTimeAsync(0)
      })
    }
    await act(async () => {
      await vi.advanceTimersByTimeAsync(0)
    })
    await act(async () => {
      await vi.advanceTimersByTimeAsync(0)
    })
    expect(screen.getByText('device-1')).toBeTruthy()
    expect(mocks.server.listDevices).toHaveBeenCalledTimes(1)

    // 设备 1 掉线、设备 2 上线：下一次轮询后 UI 翻转
    mocks.server.listDevices.mockResolvedValue([
      { ...DEVICES[0], status: 'Offline' },
      { ...DEVICES[1], status: 'Online', lastIp: '10.0.0.2' },
    ])
    await act(async () => {
      await vi.advanceTimersByTimeAsync(5000)
    })
    // 5s 轮询定时器已触发；其 promise → 列表渲染链再补两轮空转落定后断言
    await act(async () => {
      await vi.advanceTimersByTimeAsync(0)
    })
    await act(async () => {
      await vi.advanceTimersByTimeAsync(0)
    })
    expect(mocks.server.listDevices).toHaveBeenCalledTimes(2)
    expect(screen.getByText('10.0.0.2')).toBeTruthy()
    // 状态翻转（原在线 device-1 变离线）
    expect(screen.getAllByText('离线').length).toBeGreaterThanOrEqual(1)
    expect(screen.getAllByText('在线').length).toBeGreaterThanOrEqual(1)
  })
})
