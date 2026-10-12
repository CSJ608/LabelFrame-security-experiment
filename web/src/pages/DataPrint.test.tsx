// @vitest-environment jsdom
// draft 保留（切 tab / 刷新 / 标签页隔离 / Excel 不保留）+ 调试开关下按钮行为与下载（单张 PNG / zip）
// 双 base（serverApi / localApi 跟随 deviceMode）+ 本机设备默认选中（hostConfig.deviceId 匹配）+ 单机降级守门

import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { act, cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import type { DeviceView, JobView, TemplatePackage } from '../lib/api/types'
import { ApiError } from '../lib/api/types'
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
    excelTemplate: vi.fn(),
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
    excelTemplate: vi.fn(),
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

vi.mock('../lib/api/client', () => ({ serverApi: mocks.server, localApi: mocks.local, setServerBaseUrl: vi.fn() }))
// 本文件为 client 构建语义用例，显式注入 client 分支（VITE_UI_MODE=server 整仓测试时保持稳定）
vi.mock('../lib/uiMode', () => ({ UI_MODE: 'client', isServerUi: false }))

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

const DONE_JOB: JobView = {
  jobId: 'job-1',
  requestId: 'r-1',
  status: 'Completed',
  totalItems: 1,
  completedItems: 1,
  items: [{ index: 0, status: 'Completed' }],
}

/* * Server 作业视图（无逐张 items，只有汇总字段）。 */
const DONE_JOB_SERVER: JobView = {
  jobId: 'job-1',
  requestId: 'r-1',
  status: 'Completed',
  totalItems: 1,
  completedItems: 1,
  targetDeviceId: 'device-1',
  deviceStatus: 'Online',
}

const DEVICES: DeviceView[] = [
  { deviceId: 'device-1', name: '仓库-1 打印电脑', registeredAt: '2026-08-11T00:00:00Z', lastSeenAt: '2026-08-11T01:00:00Z', status: 'Online' },
  { deviceId: 'device-2', name: '仓库-2 打印电脑', registeredAt: '2026-08-11T00:00:00Z', lastSeenAt: '2026-08-10T23:00:00Z', status: 'Offline' },
]

let clickSpy: ReturnType<typeof vi.spyOn>

/** 模拟 DataPrint 挂载在 AppProvider 下的切 tab 行为（provider 不卸载，页面卸载重挂）。
 * onOpenJobHistory 注入（进度区「作业历史」指引可点击跳转的断言入口）。 */
function Harness({ show, onOpenJobHistory }: { show: boolean; onOpenJobHistory?: () => void }) {
  return (
    <AppProvider>
      <div>{show && <DataPrint onOpenJobHistory={onOpenJobHistory ?? (() => {})} />}</div>
    </AppProvider>
  )
}

beforeEach(() => {
  vi.clearAllMocks()
  window.localStorage.clear()
  window.sessionStorage.clear()
  clickSpy = vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => {})
  vi.stubGlobal('URL', { createObjectURL: vi.fn(() => 'blob:mock'), revokeObjectURL: vi.fn() })
  mocks.server.healthz.mockResolvedValue({ service: 'LabelFrame.Server', status: 'ok' })
  mocks.local.getHostConfig.mockResolvedValue({ serverUrl: 'http://127.0.0.1:53961', deviceId: 'device-1', deviceName: '仓库-1 打印电脑' })
  mocks.local.getTransport.mockResolvedValue({ mode: 'Log', params: {} })
  // 默认单机模式（旧 WinHost 无 /api/devices → 404）：隐藏设备选择、提交不带 targetDeviceId；业务 API 走 localApi
  mocks.server.listDevices.mockRejectedValue(new ApiError('HTTP_404', 'Not Found'))
  mocks.local.listTemplates.mockResolvedValue([{ name: '库位标签', group: '默认', updatedAt: '2026-08-10' }])
  mocks.local.getTemplate.mockResolvedValue(PKG)
  mocks.local.submitJob.mockResolvedValue(DONE_JOB)
  mocks.local.getJob.mockResolvedValue(DONE_JOB)
  mocks.local.importExcel.mockResolvedValue({ headers: ['Location'], rows: [['X-01'], ['Y-02']] })
  mocks.local.excelTemplate.mockResolvedValue({ blob: new Blob(['xlsx']), filename: 'excel-template.xlsx' })
  mocks.local.renderImage.mockResolvedValue({ blob: new Blob(['png']), filename: 'label-1.png' })
  mocks.local.renderImages.mockResolvedValue({ blob: new Blob(['zip']), filename: 'labels-debug.zip' })
  // 服务端模式各方法默认就绪（渲染后由用例覆盖 listDevices 的 mock）
  mocks.server.listTemplates.mockResolvedValue([{ name: '库位标签', group: '默认', updatedAt: '2026-08-10' }])
  mocks.server.getTemplate.mockResolvedValue(PKG)
  mocks.server.submitJob.mockResolvedValue(DONE_JOB)
  mocks.server.getJob.mockResolvedValue(DONE_JOB)
  mocks.server.importExcel.mockResolvedValue({ headers: ['Location'], rows: [['X-01'], ['Y-02']] })
  mocks.server.excelTemplate.mockResolvedValue({ blob: new Blob(['xlsx']), filename: 'excel-template.xlsx' })
  mocks.server.renderImage.mockResolvedValue({ blob: new Blob(['png']), filename: 'label-1.png' })
  mocks.server.renderImages.mockResolvedValue({ blob: new Blob(['zip']), filename: 'labels-debug.zip' })
})

afterEach(() => {
  vi.unstubAllGlobals()
  clickSpy.mockRestore()
  cleanup()
})

/** 挂载链等待超时（ms）：DataPrint 挂载要串行走完「设备探测 → 模板列表 → 模板详情 → testData 预填」多段异步链，
 * CI 高负载下可能超过 findBy / waitFor 默认 1000ms（偶发超时，统一放宽）。
 * 3000ms 仍被击穿（「切 tab（页面卸载重挂）保留」找不到），对齐
 *  本文件竞态用例既有口径放宽到 8000ms；单测总超时由 vitest.config testTimeout（20s）兜住。 */
const MOUNT_WAIT = { timeout: 8000 }

