// 帮助体系 V3 纯数据（迭代 126 · #308，决策 #176）：分界面文章的主题小节清单 +
// 文档点锚点 + 深链辅助。文案词条唯一来源是「✅ 文案定稿」写入的 help 域语言包
// （help.article.<page>.intro / help.topic.<page>.<id>），文章视图与「?」文档点气泡
// 共用同一词条（单源双视图，防双份漂移——拍板 5）。
//
// 锚点命名法与 lib/guide.ts 的 data-guide 对称（data-help 语义分离，决策 #176 ②）；
// 纯文章小节无 anchor——帮助页不渲染「去界面查看」深链按钮。
// 演示预设是显示层注入的示例值（mode/key/displayName/text），由 Designer 合并进
// viewElements，不写 stateRef / historyRef（零落库的结构性前提，AC-05）。
// 迭代 128（#316，决策 #178）：深链经路由 sub 段承载（#/designer/<anchor> 形，可分享 / 刷新
// 还原）——HELP_LINK_ELEMENTS 为深链自动开草稿的示例元素预设（拍板 A：随草稿 init 并入初始
// 基线，机制与演示预设的显示层注入不同，见 Designer.tsx 落点）；页内锚点轮询 hook 在
// lib/helpLink.ts（本文件保持纯数据，不引 react）。
// 迭代 129（#320，决策 #179）：server 管理界面纳入帮助体系——HELP_PAGES 收录双构建全部页面
// （i18n key 段 camelCase：pluginPackages 对齐 pluginPackages.json 域名先例，tab 字段仍用
// 'plugin-packages' 原 TabId），构建适配经 helpPagesFor 按当前构建主导航页集单源过滤（判据
// 「帮助文章页集 = 当前构建主导航页集」从结构上防漂移，无需逐条目标注模式）。

import i18next from 'i18next'
import type { TabId } from '../state/types'
import type { DesignElement } from './design/types'
import type { TabMeta } from './tabs'
import { CLIENT_TABS, SERVER_TABS } from './tabs'
import { isServerUi } from './uiMode'

/** 帮助文章覆盖的页面（双构建并集——client 五页 + server 独有三页；help 域词条按此分组，
 *  某构建实际渲染哪些页由 helpPagesFor 按构建 tab 集过滤）。 */
export type HelpPageId = 'workbench' | 'designer' | 'data' | 'jobs' | 'settings' | 'devices' | 'packages' | 'pluginPackages'

/** 主题小节定义（文章视图渲染单元；有 anchor 即文档点主题）。 */
export interface HelpTopic {
  /** 主题 id——词条 help.topic.<page>.<id> 的末段。 */
  id: string
  /** 就地文档点锚点值（[data-help] 属性）；缺省 = 纯文章小节（首波无文档点覆盖）。 */
  anchor?: string
  /** 深链目标 tab（与 anchor 成对出现）。 */
  target?: TabId
  /** 文档气泡内是否提供「看实时效果」演示按钮（首波仅设计器填充组，决策 #171 旗舰示例）。 */
  hasDemo?: boolean
}

/** 分界面文章（导览段词条固定 help.article.<page>.intro，小节清单见 topics）。 */
export interface HelpPageDoc {
  page: HelpPageId
  /** 该页对应的界面 tab（深链目标）。 */
  tab: TabId
  topics: readonly HelpTopic[]
}

export const HELP_PAGES: readonly HelpPageDoc[] = [
  {
    page: 'workbench',
    tab: 'workbench',
    topics: [
      { id: 'cards' },
      { id: 'actions' },
      { id: 'filter' },
    ],
  },
  {
    page: 'designer',
    tab: 'designer',
    topics: [
      { id: 'canvas' },
      { id: 'fieldsSide' },
      { id: 'testData' },
      { id: 'saveFlow' },
      { id: 'position', anchor: 'designer.position', target: 'designer' },
      { id: 'fill', anchor: 'designer.fill', target: 'designer', hasDemo: true },
      { id: 'box', anchor: 'designer.box', target: 'designer' },
      { id: 'text', anchor: 'designer.text', target: 'designer' },
      { id: 'barcode', anchor: 'designer.barcode', target: 'designer' },
      { id: 'qrcode', anchor: 'designer.qrcode', target: 'designer' },
      { id: 'rect', anchor: 'designer.rect', target: 'designer' },
      { id: 'line', anchor: 'designer.line', target: 'designer' },
      { id: 'image', anchor: 'designer.image', target: 'designer' },
      { id: 'compat', anchor: 'designer.compat', target: 'designer' },
      { id: 'align', anchor: 'designer.align', target: 'designer' },
    ],
  },
  {
    page: 'data',
    tab: 'data',
    topics: [
      { id: 'form' },
      { id: 'debug' },
      { id: 'excel' },
      { id: 'progress' },
    ],
  },
  {
    page: 'jobs',
    tab: 'jobs',
    topics: [
      { id: 'overview' },
      { id: 'detail' },
    ],
  },
  {
    page: 'settings',
    tab: 'settings',
    topics: [
      { id: 'language', anchor: 'settings.language', target: 'settings' },
      { id: 'serverAddress', anchor: 'settings.serverAddress', target: 'settings' },
      { id: 'transport', anchor: 'settings.transport', target: 'settings' },
      { id: 'batch', anchor: 'settings.batch', target: 'settings' },
      { id: 'printer', anchor: 'settings.printer', target: 'settings' },
      { id: 'updates', anchor: 'settings.updates', target: 'settings' },
      { id: 'plugins', anchor: 'settings.plugins', target: 'settings' },
    ],
  },
  // 迭代 129（#320）：server 独有三页（devices / packages / pluginPackages）——各 intro＋2 纯文章
  // 小节、无 anchor（设备 / 插件页为单表平页无锚点结构，下载中心 sub 段已被页内 tab 占用；
  // 与 workbench / data / jobs 纯文章先例一致，拍板 2-A）
  {
    page: 'devices',
    tab: 'devices',
    topics: [{ id: 'listStatus' }, { id: 'defaultTarget' }],
  },
  {
    page: 'packages',
    tab: 'packages',
    topics: [{ id: 'quick' }, { id: 'manage' }],
  },
  {
    page: 'pluginPackages',
    tab: 'plugin-packages',
    topics: [{ id: 'distribute' }, { id: 'manage' }],
  },
]

