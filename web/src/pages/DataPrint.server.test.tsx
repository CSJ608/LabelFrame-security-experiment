// @vitest-environment jsdom
// DataPrint server 构建（VITE_UI_MODE=server）——在线设备选择器（必选、仅在线可选、离线置灰显示
// 上次心跳）；默认目标优先级 = 用户点选（localStorage labelframe.defaultTargetDeviceId，须在线）> 第一台在线；
// 提交时现拉 GET /api/devices 校验在线（掉线提示并禁止提交、作业不排队，不复用缓存列表）；
// 隐藏打印机连接徽标与逐张失败重试表格（server 构建无逐张 retry 端点）；业务 API 恒 serverApi（无 standalone 分支）。
// 图片预览页内弹层与下载分离双形态走查（与 client 构建行为一致）。

import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import type { DeviceView, JobView, TemplatePackage } from '../lib/api/types'
import { AppProvider } from '../state/AppContext'
import { DataPrint } from './DataPrint'

const mocks = vi.hoisted(() => ({
  server: {
    healthz: vi.fn(),
    listDevices: vi.fn(),
    listTemplates: vi.fn(),
    getTemplate: vi.fn(),
    submitJob: vi.fn(),
    getJob: vi.fn(),
    retryJobItem: vi.fn(),
    importExcel: vi.fn(),
    renderImage: vi.fn(),
    renderImages: vi.fn(),
  },
  local: {
    healthz: vi.fn(),
    listDevices: vi.fn(),
    listTemplates: vi.fn(),
    getTemplate: vi.fn(),
    submitJob: vi.fn(),
    getJob: vi.fn(),
    retryJobItem: vi.fn(),
    importExcel: vi.fn(),
    renderImage: vi.fn(),
    renderImages: vi.fn(),
    getHostConfig: vi.fn(),
    getTransport: vi.fn(),
    setHostConfig: vi.fn(),
    setTransport: vi.fn(),
    testTransport: vi.fn(),
    getPrinterStatus: vi.fn(),
    testPrinter: vi.fn(),
  },
}))

vi.mock('../lib/api/client', () => ({
  serverApi: mocks.server,
  localApi: mocks.local,
  setServerBaseUrl: vi.fn(),
  probeHealthz: vi.fn(),
}))

vi.mock('../lib/uiMode', () => ({ UI_MODE: 'server', isServerUi: true }))

const PKG: TemplatePackage = {
  name: '库位标签',
  group: '默认',
  contract: {
    name: 'contract-1',
    version: '1',
    fields: [{ key: 'location', displayName: '库位', isRequired: true, type: 'Text' }],
  },
  layout: { name: 'layout-1', contractName: 'contract-1', contractVersion: '1', widthMm: 70, heightMm: 50, elements: [] },
  testData: { location: 'A-01' },
}

/** Server 作业视图（无逐张 items 的汇总形状）。 */
const DONE_JOB_SERVER: JobView = {
  jobId: 'job-1',
  requestId: 'r-1',
  status: 'Completed',
  totalItems: 2,
  completedItems: 1,
  failedItems: 1,
  targetDeviceId: 'device-1',
  deviceStatus: 'Online',
}

/** 含逐张 items 的作业视图（client 形状；server 构建下即使返回也不渲染重试表格——强制隐藏）。 */
const DONE_JOB_WITH_ITEMS: JobView = {
  ...DONE_JOB_SERVER,
  items: [
    { index: 0, status: 'Completed' },
    { index: 1, status: 'Failed', errorMessage: '打印失败' },
  ],
}

const DEVICES: DeviceView[] = [
  { deviceId: 'device-1', name: '仓库-1 打印电脑', registeredAt: '2026-08-11T00:00:00Z', lastSeenAt: '2026-08-11T01:00:00Z', status: 'Online', lastIp: '192.168.1.5' },
  { deviceId: 'device-2', name: '仓库-2 打印电脑', registeredAt: '2026-08-11T00:00:00Z', lastSeenAt: '2026-08-10T23:00:00Z', status: 'Offline', lastIp: '192.168.1.6' },
]

