// @vitest-environment jsdom
// 帮助页 V3.1（迭代 128 · #316 AC-02）：左目录树版式测试——两级目录树（根＋五页＋当前页小节手风琴）、
// 目录点击导航（页节点进文章 / 小节节点 scrollIntoView 跳转）、滚动高亮（rect mock 驱动）、窄窗目录
// 收起 / 展开；深链回调改 hash 断言形态（#316 机制迁移：onOpenInUi 由 Shell 以 hash 赋值实现）；
// 功能索引 / 五界面文章 / 词条单源 / 数据自洽（HELP_LINK_ELEMENTS 登记校验）维持。

import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen, within } from '@testing-library/react'
import { Help } from './Help'
import { HELP_LINK_ELEMENTS, HELP_PAGES, helpPagesFor } from '../lib/help'
import { formatHash } from '../lib/router'
import type { TabId } from '../state/types'
// 组件级单测不经 App 引用链，显式 import 初始化 i18next 单例（语言包资源就位，词条断言可用）
import '../i18n'

// 迭代 129（#320）：本文件为 client 构建语义用例（五页集 / client 副标题 / settings 卡片），
// 显式注入 client 分支——VITE_UI_MODE=server 整仓批次下 BUILD_HELP_PAGES 与副标题词条保持
// client 折叠（循 Guide.test.tsx / JobHistory.test.tsx 先例）；server 页集纯函数面由本文件
// 「构建页集过滤」用例覆盖，server 构建挂点守门见 HelpDot.server.test.tsx。
vi.mock('../lib/uiMode', () => ({ UI_MODE: 'client', isServerUi: false }))

afterEach(() => {
  cleanup()
  // hash 断言用例残留清理（jsdom location 跨用例共享）
  window.location.hash = ''
  vi.restoreAllMocks()
  vi.unstubAllGlobals()
})

/** 以「真实接线形态」渲染：onOpenInUi 按 Shell 现实现（hash 赋值导航）注入。 */
function renderHelp() {
  const onOpenInUi = (tab: TabId, anchor: string) => {
    window.location.hash = formatHash(tab, anchor)
  }
  render(<Help onOpenInUi={onOpenInUi} />)
}