async function renderDataPrint() {
  render(<Harness show />)
  // 等模板与 testData 预填值出现
  await screen.findByDisplayValue('A-01', undefined, MOUNT_WAIT)
}

describe('DataPrint 会话保留（迭代 15 §6.1）', () => {
  it('切 tab（页面卸载重挂）：模板、字段值、调试开关保留', async () => {
    const { rerender } = render(<Harness show />)
    await screen.findByDisplayValue('A-01', undefined, MOUNT_WAIT)
    fireEvent.change(screen.getByDisplayValue('A-01'), { target: { value: 'B-02' } })
    fireEvent.click(screen.getByRole('checkbox', { name: /模拟出图/ }))
    await waitFor(() => expect((screen.getByRole('checkbox', { name: /模拟出图/ }) as HTMLInputElement).checked).toBe(true))

    // 切走再切回
    rerender(<Harness show={false} />)
    rerender(<Harness show />)
    await waitFor(() => expect(screen.getByDisplayValue('B-02')).toBeTruthy(), MOUNT_WAIT)
    expect((screen.getByRole('checkbox', { name: /模拟出图/ }) as HTMLInputElement).checked).toBe(true)
    // 模板仍是选中项（第一个下拉 = 模板选择）
    expect((screen.getAllByRole('combobox')[0] as HTMLSelectElement).value).toBe('库位标签')
  })

  it('刷新（sessionStorage 恢复）：字段值与调试开关保留', async () => {
    const { unmount } = render(<Harness show />)
    await screen.findByDisplayValue('A-01', undefined, MOUNT_WAIT)
    fireEvent.change(screen.getByDisplayValue('A-01'), { target: { value: 'C-03' } })
    fireEvent.click(screen.getByRole('checkbox', { name: /模拟出图/ }))
    await waitFor(() => expect(window.sessionStorage.getItem('labelframe.printDraft')).toContain('C-03'))

    unmount()
    // 全新会话（模拟刷新页面）：草稿从 sessionStorage 恢复
    render(<Harness show />)
    await waitFor(() => expect(screen.getByDisplayValue('C-03')).toBeTruthy(), MOUNT_WAIT)
    expect((screen.getByRole('checkbox', { name: /模拟出图/ }) as HTMLInputElement).checked).toBe(true)
  })

  it('草稿只用 sessionStorage，不用 localStorage（D5：避免跨标签页共享）', async () => {
    const { unmount } = render(<Harness show />)
    await screen.findByDisplayValue('A-01', undefined, MOUNT_WAIT)
    fireEvent.change(screen.getByDisplayValue('A-01'), { target: { value: 'D-04' } })
    await waitFor(() => expect(window.sessionStorage.getItem('labelframe.printDraft')).toContain('D-04'))
    // 刷新后从 sessionStorage 恢复，localStorage 无草稿（标签页隔离的存储基础）
    expect(window.localStorage.getItem('labelframe.printDraft')).toBeNull()
    unmount()
  })

  it('Excel 导入数据与列映射不保留：切页后重新上传', async () => {
    const { rerender } = render(<Harness show />)
    // 导入 Excel → 映射弹窗
    fireEvent.change(document.getElementById('excelFile')!, { target: { files: [new File(['x'], 'data.xlsx')] } })
    await screen.findByText('列映射（2 行数据）')
    expect(screen.getByRole('button', { name: '重新映射（data.xlsx）' })).toBeTruthy()

    // 切走再切回：Excel 状态丢弃（无重新映射按钮、无弹窗）
    rerender(<Harness show={false} />)
    rerender(<Harness show />)
    await screen.findByDisplayValue('A-01', undefined, MOUNT_WAIT)
    expect(screen.queryByRole('button', { name: /重新映射/ })).toBeNull()
    expect(screen.queryByText('列映射（2 行数据）')).toBeNull()
    // 模板与字段值仍在
    expect(screen.getByDisplayValue('A-01')).toBeTruthy()
  })
})

describe('调试开关与按钮语义（迭代 15 §6.3）', () => {
  it('模拟出图关：打印测试提交作业，「图片预览」页内弹层呈现渲染图（不下载、不建作业）（迭代 81 AC-01）', async () => {
    await renderDataPrint()
    expect(screen.getByRole('button', { name: /打印测试（单张）/ })).toBeTruthy()
    expect(screen.getByRole('button', { name: '图片预览' })).toBeTruthy()

    // 打印测试 → 正常作业
    fireEvent.click(screen.getByRole('button', { name: /打印测试（单张）/ }))
    await waitFor(() => {
      expect(mocks.local.submitJob).toHaveBeenCalledWith(expect.objectContaining({ labels: [{ data: { location: 'A-01' } }] }))
    })
    // 作业进度出现（jobId 保留链路）
    expect(await screen.findByText('已完成 1 / 1 张')).toBeTruthy()
    expect(mocks.local.renderImage).not.toHaveBeenCalled()

    // 图片预览 → render-image 弹层呈现当前数据渲染图，不建作业、不触发下载
    fireEvent.click(screen.getByRole('button', { name: '图片预览' }))
    await waitFor(() => {
      expect(mocks.local.renderImage).toHaveBeenCalledWith(expect.objectContaining({ labels: [{ data: { location: 'A-01' } }] }))
    })
    const img = await screen.findByAltText('按当前填写数据渲染的标签图片')
    expect(img.getAttribute('src')).toBe('blob:mock')
    expect(screen.getByRole('dialog', { name: '标签图片预览' })).toBeTruthy()
    expect(mocks.local.submitJob).toHaveBeenCalledTimes(1) // 仍只有打印测试那一次
    expect(clickSpy).not.toHaveBeenCalled() // 预览不自动下载（预览与下载分离）
  })

  it('模拟出图开：按钮文案联动、隐藏「图片预览」、打印测试改为 render-image 下载、不提交作业', async () => {
    await renderDataPrint()
    fireEvent.click(screen.getByRole('checkbox', { name: /模拟出图/ }))

    // 文案联动
    expect(screen.getByRole('button', { name: '生成图片（单张）' })).toBeTruthy()
    expect(screen.queryByRole('button', { name: '图片预览' })).toBeNull()
    // 作业进度区提示模拟出图（未实际打印）
    expect(screen.getByText('已生成标签图片并下载（未实际打印）。')).toBeTruthy()

    // 单张出图 → render-image 下载 PNG，不提交作业
    fireEvent.click(screen.getByRole('button', { name: '生成图片（单张）' }))
    await waitFor(() => {
      expect(mocks.local.renderImage).toHaveBeenCalledWith(expect.objectContaining({ labels: [{ data: { location: 'A-01' } }] }))
    })
    expect(mocks.local.submitJob).not.toHaveBeenCalled()
    await waitFor(() => expect(clickSpy.mock.instances[0]?.download).toBe('label-1.png'))
  })

  it('调试开 + 批量：下载 zip（全部行），不提交作业', async () => {
    await renderDataPrint()
    fireEvent.click(screen.getByRole('checkbox', { name: /模拟出图/ }))
    fireEvent.change(document.getElementById('excelFile')!, { target: { files: [new File(['x'], 'data.xlsx')] } })
    await screen.findByText('列映射（2 行数据）')

    fireEvent.click(screen.getByRole('button', { name: '下载图片（2 张）' }))
    await waitFor(() => {
      expect(mocks.local.renderImages).toHaveBeenCalledWith(
        expect.objectContaining({ labels: [{ data: { location: 'X-01' } }, { data: { location: 'Y-02' } }] }),
      )
    })
    expect(mocks.local.submitJob).not.toHaveBeenCalled()
    await waitFor(() => expect(clickSpy.mock.instances[0]?.download).toBe('labels-debug.zip'))
  })

  it('调试关 + 批量：批量打印提交作业', async () => {
    await renderDataPrint()
    fireEvent.change(document.getElementById('excelFile')!, { target: { files: [new File(['x'], 'data.xlsx')] } })
    await screen.findByText('列映射（2 行数据）')

    fireEvent.click(screen.getByRole('button', { name: '批量打印 2 张' }))
    await waitFor(() => {
      expect(mocks.local.submitJob).toHaveBeenCalledWith(
        expect.objectContaining({ labels: [{ data: { location: 'X-01' } }, { data: { location: 'Y-02' } }] }),
      )
    })
    expect(mocks.local.renderImages).not.toHaveBeenCalled()
  })
})