/* * 无字段模板（静态标签）：contract.fields 空 + 版式无 field 元素 → fieldKeys 为空。 */
const STATIC_PKG: TemplatePackage = {
  name: '固定警示标签',
  group: '默认',
  contract: { name: 'contract-static', version: '1', fields: [] },
  layout: { name: 'layout-static', contractName: 'contract-static', contractVersion: '1', widthMm: 70, heightMm: 50, elements: [] },
  testData: {},
}

const DEFAULT_TARGET_KEY = 'labelframe.defaultTargetDeviceId'

function Harness() {
  return (
    <AppProvider>
      <DataPrint onOpenJobHistory={() => {}} />
    </AppProvider>
  )
}

let clickSpy: ReturnType<typeof vi.spyOn>

beforeEach(() => {
  vi.clearAllMocks()
  window.localStorage.clear()
  window.sessionStorage.clear()
  clickSpy = vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => {})
  vi.stubGlobal('URL', { createObjectURL: vi.fn(() => 'blob:mock'), revokeObjectURL: vi.fn() })
  mocks.server.healthz.mockResolvedValue({ service: 'LabelFrame.Server', status: 'ok' })
  mocks.server.listDevices.mockResolvedValue(DEVICES)
  mocks.server.listTemplates.mockResolvedValue([{ name: '库位标签', group: '默认', updatedAt: '2026-08-10' }])
  mocks.server.getTemplate.mockResolvedValue(PKG)
  mocks.server.submitJob.mockResolvedValue(DONE_JOB_SERVER)
  mocks.server.getJob.mockResolvedValue(DONE_JOB_SERVER)
  mocks.server.importExcel.mockResolvedValue({ headers: ['Location'], rows: [['X-01']] })
  mocks.server.renderImage.mockResolvedValue({ blob: new Blob(['png']), filename: 'label-1.png' })
  mocks.server.renderImages.mockResolvedValue({ blob: new Blob(['zip']), filename: 'labels-debug.zip' })
  // localApi 全量 mock（AppProvider 启动链在 server 构建下不应调用，保险提供）
  mocks.local.getHostConfig.mockResolvedValue({ serverUrl: 'http://127.0.0.1:53961', deviceId: 'device-1', deviceName: '仓库-1 打印电脑' })
  mocks.local.getTransport.mockResolvedValue({ mode: 'Log', params: {} })
})

afterEach(() => {
  vi.unstubAllGlobals()
  clickSpy.mockRestore()
  cleanup()
})

/** 挂载链等待超时（ms）：DataPrint 挂载要串行走完「设备列表 → 模板列表 → 模板详情 → testData 预填」多段异步链，
 * CI 高负载下可能超过 findBy / waitFor 默认 1000ms（偶发超时），统一放宽。 */
// 3000ms 在 CI 高负载下被击穿（见 DataPrint.test.tsx 注释），对齐放宽到 8000ms
const MOUNT_WAIT = { timeout: 8000 }

async function renderDataPrint() {
  render(<Harness />)
  await screen.findByDisplayValue('A-01', undefined, MOUNT_WAIT)
  await waitFor(() => expect(screen.getByLabelText('目标设备')).toBeTruthy(), MOUNT_WAIT)
}