describe('帮助页功能索引与文章（AC-02 维持面）', () => {
  it('五界面索引卡齐全，标题与页面副标题渲染（目录树页节点与索引卡同词条并存）', () => {
    renderHelp()
    expect(screen.getByText('帮助')).toBeTruthy()
    expect(screen.getByText('五个页面的使用文章；界面内「?」就近说明')).toBeTruthy()
    for (const title of ['工作台', '设计器', '数据与打印', '作业历史', '设置']) {
      expect(screen.getAllByText(title).length).toBeGreaterThanOrEqual(2) // 目录页节点＋索引卡
    }
  })

  it('client 构建负断言（迭代 129 · #320）：server 独有三页索引卡与 server 副标题不出现', () => {
    renderHelp()
    for (const serverOnly of ['在线设备', '下载中心', '插件管理']) {
      expect(screen.queryByText(serverOnly)).toBeNull()
    }
    expect(screen.queryByText('七个页面的使用文章；界面内「?」就近说明')).toBeNull()
  })

  it('点击索引卡进入对应文章（工作台篇：导览段＋三个纯文章小节、无「去界面查看」）', () => {
    renderHelp()
    fireEvent.click(screen.getByText('新建、导入模板并按分组管理'))
    expect(screen.getByText(/工作台是模板的起点/)).toBeTruthy()
    const article = document.querySelector('.help-article') as HTMLElement
    expect(article).toBeTruthy()
    for (const title of ['模板卡片与缩略图', '卡片操作', '搜索与分组过滤']) {
      expect(within(article).getByText(title)).toBeTruthy()
    }
    expect(screen.queryByRole('button', { name: /去界面查看/ })).toBeNull()
  })

  it('设计器文章：文档点小节带「去界面查看」深链（hash 断言形态）；纯文章小节无按钮', () => {
    renderHelp()
    fireEvent.click(screen.getByText('在画布上设计标签版式'))
    // 词条与文档点气泡同源（单源双视图）：正文与 help.topic.designer.fill 一致
    expect(screen.getByText(/「来源」决定打印内容：固定值立即按所填内容渲染/)).toBeTruthy()
    // 纯文章 4 节无深链；文档点 11 节有深链（迭代 132 · #329 增 designer.image）
    const openButtons = screen.getAllByRole('button', { name: /去界面查看/ })
    expect(openButtons.length).toBe(11)
    fireEvent.click(openButtons[0])
    // 机制迁移（#316）：深链 = hash 赋值 #/<page>/<anchor>（Shell openHelpInUi 现实现同法）
    expect(window.location.hash).toBe('#/designer/designer.position')
  })

  it('设置文章：7 个卡片主题全部可深链（hash 形态）；返回索引回到功能索引视图', () => {
    renderHelp()
    fireEvent.click(screen.getByText('语言、服务端地址、打印机与插件等本机配置'))
    const openButtons = screen.getAllByRole('button', { name: /去界面查看/ })
    expect(openButtons.length).toBe(7)
    fireEvent.click(openButtons[6])
    expect(window.location.hash).toBe('#/settings/settings.plugins')
    fireEvent.click(screen.getByRole('button', { name: /返回索引/ }))
    expect(screen.getByText('五个页面的使用文章；界面内「?」就近说明')).toBeTruthy()
  })

  it('主题清单纯数据自洽：有 anchor 必有 target，且 anchor = <page>.<id>（深链与文档点同键）', () => {
    for (const page of HELP_PAGES) {
      for (const topic of page.topics) {
        if (topic.anchor) {
          expect(topic.target).toBe(page.tab)
          expect(topic.anchor).toBe(`${page.page}.${topic.id}`)
        } else {
          expect(topic.target).toBeUndefined()
        }
      }
    }
  })

  it('深链示例元素预设登记自洽：键全为 HELP_PAGES 已登记锚点，设计器侧锚点全覆盖', () => {
    const knownAnchors = new Set(HELP_PAGES.flatMap((p) => p.topics.flatMap((topic) => (topic.anchor ? [topic.anchor] : []))))
    for (const key of Object.keys(HELP_LINK_ELEMENTS)) {
      expect(knownAnchors.has(key)).toBe(true) // 登记键必须是真实文档点锚点（派生校验）
    }
    for (const key of knownAnchors) {
      if (key.startsWith('designer.')) expect(key in HELP_LINK_ELEMENTS).toBe(true) // 设计器全覆盖
    }
    // align 双元素（对齐组多选分支渲染）；fill 复用演示预设示例值（#178 两处联动口径）
    expect(HELP_LINK_ELEMENTS['designer.align'].length).toBe(2)
    expect(HELP_LINK_ELEMENTS['designer.fill'][0].patch).toHaveProperty('key', 'location')
  })

  it('构建页集过滤（迭代 129 · #320，决策 #179）：client=五页含 settings、server=七页不含 settings，双集均不含对方独有页', () => {
    const client = helpPagesFor(false).map((p) => p.page)
    const server = helpPagesFor(true).map((p) => p.page)
    expect(client).toEqual(['workbench', 'designer', 'data', 'jobs', 'settings'])
    expect(server).toEqual(['workbench', 'designer', 'data', 'jobs', 'devices', 'packages', 'pluginPackages'])
    // server 独有三页不进 client 页集；settings 文章不进 server 页集（按主导航 tab 集单源过滤）
    for (const serverOnly of ['devices', 'packages', 'pluginPackages'] as const) {
      expect(client).not.toContain(serverOnly)
    }
    expect(server).not.toContain('settings')
  })
})

