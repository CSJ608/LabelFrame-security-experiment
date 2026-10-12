// @vitest-environment jsdom
// 「看实时效果」演示全链——临时注入 / 画布实时反映 / 退出自动还原 /
// 历史栈零污染 / 零落库。画布为 react-konva 重组件（jsdom 无 2d context），以 props 探针桩替换：
// 捕获 Designer 传入的 viewState 断言「画布收到的元素」，演示合并与还原在数据层可完整验证；
// Konva 真实交互由专门画布测试与真机验收覆盖（Designer.test.tsx 同口径）。

import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { act, cleanup, configure, fireEvent, render, screen, waitFor } from '@testing-library/react'
import type { TemplatePackage } from '../../lib/api/types'
import { AppProvider } from '../../state/AppContext'
import { Designer } from '../Designer'

configure({ asyncUtilTimeout: 8000 })

/** 画布 props 探针：捕获最近一次渲染的 CanvasViewport props（viewState 合并结果）。 */
const canvasProbe = vi.hoisted(() => ({ current: null as { state: { elements: Array<Record<string, unknown>> } } | null }))

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

vi.mock('../../lib/api/client', () => ({
  serverApi: mocks.server,
  localApi: mocks.local,
  setServerBaseUrl: vi.fn(),
  probeHealthz: vi.fn(async () => false),
}))
// 本文件为 client 构建语义用例，显式注入 client 分支（VITE_UI_MODE=server 整仓测试时保持稳定）
vi.mock('../../lib/uiMode', () => ({ UI_MODE: 'client', isServerUi: false }))
vi.mock('../designer/CanvasViewport', () => ({
  CanvasViewport: (props: { state: { elements: Array<Record<string, unknown>> } } | null) => {
    canvasProbe.current = props as never
    return null
  },
}))

/** 单个固定值文本元素的字段模板（literal 模式——演示注入后转字段填充，差异完全可见）。 */
const PKG: TemplatePackage = {
  name: '演示模板',
  group: '默认',
  contract: { name: 'contract-1', version: '1', fields: [] },
  layout: {
    name: 'layout-1',
    contractName: 'contract-1',
    contractVersion: '1',
    widthMm: 70,
    heightMm: 50,
    elements: [{ type: 'text', xMm: 5, yMm: 5, literal: '固定文本' }],
  },
  testData: {},
}

function renderDesigner(opts: { onClose?: () => void } = {}) {
  return render(
    <AppProvider>
      <Designer request={{ kind: 'edit', name: '演示模板' }} onClose={opts.onClose ?? (() => {})} />
    </AppProvider>,
  )
}

/** 挂载并选中唯一元素（点图层行），打开填充组文档气泡。 */
async function openFillBubble(opts: { onClose?: () => void } = {}) {
  renderDesigner(opts)
  await screen.findByText(/固定文本/)
  fireEvent.click(screen.getByText(/固定文本/))
  fireEvent.click(document.querySelector('[data-help="designer.fill"]') as HTMLButtonElement)
  await screen.findByRole('dialog')
}

beforeEach(() => {
  vi.clearAllMocks()
  window.localStorage.clear()
  window.sessionStorage.clear()
  // 服务端探测必败（未配置的 vi.fn() 返回 undefined，await 不抛错会误判 server 模式）→ standalone 走 localApi
  mocks.server.healthz.mockRejectedValue(new Error('unreachable'))
  mocks.local.getHostConfig.mockResolvedValue({ serverUrl: 'http://127.0.0.1:53961', deviceId: 'device-1', deviceName: '打印电脑' })
  mocks.local.getTransport.mockResolvedValue({ mode: 'Log', params: {} })
  mocks.local.getTemplate.mockResolvedValue(PKG)
  mocks.local.listTemplates.mockResolvedValue([])
})

afterEach(() => {
  cleanup()
})