describe('DataPrint server 构建：在线设备选择器', () => {
  it('仅在线设备可选：离线设备 option 置灰并显示上次连接时间', async () => {
    await renderDataPrint()
    const select = screen.getByLabelText('目标设备') as HTMLSelectElement
    expect(select.value).toBe('device-1') // 第一台在线默认选中
    const offlineOpt = screen.getByRole('option', { name: /仓库-2 打印电脑/ }) as HTMLOptionElement
    expect(offlineOpt.disabled).toBe(true)
    expect(offlineOpt.textContent).toContain('上次连接')
    const onlineOpt = screen.getByRole('option', { name: /仓库-1 打印电脑/ }) as HTMLOptionElement
    expect(onlineOpt.disabled).toBe(false)
    // 提示文案：仅在线可选 + 提交前再校验
    expect(screen.getByText('仅在线设备可选；提交时将再次校验所选设备在线状态。')).toBeTruthy()
  })

  it('默认目标优先级：用户点选（localStorage，在线）> 第一台在线', async () => {
    // device-2 在线、device-1 离线：用户点选 device-2 → 选中 device-2（而非第一台 device-1）
    mocks.server.listDevices.mockResolvedValue([
      { ...DEVICES[1], status: 'Online' },
      { ...DEVICES[0], status: 'Offline' },
    ])
    window.localStorage.setItem(DEFAULT_TARGET_KEY, 'device-2')
    await renderDataPrint()
    expect((screen.getByLabelText('目标设备') as HTMLSelectElement).value).toBe('device-2')
  })

  it('用户点选的设备离线：回退第一台在线', async () => {
    window.localStorage.setItem(DEFAULT_TARGET_KEY, 'device-2') // 离线
    await renderDataPrint()
    expect((screen.getByLabelText('目标设备') as HTMLSelectElement).value).toBe('device-1')
  })

  it('不显示「本机连接」打印机徽标（server 构建无打印机相关内容）', async () => {
    await renderDataPrint()
    expect(screen.queryByText('本机连接')).toBeNull()
    expect(screen.queryByText('模拟打印')).toBeNull()
  })
})

describe('DataPrint server 构建：提交前现拉校验（K3）', () => {
  it('提交时现拉设备列表：所选设备在线 → 正常提交（listDevices 第 2 次调用 = 提交时现拉）', async () => {
    await renderDataPrint()
    expect(mocks.server.listDevices).toHaveBeenCalledTimes(1) // 仅进入页面拉取一次

    fireEvent.click(screen.getByRole('button', { name: /打印测试（单张）/ }))
    await waitFor(() => expect(mocks.server.submitJob).toHaveBeenCalledTimes(1))
    expect(mocks.server.listDevices).toHaveBeenCalledTimes(2)
    const req = mocks.server.submitJob.mock.calls[0][0]
    expect(req).toMatchObject({ templateName: '库位标签', targetDeviceId: 'device-1' })
    expect(req.template).toBeUndefined()
    // 双 base 守门：server 构建恒 serverApi，localApi 不提交
    expect(mocks.local.submitJob).not.toHaveBeenCalled()
  })

  it('所选设备提交时已离线：禁止提交、作业不排队（不复用进入页面时的缓存列表）', async () => {
    await renderDataPrint()
    // 进入页面时 device-1 在线（缓存），提交时现拉已离线
    mocks.server.listDevices.mockResolvedValue([
      { ...DEVICES[0], status: 'Offline' },
      { ...DEVICES[1] },
    ])
    fireEvent.click(screen.getByRole('button', { name: /打印测试（单张）/ }))
    expect(await screen.findByText('所选设备已离线或不存在，无法提交（作业不会排队）。请重新选择在线设备。')).toBeTruthy()
    expect(mocks.server.submitJob).not.toHaveBeenCalled()
    // 选择器数据随现拉结果刷新（device-1 置灰）
    const opt = screen.getByRole('option', { name: /仓库-1 打印电脑/ }) as HTMLOptionElement
    expect(opt.disabled).toBe(true)
  })

  it('现拉校验失败（网络错误）：提示并禁止提交', async () => {
    await renderDataPrint()
    mocks.server.listDevices.mockRejectedValue(new Error('network down'))
    fireEvent.click(screen.getByRole('button', { name: /打印测试（单张）/ }))
    expect(await screen.findByText(/校验设备在线状态失败/)).toBeTruthy()
    expect(mocks.server.submitJob).not.toHaveBeenCalled()
  })
})

describe('DataPrint server 构建：隐藏逐张失败重试表格（G4）', () => {
  it('作业返回 items 也不渲染逐张表格与重试按钮，失败提示不带「下方表格重试」', async () => {
    mocks.server.getJob.mockResolvedValue(DONE_JOB_WITH_ITEMS)
    await renderDataPrint()
    fireEvent.click(screen.getByRole('button', { name: /打印测试（单张）/ }))
    expect(await screen.findByText('已完成 1 / 2 张')).toBeTruthy()
    expect(screen.getByText(/有 1 张打印失败/)).toBeTruthy()
    // server 构建强制隐藏逐张表格 / 重试按钮
    expect(screen.queryByRole('button', { name: /重试/ })).toBeNull()
    expect(screen.queryByText(/可在下方列表中逐张重试/)).toBeNull()
    // 失败原因提示走汇总文案（指引改为可点击「作业历史」跳转入口——文本经按钮分段）
    expect(screen.getByText(/可在「」中查看失败原因/)).toBeTruthy()
    expect(screen.getByRole('button', { name: '作业历史' })).toBeTruthy()
  })
})