describe('帮助页左目录树（AC-02 新版式）', () => {
  it('两级目录树：根「功能索引」＋五页节点；当前文章页展开小节、其余页收起（手风琴）', () => {
    renderHelp()
    const toc = document.querySelector('.help-toc') as HTMLElement
    expect(toc).toBeTruthy()
    expect(toc.getAttribute('aria-label')).toBe('目录')
    // 根节点（索引视图选中态）
    const rootBtn = screen.getByRole('button', { name: '功能索引' })
    expect(rootBtn.className).toContain('active')
    // 索引视图：任何页的小节都不展开
    expect(toc.querySelector('.help-toc-sections')).toBeNull()

    // 进设计器文章：仅设计器页展开 15 个小节（词条与正文 /「?」气泡同源；#329 增图片小节）
    fireEvent.click(screen.getByText('在画布上设计标签版式'))
    const sections = toc.querySelectorAll('.help-toc-item.section')
    expect(sections.length).toBe(15)
    expect(within(toc).getAllByText('填充：固定值或字段填充').length).toBe(1)
  })

  it('目录导航：页节点进对应文章；小节节点滚动到目标 section（scrollIntoView mock）', () => {
    const scrollIntoView = vi.fn()
    Element.prototype.scrollIntoView = scrollIntoView
    renderHelp()

    // 根节点回索引视图（文章态点击根节点 = 返回索引卡）
    fireEvent.click(screen.getByText('在画布上设计标签版式'))
    fireEvent.click(screen.getByRole('button', { name: '功能索引' }))
    expect(screen.getByText('五个页面的使用文章；界面内「?」就近说明')).toBeTruthy()

    // 页节点：直接进文章
    const toc = document.querySelector('.help-toc') as HTMLElement
    fireEvent.click(within(toc).getByText('作业历史'))
    expect(screen.getByText(/作业历史集中查看打印结果/)).toBeTruthy()

    // 小节节点：滚动到目标 section（activeTopic 变更驱动渲染，effect 消费待跳转）
    fireEvent.click(within(toc).getByText('设计器'))
    expect(screen.getByText(/设计器编辑标签版式/)).toBeTruthy()
    scrollIntoView.mockClear()
    fireEvent.click(within(toc).getByText('矩形')) // designer.rect 小节
    expect(scrollIntoView).toHaveBeenCalledTimes(1)
    expect(document.querySelector('[data-topic-id="rect"]')).toBeTruthy()

    // 同文章内小节点击同样滚动（不切换文章）
    scrollIntoView.mockClear()
    fireEvent.click(within(toc).getByText('容器')) // designer.compat 小节（#329 收敛为仅容器）
    expect(scrollIntoView).toHaveBeenCalledTimes(1)
  })

  it('滚动高亮：主区滚动以 section 视口位置判定当前节，目录对应项加 active / aria-current', () => {
    renderHelp()
    fireEvent.click(screen.getByText('在画布上设计标签版式'))
    // rect mock：主容器 top 0（阈值 96）；canvas/fieldsSide 在阈值上方以下、fill 已滚过、其余在下方未到
    const tops: Record<string, number> = { canvas: 50, fieldsSide: 20, fill: -200 }
    vi.spyOn(HTMLElement.prototype, 'getBoundingClientRect').mockImplementation(function (this: HTMLElement) {
      const id = (this as HTMLElement).dataset?.topicId
      return { top: id !== undefined ? (tops[id] ?? 500) : 0 } as DOMRect
    })
    const main = document.querySelector('.help-main') as HTMLElement
    fireEvent.scroll(main)
    const toc = document.querySelector('.help-toc') as HTMLElement
    const activeItem = toc.querySelector('.help-toc-item.section.active') as HTMLElement
    expect(activeItem?.getAttribute('aria-current')).toBe('true')
    expect(activeItem?.textContent).toBe('填充：固定值或字段填充')
  })

  it('窄窗目录收起 / 展开：初始收起（innerWidth 判定），切换按钮 aria-expanded 跟随', () => {
    Object.defineProperty(window, 'innerWidth', { value: 800, configurable: true })
    renderHelp()
    const toggle = screen.getByRole('button', { name: '目录' })
    expect(toggle.getAttribute('aria-expanded')).toBe('false')
    expect(document.querySelector('.help-toc')?.className).not.toContain('open')
    fireEvent.click(toggle)
    expect(toggle.getAttribute('aria-expanded')).toBe('true')
    expect(document.querySelector('.help-toc')?.className).toContain('open')
  })
})
