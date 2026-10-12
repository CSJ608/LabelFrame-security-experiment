// @vitest-environment jsdom
// HTTP 非安全上下文（HTTP + 非 localhost IP 访问 server UI）回归——
// crypto.randomUUID 是浏览器安全上下文专属 API（仅 HTTPS / localhost 暴露），非安全上下文下为 undefined，
// 旧实现在 buildRequest 内同步抛 TypeError：图片预览弹层不打开、打印请求无法构造。
// 现改用 uuid 包 v4（randomUUID 不可用时回退 crypto.getRandomValues，不受安全上下文限制）。
// 本文件显式把 crypto.randomUUID 重定义为 undefined 模拟非安全上下文（jsdom 30 已提供该 API，
// 需显式破坏），断言 job / debug 两分支 buildRequest 正常返回、requestId 为合法唯一 UUID v4、不抛错。

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

// 缺陷报告场景 = server 构建（服务端 Web UI 经 HTTP + 局域网 IP 访问）；job / debug 两分支同源 buildRequest
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
  { deviceId: 'device-1', name: '仓库-1 打印电脑', registeredAt: '2026-08-11T00:00:00Z', lastSeenAt: '2026-08-11T01:00:00Z', status: 'Online', lastIp: '192.168.1.5' },
]

/** UUID v4 形态（版本位 4 + 变体位 89ab；服务端契约仅要求唯一非空白字符串，此处按客户端生成形态从严断言）。 */
const UUID_V4 = /^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/

function Harness() {
  return (
    <AppProvider>
      <DataPrint onOpenJobHistory={() => {}} />
    </AppProvider>
  )
}

// —— 非安全上下文模拟：把 crypto.randomUUID 置为 undefined（等价页面实测 randomUUID=undefined）——
// jsdom 的 randomUUID 可能定义在 crypto 实例或其原型上：保存原自身描述符，afterEach 精确还原，
// 不污染同 worker 后续用例（若定义在原型上，实例级遮蔽 + delete 即恢复原型实现）。
const cryptoObj = globalThis.crypto as Crypto & { randomUUID?: () => string }
const randomUUIDOwnDesc = Object.prototype.hasOwnProperty.call(cryptoObj, 'randomUUID')
  ? Object.getOwnPropertyDescriptor(cryptoObj, 'randomUUID')
  : undefined

let clickSpy: ReturnType<typeof vi.spyOn>

beforeEach(() => {
  vi.clearAllMocks()
  window.localStorage.clear()
  window.sessionStorage.clear()
  clickSpy = vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => {})
  vi.stubGlobal('URL', { createObjectURL: vi.fn(() => 'blob:mock'), revokeObjectURL: vi.fn() })
  // 非安全上下文：randomUUID 不可用（getRandomValues 不受限、保留）
  Object.defineProperty(cryptoObj, 'randomUUID', { value: undefined, configurable: true })
  mocks.server.healthz.mockResolvedValue({ service: 'LabelFrame.Server', status: 'ok' })
  mocks.server.listDevices.mockResolvedValue(DEVICES)
  mocks.server.listTemplates.mockResolvedValue([{ name: '库位标签', group: '默认', updatedAt: '2026-08-10' }])
  mocks.server.getTemplate.mockResolvedValue(PKG)
  mocks.server.submitJob.mockResolvedValue(DONE_JOB_SERVER)
  mocks.server.getJob.mockResolvedValue(DONE_JOB_SERVER)
  mocks.server.renderImage.mockResolvedValue({ blob: new Blob(['png']), filename: 'label-1.png' })
  mocks.server.renderImages.mockResolvedValue({ blob: new Blob(['zip']), filename: 'labels-debug.zip' })
  // localApi 全量 mock（server 构建下不应调用，保险提供）
  mocks.local.getHostConfig.mockResolvedValue({ serverUrl: 'http://127.0.0.1:53961', deviceId: 'device-1', deviceName: '仓库-1 打印电脑' })
  mocks.local.getTransport.mockResolvedValue({ mode: 'Log', params: {} })
})

afterEach(() => {
  vi.unstubAllGlobals()
  clickSpy.mockRestore()
  // 还原 randomUUID（原为自身属性则恢复原描述符；原在原型上则删除实例级遮蔽）
  if (randomUUIDOwnDesc) Object.defineProperty(cryptoObj, 'randomUUID', randomUUIDOwnDesc)
  else delete (cryptoObj as { randomUUID?: () => string }).randomUUID
  cleanup()
})

/** 挂载链等待超时（ms）：对齐 DataPrint.server.test.tsx 口径（CI 高负载下放宽）。 */
const MOUNT_WAIT = { timeout: 8000 }

async function renderDataPrint() {
  render(<Harness />)
  await screen.findByDisplayValue('A-01', undefined, MOUNT_WAIT)
  await waitFor(() => expect(screen.getByLabelText('目标设备')).toBeTruthy(), MOUNT_WAIT)
}

describe('DataPrint：非安全上下文（crypto.randomUUID 不可用）requestId 生成（迭代 117 · #268）', () => {
  it('环境前置：randomUUID 已被置为 undefined（模拟 HTTP 非 localhost）', () => {
    expect(cryptoObj.randomUUID).toBeUndefined()
  })

  it('debug 分支（图片预览）：buildRequest 正常返回，弹层出图，requestId 为合法 UUID v4，不抛 TypeError', async () => {
    await renderDataPrint()
    expect(cryptoObj.randomUUID).toBeUndefined() // 全程保持非安全上下文

    // 旧实现在此同步抛 TypeError: crypto.randomUUID is not a function，弹层不出现
    fireEvent.click(screen.getByRole('button', { name: '图片预览' }))
    const req = await waitFor(() => {
      expect(mocks.server.renderImage).toHaveBeenCalledTimes(1)
      return mocks.server.renderImage.mock.calls[0][0]
    })
    expect(req.requestId).toMatch(UUID_V4)
    expect(req.labels).toEqual([{ data: { location: 'A-01' } }])
    // 弹层正常出图（缺陷现象：弹层不打开、DOM 无 dialog / img）
    expect(await screen.findByAltText('按当前填写数据渲染的标签图片')).toBeTruthy()
    expect(screen.getByRole('dialog', { name: '标签图片预览' })).toBeTruthy()
    expect(mocks.server.submitJob).not.toHaveBeenCalled()
  })

  it('job 分支（打印测试）：请求正常构造提交，requestId 为合法且唯一的 UUID v4，不抛 TypeError', async () => {
    await renderDataPrint()
    expect(cryptoObj.randomUUID).toBeUndefined()

    fireEvent.click(screen.getByRole('button', { name: /打印测试（单张）/ }))
    await waitFor(() => expect(mocks.server.submitJob).toHaveBeenCalledTimes(1))
    const first = mocks.server.submitJob.mock.calls[0][0]
    expect(first).toMatchObject({ templateName: '库位标签', targetDeviceId: 'device-1' })
    expect(first.requestId).toMatch(UUID_V4)

    // 幂等键唯一性：两次提交 requestId 不同
    fireEvent.click(screen.getByRole('button', { name: /打印测试（单张）/ }))
    await waitFor(() => expect(mocks.server.submitJob).toHaveBeenCalledTimes(2))
    const second = mocks.server.submitJob.mock.calls[1][0]
    expect(second.requestId).toMatch(UUID_V4)
    expect(second.requestId).not.toBe(first.requestId)
  })
})