// 无字段模板 = 静态标签——server 构建下同样可打印测试（空数据提交到所选在线设备）
describe('DataPrint server 构建：无字段模板（静态标签）打印测试（迭代 65 · #62）', () => {
  /** 渲染无字段模板并等待操作区与目标设备就绪（无字段输入框可等，以打印按钮 + 设备选择器为锚点）。 */
  async function renderStaticPrint() {
    mocks.server.getTemplate.mockResolvedValue(STATIC_PKG)
    render(<Harness />)
    await screen.findByRole('button', { name: /打印测试（单张）/ }, MOUNT_WAIT)
    await waitFor(() => expect(screen.getByLabelText('目标设备')).toBeTruthy(), MOUNT_WAIT)
  }

  it('操作区照常渲染且可用：打印测试提交单张空数据（labels: [{ data: {} }]）到所选在线设备（AC-02）', async () => {
    await renderStaticPrint()
    expect(screen.getByText(/该模板为静态标签（无字段填充）/)).toBeTruthy()
    // 无字段无列可生成：Excel 模板维持禁用（tooltip 不变）
    const excelBtn = screen.getByRole('button', { name: /下载 Excel 模板/ }) as HTMLButtonElement
    expect(excelBtn.disabled).toBe(true)
    expect(excelBtn.title).toBe('当前模板没有字段，无法生成 Excel 模板')

    fireEvent.click(screen.getByRole('button', { name: /打印测试（单张）/ }))
    await waitFor(() => expect(mocks.server.submitJob).toHaveBeenCalledTimes(1))
    const req = mocks.server.submitJob.mock.calls[0][0]
    expect(req).toMatchObject({ templateName: '固定警示标签', targetDeviceId: 'device-1', labels: [{ data: {} }] })
    // 双 base 守门：server 构建恒 serverApi，localApi 不提交
    expect(mocks.local.submitJob).not.toHaveBeenCalled()
  })

  it('图片预览：空数据 render-image 弹层渲染（不建作业、不下载）（AC-03）', async () => {
    await renderStaticPrint()
    fireEvent.click(screen.getByRole('button', { name: '图片预览' }))
    await waitFor(() => {
      expect(mocks.server.renderImage).toHaveBeenCalledWith(expect.objectContaining({ labels: [{ data: {} }] }))
    })
    expect(mocks.server.submitJob).not.toHaveBeenCalled()
    expect(await screen.findByAltText('按当前填写数据渲染的标签图片')).toBeTruthy()
    expect(clickSpy).not.toHaveBeenCalled()
  })
})

