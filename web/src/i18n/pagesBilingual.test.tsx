// @vitest-environment jsdom
// 双语冒烟：第一批迁移页面（工作台 / 作业历史 / 数据与打印）中英两语言渲染——
// zh-CN（vitest.setup 钉住首启语言）断言中文文案；changeLocale('en') 切换后断言英文文案且页面文本
// 不再含 CJK（模板名 / 分组等业务数据用 ASCII，避免数据噪声）；再切回中文恢复（惯例：
// en 行为用 changeLocale 显式切换）。另覆盖壳层残余：document.title 与状态栏初始消息随语言。

import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { cleanup, configure, render, screen, waitFor, within } from '@testing-library/react'
import type { JobView, TemplatePackage, TemplateSummary } from '../lib/api/types'
import { AppProvider, useApp } from '../state/AppContext'
import { Workbench } from '../pages/Workbench'
import { JobHistory } from '../pages/JobHistory'
import { DataPrint } from '../pages/DataPrint'
import i18next, { changeLocale } from './index'

configure({ asyncUtilTimeout: 8000 })

const mocks = vi.hoisted(() => ({
  server: {
    healthz: vi.fn(),
    listTemplates: vi.fn(),
    getTemplate: vi.fn(),
    deleteTemplate: vi.fn(),
    exportTemplate: vi.fn(),
    importTemplate: vi.fn(),
    previewTemplate: vi.fn(),
    getJobs: vi.fn(),
    listDevices: vi.fn(),
  },
  local: {
    healthz: vi.fn(),
    listTemplates: vi.fn(),
    getTemplate: vi.fn(),
    getJobs: vi.fn(),
    listDevices: vi.fn(),
    getHostConfig: vi.fn(),
    getTransport: vi.fn(),
    previewTemplate: vi.fn(),
  },
}))

vi.mock('../lib/api/client', () => ({ serverApi: mocks.server, localApi: mocks.local, setServerBaseUrl: vi.fn() }))
vi.mock('../lib/uiMode', () => ({ UI_MODE: 'client', isServerUi: false }))

// 业务数据一律 ASCII（no-CJK 断言只针对界面文案，不把数据噪声算进去）
const TEMPLATES: TemplateSummary[] = [{ name: 'Bin-Label', group: 'Default', updatedAt: '2026-09-01T10:00:00Z' }]
const PKG: TemplatePackage = {
  name: 'Bin-Label',
  group: 'Default',
  contract: {
    name: 'contract-1',
    version: '1',
    fields: [{ key: 'location', displayName: 'location', isRequired: true, type: 'Text' }],
  },
  layout: { name: 'layout-1', contractName: 'contract-1', contractVersion: '1', widthMm: 70, heightMm: 50, elements: [] },
  testData: { location: 'A-01' },
}
const JOB: JobView = {
  jobId: 'job-0001',
  requestId: 'req-0001',
  status: 'Completed',
  totalItems: 2,
  completedItems: 2,
  targetDeviceId: 'device-1',
}

const CJK = /[\u3400-\u4DBF\u4E00-\u9FFF\uF900-\uFAFF]/

beforeEach(() => {
  vi.clearAllMocks()
  window.localStorage.clear()
  window.sessionStorage.clear()
  mocks.server.healthz.mockResolvedValue({ service: 'LabelFrame.Server', status: 'ok' })
  mocks.local.healthz.mockResolvedValue({ service: 'LabelFrame.WinHost', status: 'ok' })
  mocks.local.getHostConfig.mockResolvedValue({ serverUrl: 'http://127.0.0.1:53961', deviceId: 'device-1', deviceName: 'PC-1' })
  mocks.local.getTransport.mockResolvedValue({ mode: 'Log', params: {} })
  mocks.server.listTemplates.mockResolvedValue(TEMPLATES)
  mocks.local.listTemplates.mockResolvedValue(TEMPLATES)
  mocks.server.getTemplate.mockResolvedValue(PKG)
  mocks.local.getTemplate.mockResolvedValue(PKG)
  mocks.server.getJobs.mockResolvedValue([JOB])
  mocks.local.getJobs.mockResolvedValue([JOB])
  mocks.server.listDevices.mockResolvedValue([
    { deviceId: 'device-1', name: 'PC-1', registeredAt: '2026-08-11T00:00:00Z', lastSeenAt: '2026-08-11T01:00:00Z', status: 'Online' },
  ])
  mocks.local.listDevices.mockRejectedValue(new Error('standalone'))
  mocks.server.previewTemplate.mockResolvedValue({ blob: new Blob(['png']) })
  mocks.local.previewTemplate.mockResolvedValue({ blob: new Blob(['png']) })
})