// 「图片预览」页内弹层呈现当前字段值的渲染图
// 弹层内显式「下载」按钮，预览与下载分离；模拟出图（生成图片 / 批量 zip）链路零回归。
describe('图片预览页内弹层与下载分离（迭代 81 · #129）', () => {
  /** URL 全局桩上的 revokeObjectURL（beforeEach 内 stubGlobal 重建，用例内现取引用）。 */
  const revokeStub = () => (URL as unknown as { revokeObjectURL: ReturnType<typeof vi.fn> }).revokeObjectURL

  it('AC-02：弹层内显式「下载」按钮——点击才下载（携带服务端文件名），预览阶段零下载', async () => {
    await renderDataPrint()
    fireEvent.click(screen.getByRole('button', { name: '图片预览' }))
    await screen.findByAltText('按当前填写数据渲染的标签图片')
    expect(clickSpy).not.toHaveBeenCalled() // 预览本身不触发下载

    fireEvent.click(screen.getByRole('button', { name: /下载图片/ }))
    await waitFor(() => expect(clickSpy.mock.instances[0]?.download).toBe('label-1.png'))
    // 弹层保持打开（下载不等于关闭预览），不重复请求
    expect(screen.getByAltText('按当前填写数据渲染的标签图片')).toBeTruthy()
    expect(mocks.local.renderImage).toHaveBeenCalledTimes(1)
  })

  it('AC-01：弹层呈现当前字段值——修改字段后再次预览按新值渲染', async () => {
    await renderDataPrint()
    fireEvent.change(screen.getByDisplayValue('A-01'), { target: { value: 'B-09' } })
    fireEvent.click(screen.getByRole('button', { name: '图片预览' }))
    await waitFor(() => {
      expect(mocks.local.renderImage).toHaveBeenCalledWith(expect.objectContaining({ labels: [{ data: { location: 'B-09' } }] }))
    })
    expect(await screen.findByAltText('按当前填写数据渲染的标签图片')).toBeTruthy()
  })

  it('关闭交互与工作台灯箱一致：× / 点背景 / Esc 均关闭，点卡片本体不关闭；关闭释放 blob URL', async () => {
    await renderDataPrint()
    fireEvent.click(screen.getByRole('button', { name: '图片预览' }))
    await screen.findByAltText('按当前填写数据渲染的标签图片')

    // 点卡片本体（图片）不关闭
    fireEvent.click(screen.getByAltText('按当前填写数据渲染的标签图片'))
    expect(screen.getByAltText('按当前填写数据渲染的标签图片')).toBeTruthy()

    // × 关闭
    fireEvent.click(screen.getByTitle('关闭预览（Esc）'))
    expect(screen.queryByAltText('按当前填写数据渲染的标签图片')).toBeNull()
    expect(revokeStub()).toHaveBeenCalledWith('blob:mock')

    // 点背景关闭
    fireEvent.click(screen.getByRole('button', { name: '图片预览' }))
    await screen.findByAltText('按当前填写数据渲染的标签图片')
    fireEvent.click(document.querySelector('.preview-modal') as HTMLElement)
    expect(screen.queryByAltText('按当前填写数据渲染的标签图片')).toBeNull()

    // Esc 关闭
    fireEvent.click(screen.getByRole('button', { name: '图片预览' }))
    await screen.findByAltText('按当前填写数据渲染的标签图片')
    fireEvent.keyDown(window, { key: 'Escape' })
    expect(screen.queryByAltText('按当前填写数据渲染的标签图片')).toBeNull()
  })

  it('加载与失败态：在途显示「正在生成预览…」；出图失败弹层内提示原因且可关闭重试', async () => {
    let resolveImg!: (v: { blob: Blob; filename: string }) => void
    mocks.local.renderImage.mockImplementation(() => new Promise((res) => (resolveImg = res)))
    await renderDataPrint()
    fireEvent.click(screen.getByRole('button', { name: '图片预览' }))
    expect(await screen.findByText('正在生成预览…')).toBeTruthy()

    // 在途关闭：结果回来不重开「幽灵弹层」
    fireEvent.keyDown(window, { key: 'Escape' })
    expect(screen.queryByText('正在生成预览…')).toBeNull()
    resolveImg({ blob: new Blob(['png']), filename: 'label-1.png' })
    await act(async () => {
      await Promise.resolve()
    })
    expect(screen.queryByAltText('按当前填写数据渲染的标签图片')).toBeNull()

    // 失败态：弹层内显示原因（不落下载、不崩溃），再次预览可恢复
    mocks.local.renderImage.mockRejectedValueOnce(new ApiError('RENDER_FAILED', '出图失败（字段缺失）。'))
    fireEvent.click(screen.getByRole('button', { name: '图片预览' }))
    expect(await screen.findByText('预览不可用')).toBeTruthy()
    expect(screen.getByText('出图失败（字段缺失）。')).toBeTruthy()
    expect(clickSpy).not.toHaveBeenCalled()
    fireEvent.click(screen.getByTitle('关闭预览（Esc）'))
    expect(screen.queryByText('预览不可用')).toBeNull()

    // 恢复：Once 拒绝耗尽后恢复正常 resolve（覆盖首个可控 pending 实现）
    mocks.local.renderImage.mockResolvedValue({ blob: new Blob(['png']), filename: 'label-1.png' })
    fireEvent.click(screen.getByRole('button', { name: '图片预览' }))
    expect(await screen.findByAltText('按当前填写数据渲染的标签图片')).toBeTruthy()
  })

  it('AC-03：模拟出图链路不回归——勾选后「生成图片（单张）」仍直接下载、隐藏「图片预览」', async () => {
    await renderDataPrint()
    fireEvent.click(screen.getByRole('checkbox', { name: /模拟出图/ }))
    expect(screen.getByRole('button', { name: '生成图片（单张）' })).toBeTruthy()
    expect(screen.queryByRole('button', { name: '图片预览' })).toBeNull()
    expect(screen.queryByRole('dialog', { name: '标签图片预览' })).toBeNull()

    fireEvent.click(screen.getByRole('button', { name: '生成图片（单张）' }))
    await waitFor(() => expect(mocks.local.renderImage).toHaveBeenCalledTimes(1))
    await waitFor(() => expect(clickSpy.mock.instances[0]?.download).toBe('label-1.png'))
    // 模拟出图走直接下载，不弹预览层
    expect(screen.queryByRole('dialog', { name: '标签图片预览' })).toBeNull()
  })
})