// 图片预览页内弹层与下载分离——server 构建（服务端管理界面）
// 与 client 构建行为一致：点击「图片预览」弹层呈现当前字段值渲染图，弹层内显式「下载」按钮；
// 模拟出图（生成图片 / 批量 zip）文案与直接下载行为零回归。
describe('DataPrint server 构建：图片预览页内弹层与下载分离（迭代 81 · #129）', () => {
  it('AC-01 / AC-02：点击「图片预览」页内弹层呈现当前字段值渲染图；弹层内「下载」按钮显式下载（不混用）', async () => {
    await renderDataPrint()
    fireEvent.click(screen.getByRole('button', { name: '图片预览' }))
    await waitFor(() => {
      expect(mocks.server.renderImage).toHaveBeenCalledWith(expect.objectContaining({ labels: [{ data: { location: 'A-01' } }] }))
    })
    const img = await screen.findByAltText('按当前填写数据渲染的标签图片')
    expect(img.getAttribute('src')).toBe('blob:mock')
    expect(screen.getByRole('dialog', { name: '标签图片预览' })).toBeTruthy()
    expect(mocks.server.submitJob).not.toHaveBeenCalled()
    expect(clickSpy).not.toHaveBeenCalled() // 预览不自动下载

    // 弹层内显式「下载」按钮
    fireEvent.click(screen.getByRole('button', { name: /下载图片/ }))
    await waitFor(() => expect(clickSpy.mock.instances[0]?.download).toBe('label-1.png'))
    expect(mocks.server.renderImage).toHaveBeenCalledTimes(1) // 下载复用弹层 blob，不重复请求

    // Esc 关闭
    fireEvent.keyDown(window, { key: 'Escape' })
    expect(screen.queryByAltText('按当前填写数据渲染的标签图片')).toBeNull()
  })

  it('AC-03：模拟出图不回归——勾选后按钮改「生成图片（单张）」、隐藏「图片预览」，点击仍直接下载（不弹层）', async () => {
    await renderDataPrint()
    fireEvent.click(screen.getByRole('checkbox', { name: /模拟出图/ }))
    expect(screen.getByRole('button', { name: '生成图片（单张）' })).toBeTruthy()
    expect(screen.queryByRole('button', { name: '图片预览' })).toBeNull()

    fireEvent.click(screen.getByRole('button', { name: '生成图片（单张）' }))
    await waitFor(() => expect(mocks.server.renderImage).toHaveBeenCalledTimes(1))
    await waitFor(() => expect(clickSpy.mock.instances[0]?.download).toBe('label-1.png'))
    expect(mocks.server.submitJob).not.toHaveBeenCalled()
    expect(screen.queryByRole('dialog', { name: '标签图片预览' })).toBeNull()
  })
})

describe('DataPrint server 构建：字段显示名渲染（迭代 83 · #131 决议 1，与 client 构建同源）', () => {
  it('契约字段带显示名：表单标签与占位符显示显示名，值仍按字段名（键）绑定（AC-01 双形态）', async () => {
    await renderDataPrint()
    expect(screen.getByText('库位')).toBeTruthy()
    expect(screen.queryByText('location')).toBeNull()
    expect(screen.getByPlaceholderText('字段 库位 的值（打印时使用）')).toBeTruthy()
    expect(screen.getByDisplayValue('A-01')).toBeTruthy()
  })
})

// 副标题用户化 + 作业进度目标设备显示设备名（server 构建 / 服务端管理界面）。
describe('DataPrint server 构建：打印页称谓与作业进度设备名（迭代 84 · #132）', () => {
  it('AC-01：副标题「填写数据并打印 / Excel 批量打印」，无「测试」字样（双形态）', async () => {
    await renderDataPrint()
    const subtitle = screen.getByText('填写数据并打印 / Excel 批量打印')
    expect(subtitle.closest('.page-title')?.textContent).toContain('数据与打印')
    expect(subtitle.textContent).not.toContain('测试')
    expect(screen.queryByText('测试数据 / Excel 批量打印 / 打印测试')).toBeNull()
  })

  it('AC-02：作业进度目标设备显示设备名（与在线设备页 / 目标设备下拉同源），未知设备回退 ID', async () => {
    await renderDataPrint()
    fireEvent.click(screen.getByRole('button', { name: /打印测试（单张）/ }))
    expect(await screen.findByText('已完成 1 / 2 张')).toBeTruthy()
    // device-1 在设备列表（DEVICES）→ 显示设备名「仓库-1 打印电脑」，不直出设备 ID
    expect(screen.getByText(/目标设备：仓库-1 打印电脑（在线）/)).toBeTruthy()

    // 未知设备（不在列表，如设备已注销清理）→ 回退设备 ID（换 jobId 触发新一轮作业轮询）
    mocks.server.submitJob.mockResolvedValueOnce({ ...DONE_JOB_SERVER, jobId: 'job-2', targetDeviceId: 'device-x' })
    mocks.server.getJob.mockResolvedValue({ ...DONE_JOB_SERVER, jobId: 'job-2', targetDeviceId: 'device-x' })
    fireEvent.click(screen.getByRole('button', { name: /打印测试（单张）/ }))
    expect(await screen.findByText(/目标设备：device-x（在线）/)).toBeTruthy()
  })
})
