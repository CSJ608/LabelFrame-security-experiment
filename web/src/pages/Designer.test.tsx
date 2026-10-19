// @vitest-environment jsdom
// Designer 加载 effect 与 Workbench 的 serverMode 守卫对齐——
// unknown 阶段（模式探测未完成）不以 localApi 误发模板详情请求；模式解析后按正确 base 正常加载。
// 画布为 react-konva 重组件（jsdom 无 2d context），以桩替换——本文件只验证加载链路守卫，不测画布交互。
// 页级保存链路（新建保存 / 同名覆盖确认 / 保存失败）与
// 未保存离开保护三选路径（dirty = 历史栈有已提交更改）。

import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { act, cleanup, configure, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { ApiError } from '../lib/api/types'
import type { TemplatePackage } from '../lib/api/types'
import { AppProvider, useApp } from '../state/AppContext'

// 挂载链（AppContext 启动链 → 模板加载）为多段 promise + React 真实宏任务调度
// CI 高负载 runner 上偶发超过 findBy / waitFor 默认 1000ms；统一放宽到 8000ms（与 JobHistory /
// DataPrint / Settings 同口径），单测总超时由 vitest.config testTimeout（20s）兜住。
configure({ asyncUtilTimeout: 8000 })
import { Designer, sanitizeImageKey } from './Designer'

const mocks = vi.hoisted(() => ({
  server: {
    healthz: vi.fn(),
    getTemplate: vi.fn(),
    listTemplates: vi.fn(),
    saveTemplate: vi.fn(),
  },
  local: {
    healthz: vi.fn(),
    getTemplate: vi.fn(),
    listTemplates: vi.fn(),
    saveTemplate: vi.fn(),
    getHostConfig: vi.fn(),
    getTransport: vi.fn(),
  },
}))

vi.mock('../lib/api/client', () => ({
  serverApi: mocks.server,
  localApi: mocks.local,
  setServerBaseUrl: vi.fn(),
  probeHealthz: vi.fn(async () => false),
}))
// 本文件为 client 构建语义用例，显式注入 client 分支（VITE_UI_MODE=server 整仓测试时保持稳定）
vi.mock('../lib/uiMode', () => ({ UI_MODE: 'client', isServerUi: false }))
// 画布桩：本文件只验证加载链路（serverMode 守卫），konva 画布由专门交互测试与真机验收覆盖。
// 两步式链路需经画布动作驱动——桩以探针按钮转接两类回调：canvas-place＝画布点击
// 放置 pendingType（onAddElement）；canvas-upload-empty＝画布空占位单击（onRequestImageUpload，
// 的画布侧入口）。真实 Konva 命中 / 点击归 ElementNode 结构级断言与复陪验。
vi.mock('./designer/CanvasViewport', () => ({
  CanvasViewport: (props: {
    state: { elements: Array<{ id: string; type: string; key?: string }> }
    pendingType: string | null
    onAddElement: (type: string, xMm: number, yMm: number) => void
    onRequestImageUpload?: (id: string) => void
  }) => (
    <div data-testid="canvas-stub">
      <button data-testid="canvas-place" onClick={() => props.onAddElement(props.pendingType ?? 'Text', 10, 10)}>
        place
      </button>
      <button
        data-testid="canvas-upload-empty"
        onClick={() => {
          const empty = props.state.elements.find((e) => e.type === 'Image' && !e.key)
          if (empty && props.onRequestImageUpload) props.onRequestImageUpload(empty.id)
        }}
      >
        upload-empty
      </button>
    </div>
  ),
}))

const PKG: TemplatePackage = {
  name: '库位标签',
  group: '默认',
  contract: { name: 'contract-1', version: '1', fields: [] },
  layout: { name: 'layout-1', contractName: 'contract-1', contractVersion: '1', widthMm: 70, heightMm: 50, elements: [] },
  testData: {},
}

function renderDesigner(
  name?: string,
  opts: { onClose?: () => void; registerLeaveGuard?: (fn: ((leave: () => void) => void) | null) => void; helpAnchor?: string | null } = {},
) {
  return render(
    <AppProvider>
      {/* 状态栏消息探针：保存 / 守卫错误经 app.setStatus 呈现（真实界面在 Shell 状态栏，测试树内以探针捕获） */}
      <StatusProbe />
      <Designer
        request={name ? { kind: 'edit', name } : { kind: 'new' }}
        onClose={opts.onClose ?? (() => {})}
        registerLeaveGuard={opts.registerLeaveGuard}
        helpAnchor={opts.helpAnchor}
      />
    </AppProvider>,
  )
}

function StatusProbe() {
  const app = useApp()
  return <div data-testid="status-msg">{app.statusMsg}</div>
}

/** healthz 挂起（可控解析）——保持 serverMode 停留 'unknown'，用于断言 unknown 阶段行为。 */
function pendingHealthz(): () => void {
  let resolve!: () => void
  mocks.server.healthz.mockImplementation(() => new Promise<void>((res) => (resolve = res)))
  return () => resolve()
}

beforeEach(() => {
  vi.clearAllMocks()
  window.localStorage.clear()
  window.sessionStorage.clear()
  mocks.local.getHostConfig.mockResolvedValue({ serverUrl: 'http://127.0.0.1:53961', deviceId: 'device-1', deviceName: '打印电脑' })
  mocks.local.getTransport.mockResolvedValue({ mode: 'Log', params: {} })
})

afterEach(() => {
  cleanup()
})

describe('serverMode 守卫对齐 Workbench（迭代 91 F-13）', () => {
  it('AC-04：unknown 阶段（healthz 在途）不发模板详情请求；解析为 server 后以 serverApi 加载', async () => {
    const resolveHealthz = pendingHealthz()
    mocks.server.getTemplate.mockResolvedValue(PKG)
    renderDesigner('库位标签')

    // unknown 阶段：既不以 localApi 误发，也不以 serverApi 发（模式未定不发请求）
    await waitFor(() => expect(mocks.local.getHostConfig).toHaveBeenCalled())
    expect(mocks.server.getTemplate).not.toHaveBeenCalled()
    expect(mocks.local.getTemplate).not.toHaveBeenCalled()

    // 模式解析为 server → 以 serverApi 加载模板详情
    resolveHealthz()
    await waitFor(() => expect(mocks.server.getTemplate).toHaveBeenCalledWith('库位标签'))
    expect(mocks.local.getTemplate).not.toHaveBeenCalled()
    // 加载完成：顶栏模板名填充
    await waitFor(() => expect((screen.getByDisplayValue('库位标签') as HTMLInputElement).value).toBe('库位标签'), { timeout: 3000 })
  })

  it('AC-04 回退：解析为 standalone（healthz 失败 / 服务端不可达）后以 localApi 加载（单机降级）', async () => {
    mocks.server.healthz.mockRejectedValue(new Error('unreachable'))
    mocks.local.getTemplate.mockResolvedValue(PKG)
    renderDesigner('库位标签')

    await waitFor(() => expect(mocks.local.getTemplate).toHaveBeenCalledWith('库位标签'), { timeout: 3000 })
    expect(mocks.server.getTemplate).not.toHaveBeenCalled()
    await waitFor(() => expect((screen.getByDisplayValue('库位标签') as HTMLInputElement).value).toBe('库位标签'), { timeout: 3000 })
  })

  it('新建模板不依赖模式解析：unknown 阶段即可用，且不发任何模板详情请求', async () => {
    pendingHealthz()
    renderDesigner()

    await act(async () => {
      await Promise.resolve()
    })
    // 新建路径立即初始化（分组默认值可见），本地零请求
    expect(screen.getByDisplayValue('默认')).toBeTruthy()
    expect(mocks.server.getTemplate).not.toHaveBeenCalled()
    expect(mocks.local.getTemplate).not.toHaveBeenCalled()
  })

  it('加载失败呈现中文原因（ApiError message），与 Workbench 同一错误通道', async () => {
    mocks.server.healthz.mockResolvedValue({ service: 'LabelFrame.Server', status: 'ok' })
    mocks.server.getTemplate.mockRejectedValue(new ApiError('TEMPLATE_NOT_FOUND', '模板「不存在的模板」不存在。'))
    renderDesigner('不存在的模板')
    await waitFor(() => expect(screen.getByText('模板「不存在的模板」不存在。')).toBeTruthy(), { timeout: 3000 })
  })
})

// ---------- 页级保存链路 + 未保存离开保护（单机模式，biz = localApi） ----------

/** 单机模式默认桩：服务端不可达 → serverMode=standalone；本机 API 就绪（沿用 DataPrint.test.tsx 范式）。 */
function useStandaloneStubs() {
  beforeEach(() => {
    mocks.server.healthz.mockRejectedValue(new Error('unreachable'))
    mocks.local.getTemplate.mockResolvedValue(PKG)
    mocks.local.listTemplates.mockResolvedValue([])
    mocks.local.saveTemplate.mockResolvedValue(undefined)
  })
}

/** 等新建初始化完成（宽 100mm 输入出现 = state 就绪，保存按钮可用）。 */
async function waitForNewReady() {
  await screen.findByDisplayValue('100', undefined, { timeout: 3000 })
}

/** 等编辑模式加载完成（模板名 + 宽 70mm 输入出现）。 */
async function waitForEditLoaded() {
  await screen.findByDisplayValue('库位标签', undefined, { timeout: 3000 })
  await screen.findByDisplayValue('70', undefined, { timeout: 3000 })
}

/** 提交一步历史更改（改纸张宽）——undoCount > 0 即 dirty。 */
function makeDirty() {
  fireEvent.change(screen.getByDisplayValue('70'), { target: { value: '80' } })
  expect(screen.getByDisplayValue('80')).toBeTruthy()
}

describe('页级保存链路（迭代 92 F-03）', () => {
  useStandaloneStubs()

  it('新建保存成功：查重通过后以最终名保存（契约名 = 模板名），提示并回工作台', async () => {
    const onClose = vi.fn()
    renderDesigner(undefined, { onClose })
    await waitForNewReady()
    fireEvent.change(screen.getByPlaceholderText('模板名称'), { target: { value: '新模板' } })
    fireEvent.click(screen.getByRole('button', { name: '保存模板' }))

    await waitFor(() => expect(mocks.local.saveTemplate).toHaveBeenCalledTimes(1))
    const pkg = mocks.local.saveTemplate.mock.calls[0][0] as TemplatePackage
    expect(pkg.name).toBe('新模板')
    expect(pkg.group).toBe('默认')
    // 新建路径：契约名 / 版本由最终名派生（toContract / toLayout 组装）
    expect(pkg.contract?.name).toBe('新模板')
    expect(pkg.contract?.version).toBe('1')
    expect(pkg.layout?.widthMm).toBe(100)
    // 既有行为零回归：保存成功回工作台 + 状态栏提示
    await waitFor(() => expect(onClose).toHaveBeenCalledTimes(1))
    expect(screen.getByTestId('status-msg').textContent).toContain('模板「新模板」已保存')
  })

  it('空名保存：名称框红边 + 内联提示 + 聚焦，补名即消可保存', async () => {
    const onClose = vi.fn()
    renderDesigner(undefined, { onClose })
    await waitForNewReady()
    // 名称留空直接保存：不提交，名称框立即可见反馈
    fireEvent.click(screen.getByRole('button', { name: '保存模板' }))

    const input = screen.getByPlaceholderText('模板名称') as HTMLInputElement
    expect(await screen.findByTestId('designer-name-error')).toBeTruthy()
    expect(screen.getByTestId('designer-name-error').textContent).toContain('请先填写模板名称')
    expect(input.className).toContain('error')
    expect(document.activeElement).toBe(input)
    expect(mocks.local.saveTemplate).not.toHaveBeenCalled()

    // 补名：红错即消，再次保存走正常链路
    fireEvent.change(input, { target: { value: '补名模板' } })
    expect(screen.queryByTestId('designer-name-error')).toBeNull()
    fireEvent.click(screen.getByRole('button', { name: '保存模板' }))

    await waitFor(() => expect(mocks.local.saveTemplate).toHaveBeenCalledTimes(1))
    expect(mocks.local.saveTemplate.mock.calls[0][0].name).toBe('补名模板')
  })

  it('同名覆盖确认 → 取消：不保存、停留设计器', async () => {
    mocks.local.listTemplates.mockResolvedValue([{ name: '库位标签', group: '默认', updatedAt: '2026-09-18' }])
    const onClose = vi.fn()
    renderDesigner(undefined, { onClose })
    await waitForNewReady()
    fireEvent.change(screen.getByPlaceholderText('模板名称'), { target: { value: '库位标签' } })
    fireEvent.click(screen.getByRole('button', { name: '保存模板' }))

    expect(await screen.findByText('模板已存在', undefined, { timeout: 3000 })).toBeTruthy()
    fireEvent.click(screen.getByRole('button', { name: '取消' }))
    await waitFor(() => expect(screen.queryByText('模板已存在')).toBeNull())
    expect(mocks.local.saveTemplate).not.toHaveBeenCalled()
    expect(onClose).not.toHaveBeenCalled()
  })

  it('同名覆盖确认 → 确认：覆盖保存并回工作台', async () => {
    mocks.local.listTemplates.mockResolvedValue([{ name: '库位标签', group: '默认', updatedAt: '2026-09-18' }])
    const onClose = vi.fn()
    renderDesigner(undefined, { onClose })
    await waitForNewReady()
    fireEvent.change(screen.getByPlaceholderText('模板名称'), { target: { value: '库位标签' } })
    fireEvent.click(screen.getByRole('button', { name: '保存模板' }))

    expect(await screen.findByText('模板已存在', undefined, { timeout: 3000 })).toBeTruthy()
    fireEvent.click(screen.getByRole('button', { name: '覆盖保存' }))

    await waitFor(() => expect(mocks.local.saveTemplate).toHaveBeenCalledTimes(1))
    expect((mocks.local.saveTemplate.mock.calls[0][0] as TemplatePackage).name).toBe('库位标签')
    await waitFor(() => expect(onClose).toHaveBeenCalledTimes(1))
  })

  it('保存失败提示：呈现中文原因，停留设计器不回工作台', async () => {
    mocks.local.saveTemplate.mockRejectedValueOnce(new ApiError('LF_TPL_SAVE_FAILED', '模板保存失败：名称重复。'))
    const onClose = vi.fn()
    renderDesigner(undefined, { onClose })
    await waitForNewReady()
    fireEvent.change(screen.getByPlaceholderText('模板名称'), { target: { value: '新模板' } })
    fireEvent.click(screen.getByRole('button', { name: '保存模板' }))

    await waitFor(() => expect(screen.getByTestId('status-msg').textContent).toContain('模板保存失败：名称重复'), { timeout: 3000 })
    expect(onClose).not.toHaveBeenCalled()
    // 按钮复位（非「保存中…」），可重试
    expect(screen.getByRole('button', { name: '保存模板' })).toBeTruthy()
  })
})

describe('未保存离开保护三选（迭代 92 F-02，决议 a：dirty = 历史栈有已提交更改）', () => {
  useStandaloneStubs()

  it('AC-01「继续编辑」：dirty 后点返回弹三选，选择后留在设计器（不保存不离开）', async () => {
    const onClose = vi.fn()
    renderDesigner('库位标签', { onClose })
    await waitForEditLoaded()
    makeDirty()

    fireEvent.click(screen.getByTitle('返回工作台'))
    expect(await screen.findByText('未保存的更改', undefined, { timeout: 3000 })).toBeTruthy()
    expect(onClose).not.toHaveBeenCalled()

    fireEvent.click(screen.getByRole('button', { name: '继续编辑' }))
    await waitFor(() => expect(screen.queryByText('未保存的更改')).toBeNull())
    expect(onClose).not.toHaveBeenCalled()
    expect(mocks.local.saveTemplate).not.toHaveBeenCalled()
    // 编辑未丢：宽仍为改后的 80
    expect(screen.getByDisplayValue('80')).toBeTruthy()
  })

  it('AC-01「放弃更改」：不保存直接回工作台', async () => {
    const onClose = vi.fn()
    renderDesigner('库位标签', { onClose })
    await waitForEditLoaded()
    makeDirty()

    fireEvent.click(screen.getByTitle('返回工作台'))
    expect(await screen.findByText('未保存的更改', undefined, { timeout: 3000 })).toBeTruthy()
    fireEvent.click(screen.getByRole('button', { name: '放弃更改' }))

    await waitFor(() => expect(onClose).toHaveBeenCalledTimes(1))
    expect(mocks.local.saveTemplate).not.toHaveBeenCalled()
  })

  it('AC-01「保存并离开」：走既有保存链路，成功后离开（保存成功自然复位 dirty——组件随离开卸载）', async () => {
    const onClose = vi.fn()
    renderDesigner('库位标签', { onClose })
    await waitForEditLoaded()
    makeDirty()

    fireEvent.click(screen.getByTitle('返回工作台'))
    expect(await screen.findByText('未保存的更改', undefined, { timeout: 3000 })).toBeTruthy()
    fireEvent.click(screen.getByRole('button', { name: '保存并离开' }))

    await waitFor(() => expect(mocks.local.saveTemplate).toHaveBeenCalledTimes(1))
    expect((mocks.local.saveTemplate.mock.calls[0][0] as TemplatePackage).name).toBe('库位标签')
    await waitFor(() => expect(onClose).toHaveBeenCalledTimes(1))
    expect(screen.getByTestId('status-msg').textContent).toContain('模板「库位标签」已保存')
  })

  it('AC-01 导航切 tab 路径：经 registerLeaveGuard 注册的守卫挂起切换，「放弃更改」后执行挂起的切换', async () => {
    let guard: ((leave: () => void) => void) | null = null
    renderDesigner('库位标签', { registerLeaveGuard: (fn) => (guard = fn) })
    await waitForEditLoaded()
    makeDirty()
    expect(guard).toBeTruthy()

    // 模拟 Shell switchTab：把挂起的 setTab 交给守卫
    const pendingSwitch = vi.fn()
    act(() => guard!(pendingSwitch))
    expect(await screen.findByText('未保存的更改', undefined, { timeout: 3000 })).toBeTruthy()
    expect(pendingSwitch).not.toHaveBeenCalled()

    fireEvent.click(screen.getByRole('button', { name: '放弃更改' }))
    await waitFor(() => expect(pendingSwitch).toHaveBeenCalledTimes(1))
    expect(mocks.local.saveTemplate).not.toHaveBeenCalled()
  })

  it('AC-02 无编辑（无已提交更改）点返回：直接离开不弹窗（编辑已加载 / 新建两路径）', async () => {
    const onClose = vi.fn()
    const view = renderDesigner('库位标签', { onClose })
    await waitForEditLoaded()

    fireEvent.click(screen.getByTitle('返回工作台'))
    expect(onClose).toHaveBeenCalledTimes(1)
    expect(screen.queryByText('未保存的更改')).toBeNull()
    view.unmount()

    const onClose2 = vi.fn()
    renderDesigner(undefined, { onClose: onClose2 })
    await waitForNewReady()
    fireEvent.click(screen.getByTitle('返回工作台'))
    expect(onClose2).toHaveBeenCalledTimes(1)
    expect(screen.queryByText('未保存的更改')).toBeNull()
  })

  it('AC-03「保存并离开」遇保存失败：停留设计器显示错误、不丢编辑；挂起动作作废后普通保存仍可正常离开', async () => {
    mocks.local.saveTemplate.mockRejectedValueOnce(new ApiError('LF_TPL_SAVE_FAILED', '模板保存失败：服务端写入失败。'))
    const onClose = vi.fn()
    renderDesigner('库位标签', { onClose })
    await waitForEditLoaded()
    makeDirty()

    fireEvent.click(screen.getByTitle('返回工作台'))
    expect(await screen.findByText('未保存的更改', undefined, { timeout: 3000 })).toBeTruthy()
    fireEvent.click(screen.getByRole('button', { name: '保存并离开' }))

    await waitFor(() => expect(screen.getByTestId('status-msg').textContent).toContain('模板保存失败：服务端写入失败'), { timeout: 3000 })
    expect(onClose).not.toHaveBeenCalled()
    expect(screen.queryByText('未保存的更改')).toBeNull()
    // 不丢编辑：宽仍为 80，重试普通保存成功后按既有行为回工作台
    expect(screen.getByDisplayValue('80')).toBeTruthy()
    fireEvent.click(screen.getByRole('button', { name: '保存模板' }))
    await waitFor(() => expect(onClose).toHaveBeenCalledTimes(1))
  })
})

// ---------- 深链自动开示例草稿——元素入初始基线并选中 ----------

describe('深链自动开示例草稿（#316 拍板 A·初始基线）', () => {
  useStandaloneStubs()

  it('示例元素入基线并选中：填充组锚点自动弹气泡（无可见标题正文直出）、draftOpened 终态、undoCount=0 退出静默弃不弹三选', async () => {
    // 本机配置读取挂起（新建不依赖模式解析）：防 AppContext 启动链的「已读取本机配置」晚到覆盖 draftOpened
    mocks.local.getHostConfig.mockImplementation(() => new Promise(() => {}))
    const onClose = vi.fn()
    renderDesigner(undefined, { onClose, helpAnchor: 'designer.fill' })
    await waitForNewReady()

    // 示例元素已选中（fill 预设 = 演示同款示例值：字段名 location）
    expect(screen.getByText('location')).toBeTruthy()
    // 页内轮询命中 → 自动弹填充组文档点气泡——dialog 无障碍名保留、可见标题不渲染
    const dialog = await screen.findByRole('dialog')
    expect(dialog.getAttribute('aria-label')).toBe('填充：固定值或字段填充')
    expect(screen.queryByText('填充：固定值或字段填充')).toBeNull()
    // draftOpened 终态提示（pending 一闪被覆盖属预期）
    expect(screen.getByTestId('status-msg').textContent).toContain('帮助打开的是临时示例草稿')
    // 基线不 dirty：未编辑直接退出不弹三选（静默即弃）
    fireEvent.click(screen.getByTitle('返回工作台'))
    expect(onClose).toHaveBeenCalledTimes(1)
    expect(screen.queryByText('未保存的更改')).toBeNull()
  })

  it('深链草稿可正常编辑：人为提交一处编辑后 dirty，退出弹三选、放弃更改后离开（守卫与普通草稿一致）', async () => {
    const onClose = vi.fn()
    renderDesigner(undefined, { onClose, helpAnchor: 'designer.fill' })
    await waitForNewReady()
    fireEvent.change(screen.getByDisplayValue('100'), { target: { value: '80' } })

    fireEvent.click(screen.getByTitle('返回工作台'))
    expect(await screen.findByText('未保存的更改', undefined, { timeout: 3000 })).toBeTruthy()
    fireEvent.click(screen.getByRole('button', { name: '放弃更改' }))
    await waitFor(() => expect(onClose).toHaveBeenCalledTimes(1))
  })

  it('align 锚点：两个示例 Rect 同时选中（多选分支才渲染对齐组）并自动弹对齐气泡', async () => {
    renderDesigner(undefined, { helpAnchor: 'designer.align' })
    await waitForNewReady()
    // 多选分支渲染：对齐组文档点锚点在 DOM（预设缺一即锚点不现——双元素选中是前置）
    expect(document.querySelector('[data-help="designer.align"]')).toBeTruthy()
    expect(screen.getByText('已选 2 个元素')).toBeTruthy()
    const dialog = await screen.findByRole('dialog')
    expect(dialog.getAttribute('aria-label')).toBe('对齐')
  })
})

// ---------- 图片控件两步式上传链路（控件栏武装 → 画布空占位 → 上传填充 → 保存 → 重开往返） ----------

/** 最小 PNG 字节（魔数 + 最小结构，前端不做图片解码校验——尺寸解码经下方 Image 桩）。 */
const PNG_BYTES = new Uint8Array([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a, 0, 0, 0, 0x0d, 0x49, 0x48, 0x44, 0x52])

/** jsdom 不解码图片位（onload 不触发）：以桩替换全局 Image，src 赋值即异步 onload 并给出固定像素尺寸（8×4）。 */
function stubImageElement() {
  class FakeImage {
    onload: (() => void) | null = null
    onerror: (() => void) | null = null
    naturalWidth = 8
    naturalHeight = 4
    private _src = ''
    get src() {
      return this._src
    }
    set src(value: string) {
      this._src = value
      queueMicrotask(() => this.onload?.())
    }
  }
  vi.stubGlobal('Image', FakeImage as unknown as typeof Image)
}

/** 桩 canvas 编码通道（压缩成功路径用）：getContext 回假 2d 上下文、toBlob 回固定体积 Blob。 */
function stubCanvasEncode(blobBytes = new Uint8Array(300 * 1024)) {
  const ctx = { drawImage: vi.fn() }
  vi.spyOn(HTMLCanvasElement.prototype, 'getContext').mockReturnValue(ctx as unknown as CanvasRenderingContext2D)
  vi.spyOn(HTMLCanvasElement.prototype, 'toBlob').mockImplementation(function (this: HTMLCanvasElement, cb: BlobCallback, mime?: string) {
    cb(new Blob([blobBytes], { type: mime ?? 'application/octet-stream' }))
    return undefined
  })
}

/** 上传一个文件（经隐藏 input 的 change 事件）。 */
function uploadImage(fileName = 'logo.png', bytes: BlobPart = PNG_BYTES, type = 'image/png') {
  const input = screen.getByTestId('image-file-input') as HTMLInputElement
  const file = new File([bytes], fileName, { type })
  fireEvent.change(input, { target: { files: [file] } })
}

/** 两步式第一步：控件栏点「图片」武装 → 画布点击落空占位并选中。 */
async function placeEmptyImagePlaceholder() {
  fireEvent.click(screen.getByRole('button', { name: '图片' }))
  // 武装态（pendingType=Image）后再点画布（桩按 pendingType 放置）
  fireEvent.click(screen.getByTestId('canvas-place'))
  // 空占位元素建立并选中：属性面板出现「未指定」态提示
  await screen.findByText('尚未指定图片；点占位元素或「上传图片」均可。', undefined, { timeout: 3000 })
}

describe('图片控件两步式上传链路（#329 方案 v2）', () => {
  useStandaloneStubs()

  beforeEach(() => {
    stubImageElement()
  })
  afterEach(() => {
    vi.unstubAllGlobals()
    vi.restoreAllMocks()
  })

  it('AC-02：两步式放置——控件栏点「图片」武装 pendingType，画布点击落空占位（key 空、20×20mm），保存 images 为空字典', async () => {
    renderDesigner()
    await waitForNewReady()
    // 控件栏第五入口「图片」在场且点击进入武装态（与其他控件同模式）
    const paletteBtn = screen.getByRole('button', { name: '图片' })
    fireEvent.click(paletteBtn)
    expect(paletteBtn.className).toContain('armed')

    fireEvent.click(screen.getByTestId('canvas-place'))
    // 已放置并选中（状态栏提示 + 属性面板图片组「未指定」态）
    await screen.findByText('尚未指定图片；点占位元素或「上传图片」均可。', undefined, { timeout: 3000 })
    expect(screen.getByTestId('status-msg').textContent).toContain('已添加「图片」')

    fireEvent.change(screen.getByPlaceholderText('模板名称'), { target: { value: '图片模板' } })
    fireEvent.click(screen.getByRole('button', { name: '保存模板' }))
    await waitFor(() => expect(mocks.local.saveTemplate).toHaveBeenCalledTimes(1))

    const pkg = mocks.local.saveTemplate.mock.calls[0][0] as TemplatePackage
    // 空占位元素持久化：sourceKey 空字符串（key 空＝未指定），盒尺寸保持默认 20×20mm
    const imageEl = pkg.layout?.elements.find((e) => e.type === 'image')
    expect(imageEl).toBeTruthy()
    expect(imageEl?.sourceKey).toBe('')
    expect(imageEl?.widthMm).toBe(20)
    expect(imageEl?.heightMm).toBe(20)
    // 空 key 真值过滤：未被引用的资源不进 images（恒发显式空字典）
    expect(pkg.images).toEqual({})
  })

  it('AC-02：上传填充（属性面板「上传图片」入口）——填充 key、保持用户所画盒、保存 images 含被引用 key、状态栏「已更新图片。」', async () => {
    renderDesigner()
    await waitForNewReady()
    await placeEmptyImagePlaceholder()

    // 属性面板「上传图片」按钮（key 空态入口，与「更换图片」同回调语义）
    fireEvent.click(screen.getByRole('button', { name: '上传图片' }))
    uploadImage()
    // 填充成功：元信息行出现（key + 解码尺寸），状态栏成功提示
    await screen.findByText('图片资源：logo.png · 8×4 px', undefined, { timeout: 3000 })
    expect(screen.getByTestId('status-msg').textContent).toContain('已更新图片')

    fireEvent.change(screen.getByPlaceholderText('模板名称'), { target: { value: '图片模板' } })
    fireEvent.click(screen.getByRole('button', { name: '保存模板' }))
    await waitFor(() => expect(mocks.local.saveTemplate).toHaveBeenCalledTimes(1))

    const pkg = mocks.local.saveTemplate.mock.calls[0][0] as TemplatePackage
    const imageEl = pkg.layout?.elements.find((e) => e.type === 'image')
    expect(imageEl?.sourceKey).toBe('logo.png')
    // 填充后保持用户所画盒（20×20mm 默认值不被 30mm 宽高比夹取改写）
    expect(imageEl?.widthMm).toBe(20)
    expect(imageEl?.heightMm).toBe(20)
    expect(Object.keys(pkg.images ?? {})).toEqual(['logo.png'])
    expect(pkg.images?.['logo.png']).toBe(btoa(String.fromCharCode(...PNG_BYTES)))
  })

  it('AC-02：画布空占位单击入口（拍板 2-A 的画布侧通道）——onRequestImageUpload 直接填充', async () => {
    renderDesigner()
    await waitForNewReady()
    await placeEmptyImagePlaceholder()

    // 画布桩转接：空占位元素的单击回调（真实 Konva 命中归 ElementNode 结构级断言）
    fireEvent.click(screen.getByTestId('canvas-upload-empty'))
    uploadImage()
    await screen.findByText('图片资源：logo.png · 8×4 px', undefined, { timeout: 3000 })
  })

  it('AC-02：空 key 元素往返——保存后重开占位复原（key 空、图层标签「图片（未指定）」、再保存 images 仍空）', async () => {
    mocks.local.getTemplate.mockResolvedValue({
      ...PKG,
      layout: {
        ...PKG.layout,
        elements: [{ type: 'image', sourceKey: '', xMm: 2, yMm: 2, widthMm: 20, heightMm: 20 }],
      },
    })
    renderDesigner('库位标签')
    await waitForEditLoaded()

    // 图层标签提升辨识：key 空显示「图片（未指定）」；点击选中
    fireEvent.click(screen.getByText('图片（未指定）'))
    expect(await screen.findByText('尚未指定图片；点占位元素或「上传图片」均可。', undefined, { timeout: 3000 })).toBeTruthy()

    fireEvent.click(screen.getByRole('button', { name: '保存模板' }))
    await waitFor(() => expect(mocks.local.saveTemplate).toHaveBeenCalledTimes(1))
    const pkg = mocks.local.saveTemplate.mock.calls[0][0] as TemplatePackage
    expect(pkg.layout?.elements[0]?.type).toBe('image')
    expect(pkg.layout?.elements[0]?.sourceKey).toBe('')
    expect(pkg.images).toEqual({})
  })

  it('AC-02：移除图片（删除元素）后保存，images 为空字典（恒发显式字典不依赖缺省态）', async () => {
    renderDesigner()
    await waitForNewReady()
    await placeEmptyImagePlaceholder()
    fireEvent.click(screen.getByRole('button', { name: '上传图片' }))
    uploadImage()
    await screen.findByText('图片资源：logo.png · 8×4 px', undefined, { timeout: 3000 })

    fireEvent.click(screen.getByRole('button', { name: '移除图片' }))
    fireEvent.change(screen.getByPlaceholderText('模板名称'), { target: { value: '图片模板' } })
    fireEvent.click(screen.getByRole('button', { name: '保存模板' }))
    await waitFor(() => expect(mocks.local.saveTemplate).toHaveBeenCalledTimes(1))

    const pkg = mocks.local.saveTemplate.mock.calls[0][0] as TemplatePackage
    expect(pkg.images).toEqual({})
    expect(pkg.layout?.elements.some((e) => e.type === 'image')).toBe(false)
  })

  it('AC-02：重开回填——GET 响应 images 回填资源表，未改动直接保存时被引用 key 随请求带回', async () => {
    mocks.local.getTemplate.mockResolvedValue({
      ...PKG,
      layout: {
        ...PKG.layout,
        elements: [{ type: 'image', sourceKey: 'old.png', xMm: 2, yMm: 2, widthMm: 20, heightMm: 10 }],
      },
      images: { 'old.png': btoa('old-image-bytes') },
    })
    renderDesigner('库位标签')
    await waitForEditLoaded()

    // 未选中不展开属性面板——资源表回填直接以「保存请求体带回被引用 key」验证（回填失败则 GC 后为空字典）
    fireEvent.click(screen.getByRole('button', { name: '保存模板' }))
    await waitFor(() => expect(mocks.local.saveTemplate).toHaveBeenCalledTimes(1))
    const pkg = mocks.local.saveTemplate.mock.calls[0][0] as TemplatePackage
    expect(pkg.images).toEqual({ 'old.png': btoa('old-image-bytes') })
    expect(pkg.layout?.elements[0]?.type).toBe('image')
  })

  it('AC-01②：上传失败提醒为页内 toast（标题＋原因体），不再只是状态栏单条文字', async () => {
    renderDesigner()
    await waitForNewReady()
    await placeEmptyImagePlaceholder()
    fireEvent.click(screen.getByRole('button', { name: '上传图片' }))

    // jsdom 无 2d context：压缩管道回退原文件 → 3MB 原样超限拒收（拒收闸按最终 blob 字节）
    uploadImage('big.png', new Uint8Array(3 * 1024 * 1024))

    const toast = await screen.findByTestId('image-toast', undefined, { timeout: 3000 })
    expect(toast.textContent).toContain('图片上传失败')
    expect(toast.textContent).toContain('已自动压缩，仍超过 2MB 上限')
    expect(toast.textContent).toContain('请调整后重试')
    // toast 自动消失（5s）
    await waitFor(() => expect(screen.queryByTestId('image-toast')).toBeNull(), { timeout: 8000 })
  })

  it('AC-01④：>1MB 触发自动压缩——压缩后入库（压缩提示走 toast 含前后体积），保存请求体 images 体积 <2MB', async () => {
    stubCanvasEncode(new Uint8Array(300 * 1024)) // 压缩产物 300KB
    renderDesigner()
    await waitForNewReady()
    await placeEmptyImagePlaceholder()
    fireEvent.click(screen.getByRole('button', { name: '上传图片' }))

    // 1.5MB JPEG：>1MB 阈值触发压缩管道（桩 canvas），压后 300KB 过 2MB 闸
    uploadImage('photo.jpg', new Uint8Array(Math.floor(1.5 * 1024 * 1024)), 'image/jpeg')
    await screen.findByText('图片资源：photo.jpg · 8×4 px', undefined, { timeout: 3000 })
    // 压缩提示并入 toast（状态栏轻提示用户实证看不见——踩过）
    const toast = await screen.findByTestId('image-toast', undefined, { timeout: 3000 })
    expect(toast.textContent).toContain('已自动压缩 1.5MB → 0.29MB')

    fireEvent.change(screen.getByPlaceholderText('模板名称'), { target: { value: '图片模板' } })
    fireEvent.click(screen.getByRole('button', { name: '保存模板' }))
    await waitFor(() => expect(mocks.local.saveTemplate).toHaveBeenCalledTimes(1))
    const pkg = mocks.local.saveTemplate.mock.calls[0][0] as TemplatePackage
    const base64 = pkg.images?.['photo.jpg'] ?? ''
    expect(base64.length).toBeGreaterThan(0)
    // 入库字节 = 压缩产物（300KB），非原文件 1.5MB
    expect(Math.floor((base64.length * 3) / 4)).toBe(300 * 1024)
  })

  it('格式拒收：非 PNG / JPG / BMP 以 toast 提示不支持', async () => {
    renderDesigner()
    await waitForNewReady()
    await placeEmptyImagePlaceholder()
    fireEvent.click(screen.getByRole('button', { name: '上传图片' }))

    uploadImage('anim.gif', new Uint8Array(8), 'image/gif')
    const toast = await screen.findByTestId('image-toast', undefined, { timeout: 3000 })
    expect(toast.textContent).toContain('不支持的图片格式')
  })

  it('复陪验反馈 2-③：中文文件名保留为资源 key（Unicode 口径对齐后端，不再替换为下划线）', async () => {
    renderDesigner()
    await waitForNewReady()
    await placeEmptyImagePlaceholder()
    fireEvent.click(screen.getByRole('button', { name: '上传图片' }))

    uploadImage('微信图片_20260401170145_16_2679.png', new Uint8Array(16))
    await screen.findByText('图片资源：微信图片_20260401170145_16_2679.png · 8×4 px', undefined, { timeout: 3000 })
  })
})

describe('sanitizeImageKey（文件名净化 · Unicode 口径，复陪验反馈 2-③）', () => {
  it('中文保留；路径分隔符 / 空白等替换为 _；空名兜底 image', () => {
    expect(sanitizeImageKey('微信图片_20260401170145_16_2679.png')).toBe('微信图片_20260401170145_16_2679.png')
    expect(sanitizeImageKey('a/b' + String.fromCharCode(92) + 'c d.png')).toBe('a_b_c_d.png')
    expect(sanitizeImageKey('logo (final).png')).toBe('logo__final_.png')
    expect(sanitizeImageKey('')).toBe('image')
  })
})