describe('目标设备固定本机（迭代 22 决策 1A）', () => {
  /** 服务端模式：listDevices 成功 + 本机 hostConfig（deviceId / deviceName 可覆盖）。 */
  async function renderServerMode(devices: DeviceView[], host: { deviceId?: string; deviceName?: string } = { deviceId: 'device-1', deviceName: '仓库-1 打印电脑' }) {
    mocks.server.listDevices.mockResolvedValue(devices)
    mocks.local.getHostConfig.mockResolvedValue({ serverUrl: 'http://127.0.0.1:53961', ...host })
    render(<Harness show />)
    await screen.findByDisplayValue('A-01', undefined, MOUNT_WAIT)
    // 等本机目标标签出现（listDevices 异步 resolve）
    await waitFor(() => expect(screen.getByText(/^本机（/)).toBeTruthy(), MOUNT_WAIT)
  }

  it('本机已注册且在线：只显示「本机（{deviceName}）」标签，无设备选择器', async () => {
    await renderServerMode(DEVICES)
    expect(screen.getByText('本机（仓库-1 打印电脑）')).toBeTruthy()
    expect(screen.getByText(/本机已加入服务端，打印记录也会同步到服务端/)).toBeTruthy()
    // 客户端构建不再有设备选择器
    expect(screen.queryByLabelText('目标设备')).toBeNull()
  })

  it('本机在线提交：templateName + targetDeviceId=本机 deviceId 走 serverApi；localApi 不提交（双 base 守门）', async () => {
    await renderServerMode(DEVICES)
    fireEvent.click(screen.getByRole('button', { name: /打印测试（单张）/ }))
    await waitFor(() => {
      expect(mocks.server.submitJob).toHaveBeenCalledTimes(1)
    })
    const req = mocks.server.submitJob.mock.calls[0][0]
    expect(req).toMatchObject({ templateName: '库位标签', targetDeviceId: 'device-1', labels: [{ data: { location: 'A-01' } }] })
    expect(req.template).toBeUndefined()
    expect(mocks.local.submitJob).not.toHaveBeenCalled()
  })

  it('本机设备离线：降级本机直连并提示原因；提交自包含 template 走 localApi', async () => {
    await renderServerMode(DEVICES, { deviceId: 'device-2', deviceName: '仓库-2 打印电脑' })
    expect(screen.getByText('本机（仓库-2 打印电脑）')).toBeTruthy()
    expect(screen.getByText(/本机当前离线：暂用本机直接打印/)).toBeTruthy()

    fireEvent.click(screen.getByRole('button', { name: /打印测试（单张）/ }))
    await waitFor(() => {
      expect(mocks.local.submitJob).toHaveBeenCalledTimes(1)
    })
    const req = mocks.local.submitJob.mock.calls[0][0]
    expect(req.templateName).toBeUndefined()
    expect(req.targetDeviceId).toBeUndefined()
    expect(req.template).toMatchObject({ name: '库位标签', contract: PKG.contract, layout: PKG.layout })
    expect(mocks.server.submitJob).not.toHaveBeenCalled()
  })

  it('本机未注册（deviceId 不在服务端列表）：降级本机直连并提示未注册', async () => {
    await renderServerMode(DEVICES, { deviceId: 'pc-x', deviceName: '未注册电脑' })
    expect(screen.getByText(/本机未加入服务端：暂用本机直接打印/)).toBeTruthy()
    // 提交走本机直连（localApi）
    fireEvent.click(screen.getByRole('button', { name: /打印测试（单张）/ }))
    await waitFor(() => expect(mocks.local.submitJob).toHaveBeenCalledTimes(1))
    expect(mocks.server.submitJob).not.toHaveBeenCalled()
  })

  it('旧客户端无 deviceId：降级本机直连并提示未注册', async () => {
    await renderServerMode(DEVICES, { deviceId: undefined, deviceName: undefined })
    expect(screen.getByText('本机（未知）')).toBeTruthy()
    expect(screen.getByText(/本机未加入服务端：暂用本机直接打印/)).toBeTruthy()
  })

  it('服务端模式无设备（空列表）：本机未注册降级直连，打印测试仍可用', async () => {
    await renderServerMode([])
    expect(screen.getByText(/本机未加入服务端：暂用本机直接打印/)).toBeTruthy()
    expect((screen.getByRole('button', { name: /打印测试（单张）/ }) as HTMLButtonElement).disabled).toBe(false)
  })

  it('Server 作业视图（无 items）：进度与目标设备可见，不渲染逐张表格', async () => {
    mocks.server.listDevices.mockResolvedValue(DEVICES)
    mocks.server.getJob.mockResolvedValue(DONE_JOB_SERVER)
    render(<Harness show />)
    await screen.findByDisplayValue('A-01', undefined, MOUNT_WAIT)
    await waitFor(() => expect(screen.getByText(/^本机（/)).toBeTruthy(), MOUNT_WAIT)

    fireEvent.click(screen.getByRole('button', { name: /打印测试（单张）/ }))
    expect(await screen.findByText('已完成 1 / 1 张')).toBeTruthy()
    // 目标设备显示设备名（与目标设备下拉同源），不再直出设备 ID
    expect(screen.getByText(/目标设备：仓库-1 打印电脑（在线）/)).toBeTruthy()
    // 无逐张表格，显示说明行
    expect(screen.getByText(/该作业无逐张明细/)).toBeTruthy()
  })

  it('单机降级（/api/devices 404）：无目标设备 UI，模板列表走 localApi，提交自包含 template 走 localApi（双 base 守门）', async () => {
    await renderDataPrint()
    expect(screen.queryByText(/^本机（/)).toBeNull()
    expect(screen.queryByLabelText('目标设备')).toBeNull()
    // 模板列表来自本机（standalone → localApi）
    expect(mocks.local.listTemplates).toHaveBeenCalled()
    expect(mocks.server.listTemplates).not.toHaveBeenCalled()

    fireEvent.click(screen.getByRole('button', { name: /打印测试（单张）/ }))
    await waitFor(() => {
      expect(mocks.local.submitJob).toHaveBeenCalledTimes(1)
    })
    const req = mocks.local.submitJob.mock.calls[0][0]
    expect(req.templateName).toBeUndefined()
    expect(req.targetDeviceId).toBeUndefined()
    expect(req.template).toMatchObject({ name: '库位标签', contract: PKG.contract, layout: PKG.layout })
    expect(mocks.server.submitJob).not.toHaveBeenCalled()
  })
})

