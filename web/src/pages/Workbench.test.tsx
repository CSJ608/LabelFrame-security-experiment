// @vitest-environment jsdom
// 工作台模板名搜索——子串匹配、大小写不敏感，与分组过滤叠加生效；清空恢复完整列表。
// mock 覆盖组件树用到的全部 client 方法（含 AppContext 启动链）。
// 卡片「打印」直达入口——每张卡片均有打印按钮、点击回调携带模板名；双击卡片仍进设计器。
// 卡片脚收纳回归——「编辑」主色列首＋「打印」常驻＋⋯ 溢出菜单（导出 / 删除），删除仍走确认 Modal（含「不可恢复」）。
// 视觉裁剪（196px 最窄轨道下无按钮被裁）以结构断言＋样式走查自证：卡片脚恒 3 枚按钮（两常驻 flex:1 ＋
// ⋯ 图标按钮 .wb-more 不占弹性宽度）、菜单 portal 挂 body 直下且 fixed（脱离 .wb-card overflow:hidden）——
// 浏览器实测（逐级缩窗）留待验收走查，本文件不伪装视觉验证。

import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { cleanup, configure, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import type { TemplateSummary } from '../lib/api/types'
import { AppProvider } from '../state/AppContext'
import { Workbench } from './Workbench'
// 挂载链（AppContext 启动链 → 模板列表加载）为多段 promise + React 真实宏任务
// 调度，CI 高负载 runner 上偶发超过 findBy / waitFor 默认 1000ms；统一放宽到 8000ms（与
// JobHistory / DataPrint / Settings 同口径），单测总超时由 vitest.config testTimeout（20s）兜住。
configure({ asyncUtilTimeout: 8000 })

const mocks = vi.hoisted(() => ({
  server: {
    healthz: vi.fn(),
    listTemplates: vi.fn(),
    deleteTemplate: vi.fn(),
    exportTemplate: vi.fn(),
    importTemplate: vi.fn(),
    previewTemplate: vi.fn(),
  },
  local: {
    healthz: vi.fn(),
    listTemplates: vi.fn(),
    getHostConfig: vi.fn(),
    getTransport: vi.fn(),
    previewTemplate: vi.fn(),
  },
}))

vi.mock('../lib/api/client', () => ({ serverApi: mocks.server, localApi: mocks.local, setServerBaseUrl: vi.fn() }))
// 本文件为 client 构建语义用例，显式注入 client 分支（VITE_UI_MODE=server 整仓测试时保持稳定）
vi.mock('../lib/uiMode', () => ({ UI_MODE: 'client', isServerUi: false }))

// 大小写混合命名 + 两组分组，覆盖搜索与分组叠加；Shelf-Tag 携带 uid（打开设计器 request 透传断言用）
const TEMPLATES: TemplateSummary[] = [
  { name: 'Carton-Label-A', group: '华东仓', updatedAt: '2026-09-01T10:00:00Z' },
  { name: 'carton-label-b', group: '华东仓', updatedAt: '2026-09-02T10:00:00Z' },
  { name: 'Shelf-Tag', group: '华南仓', updatedAt: '2026-09-03T10:00:00Z', uid: 'uid-shelf' },
]

function renderWorkbench(handlers?: { onOpenPrint?: (name: string) => void }) {
  return render(
    <AppProvider>
      <Workbench onOpenDesigner={() => {}} onOpenPrint={handlers?.onOpenPrint ?? (() => {})} />
    </AppProvider>,
  )
}

/* * 取指定模板名所在卡片的容器（wb-card）—— 各 describe 共用。 */
function cardOf(name: string): HTMLElement {
  return screen.getByText(name).closest('.wb-card') as HTMLElement
}

beforeEach(() => {
  vi.clearAllMocks()
  window.localStorage.clear()
  window.sessionStorage.clear()
  mocks.server.healthz.mockResolvedValue({ service: 'LabelFrame.Server', status: 'ok' })
  mocks.local.getHostConfig.mockResolvedValue({ serverUrl: 'http://127.0.0.1:53961', deviceId: 'device-1', deviceName: 'PC' })
  mocks.local.getTransport.mockResolvedValue({ mode: 'Log', params: {} })
  mocks.server.listTemplates.mockResolvedValue(TEMPLATES)
  mocks.local.listTemplates.mockResolvedValue(TEMPLATES)
  // 预览缓存链（previewTemplate → URL.createObjectURL）——jsdom 未实现 createObjectURL，全局桩
  // （DataPrint.test 同口径）；未桩时 ensure 走 .catch 落错误占位、不影响断言，桩后链路完整
  vi.stubGlobal('URL', { createObjectURL: vi.fn(() => 'blob:mock'), revokeObjectURL: vi.fn() })
  mocks.server.previewTemplate.mockResolvedValue({ blob: new Blob(['png']) })
  mocks.local.previewTemplate.mockResolvedValue({ blob: new Blob(['png']) })
})

afterEach(() => {
  cleanup()
  vi.unstubAllGlobals()
})

describe('工作台模板名搜索（迭代 45）', () => {
  it('名称搜索：子串匹配、大小写不敏感，清空后恢复完整列表', async () => {
    renderWorkbench()
    expect(await screen.findByText('Carton-Label-A')).toBeTruthy()
    expect(screen.getByText('carton-label-b')).toBeTruthy()
    expect(screen.getByText('Shelf-Tag')).toBeTruthy()

    // 大写关键词命中小写命名（子串、不区分大小写）
    fireEvent.change(screen.getByPlaceholderText('搜索模板名称'), { target: { value: 'CARTON' } })
    await waitFor(() => expect(screen.queryByText('Shelf-Tag')).toBeNull())
    expect(screen.getByText('Carton-Label-A')).toBeTruthy()
    expect(screen.getByText('carton-label-b')).toBeTruthy()

    // 清空搜索 → 恢复完整列表
    fireEvent.change(screen.getByPlaceholderText('搜索模板名称'), { target: { value: '' } })
    await waitFor(() => expect(screen.getByText('Shelf-Tag')).toBeTruthy())
    expect(screen.getByText('Carton-Label-A')).toBeTruthy()
  })

  it('搜索与分组过滤叠加生效：交集为准，双清空恢复完整列表', async () => {
    renderWorkbench()
    expect(await screen.findByText('Shelf-Tag')).toBeTruthy()

    // 分组 = 华东仓：仅两个 carton 模板
    fireEvent.change(screen.getByTitle('按分组过滤'), { target: { value: '华东仓' } })
    await waitFor(() => expect(screen.queryByText('Shelf-Tag')).toBeNull())

    // 叠加搜索 shelf：华东仓内无匹配 → 空态提示
    fireEvent.change(screen.getByPlaceholderText('搜索模板名称'), { target: { value: 'shelf' } })
    expect(await screen.findByText('没有匹配的模板')).toBeTruthy()

    // 清空搜索 → 回到分组过滤结果
    fireEvent.change(screen.getByPlaceholderText('搜索模板名称'), { target: { value: '' } })
    expect(await screen.findByText('Carton-Label-A')).toBeTruthy()
    expect(screen.queryByText('Shelf-Tag')).toBeNull()

    // 再清空分组 → 完整列表
    fireEvent.change(screen.getByTitle('按分组过滤'), { target: { value: '' } })
    await waitFor(() => expect(screen.getByText('Shelf-Tag')).toBeTruthy())
  })

  it('搜索词前后空白不影响匹配', async () => {
    renderWorkbench()
    expect(await screen.findByText('Shelf-Tag')).toBeTruthy()

    fireEvent.change(screen.getByPlaceholderText('搜索模板名称'), { target: { value: '  shelf  ' } })
    await waitFor(() => expect(screen.queryByText('Carton-Label-A')).toBeNull())
    expect(screen.getByText('Shelf-Tag')).toBeTruthy()
  })
})

// 模板卡片「打印」直达——跳「数据与打印」页并预选该模板；双击卡片仍进设计器。
describe('模板卡片打印直达（迭代 85 · #133 C-4）', () => {
  it('AC-01：每张卡片操作区均有「打印」按钮——点击回调携带该模板名（App 侧据此预选并跳数据与打印页）', async () => {
    const onOpenPrint = vi.fn()
    renderWorkbench({ onOpenPrint })
    expect(await screen.findByText('Shelf-Tag')).toBeTruthy()

    for (const name of ['Carton-Label-A', 'carton-label-b', 'Shelf-Tag']) {
      const btn = within(cardOf(name)).getByRole('button', { name: '打印' })
      expect((btn as HTMLButtonElement).disabled).toBe(false)
      fireEvent.click(btn)
    }
    expect(onOpenPrint).toHaveBeenCalledTimes(3)
    expect(onOpenPrint).toHaveBeenNthCalledWith(1, 'Carton-Label-A')
    expect(onOpenPrint).toHaveBeenNthCalledWith(2, 'carton-label-b')
    expect(onOpenPrint).toHaveBeenNthCalledWith(3, 'Shelf-Tag')
  })

  it('AC-01 不回归：双击卡片仍进设计器（编辑该模板），单击「打印」不触发设计器', async () => {
    const onOpenDesigner = vi.fn()
    const onOpenPrint = vi.fn()
    render(
      <AppProvider>
        <Workbench onOpenDesigner={onOpenDesigner} onOpenPrint={onOpenPrint} />
      </AppProvider>,
    )
    expect(await screen.findByText('Shelf-Tag')).toBeTruthy()

    fireEvent.doubleClick(cardOf('Shelf-Tag'))
    expect(onOpenDesigner).toHaveBeenCalledTimes(1)
    expect(onOpenDesigner).toHaveBeenCalledWith({ kind: 'edit', name: 'Shelf-Tag', uid: 'uid-shelf' })

    // 单击「打印」只走打印直达，不打开设计器
    fireEvent.click(within(cardOf('Shelf-Tag')).getByRole('button', { name: '打印' }))
    expect(onOpenPrint).toHaveBeenCalledWith('Shelf-Tag')
    expect(onOpenDesigner).toHaveBeenCalledTimes(1)
  })
})

// 卡片脚收纳——「编辑」升主操作，导出 / 删除收进 ⋯ 溢出菜单
// 修复四按钮平铺（最小需宽约 216px）超出最窄卡片可用宽度 176px 致「删除」被 .wb-card overflow:hidden
// 裁剪不可见不可点的回归。
describe('卡片脚操作收纳（迭代 104 · #225）', () => {
  it('卡片脚 = 编辑（唯一主色）＋ 打印（普通）＋ ⋯ 图标按钮，恒 3 枚；导出 / 删除不在卡片脚平铺', async () => {
    renderWorkbench()
    expect(await screen.findByText('Shelf-Tag')).toBeTruthy()

    const editBtn = within(cardOf('Shelf-Tag')).getByRole('button', { name: '编辑' }) as HTMLButtonElement
    const printBtn = within(cardOf('Shelf-Tag')).getByRole('button', { name: '打印' }) as HTMLButtonElement
    const moreBtn = within(cardOf('Shelf-Tag')).getByRole('button', { name: /更多操作/ }) as HTMLButtonElement
    // 主操作归属：编辑 = primary；打印 = 普通按钮
    expect(editBtn.className).toContain('primary')
    expect(printBtn.className).not.toContain('primary')
    expect(moreBtn.getAttribute('aria-haspopup')).toBe('menu')
    // 结构自证（视觉裁剪代理断言）：卡片脚恒 3 枚按钮（两常驻 flex:1 ＋ ⋯ 不占弹性宽度 .wb-more）
    const foot = editBtn.closest('.wb-card-foot') as HTMLElement
    expect(foot.querySelectorAll('button')).toHaveLength(3)
    expect(foot.querySelector('button.wb-more')).toBeTruthy()
    // 菜单未开时：导出 / 删除不以卡片脚按钮形态出现（旧回归形态 = 第 4 枚按钮被裁不可见）
    expect(screen.queryAllByRole('button', { name: '导出' })).toHaveLength(0)
    expect(screen.queryAllByRole('button', { name: '删除' })).toHaveLength(0)
  })

  it('「编辑」直达设计器（卡片内单击与双击等价）', async () => {
    const onOpenDesigner = vi.fn()
    render(
      <AppProvider>
        <Workbench onOpenDesigner={onOpenDesigner} onOpenPrint={() => {}} />
      </AppProvider>,
    )
    expect(await screen.findByText('Shelf-Tag')).toBeTruthy()
    fireEvent.click(within(cardOf('Shelf-Tag')).getByRole('button', { name: '编辑' }))
    expect(onOpenDesigner).toHaveBeenCalledTimes(1)
    expect(onOpenDesigner).toHaveBeenCalledWith({ kind: 'edit', name: 'Shelf-Tag', uid: 'uid-shelf' })
  })
})

// ⋯ 溢出菜单交互——菜单项可达、Esc / 外部点击关闭、动作触发即收起。
describe('⋯ 溢出菜单（迭代 104 · #225）', () => {
  it('点 ⋯ 打开：菜单 portal 挂 body 直下 + fixed（防 .wb-card overflow 裁剪），含导出与 danger 删除菜单项', async () => {
    renderWorkbench()
    expect(await screen.findByText('Shelf-Tag')).toBeTruthy()

    fireEvent.click(within(cardOf('Shelf-Tag')).getByRole('button', { name: /更多操作/ }))
    const menu = screen.getByRole('menu')
    expect(menu.getAttribute('aria-label')).toContain('Shelf-Tag')
    // 防裁剪两要素：不在 .wb-card 子树内 + fixed 定位
    expect(menu.closest('.wb-card')).toBeNull()
    expect(menu.parentElement).toBe(document.body)
    expect(menu.style.position).toBe('fixed')
    expect(within(menu).getByRole('menuitem', { name: '导出' })).toBeTruthy()
    expect(within(menu).getByRole('menuitem', { name: '删除' }).className).toContain('danger')
  })

  it('按 Esc 或点菜单外区域关闭；再点 ⋯ 切换收起', async () => {
    renderWorkbench()
    expect(await screen.findByText('Shelf-Tag')).toBeTruthy()
    const moreBtn = within(cardOf('Shelf-Tag')).getByRole('button', { name: /更多操作/ })

    fireEvent.click(moreBtn)
    expect(moreBtn.getAttribute('aria-expanded')).toBe('true')
    fireEvent.keyDown(document, { key: 'Escape' })
    expect(screen.queryByRole('menu')).toBeNull()
    expect(moreBtn.getAttribute('aria-expanded')).toBe('false')

    // 再开 → 菜单外 mousedown 关闭
    fireEvent.click(moreBtn)
    expect(screen.getByRole('menu')).toBeTruthy()
    fireEvent.mouseDown(document.body)
    expect(screen.queryByRole('menu')).toBeNull()

    // 再开 → 再点同一 ⋯ 切换收起（锚点点击不算「外部」）
    fireEvent.click(moreBtn)
    expect(screen.getByRole('menu')).toBeTruthy()
    fireEvent.mouseDown(moreBtn)
    fireEvent.click(moreBtn)
    expect(screen.queryByRole('menu')).toBeNull()
  })

  it('菜单内点「导出」：菜单收起并走既有导出链（携带模板名）', async () => {
    renderWorkbench()
    expect(await screen.findByText('Shelf-Tag')).toBeTruthy()
    mocks.server.exportTemplate.mockResolvedValue({ blob: new Blob(['pkg']), filename: 'Shelf-Tag.lfpkg' })

    fireEvent.click(within(cardOf('Shelf-Tag')).getByRole('button', { name: /更多操作/ }))
    fireEvent.click(within(screen.getByRole('menu')).getByRole('menuitem', { name: '导出' }))
    await waitFor(() => expect(mocks.server.exportTemplate).toHaveBeenCalledWith('Shelf-Tag'))
    expect(screen.queryByRole('menu')).toBeNull()
  })
})

// 删除 = 销毁类操作——菜单内删除项点击仍走既有确认 Modal
// （含「不可恢复」文案、确认按钮 danger），确认后才调删除接口。
describe('删除确认流（迭代 104 · #225）', () => {
  it('菜单内点「删除」→ 菜单收起 → 确认 Modal（不可恢复文案、danger 确认按钮）→ 确认后调删除接口', async () => {
    renderWorkbench()
    expect(await screen.findByText('Shelf-Tag')).toBeTruthy()

    fireEvent.click(within(cardOf('Shelf-Tag')).getByRole('button', { name: /更多操作/ }))
    fireEvent.click(within(screen.getByRole('menu')).getByRole('menuitem', { name: '删除' }))
    // 菜单已收起、确认框弹出
    expect(screen.queryByRole('menu')).toBeNull()
    expect(screen.getByRole('dialog')).toBeTruthy()
    expect(screen.getByText(/不可恢复/)).toBeTruthy()
    const confirmBtn = screen.getByRole('button', { name: /确认删除/ }) as HTMLButtonElement
    expect(confirmBtn.className).toContain('danger')

    mocks.server.deleteTemplate.mockResolvedValue(undefined)
    fireEvent.click(confirmBtn)
    await waitFor(() => expect(mocks.server.deleteTemplate).toHaveBeenCalledWith('Shelf-Tag'))
    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull())
  })

  it('确认框点「取消」（Esc / 遮罩同语义）不删模板', async () => {
    renderWorkbench()
    expect(await screen.findByText('Shelf-Tag')).toBeTruthy()

    fireEvent.click(within(cardOf('Shelf-Tag')).getByRole('button', { name: /更多操作/ }))
    fireEvent.click(within(screen.getByRole('menu')).getByRole('menuitem', { name: '删除' }))
    fireEvent.click(screen.getByRole('button', { name: '取消' }))
    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull())
    expect(mocks.server.deleteTemplate).not.toHaveBeenCalled()
  })
})