describe('「看实时效果」演示（AC-05）', () => {
  it('点演示：面板与画布收到注入值（来源转字段填充、示例字段名 / 预览值），演示态提示出现', async () => {
    await openFillBubble()
    fireEvent.click(screen.getByRole('button', { name: '看实时效果' }))
    // 画布实时反映：探针 state 元素被合并为演示预设值
    await waitFor(() => {
      const el = canvasProbe.current?.state.elements[0]
      expect(el?.['text']).toBe('A-01-02')
      expect(el?.['mode']).toBe('field')
      expect(el?.['key']).toBe('location')
      expect(el?.['displayName']).toBe('库位')
    })
    // 属性面板同源反映（输入框收到注入值）且禁用（锁输入）
    const key = screen.getByLabelText('字段名') as HTMLInputElement
    expect(key.value).toBe('location')
    expect(key.disabled).toBe(true)
    const preview = screen.getByLabelText('预览值') as HTMLInputElement
    expect(preview.value).toBe('A-01-02')
    expect(preview.disabled).toBe(true)
    // 演示态提示行（退出将还原、不会保存）
    expect(screen.getByText(/当前为演示效果（示例数据），退出后自动还原，不会保存到模板。/)).toBeTruthy()
    // 演示本身零写请求
    expect(mocks.local.saveTemplate).not.toHaveBeenCalled()
  })

  it('退出演示：画布与面板还原为演示前取值', async () => {
    await openFillBubble()
    fireEvent.click(screen.getByRole('button', { name: '看实时效果' }))
    await waitFor(() => expect(canvasProbe.current?.state.elements[0]?.['text']).toBe('A-01-02'))
    fireEvent.click(screen.getByRole('button', { name: '退出演示' }))
    await waitFor(() => expect(canvasProbe.current?.state.elements[0]?.['text']).toBe('固定文本'))
    expect(canvasProbe.current?.state.elements[0]?.['mode']).toBe('literal')
    const literal = screen.getByLabelText('固定值') as HTMLInputElement
    expect(literal.value).toBe('固定文本')
    expect(literal.disabled).toBe(false)
  })

  it('历史栈零污染：演示 + 退出后返回工作台不弹未保存确认（undoCount = 0 直接放行）', async () => {
    const onClose = vi.fn()
    await openFillBubble({ onClose })
    fireEvent.click(screen.getByRole('button', { name: '看实时效果' }))
    await waitFor(() => expect(canvasProbe.current?.state.elements[0]?.['text']).toBe('A-01-02'))
    fireEvent.click(screen.getByRole('button', { name: '退出演示' }))
    await waitFor(() => expect(canvasProbe.current?.state.elements[0]?.['text']).toBe('固定文本'))
    // 返回（requestLeave）：历史栈无已提交更改 → 不弹三选确认、直接离开
    fireEvent.click(screen.getByTitle('返回工作台'))
    await waitFor(() => expect(onClose).toHaveBeenCalledTimes(1))
    expect(screen.queryByText('未保存的更改')).toBeNull()
  })

  it('零落库：演示期间保存，写请求 payload 只含原始数据、不含任何注入值', async () => {
    mocks.local.saveTemplate.mockResolvedValue(undefined)
    await openFillBubble()
    fireEvent.click(screen.getByRole('button', { name: '看实时效果' }))
    await waitFor(() => expect(canvasProbe.current?.state.elements[0]?.['text']).toBe('A-01-02'))
    // doSave 读 stateRef（原始数据）——演示注入值不进保存链路（结构性保证）
    fireEvent.change(screen.getByPlaceholderText('模板名称'), { target: { value: '演示模板' } })
    fireEvent.click(screen.getByRole('button', { name: '保存模板' }))
    await waitFor(() => expect(mocks.local.saveTemplate).toHaveBeenCalledTimes(1))
    const pkg = mocks.local.saveTemplate.mock.calls[0][0] as TemplatePackage
    const layoutJson = JSON.stringify(pkg.layout)
    expect(layoutJson).not.toContain('A-01-02')
    expect(layoutJson).not.toContain('location')
    expect(layoutJson).toContain('固定文本')
  })

  it('演示期间快捷键停用：Ctrl+Z 不改画布数据（撤销不绕过锁输入）', async () => {
    await openFillBubble()
    fireEvent.click(screen.getByRole('button', { name: '看实时效果' }))
    await waitFor(() => expect(canvasProbe.current?.state.elements[0]?.['text']).toBe('A-01-02'))
    await act(async () => {
      fireEvent.keyDown(document, { key: 'z', ctrlKey: true })
    })
    expect(canvasProbe.current?.state.elements[0]?.['text']).toBe('A-01-02')
  })
})