// 副标题用户化 + 作业进度目标设备显示设备名（client 构建）。
describe('打印页称谓与作业进度设备名（迭代 84 · #132）', () => {
  it('AC-01：副标题「填写数据并打印 / Excel 批量打印」，无「测试」字样', async () => {
    await renderDataPrint()
    const subtitle = screen.getByText('填写数据并打印 / Excel 批量打印')
    expect(subtitle.closest('.page-title')?.textContent).toContain('数据与打印')
    // 副标题不含「测试」自称
    expect(subtitle.textContent).not.toContain('测试')
    expect(screen.queryByText('测试数据 / Excel 批量打印 / 打印测试')).toBeNull()
  })

  it('AC-02 回退：作业目标设备无可解析名称（不在设备列表）→ 显示设备 ID', async () => {
    mocks.server.listDevices.mockResolvedValue(DEVICES)
    mocks.server.getJob.mockResolvedValue({ ...DONE_JOB_SERVER, targetDeviceId: 'device-x' })
    render(<Harness show />)
    await screen.findByDisplayValue('A-01', undefined, MOUNT_WAIT)
    await waitFor(() => expect(screen.getByText(/^本机（/)).toBeTruthy(), MOUNT_WAIT)

    fireEvent.click(screen.getByRole('button', { name: /打印测试（单张）/ }))
    expect(await screen.findByText('已完成 1 / 1 张')).toBeTruthy()
    expect(screen.getByText(/目标设备：device-x（在线）/)).toBeTruthy()
  })
})

describe('下载 Excel 模板（迭代 22 §2.1）', () => {
  it('点击「下载 Excel 模板」：按契约字段 + testData 生成请求并下载 xlsx', async () => {
    mocks.local.excelTemplate.mockResolvedValue({ blob: new Blob(['xlsx']), filename: '库位标签.xlsx' })
    await renderDataPrint()
    fireEvent.click(screen.getByRole('button', { name: /下载 Excel 模板/ }))
    await waitFor(() => {
      expect(mocks.local.excelTemplate).toHaveBeenCalledWith(
        [{ key: 'location', displayName: '库位' }],
        { location: 'A-01' },
      )
    })
    await waitFor(() => expect(clickSpy.mock.instances[0]?.download).toBe('库位标签.xlsx'))
  })

  it('模板无契约字段（静态标签）：显示说明性提示、操作区可用，「下载 Excel 模板」仍禁用', async () => {
    mocks.local.getTemplate.mockResolvedValue({
      ...PKG,
      contract: { ...PKG.contract, fields: [] },
      layout: { ...PKG.layout, elements: [] },
    })
    render(<Harness show />)
    // 无字段模板改为说明性提示（不再是「请先绑定字段」），操作区照常渲染
    // testData 残留键不影响——无输入框渲染，Excel 模板无列可生成维持禁用
    expect(await screen.findByText(/该模板为静态标签（无字段填充）/)).toBeTruthy()
    expect(screen.queryByDisplayValue('A-01')).toBeNull()
    expect((screen.getByRole('button', { name: /打印测试（单张）/ }) as HTMLButtonElement).disabled).toBe(false)
    expect((screen.getByRole('button', { name: /下载 Excel 模板/ }) as HTMLButtonElement).disabled).toBe(true)
  })
})