afterEach(() => {
  cleanup()
  window.localStorage.clear()
  if (i18next.language !== 'zh-CN') changeLocale('zh-CN')
})

describe('第一批迁移页面双语渲染冒烟（迭代 111 · #245）', () => {
  it('工作台：中文态文案 → 切 en 全英文且无 CJK → 切回中文恢复', async () => {
    const { container } = render(
      <AppProvider>
        <Workbench onOpenDesigner={() => {}} onOpenPrint={() => {}} />
      </AppProvider>,
    )
    expect(await screen.findByText('Bin-Label')).toBeTruthy()
    expect(screen.getByText('工作台')).toBeTruthy()
    expect(screen.getByRole('button', { name: /新建模板/ })).toBeTruthy()
    expect(screen.getByRole('button', { name: /导入模板/ })).toBeTruthy()

    changeLocale('en')
    await waitFor(() => expect(screen.getByText('Workbench')).toBeTruthy())
    expect(screen.getByRole('button', { name: /New Template/ })).toBeTruthy()
    expect(screen.getByRole('button', { name: /Import Template/ })).toBeTruthy()
    expect(within(screen.getByText('Bin-Label').closest('.wb-card')!).getByRole('button', { name: /Edit/ })).toBeTruthy()
    expect(CJK.test(container.textContent ?? '')).toBe(false)

    changeLocale('zh-CN')
    await waitFor(() => expect(screen.getByText('工作台')).toBeTruthy())
  })

  it('作业历史：中文态文案与状态标签 → 切 en 全英文且无 CJK', async () => {
    const { container } = render(
      <AppProvider>
        <JobHistory />
      </AppProvider>,
    )
    expect(await screen.findByText('job-0001'.slice(0, 8))).toBeTruthy()
    expect(screen.getByText('作业历史')).toBeTruthy()
    expect(screen.getByText('已完成')).toBeTruthy()
    expect(screen.getByRole('columnheader', { name: '目标设备' })).toBeTruthy()

    changeLocale('en')
    await waitFor(() => expect(screen.getByText('Job History')).toBeTruthy())
    expect(screen.getByText('Completed')).toBeTruthy()
    expect(screen.getByRole('columnheader', { name: 'Target device' })).toBeTruthy()
    expect(CJK.test(container.textContent ?? '')).toBe(false)
  })

  it('数据与打印：中文态表单与连接徽标 → 切 en 全英文且无 CJK（含传输标签与标题；状态栏初值中文回归）', async () => {
    /** 状态栏初始消息探针（AppContext 惰性初值——壳层残余「就绪」key 化的中文回归断言）。 */
    function StatusProbe() {
      const app = useApp()
      return <div data-testid="status-msg">{app.statusMsg}</div>
    }
    const { container } = render(
      <AppProvider>
        <StatusProbe />
        <DataPrint onOpenJobHistory={() => {}} />
      </AppProvider>,
    )
    expect(await screen.findByText('数据与打印')).toBeTruthy()
    expect(screen.getByText('测试数据')).toBeTruthy()
    expect(await screen.findByRole('button', { name: /打印测试（单张）/ })).toBeTruthy()
    expect(screen.getByText('本机连接')).toBeTruthy()
    expect(screen.getByText('模拟打印')).toBeTruthy() // formatTransport 跟随语言（zh 值与原硬编码一致）
    // 状态栏消息（AppContext 壳层残余 key 化）：启动链完成后的「已读取本机配置」中文回归
    await waitFor(() => expect(screen.getByTestId('status-msg').textContent).toContain('已读取本机配置'))

    changeLocale('en')
    await waitFor(() => expect(screen.getByText('Data & Print')).toBeTruthy())
    expect(screen.getByText('Test Data')).toBeTruthy()
    expect(await screen.findByRole('button', { name: /Test Print \(1 label\)/ })).toBeTruthy()
    expect(screen.getByText('Local connection')).toBeTruthy()
    expect(screen.getByText('Simulated printing')).toBeTruthy()
    expect(document.title).toBe('LabelFrame Label Printing') // 壳层残余：标题随语言
    // no-CJK 只查页面本体（.page）：探针显示的状态栏旧消息在语言切换后不回译（既定语义），不计入
    expect(CJK.test(container.querySelector('.page')!.textContent ?? '')).toBe(false)

    changeLocale('zh-CN')
    await waitFor(() => expect(screen.getByText('数据与打印')).toBeTruthy())
    expect(document.title).toBe('LabelFrame 标签打印')
  })
})