/** 页集过滤谓词（单源）：帮助文章页集 = 所给主导航 tab 集——settings 文章因 settings tab 不在
 *  SERVER_TABS 而 server 不出现，server 独有三页因 tab 不在 CLIENT_TABS 而 client 不出现；
 *  页集增减自动跟随，无需重复标注模式。 */
function pagesInTabs(tabs: readonly TabMeta[]): readonly HelpPageDoc[] {
  const tabIds = new Set(tabs.map((t) => t.id))
  return HELP_PAGES.filter((doc) => tabIds.has(doc.tab))
}

/** 按构建模式的帮助页集（迭代 129 · #320，决策 #179）：双参形态供建页集直测（Help.test.tsx
 *  数据自洽用例）与未来双集消费方；生产消费点用 BUILD_HELP_PAGES——形参分支使双 tab 集在两种
 *  产物中都被引用（摇树不可折叠，server 特征串 nav.devices 会渗入 client 产物），生产 bundle
 *  本函数无引用即被裁除。 */
export function helpPagesFor(isServer: boolean): readonly HelpPageDoc[] {
  return pagesInTabs(isServer ? SERVER_TABS : CLIENT_TABS)
}

/** 当前构建的帮助页集（构建期折叠出口）：模块级 isServerUi 三元与 App ROUTE_ALLOWED 同款折叠
 *  先例——client 产物摇树裁除 SERVER_TABS（AC-04 特征串四指标之 client nav.devices=0）。 */
export const BUILD_HELP_PAGES: readonly HelpPageDoc[] = pagesInTabs(isServerUi ? SERVER_TABS : CLIENT_TABS)

/** 字段显示名示例值（随界面语言：zh「库位」/ en "Bin"——#308 结项 wrapup 定夺；本文件唯一
 *  随语言取值的示例字段，消费点＝演示注入与深链示例构建（两处经同一预设联动，防漂移记录见 #178）。 */
export function helpSampleDisplayName(): string {
  return i18next.language?.startsWith('zh') ? '库位' : 'Bin'
}

/** 演示注入预设（键 = 文档点 anchor；当前仅填充组——字段填充三件套各给示例值；displayName
 *  随语言经 helpSampleDisplayName() 在消费点补入，不静态写死）。 */
export const HELP_DEMO_PRESETS: Record<string, Partial<DesignElement>> = {
  'designer.fill': { mode: 'field', key: 'location', text: 'A-01-02' },
}

/** 深链示例元素预设（迭代 128 · #316，决策 #178 拍板 A）：帮助深链自动开草稿时按锚点小节类型
 *  放置的示例元素——以 spec（type + patch）登记而非现成元素，构造时经 defaultElement 生成新 id
 *  （跨草稿不串号）且默认文案随当前语言。fill 复用 HELP_DEMO_PRESETS 同款示例值（「库位」en
 *  示例值定夺属 #308 结项 wrapup——若改值两处须联动，#178 已记防漂移）；align 给两个 Rect 并
 *  错开位置（对齐组仅多选分支渲染，双选中锚点才现）。键集登记校验（须为 HELP_PAGES 已登记
 *  anchor、设计器侧全覆盖）在 Help.test.tsx 数据自洽用例断言。 */
export interface HelpLinkElementSpec {
  type: DesignElement['type']
  patch?: Partial<DesignElement>
}

export const HELP_LINK_ELEMENTS: Record<string, readonly HelpLinkElementSpec[]> = {
  'designer.position': [{ type: 'Text' }],
  'designer.fill': [{ type: 'Text', patch: HELP_DEMO_PRESETS['designer.fill'] }],
  'designer.box': [{ type: 'Text' }],
  'designer.text': [{ type: 'Text' }],
  'designer.barcode': [{ type: 'Barcode' }],
  'designer.qrcode': [{ type: 'QrCode' }],
  'designer.rect': [{ type: 'Rect' }],
  'designer.line': [{ type: 'Line' }],
  // 图片（迭代 132 · #329）：示例给 key 不给资源——深链草稿呈现「缺资源占位」形态，
  // 与属性面板「图片资源缺失」提示配套（真实图片经控件栏上传获得）
  'designer.image': [{ type: 'Image', patch: { key: 'sample.png' } }],
  'designer.compat': [{ type: 'Region' }],
  'designer.align': [{ type: 'Rect' }, { type: 'Rect', patch: { x: 30, y: 25 } }],
}

/** 文档点锚点的 DOM 查询选择器（data-help 语义属性，与 guideAnchorSelector 同法对称）。 */
export function helpAnchorSelector(anchor: string): string {
  return `[data-help="${anchor}"]`
}

/** 深链跳转后等待锚点就位的轮询间隔与上限（口径对齐 Guide.tsx 的 ANCHOR_POLL_*）。 */
export const HELP_ANCHOR_POLL_MS = 80
export const HELP_ANCHOR_POLL_MAX_MS = 4000