describe('字段显示名渲染（迭代 83 · #131 决议 1：displayName || key 回退）', () => {
  it('契约字段带显示名：表单标签与占位符显示显示名，值仍按字段名（键）提交（AC-01）', async () => {
    await renderDataPrint()
    // 标签 = displayName（库位），不再直出键名 location
    expect(screen.getByText('库位')).toBeTruthy()
    expect(screen.queryByText('location')).toBeNull()
    // 占位符同步显示名
    expect(screen.getByPlaceholderText('字段 库位 的值（打印时使用）')).toBeTruthy()
    // 值按字段名（键）绑定：testData 预填 location= 正常出现在输入框
    expect(screen.getByDisplayValue('A-01')).toBeTruthy()
    fireEvent.change(screen.getByDisplayValue('A-01'), { target: { value: 'B-02' } })
    fireEvent.click(screen.getByRole('button', { name: /打印测试（单张）/ }))
    await waitFor(() =>
      expect(mocks.local.submitJob).toHaveBeenCalledWith(expect.objectContaining({ labels: [{ data: { location: 'B-02' } }] })),
    )
  })

  it('存量模板（旧口径 displayName = 键 / 显示名为空）：回退显示键名，不报错（AC-01）', async () => {
    mocks.local.getTemplate.mockResolvedValue({
      ...PKG,
      contract: {
        ...PKG.contract,
        fields: [
          { key: 'location', displayName: 'location', isRequired: true, type: 'Text' }, // 旧口径：displayName 取键
          { key: 'sku', displayName: '', isRequired: false, type: 'Text' }, // 显示名为空
        ],
      },
      testData: { location: 'A-01' },
    })
    await renderDataPrint()
    expect(screen.getByText('location')).toBeTruthy()
    expect(screen.getByText('sku')).toBeTruthy()
    expect(screen.getByPlaceholderText('字段 location 的值（打印时使用）')).toBeTruthy()
  })
})

describe('连接状态徽标（迭代 80「三名义」③：已加入 / 未加入服务端）', () => {
  /** 服务端模式挂载（与「目标设备固定本机」describe 同构，deviceId / deviceName 可覆盖）。 */
  async function renderServerModeLocal(devices: DeviceView[], host: { deviceId?: string; deviceName?: string } = { deviceId: 'device-1', deviceName: '仓库-1 打印电脑' }) {
    mocks.server.listDevices.mockResolvedValue(devices)
    mocks.local.getHostConfig.mockResolvedValue({ serverUrl: 'http://127.0.0.1:53961', ...host })
    render(<Harness show />)
    await screen.findByDisplayValue('A-01', undefined, MOUNT_WAIT)
    await waitFor(() => expect(screen.getByText(/^本机（/)).toBeTruthy(), MOUNT_WAIT)
  }

  it('单机模式（/api/devices 404）：徽标显示「未加入」', async () => {
    await renderDataPrint()
    expect(screen.getByText('本机连接')).toBeTruthy()
    expect(screen.getByText('服务端')).toBeTruthy()
    // 本机连接徽标：来自 localApi.getTransport（Log 模式 → 用户语摘要「模拟打印」）
    expect(screen.getByText('模拟打印')).toBeTruthy()
    // 服务端 = 设备是否已加入服务端设备列表（hostInList），不再是 healthz 连通性
    expect(screen.getByText('未加入')).toBeTruthy()
    expect(screen.queryByText('已连接')).toBeNull()
    expect(screen.queryByText('未连接（单机模式可用）')).toBeNull()
  })

  it('插件模式 Log 连接（displayText = 插件 Describe「模拟打印」）：本机连接徽标用户语，无「LOG」直出（迭代 82，#130 AC-01）', async () => {
    mocks.local.getTransport.mockResolvedValue({
      pluginId: 'log',
      displayText: '模拟打印',
      params: {},
      availablePlugins: [{ id: 'log', displayName: '模拟打印', parameters: [] }],
      mode: 'Log',
    })
    await renderDataPrint()
    expect(screen.getByText('本机连接')).toBeTruthy()
    expect(screen.getByText('模拟打印')).toBeTruthy()
    expect(screen.queryByText(/LOG/)).toBeNull()
  })

  it('本机已注册（deviceId 在服务端列表）：徽标显示「已加入」', async () => {
    await renderServerModeLocal(DEVICES)
    expect(screen.getByText('已加入')).toBeTruthy()
  })

  it('本机未注册（deviceId 不在列表）：徽标显示「未加入」', async () => {
    await renderServerModeLocal(DEVICES, { deviceId: 'pc-x', deviceName: '未注册电脑' })
    expect(screen.getByText('未加入')).toBeTruthy()
  })

  it('设备加入状态随探测周期刷新（#128 C-3）：后台注册后徽标自动变「已加入」，无需切页', async () => {
    // 初始：服务端设备列表不含本机 → 未加入；10s 探测周期后本机注册上线 → 已加入（无卸载重挂）
    vi.useFakeTimers()
    try {
      mocks.server.listDevices.mockResolvedValue([DEVICES[1]])
      render(<Harness show />)
      // 挂载链落定（配置 → 探测 → 模板列表 → 详情 → testData 预填 + 首次设备探测）：
      // fake timers 下 findBy 轮询不可用（interval 已被 fake），用有界多轮冲洗、以渲染条件为落定准出
      for (let i = 0; i < 20 && !(screen.queryByDisplayValue('A-01') && screen.queryByText('未加入')); i++) {
        await act(async () => {
          await vi.advanceTimersByTimeAsync(0)
        })
      }
      expect(screen.getByDisplayValue('A-01')).toBeTruthy()
      expect(screen.getByText('未加入')).toBeTruthy()

      mocks.server.listDevices.mockResolvedValue(DEVICES)
      await act(async () => {
        await vi.advanceTimersByTimeAsync(10_000)
      })
      // 探测周期定时器已触发；其 promise → 徽标渲染链再补两轮空转落定后断言（消除 同类时序敏感）
      await act(async () => {
        await vi.advanceTimersByTimeAsync(0)
      })
      await act(async () => {
        await vi.advanceTimersByTimeAsync(0)
      })
      expect(screen.getByText('已加入')).toBeTruthy()
    } finally {
      vi.useRealTimers()
    }
  })

  it('原生指令模式连接：打印操作区出现「无预览，效果以真机为准」提示（迭代 78，#120 AC-04）', async () => {
    mocks.local.getTransport.mockResolvedValue({
      pluginId: 'labelframe-transport-zebra',
      displayName: 'Zebra',
      displayText: 'Zebra TCP 127.0.0.1:9100',
      params: { kind: 'Tcp', host: '127.0.0.1', printMode: 'native' },
      availablePlugins: [],
      mode: 'Log',
    })
    await renderDataPrint()
    const hint = await screen.findByTestId('native-print-mode-hint', undefined, MOUNT_WAIT)
    expect(hint.textContent).toContain('原生指令模式无预览，效果以真机为准')
  })

  it('图片（默认）连接：不出现原生指令提示', async () => {
    mocks.local.getTransport.mockResolvedValue({
      pluginId: 'labelframe-transport-zebra',
      displayName: 'Zebra',
      displayText: 'Zebra TCP 127.0.0.1:9100',
      params: { kind: 'Tcp', host: '127.0.0.1', printMode: 'image' },
      availablePlugins: [],
      mode: 'Log',
    })
    await renderDataPrint()
    await screen.findByText('Zebra TCP 127.0.0.1:9100')
    expect(screen.queryByTestId('native-print-mode-hint')).toBeNull()
  })
})

