// @vitest-environment jsdom
// F6：作业历史页——服务端 / 单机降级列表、空态按模式文案、刷新、徽标区分。
// mock 覆盖组件树用到的全部 client 方法（含 AppContext 启动链）。
// 目标设备列设备名解析与编号收敛断言。
// （C-5，决议 2）：行展开明细——本机直连作业逐张结果（状态 / 失败原因）；服务端形态汇总 + 如实占位。

import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { act, cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import type { DeviceView, JobView } from '../lib/api/types'
import { AppProvider } from '../state/AppContext'
import { JobHistory } from './JobHistory'

const mocks = vi.hoisted(() => ({
  server: {
    healthz: vi.fn(),
    getJobs: vi.fn(),
    // 目标设备列设备名解析（deviceId → name）
    listDevices: vi.fn(),
  },
  local: {
    healthz: vi.fn(),
    getJobs: vi.fn(),
    getHostConfig: vi.fn(),
    getTransport: vi.fn(),
  },
  clipboard: {
    // 编号一键复制（jsdom 无 Clipboard API / execCommand，mock 模块）
    copyText: vi.fn(),
  },
}))

vi.mock('../lib/api/client', () => ({ serverApi: mocks.server, localApi: mocks.local, setServerBaseUrl: vi.fn() }))
vi.mock('../lib/clipboard', () => ({ copyText: mocks.clipboard.copyText }))
// 本文件为 client 构建语义用例，显式注入 client 分支（VITE_UI_MODE=server 整仓测试时保持稳定）
vi.mock('../lib/uiMode', () => ({ UI_MODE: 'client', isServerUi: false }))

/* * ：设备名解析表数据源——device-1 / device-2 有名称，device-3 无名称（回退设备 ID）。 */
const DEVICES: DeviceView[] = [
  { deviceId: 'device-1', name: '仓库-1 打印电脑', registeredAt: '2026-08-11T00:00:00Z', lastSeenAt: '2026-08-11T01:00:00Z', status: 'Online' },
  { deviceId: 'device-2', name: '仓库-2 打印电脑', registeredAt: '2026-08-11T00:00:00Z', lastSeenAt: '2026-08-11T01:00:00Z', status: 'Online' },
  { deviceId: 'device-3', name: '', registeredAt: '2026-08-11T00:00:00Z', lastSeenAt: '2026-08-11T01:00:00Z', status: 'Online' },
]

const JOBS: JobView[] = [
  {
    jobId: 'job-aaa-1',
    requestId: 'req-aaa-1',
    status: 'Completed',
    totalItems: 3,
    completedItems: 3,
    targetDeviceId: 'device-1',
    createdAt: '2026-08-11T10:30:00Z',
  },
  {
    jobId: 'job-bbb-2',
    requestId: 'req-bbb-2',
    status: 'Failed',
    totalItems: 2,
    completedItems: 1,
    failedItems: 1,
    errorMessage: '打印机缺纸',
    targetDeviceId: 'device-2',
    createdAt: '2026-08-11T09:00:00Z',
  },
  {
    jobId: 'job-ccc-3',
    requestId: 'req-ccc-3',
    status: 'Printing',
    totalItems: 5,
    completedItems: 2,
    createdAt: '2026-08-11T08:00:00Z',
  },
  {
    jobId: 'job-ddd-4',
    requestId: 'req-ddd-4',
    status: 'Expired',
    totalItems: 1,
    completedItems: 0,
    errorMessage: '暂存超过 12 小时未投递，服务端已放弃；需重打请用新 requestId 重发。',
    targetDeviceId: 'device-3',
    createdAt: '2026-08-10T20:00:00Z',
  },
]

/** 挂载链等待超时（ms）：AppProvider 启动链（getHostConfig → healthz → serverMode）→ 首次列表加载
 *  为多段 promise + React 真实宏任务调度，CI 高负载 runner 上偶发超过 findBy / waitFor 默认 1000ms
 * （实证：「服务端模式：列表渲染」找不到「已完成」，2530ms）；统一放宽（与 DataPrint.test 同口径）。 */
const MOUNT_WAIT = { timeout: 8000 }

function renderJobHistory() {
  return render(
    <AppProvider>
      <JobHistory />
    </AppProvider>,
  )
}

beforeEach(() => {
  vi.clearAllMocks()
  window.localStorage.clear()
  window.sessionStorage.clear()
  mocks.server.healthz.mockResolvedValue({ service: 'LabelFrame.Server', status: 'ok' })
  mocks.local.getHostConfig.mockResolvedValue({ serverUrl: 'http://127.0.0.1:53961', deviceId: 'device-1', deviceName: 'PC' })
  mocks.local.getTransport.mockResolvedValue({ mode: 'Log', params: {} })
  mocks.server.getJobs.mockResolvedValue(JOBS)
  mocks.local.getJobs.mockResolvedValue(JOBS)
  // 设备名解析与一键复制默认就绪（用例内可覆盖）
  mocks.server.listDevices.mockResolvedValue(DEVICES)
  mocks.clipboard.copyText.mockResolvedValue(true)
})

afterEach(() => {
  cleanup()
})

describe('作业历史页（迭代 18 F6）', () => {
  it('服务端模式：列表渲染（时间 / 作业编号 / 目标设备 / 状态 / 完成-失败 / 失败原因），走 serverApi', async () => {
    renderJobHistory()
    expect(await screen.findByText('已完成', undefined, MOUNT_WAIT)).toBeTruthy()
    // 三行作业都渲染
    expect(screen.getByText('失败')).toBeTruthy()
    expect(screen.getByText('打印中')).toBeTruthy()
    // 目标设备列：有名称显示设备名，无名称回退设备 ID，无目标显示「本机」
    expect(screen.getByText('仓库-1 打印电脑')).toBeTruthy()
    expect(screen.getByText('仓库-2 打印电脑')).toBeTruthy()
    expect(screen.getByText('device-3')).toBeTruthy()
    expect(screen.getByText('本机')).toBeTruthy()
    // 完成-失败张数与失败原因
    expect(screen.getByText(/3\/3/)).toBeTruthy()
    expect(screen.getByText('打印机缺纸')).toBeTruthy()
    // 双 base 守门：服务端模式走 serverApi
    // 客户端构建在服务端模式下传本机 deviceId（只看自己的作业）
    expect(mocks.server.getJobs).toHaveBeenCalledWith(100, 'device-1')
    expect(mocks.local.getJobs).not.toHaveBeenCalled()
  })

  it('状态徽标区分终态 / 进行中', async () => {
    renderJobHistory()
    await screen.findByText('已完成', undefined, MOUNT_WAIT)
    const completed = screen.getByText('已完成').closest('span')
    const printing = screen.getByText('打印中').closest('span')
    expect(completed?.className).toContain('ok')
    expect(printing?.className).toContain('info')
  })

  it('Expired 状态：显示「已过期」中文标签与中性终态样式', async () => {
    renderJobHistory()
    expect(await screen.findByText('已过期', undefined, MOUNT_WAIT)).toBeTruthy()
    const expired = screen.getByText('已过期').closest('span')
    // 已过期是服务端放弃的终态：中性灰徽标（非 ok / err / info）
    expect(expired?.className).toContain('neutral')
    expect(expired?.className).not.toContain('info')
    // 失败原因列透出服务端放弃说明
    expect(screen.getByText(/服务端已放弃/)).toBeTruthy()
  })

  it('刷新按钮：重新拉取列表', async () => {
    renderJobHistory()
    await screen.findByText('已完成', undefined, MOUNT_WAIT)
    mocks.server.getJobs.mockClear()
    fireEvent.click(screen.getByRole('button', { name: /刷新/ }))
    await waitFor(() => expect(mocks.server.getJobs).toHaveBeenCalledTimes(1), MOUNT_WAIT)
  })

  it('空态（服务端模式）：提示保留期文案', async () => {
    mocks.server.getJobs.mockResolvedValue([])
    renderJobHistory()
    expect(await screen.findByText('暂无历史作业', undefined, MOUNT_WAIT)).toBeTruthy()
    expect(screen.getByText('打印记录默认保留 30 天，到期自动清理。')).toBeTruthy()
  })

  it('单机降级（healthz 失败 → standalone）：列表走 localApi，空态文案为本机不自动清理', async () => {
    mocks.server.healthz.mockRejectedValue(new Error('down'))
    renderJobHistory()
    // 等 serverMode 探测完成 → standalone → localApi.getJobs（本机历史无需 deviceId 过滤）
    await waitFor(() => expect(mocks.local.getJobs).toHaveBeenCalledWith(100, undefined), MOUNT_WAIT)
    expect(mocks.server.getJobs).not.toHaveBeenCalled()
    expect(await screen.findByText('已完成', undefined, MOUNT_WAIT)).toBeTruthy()
  })

  it('客户端构建服务端模式：本机无 deviceId（旧客户端）时不传过滤参数（看本机全部）', async () => {
    mocks.local.getHostConfig.mockResolvedValue({ serverUrl: 'http://127.0.0.1:53961' })
    renderJobHistory()
    await waitFor(() => expect(mocks.server.getJobs).toHaveBeenCalledWith(100, undefined), MOUNT_WAIT)
  })

  it('单机降级空态：本机打印记录不自动清理文案', async () => {
    mocks.server.healthz.mockRejectedValue(new Error('down'))
    mocks.local.getJobs.mockResolvedValue([])
    renderJobHistory()
    expect(await screen.findByText('暂无历史作业', undefined, MOUNT_WAIT)).toBeTruthy()
    expect(screen.getByText('保存在本机的打印记录不会自动清理。')).toBeTruthy()
  })

  it('加载失败：显示错误信息', async () => {
    mocks.server.getJobs.mockRejectedValue(new Error('network down'))
    renderJobHistory()
    expect(await screen.findByText(/获取作业历史失败/, undefined, MOUNT_WAIT)).toBeTruthy()
  })
})

// （评审 B-6 / B-7）：目标设备列设备名解析（与在线设备页 / 目标设备下拉同源）与编号收敛（决议 2）。
describe('作业信息可读性（迭代 84 · #132）', () => {
  it('AC-02：目标设备列经 GET /api/devices 解析设备名；无名称设备回退设备 ID', async () => {
    renderJobHistory()
    expect(await screen.findByText('已完成', undefined, MOUNT_WAIT)).toBeTruthy()
    // 名称解析走 listDevices（与在线设备页 / 目标设备下拉同源）
    await waitFor(() => expect(mocks.server.listDevices).toHaveBeenCalledTimes(1), MOUNT_WAIT)
    expect(screen.getByText('仓库-1 打印电脑')).toBeTruthy()
    // device-3 名称为空 → 回退设备 ID
    expect(screen.getByText('device-3')).toBeTruthy()
    // 设备名只拉一次：作业列表轮询不重复拉设备列表
    fireEvent.click(screen.getByRole('button', { name: /刷新/ }))
    await waitFor(() => expect(mocks.server.getJobs).toHaveBeenCalledTimes(2), MOUNT_WAIT)
    expect(mocks.server.listDevices).toHaveBeenCalledTimes(1)
  })

  it('AC-02：设备名拉取失败不阻断——目标设备列回退设备 ID，无错误横幅', async () => {
    mocks.server.listDevices.mockRejectedValue(new Error('down'))
    renderJobHistory()
    expect(await screen.findByText('已完成', undefined, MOUNT_WAIT)).toBeTruthy()
    await waitFor(() => expect(mocks.server.listDevices).toHaveBeenCalled(), MOUNT_WAIT)
    expect(screen.getByText('device-1')).toBeTruthy()
    expect(screen.getByText('device-2')).toBeTruthy()
    expect(screen.getByText('失败')).toBeTruthy() // 状态徽标「失败」仍在（作业列表正常渲染）
    expect(screen.queryByText(/获取作业历史失败/)).toBeNull()
  })

  it('AC-02：未知设备（已不在设备列表，如离线过期清理）回退设备 ID', async () => {
    mocks.server.getJobs.mockResolvedValue([{ ...JOBS[0], targetDeviceId: 'device-gone' }])
    renderJobHistory()
    expect(await screen.findByText('已完成', undefined, MOUNT_WAIT)).toBeTruthy()
    await waitFor(() => expect(mocks.server.listDevices).toHaveBeenCalled(), MOUNT_WAIT)
    expect(screen.getByText('device-gone')).toBeTruthy()
  })

  it('AC-03：编号列仅「作业编号」可见且可一键复制；「请求编号」悬停可达（title 携带完整编号）', async () => {
    renderJobHistory()
    expect(await screen.findByText('已完成', undefined, MOUNT_WAIT)).toBeTruthy()
    // 表头收敛：仅「作业编号」一列，无「请求编号」列
    expect(screen.getByText('作业编号')).toBeTruthy()
    expect(screen.queryByText('请求编号')).toBeNull()
    // 可见编号 = 作业编号前 8 位（job-aaa-）；请求编号不直出（不在任何文本节点）
    expect(screen.getByText('job-aaa-')).toBeTruthy()
    expect(screen.queryByText('req-aaa-1')).toBeNull()
    // 悬停可达：title 同时携带完整作业编号与请求编号
    const btn = screen.getByRole('button', { name: /job-aaa-/ })
    expect(btn.title).toContain('job-aaa-1')
    expect(btn.title).toContain('请求编号：req-aaa-1')
    // 一键复制：点击复制完整作业编号（非前 8 位截断值）
    fireEvent.click(btn)
    await waitFor(() => expect(mocks.clipboard.copyText).toHaveBeenCalledWith('job-aaa-1'), MOUNT_WAIT)
  })

  it('AC-03：复制失败（如非安全上下文降级失败）提示手动复制，不抛错', async () => {
    mocks.clipboard.copyText.mockResolvedValue(false)
    renderJobHistory()
    expect(await screen.findByText('job-aaa-', undefined, MOUNT_WAIT)).toBeTruthy()
    fireEvent.click(screen.getByRole('button', { name: /job-aaa-/ }))
    await waitFor(() => expect(mocks.clipboard.copyText).toHaveBeenCalledTimes(1), MOUNT_WAIT)
    // 不抛错（状态栏反馈由 App 布局呈现，本测试树只断言不崩溃 + 列表仍在）
    expect(screen.getByText('job-aaa-')).toBeTruthy()
  })
})

// （C-5，决议 2）：作业历史行展开明细。
describe('作业行展开明细（迭代 85 · #133 C-5）', () => {
  /** 本机直连作业（WinHost 列表即返回逐张 items）：完成 2 / 失败 2（errorMessage 与 errorCode 回退两形态）。 */
  const LOCAL_DETAIL_JOB: JobView = {
    jobId: 'loc-eee-5',
    requestId: 'req-eee-5',
    status: 'Failed',
    totalItems: 4,
    completedItems: 2,
    failedItems: 2,
    errorMessage: '第 3 张发送失败',
    createdAt: '2026-08-11T11:00:00Z',
    printImageDir: 'C:\\print\\loc-eee-5',
    printImageCount: 4,
    items: [
      { index: 0, status: 'Completed' },
      { index: 1, status: 'Completed' },
      { index: 2, status: 'Failed', errorMessage: '打印机离线，发送失败' },
      { index: 3, status: 'Failed', errorCode: 'LF_IO_SEND_FAILED' },
    ],
  }

  it('AC-02：本机直连作业展开行——逐张明细（每张状态 / 失败原因：errorMessage 优先、errorCode 回退），Log 出图目录附注', async () => {
    // 单机降级（服务端不可达 → standalone）：列表走 localApi（本机历史，WinHost 返回逐张 items）
    mocks.server.healthz.mockRejectedValue(new Error('down'))
    mocks.local.getJobs.mockResolvedValue([LOCAL_DETAIL_JOB])
    renderJobHistory()
    expect(await screen.findByText('loc-eee-', undefined, MOUNT_WAIT)).toBeTruthy()
    // 展开前明细不可见
    expect(screen.queryByText(/本机直接打印作业——逐张明细/)).toBeNull()

    fireEvent.click(screen.getByTitle('展开明细'))
    expect(await screen.findByText(/本机直接打印作业——逐张明细（共 4 张）/, undefined, MOUNT_WAIT)).toBeTruthy()
    // 逐张状态徽标（两张完成）与序号
    expect(screen.getAllByText('已完成').length).toBe(2)
    // 失败原因完整呈现：errorMessage 优先、errorCode 回退、双失败均无「未知错误」兜底
    expect(screen.getByText('打印机离线，发送失败')).toBeTruthy()
    expect(screen.getByText('LF_IO_SEND_FAILED')).toBeTruthy()
    expect(screen.queryByText('未知错误')).toBeNull()
    // Log 模拟打印出图目录附注（信息展示）
    expect(screen.getByText(/模拟打印生成的图片保存在/)).toBeTruthy()
    // 本机逐张作业不出服务端形态占位说明
    expect(screen.queryByText(/逐张明细仅本机直接打印的作业提供/)).toBeNull()
  })

  it('AC-03：服务端形态作业展开行——状态 / 完成失败 / 失败原因完整呈现＋决议 2 如实占位；再点收起', async () => {
    renderJobHistory() // 默认服务端模式（JOBS 均无 items）
    expect(await screen.findByText('已完成', undefined, MOUNT_WAIT)).toBeTruthy()

    // 展开 job-bbb（Failed，失败原因「打印机缺纸」）
    fireEvent.click(screen.getAllByTitle('展开明细')[1])
    expect(await screen.findByText('逐张明细仅本机直接打印的作业提供，服务端下发作业显示汇总。', undefined, MOUNT_WAIT)).toBeTruthy()
    // 汇总完整呈现：状态徽标 + 完成失败张数 + 失败原因（主行与明细两处）
    expect(screen.getAllByText('打印机缺纸').length).toBe(2)
    expect(screen.getByText(/已完成 1 \/ 2 张/)).toBeTruthy()
    expect(screen.getByText(/（失败 1 张）/)).toBeTruthy()

    // 收起：占位说明与汇总随之消失，列表行仍在
    fireEvent.click(screen.getAllByTitle('收起明细')[0])
    await waitFor(() => expect(screen.queryByText(/逐张明细仅本机直接打印的作业提供/)).toBeNull(), MOUNT_WAIT)
    expect(screen.getAllByText('打印机缺纸').length).toBe(1) // 仅剩主行列
  })

  it('点击行内非按钮区域（状态徽标单元格）同样切换展开；点击作业编号复制不触发展开', async () => {
    renderJobHistory()
    expect(await screen.findByText('已过期', undefined, MOUNT_WAIT)).toBeTruthy()

    // 点行内徽标 → 行 onClick 展开（job-ddd，服务端形态）
    fireEvent.click(screen.getByText('已过期'))
    expect(await screen.findByText(/逐张明细仅本机直接打印的作业提供/, undefined, MOUNT_WAIT)).toBeTruthy()

    // 收起后点「作业编号」复制按钮：stopPropagation，不触发展开
    fireEvent.click(screen.getAllByTitle('收起明细')[0])
    await waitFor(() => expect(screen.queryByText(/逐张明细仅本机直接打印的作业提供/)).toBeNull(), MOUNT_WAIT)
    fireEvent.click(screen.getByRole('button', { name: /job-aaa-/ }))
    await waitFor(() => expect(mocks.clipboard.copyText).toHaveBeenCalledWith('job-aaa-1'), MOUNT_WAIT)
    expect(screen.queryByText(/逐张明细仅本机直接打印的作业提供/)).toBeNull()
  })

  it('无错误信息的服务端作业展开：失败原因显示「无」占位，不误导', async () => {
    mocks.server.getJobs.mockResolvedValue([{ ...JOBS[0] }]) // job-aaa：Completed，无 errorMessage
    renderJobHistory()
    expect(await screen.findByText('已完成', undefined, MOUNT_WAIT)).toBeTruthy()
    fireEvent.click(screen.getByTitle('展开明细'))
    expect(await screen.findByText(/逐张明细仅本机直接打印的作业提供/, undefined, MOUNT_WAIT)).toBeTruthy()
    expect(screen.getByText(/无（该作业未上报错误信息）/)).toBeTruthy()
  })
})

describe('作业历史自动轮询（迭代 48，用户定稿：1.5s / 仅进行中 / 隐藏暂停）', () => {
  /** 冲洗挂载链（配置加载 → healthz 探测 → serverMode 确定 → 首次列表加载）至完全落定。
   *  挂载链本身是纯 promise + React 状态更新，但 React 调度走真实宏任务——固定两轮冲洗在
   *  高负载 runner 上可能残留未落定的渲染代际，迟到渲染落在计时窗口内会重触发加载 effect，
   * 表现为轮询计数错位（实证：「2s 退避后重试」期望 2 实得 3）。改为有界多轮冲洗：
   *  以「首次拉取已发生 + 列表行已渲染」为落定条件，命中后再补两轮空转吸收迟到的真实宏任务渲染。 */
  async function flush() {
    for (let i = 0; i < 15; i++) {
      await act(async () => {
        await vi.advanceTimersByTimeAsync(0)
      })
      if (mocks.server.getJobs.mock.calls.length > 0 && screen.queryByText('打印中')) {
        break
      }
    }
    await act(async () => {
      await vi.advanceTimersByTimeAsync(0)
    })
    await act(async () => {
      await vi.advanceTimersByTimeAsync(0)
    })
  }

  async function advance(ms: number) {
    await act(async () => {
      await vi.advanceTimersByTimeAsync(ms)
    })
  }

  let hiddenFlag = false
  function setHidden(v: boolean) {
    hiddenFlag = v
    Object.defineProperty(document, 'hidden', { configurable: true, get: () => hiddenFlag })
  }

  beforeEach(() => {
    vi.useFakeTimers()
  })

  afterEach(() => {
    delete (document as { hidden?: boolean }).hidden
    vi.useRealTimers()
  })

  it('存在进行中作业时 1.5s 自动轮询；列表全终态后停止', async () => {
    // 初始：含 Printing（进行中）→ 续排轮询
    mocks.server.getJobs.mockResolvedValueOnce(JOBS)
    renderJobHistory()
    await flush()
    expect(mocks.server.getJobs).toHaveBeenCalledTimes(1)
    expect(screen.getByText('打印中')).toBeTruthy()

    // 1.5s 后自动拉取：Printing → Completed（计数 5/5），全终态 → 停止
    mocks.server.getJobs.mockResolvedValueOnce(
      JOBS.map((j) => (j.status === 'Printing' ? { ...j, status: 'Completed', completedItems: 5 } : j)),
    )
    await advance(1500)
    expect(mocks.server.getJobs).toHaveBeenCalledTimes(2)
    expect(screen.queryByText('打印中')).toBeNull()
    expect(screen.getByText(/5\/5/)).toBeTruthy()

    await advance(5000)
    expect(mocks.server.getJobs).toHaveBeenCalledTimes(2)
  })

  it('页面隐藏时轮询暂停；恢复可见立即拉取一次', async () => {
    mocks.server.getJobs.mockResolvedValue(JOBS) // 恒含 Printing
    renderJobHistory()
    await flush()
    expect(mocks.server.getJobs).toHaveBeenCalledTimes(1)

    // 隐藏：到点的轮询跳过且不再续排
    setHidden(true)
    await advance(4500)
    expect(mocks.server.getJobs).toHaveBeenCalledTimes(1)

    // 恢复可见：visibilitychange 立即拉取（不等待间隔）
    setHidden(false)
    await act(async () => {
      document.dispatchEvent(new Event('visibilitychange'))
      await vi.advanceTimersByTimeAsync(0)
    })
    expect(mocks.server.getJobs).toHaveBeenCalledTimes(2)
  })

  it('轮询失败：保留既有列表与错误横幅，2s 退避后重试', async () => {
    mocks.server.getJobs.mockResolvedValueOnce(JOBS)
    renderJobHistory()
    await flush()
    expect(mocks.server.getJobs).toHaveBeenCalledTimes(1)

    // 1.5s 轮询失败：列表不清空、出横幅；因已知存在进行中作业 → 2s 退避重试
    mocks.server.getJobs.mockRejectedValueOnce(new Error('network down'))
    await advance(1500)
    expect(mocks.server.getJobs).toHaveBeenCalledTimes(2)
    expect(screen.getByText(/获取作业历史失败/)).toBeTruthy()
    expect(screen.getByText('已完成')).toBeTruthy()

    // 退避间隔为 2s（1.9s 时未重试，2s 到点重试成功 → 全终态停止）
    mocks.server.getJobs.mockResolvedValueOnce([])
    await advance(1900)
    expect(mocks.server.getJobs).toHaveBeenCalledTimes(2)
    await advance(100)
    expect(mocks.server.getJobs).toHaveBeenCalledTimes(3)
    expect(screen.queryByText(/获取作业历史失败/)).toBeNull()
  })
})