// 无字段模板 = 静态标签——合法模板，可在数据与打印页打印测试（空数据提交）
describe('无字段模板（静态标签）打印测试（迭代 65 · #62）', () => {
  /** 无字段模板：contract.fields 空 + 版式无 field 元素 → fieldKeys 为空。 */
  const STATIC_PKG: TemplatePackage = {
    name: '固定警示标签',
    group: '默认',
    contract: { name: 'contract-static', version: '1', fields: [] },
    layout: { name: 'layout-static', contractName: 'contract-static', contractVersion: '1', widthMm: 70, heightMm: 50, elements: [] },
    testData: {},
  }

  /** 渲染无字段模板并等待操作区就绪（无字段输入框可等，以打印测试按钮为锚点）。 */
  async function renderStaticPrint() {
    mocks.local.getTemplate.mockResolvedValue(STATIC_PKG)
    render(<Harness show />)
    await screen.findByRole('button', { name: /打印测试（单张）/ }, MOUNT_WAIT)
  }

  it('操作区照常渲染且可用：静态标签说明提示 + 模拟出图复选框 + 图片预览；Excel 模板仍禁用（AC-01）', async () => {
    await renderStaticPrint()
    expect(screen.getByText(/该模板为静态标签（无字段填充）/)).toBeTruthy()
    expect(screen.getByText('静态标签无需填写数据；打印测试提交 1 张空数据标签（内容按版式原样输出）。')).toBeTruthy()
    expect(screen.getByRole('checkbox', { name: /模拟出图/ })).toBeTruthy()
    expect((screen.getByRole('button', { name: /打印测试（单张）/ }) as HTMLButtonElement).disabled).toBe(false)
    expect((screen.getByRole('button', { name: '图片预览' }) as HTMLButtonElement).disabled).toBe(false)
    // 无字段无列可生成：Excel 模板维持禁用，tooltip 不变
    const excelBtn = screen.getByRole('button', { name: /下载 Excel 模板/ }) as HTMLButtonElement
    expect(excelBtn.disabled).toBe(true)
    expect(excelBtn.title).toBe('当前模板没有字段，无法生成 Excel 模板')
  })

  it('打印测试（调试关）：提交单张空数据作业（labels: [{ data: {} }]），进度面板正常显示（AC-02）', async () => {
    await renderStaticPrint()
    fireEvent.click(screen.getByRole('button', { name: /打印测试（单张）/ }))
    await waitFor(() => {
      expect(mocks.local.submitJob).toHaveBeenCalledWith(expect.objectContaining({ labels: [{ data: {} }] }))
    })
    expect(await screen.findByText('已完成 1 / 1 张')).toBeTruthy()
    expect(mocks.local.renderImage).not.toHaveBeenCalled()
  })

  it('模拟出图关：图片预览空数据弹层渲染（不建作业、不下载）；模拟出图开：打印测试改为生成图片（空数据）（AC-03）', async () => {
    await renderStaticPrint()
    // 调试关：图片预览 → render-image 空数据弹层呈现，不建作业、不自动下载
    fireEvent.click(screen.getByRole('button', { name: '图片预览' }))
    await waitFor(() => {
      expect(mocks.local.renderImage).toHaveBeenCalledWith(expect.objectContaining({ labels: [{ data: {} }] }))
    })
    expect(mocks.local.submitJob).not.toHaveBeenCalled()
    expect(await screen.findByAltText('按当前填写数据渲染的标签图片')).toBeTruthy()
    expect(clickSpy).not.toHaveBeenCalled()

    // 关闭弹层后勾选模拟出图：按钮文案联动为调试出图（单张），仍空数据、不建作业
    fireEvent.keyDown(window, { key: 'Escape' })
    expect(screen.queryByAltText('按当前填写数据渲染的标签图片')).toBeNull()
    fireEvent.click(screen.getByRole('checkbox', { name: /模拟出图/ }))
    expect(screen.getByRole('button', { name: '生成图片（单张）' })).toBeTruthy()
    expect(screen.queryByRole('button', { name: '图片预览' })).toBeNull()
    fireEvent.click(screen.getByRole('button', { name: '生成图片（单张）' }))
    await waitFor(() => expect(mocks.local.renderImage).toHaveBeenCalledTimes(2))
    await waitFor(() => expect(clickSpy.mock.instances[0]?.download).toBe('label-1.png'))
    expect(mocks.local.submitJob).not.toHaveBeenCalled()
  })
})

// 模板详情加载竞态——cancelled 守卫与同文件预览弹层 previewGenRef 同一标准。
// 场景：首个模板详情慢响应在途，快速切到第二个模板（立即返回）；慢响应随后到达不得覆盖新选择。
describe('模板详情加载竞态守卫（迭代 91 F-12）', () => {  it('AC-03：快速切换模板（首个慢响应）：字段表单与选中模板始终一致，无错位', async () => {
    const PKG_B: TemplatePackage = {
      name: '模板B',
      group: '默认',
      contract: {
        name: 'contract-b',
        version: '1',
        fields: [{ key: 'zone', displayName: '库区B', isRequired: true, type: 'Text' }],
      },
      layout: { name: 'layout-b', contractName: 'contract-b', contractVersion: '1', widthMm: 70, heightMm: 50, elements: [] },
      testData: { zone: 'B-99' },
    }
    let resolveSlow!: (p: TemplatePackage) => void
    mocks.local.listTemplates.mockResolvedValue([
      { name: '模板A', group: '默认', updatedAt: '2026-09-10T00:00:00Z' },
      { name: '模板B', group: '默认', updatedAt: '2026-09-11T00:00:00Z' },
    ])
    mocks.local.getTemplate.mockImplementation((name: string) =>
      name === '模板A' ? new Promise((res) => (resolveSlow = res)) : Promise.resolve(PKG_B),
    )
    render(<Harness show />)
    // 列表加载后默认选中第一项「模板 A」，其详情挂起在途（全量并行负载下放宽等待，见 MOUNT_WAIT 注释）
    await waitFor(() => expect((screen.getAllByRole('combobox')[0] as HTMLSelectElement).value).toBe('模板A'), { timeout: 8000 })
    // 选中值渲染与详情加载 effect 之间存在调度间隙——断言收敛等待而非立即判定
    // （CI 高负载下两度复现的时序间隙——须收敛等待而非立即判定）
    await waitFor(() => expect(mocks.local.getTemplate).toHaveBeenCalledWith('模板A'), { timeout: 8000 })

    // 快速切到「模板 B」：B 立即返回并渲染（字段值）
    fireEvent.change(screen.getAllByRole('combobox')[0], { target: { value: '模板B' } })
    await screen.findByDisplayValue('B-99', undefined, { timeout: 8000 })

    // 「模板 A」的慢响应此刻才返回：被 cancelled 守卫丢弃，不覆盖 B 的字段表单 / 打印数据
    //（宏任务冲刷：确保慢响应的 .then 链执行完毕后再断言）
    resolveSlow({ ...PKG, name: '模板A' })
    await act(async () => {
      await new Promise((r) => setTimeout(r, 0))
    })
    expect(screen.getByDisplayValue('B-99')).toBeTruthy()
    expect(screen.queryByDisplayValue('A-01')).toBeNull()
    // 字段标签仍是 B 的（无 A 的字段标签回流）
    expect(screen.getByText('库区B')).toBeTruthy()
  })
})

// 作业进度区指向「作业历史」的纯文字指引改为可点击跳转（onOpenJobHistory → 切作业历史页
// 行可展开逐张 / 汇总明细）——无逐张明细说明与失败汇总提示两处文案均带跳转入口。
describe('进度区「作业历史」指引可点击跳转（迭代 85 · #133 C-5）', () => {
  it('无逐张明细（服务端形态 / Server 作业）：底部说明中「作业历史」可点击——onOpenJobHistory 回调触发一次', async () => {
    const onOpenJobHistory = vi.fn()
    // Server 作业视图：无 items（本用例默认单机模式下以 localApi 提交返回，形状等价——无 items 即无逐张明细）
    mocks.local.submitJob.mockResolvedValue(DONE_JOB_SERVER)
    mocks.local.getJob.mockResolvedValue(DONE_JOB_SERVER)
    render(<Harness show onOpenJobHistory={onOpenJobHistory} />)
    await screen.findByDisplayValue('A-01', undefined, MOUNT_WAIT)

    fireEvent.click(screen.getByRole('button', { name: /打印测试（单张）/ }))
    expect(await screen.findByText(/该作业无逐张明细/)).toBeTruthy()
    fireEvent.click(screen.getByRole('button', { name: '作业历史' }))
    expect(onOpenJobHistory).toHaveBeenCalledTimes(1)
  })

  it('失败汇总提示（有 N 张打印失败、无逐张重试表格）：指引「作业历史」可点击跳转', async () => {
    const onOpenJobHistory = vi.fn()
    const failedNoItems: JobView = {
      jobId: 'job-f-1',
      requestId: 'r-f-1',
      status: 'Failed',
      totalItems: 2,
      completedItems: 1,
      failedItems: 1,
      errorMessage: '打印机缺纸',
    }
    mocks.local.submitJob.mockResolvedValue(failedNoItems)
    mocks.local.getJob.mockResolvedValue(failedNoItems)
    render(<Harness show onOpenJobHistory={onOpenJobHistory} />)
    await screen.findByDisplayValue('A-01', undefined, MOUNT_WAIT)

    fireEvent.click(screen.getByRole('button', { name: /打印测试（单张）/ }))
    expect(await screen.findByText(/有 1 张打印失败/)).toBeTruthy()
    // 无 items + 本地本可重试 → 失败提示走指引分支（两处「作业历史」入口均在）
    const links = screen.getAllByRole('button', { name: '作业历史' })
    expect(links.length).toBe(2)
    fireEvent.click(links[0])
    expect(onOpenJobHistory).toHaveBeenCalledTimes(1)
  })

  it('本机直连作业（有逐张 items、可重试）：失败提示保持「下方列表逐张重试」，不渲染跳转入口', async () => {
    const onOpenJobHistory = vi.fn()
    const failedWithItems: JobView = {
      jobId: 'job-f-2',
      requestId: 'r-f-2',
      status: 'Failed',
      totalItems: 2,
      completedItems: 1,
      items: [
        { index: 0, status: 'Completed' },
        { index: 1, status: 'Failed', errorMessage: '打印机缺纸' },
      ],
    }
    mocks.local.submitJob.mockResolvedValue(failedWithItems)
    mocks.local.getJob.mockResolvedValue(failedWithItems)
    render(<Harness show onOpenJobHistory={onOpenJobHistory} />)
    await screen.findByDisplayValue('A-01', undefined, MOUNT_WAIT)

    fireEvent.click(screen.getByRole('button', { name: /打印测试（单张）/ }))
    expect(await screen.findByText(/可在下方列表中逐张重试/)).toBeTruthy()
    expect(screen.queryByRole('button', { name: '作业历史' })).toBeNull()
    expect(onOpenJobHistory).not.toHaveBeenCalled()
  })
})
