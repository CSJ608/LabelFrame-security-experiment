// 应用框架：左侧主导航（tab 状态为渲染真源，hash 路由地基同步 URL——·）+ 底部状态栏 + 日志抽屉
// 双构建（VITE_UI_MODE）——server 构建菜单移除设置与打印机相关内容，新增「在线设备」
// 状态栏 server 显示服务端地址（同源）与 UI 模式、client 显示本机 IP。
// （决议 2「三名义」）：client 状态栏改呈现「本机打印服务：运行中 / 未运行」——
// 「服务端」一词不再兼指本机后台服务（评审 B-9）；服务端连通在设置页、加入状态在数据与打印页。
// 「PDA 日志 / 设备日志」页下线（回传链路不存在前界面收敛）——双形态导航入口移除。
// 导航切 tab 统一走 switchTab——设计器在编辑且有未保存更改时经其注册的离开守卫
// 弹三选确认（保存并离开 / 放弃更改 / 继续编辑），确认后才切换，防误触丢失排版工作。
// 日志抽屉「清空」升级实心红 danger＋点击先弹确认——
// 销毁类操作必须先确认（此前灰色 ghost 无确认直接执行），Esc / 遮罩点击默认取消。
// 壳层文案 key 化（样板迁移）——导航 / 状态栏 / 日志抽屉 / 清空确认弹窗全部走 t
// （`useTranslation('shell')`，common 词条经 fallbackNS 兜底），作为 111/112 页面迁移的 key 命名与用法样例；
// 本文件已圈入 lint 防线（oxlintrc.json overrides，裸中文 JSX 会被 oxlint 拦截）。
// nav-foot 语言切换器——循环单按钮「中 / EN」（当前语言可视，点击互切即点即生效）
// server 构建无设置页也能一步切换；与设置页语言卡同一 `changeLocale` 单点（双入口同状态源，互切一致）。
// 新用户首次使用引导（client 构建专属）——首见延迟 ~300ms 自动启动
// （lib/guide.ts 首见标记），跳过 / Esc / 完成均写标记；状态栏「使用引导」重放入口无视标记再放完整一轮；
// server 构建（dist-server）整特性不挂载（!isServerUi 条件渲染，V1 范围仅 client）。
// hash 路由地基——URL hash（#/<page>/<sub>）成为页面位置权威载体：
// 初始解析（直达 / 刷新保持）、hashchange（后退 / 前进 / 手动改 URL 经 switchTab 统一切页入口）、
// tab→hash 同步（守卫放行后 pushState 入栈）三路都经 lib/router.ts 的 useHashRoute。

import { useCallback, useEffect, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { changeLocale, toAppLocale } from './i18n'
import { AppProvider, useApp } from './state/AppContext'
import { Icon, LabelLogo } from './components/Icon'
import { Guide } from './components/Guide'
import { Modal } from './components/Modal'
import type { DesignerRequest, TabId } from './state/types'
import { isGuideSeen, markGuideSeen } from './lib/guide'
import { HELP_LINK_ELEMENTS } from './lib/help'
import { formatHash, parseHash, useHashRoute } from './lib/router'
import { CLIENT_TABS, SERVER_TABS } from './lib/tabs'
import { isServerUi } from './lib/uiMode'
import { Workbench } from './pages/Workbench'
import { Designer } from './pages/Designer'
import { DataPrint } from './pages/DataPrint'
import { Devices } from './pages/Devices'
import { JobHistory } from './pages/JobHistory'
import { Settings } from './pages/Settings'
import { Help } from './pages/Help'
import { DownloadCenter } from './pages/DownloadCenter'
import { PluginPackages } from './pages/PluginPackages'

// 导航元数据（label 经 shell 域 key 在渲染期求值——t() 绑定当前语言，切换即时生效）已迁 lib/tabs.ts
// 单源维护（·）——SERVER_TABS 末位含「帮助」，与 CLIENT_TABS 同位。

// 路由 page 段白名单 = 本构建 tab 集（server 构建解析 #/settings
// 即回退 workbench——受限页集不可经 hash 越权进入；#/help 随server tab 集纳入 help 反转为可达）
// 构建期常量，与 tab 元数据同源派生。
const ROUTE_ALLOWED: readonly TabId[] = (isServerUi ? SERVER_TABS : CLIENT_TABS).map((t) => t.id)

/** 状态栏多 IP 过长省略显示（title 给全量）。 */
function truncateIps(ips: string[], max = 28): string {
  const s = ips.join(', ')
  return s.length > max ? s.slice(0, max) + '…' : s
}

function Shell() {
  // 壳层绑定 shell 域命名空间；common 词条（取消等）经 fallbackNS 免前缀兜底
  // 解构 i18n 实例取当前语言（useTranslation 订阅 languageChanged，切换即重渲染）
  const { t, i18n } = useTranslation('shell')
  // tab 状态保持渲染真源，初始值读当前 hash（打开带 hash 的 URL
  // 直达对应页、刷新停留在当前页）；hash 由 useHashRoute 同步（见下方接线）。
  const [tab, setTab] = useState<TabId>(() => parseHash(window.location.hash, ROUTE_ALLOWED).page)
  const [designerReq, setDesignerReq] = useState<DesignerRequest | null>(null)
  // 日志抽屉「清空」的确认弹窗开关——点击先确认，确认后才清空
  const [confirmingClearLogs, setConfirmingClearLogs] = useState(false)
  // 首次引导开关——首见延迟自动启动，完成 / 跳过写标记后关闭
  const [guideOpen, setGuideOpen] = useState(false)
  // 设计器注册的离开守卫——设计器 tab 在编辑且有未保存更改时
  // 导航切 tab 交由守卫挂起（弹三选 Modal：保存并离开 / 放弃更改 / 继续编辑），确认后再切换。
  // 修复（取证）：守卫返回 boolean（false=挂起 / true=已直接放行）——switchTab 原样透传
  // 路由仅在真正挂起时 pre-revert；非 dirty 设计器放行后退不再被错误拉回（浏览器后退失效缺陷）。
  const designerLeaveRef = useRef<((leave: () => void) => boolean) | null>(null)
  const registerDesignerLeave = useCallback((fn: ((leave: () => void) => boolean) | null) => {
    designerLeaveRef.current = fn
  }, [])
  const app = useApp()
  // 解构出三个成员再入依赖——exhaustive-deps 按 `app` 聚合对象报缺依赖
  // 而三者均为 AppContext 的 useCallback 稳定引用（baseUrl 用于地址变更后重排周期探测），语义与原写法一致。
  const { checkConnection, checkLocalService, baseUrl } = app

  useEffect(() => {
    void checkConnection()
    // （决议 2「三名义」）：client 构建周期探测本机打印服务（页面来源 /healthz）——
    // 状态栏「本机打印服务：运行中 / 未运行」数据源，与服务端地址连通性（checkConnection）各自独立
    if (!isServerUi) void checkLocalService()
    // 周期探测连接（10s），后端重启后状态自动恢复
    const timer = setInterval(() => {
      void checkConnection()
      if (!isServerUi) void checkLocalService()
    }, 10000)
    return () => clearInterval(timer)
  }, [checkConnection, checkLocalService, baseUrl])

  const openDesigner = (req: DesignerRequest) => {
    setDesignerReq(req)
    setTab('designer')
  }

  // client 构建首见自动启动引导——延迟约 300ms 等壳层首个渲染帧稳定
  //（避开 AppContext 启动链的首次重渲染闪烁）；已看过不启动（存储异常视为未看过，引导每次出现为可接受降级）
  useEffect(() => {
    if (isServerUi || isGuideSeen()) return
    const timer = setTimeout(() => setGuideOpen(true), 300)
    return () => clearTimeout(timer)
    // 首见判定只在挂载时发生一次；isServerUi 为构建期常量、isGuideSeen 为模块级纯函数，均不入依赖
  }, [])

  // 引导退出（跳过 / Esc / 完成共用）：写入首见标记后关闭——重放路径重复写入幂等
  const finishGuide = useCallback(() => {
    markGuideSeen()
    setGuideOpen(false)
  }, [])

  const closeDesigner = () => {
    setDesignerReq(null)
    setTab('workbench')
  }

  // 导航切 tab 统一经此入口——设计器在编辑（dirty）时由其注册的守卫拦截：
  // 无未保存更改守卫直接放行，有则弹三选 Modal，用户选择后再执行本次切换。
  // 返回值扩展为 boolean（放行 true / 守卫挂起 false）供路由 hashchange 分支
  // 判定 pre-revert（挂起时页面与 hash 均不前进）；既有调用方（Guide / openHelpInUi 等）忽略返回值零影响。
  const switchTab = (id: TabId): boolean => {
    if (id === tab) return true
    if (tab === 'designer' && designerReq && designerLeaveRef.current) {
      // 透传守卫结论：false=挂起（路由 pre-revert）；true=守卫已直接放行（setTab 已执行，不得回退）
      return designerLeaveRef.current(() => setTab(id))
    }
    setTab(id)
    return true
  }

  // hash 路由接线——切页入历史栈、后退 / 前进经 switchTab（继承守卫）
  // 守卫取消 pre-revert；sub 段（下载中心页内 tab 首用）由 route 下传消费页。
  const route = useHashRoute({ allowed: ROUTE_ALLOWED, page: tab, requestPage: switchTab })

  // （C-4，决议 1）：工作台卡片「打印」直达——先把该模板写入打印草稿（与手动在下拉选择同一入口
  // 字段值 / 调试开关等草稿行为一致），再切到「数据与打印」页；两步内可开始填数据打印。
  const openPrintFromTemplate = (name: string) => {
    app.setDraftSelected(name)
    switchTab('data')
  }

  // （机制迁移）：帮助文章「去界面查看」深链＝hash 赋值导航——目标位置
  // #/<page>/<anchor> 经 hashchange 既有通道落位（页不同经 requestPage=switchTab 统一切页入口，
  // 继承设计器离开守卫与守卫挂起 pre-revert；router 零改动），落位后 setSubState 承载锚点、下传
  // 目标页页内轮询（lib/helpLink.ts）。sub 段驻留 URL（拍板 2-A）：分享 / 刷新 / 后退到该条目
  // 一致还原，主导航重进该页既有语义自动清 sub（入栈条目 sub 清空）不重触发。
  const openHelpInUi = (target: TabId, anchor: string) => {
    window.location.hash = formatHash(target, anchor)
  }

  // （拍板 A）：深链自动开草稿——设计器空态且路由 sub 为已登记的深链示例
  // 元素锚点时，经既有 openDesigner({kind:'new'}) 自动新建草稿（不落库零写请求；示例元素由
  // Designer 随 init 并入初始基线并选中）。已打开模板（designerReq 存在）不触发，走既有锚点
  // 路径。：撤 !isServerUi 门——server 构建与 client 同语义（设计器
  // 在 server 构建功能完整、深链可达，#178「静默空态」口径随之反转），双构建共用本 effect。
  useEffect(() => {
    if (tab !== 'designer' || designerReq) return
    // Object.hasOwn 防 'toString' 等原型链属性名经 sub 段合法进入后 in 判定为真、取值非数组致渲染崩溃（评审实证）
    if (!route.sub || !Object.hasOwn(HELP_LINK_ELEMENTS, route.sub)) return
    openDesigner({ kind: 'new' })
    // openDesigner 为本组件内闭包（setDesignerReq + setTab 稳定引用）；触发后 designerReq 置位即自停
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [tab, designerReq, route.sub])

  const tabs = (isServerUi ? SERVER_TABS : CLIENT_TABS).map(({ id, labelKey, icon }) => ({
    id,
    label: t(labelKey),
    icon,
  }))

  // 壳层语言切换器状态——当前语言短名（「中」/「EN」，语言自名不随语言翻译）
  // 与切换提示词条；点击经 changeLocale 单点互切（localStorage 持久化 + changeLanguage + <html lang> 同步）。
  const locale = toAppLocale(i18n.language)
  const switchToEn = locale === 'zh-CN'

  return (
    <div className="app">
      <div className="app-body">
        <nav className="nav" aria-label={t('nav.region')} data-guide="nav-main">
          <div className="nav-logo" title={t('appTitle')}>
            <LabelLogo size={24} />
          </div>
          <div className="nav-tabs">
            {tabs.map((item) => (
              <button
                key={item.id}
                className={'nav-tab' + (tab === item.id ? ' active' : '')}
                onClick={() => switchTab(item.id)}
                title={item.label}
              >
                <Icon name={item.icon} />
                <span>{item.label}</span>
              </button>
            ))}
          </div>
          {/* 迭代 80（#128 决议 2「三名义」）：client 构建指向「本机打印服务」（页面来源的本机后台服务），
              不再沿用「服务端」一词（评审 #114 B-9 一词三义）；server 构建维持服务端自身连通（第二义语境） */}
          <div
            className="nav-foot"
            title={
              isServerUi
                ? app.connected
                  ? t('conn.serverConnected')
                  : t('conn.serverDisconnected')
                : app.localServiceUp
                  ? t('statusbar.localServiceUp')
                  : t('statusbar.localServiceDown')
            }
          >
            <span className={'status-dot' + ((isServerUi ? app.connected : app.localServiceUp) ? ' on' : '')} />
            {/* 迭代 114（#260）：语言切换器（连接状态点旁）——server 构建无设置页的一步切换入口；
                按钮自身 title 覆盖容器连接提示（悬浮按钮时显示切换语义），两构建统一显示 */}
            <button
              type="button"
              className="nav-lang"
              title={t(switchToEn ? 'langSwitch.switchToEn' : 'langSwitch.switchToZh')}
              aria-label={t(switchToEn ? 'langSwitch.switchToEn' : 'langSwitch.switchToZh')}
              onClick={() => changeLocale(switchToEn ? 'en' : 'zh-CN')}
            >
              {t(switchToEn ? 'langSwitch.zhName' : 'langSwitch.enName')}
            </button>
          </div>
        </nav>

        <main className="main">
          {tab === 'workbench' && <Workbench onOpenDesigner={openDesigner} onOpenPrint={openPrintFromTemplate} />}
          {tab === 'designer' && designerReq && (
            <Designer
              key={designerReq.name ?? 'new'}
              request={designerReq}
              onClose={closeDesigner}
              registerLeaveGuard={registerDesignerLeave}
              helpAnchor={route.sub}
            />
          )}
          {tab === 'designer' && !designerReq && <DesignerEmpty onNew={() => openDesigner({ kind: 'new' })} />}
          {/* 迭代 85（#133 C-5）：数据与打印进度区的「作业历史」指引可点击跳转（经 switchTab 统一入口） */}
          {tab === 'data' && <DataPrint onOpenJobHistory={() => switchTab('jobs')} />}
          {tab === 'devices' && <Devices />}
          {tab === 'jobs' && <JobHistory />}
          {tab === 'packages' && <DownloadCenter sub={route.sub} onSubChange={route.setSub} />}
          {tab === 'plugin-packages' && <PluginPackages />}
          {tab === 'settings' && <Settings helpAnchor={route.sub} />}
          {/* 迭代 126（#308）：帮助页（文档底座）——迭代 129（#320）起双构建均挂载（help tab 在
              SERVER_TABS / CLIENT_TABS 均居末位；内容按构建页集适配见 lib/help.ts BUILD_HELP_PAGES） */}
          {tab === 'help' && <Help onOpenInUi={openHelpInUi} />}
        </main>
      </div>

      <footer className="statusbar">
        {/* 状态栏呈现本机打印服务运行状态——
            client 构建 = 页面来源的本机 WinHost；server 构建此段不渲染（下方 meta 显示服务端地址） */}
        {!isServerUi && (
          <span className={'conn' + (app.localServiceUp ? ' on' : ' off')}>
            <span className={'status-dot' + (app.localServiceUp ? ' on' : '')} />
            {app.localServiceUp ? t('statusbar.localServiceUp') : t('statusbar.localServiceDown')}
          </span>
        )}
        <span className="msg">{app.statusMsg}</span>
        <span className="meta">
          {isServerUi ? (
            // Server UI 状态栏显示服务端地址（页面 origin）与 UI 模式；无打印机相关内容
            // 「同源」开发者术语改为直接展示地址与服务端管理界面标识
            <span className="mono" title={window.location.origin}>
              {t('statusbar.serverAdminOrigin', { origin: window.location.origin })}
            </span>
          ) : (
            <>
              <span className="mono">{app.baseUrl}</span>
              {/* 迭代 20：客户端状态栏显示本机 IP（/api/host/config.ips，多 IP 逗号分隔全部）；
                  迭代 80：随「本机打印服务」运行状态显示（本机事实不依赖服务端地址连通性） */}
              {app.localServiceUp && app.hostIps.length > 0 && (
                <span className="mono" title={app.hostIps.join(', ')}>
                  {t('statusbar.localIps', { ips: truncateIps(app.hostIps) })}
                </span>
              )}
              {/* 迭代 22 §2.1：客户端状态栏显示本机设备名称（/api/host/config.deviceName，与本机 IP 并列） */}
              {app.localServiceUp && app.hostDeviceName && (
                <span className="mono">{t('statusbar.localDevice', { name: app.hostDeviceName })}</span>
              )}
            </>
          )}
          {/* 「使用引导」重看入口——「日志」旁常驻小图标 ghost 按钮，
              点击无视首见标记再放完整一轮（重放完成 / 跳过不改变已看状态）；server 构建不渲染 */}
          {!isServerUi && (
            <button
              className="btn sm ghost"
              title={t('guide:entry.title')}
              aria-label={t('guide:entry.title')}
              onClick={() => setGuideOpen(true)}
            >
              <Icon name="guide" size={13} />
            </button>
          )}
          <button className="btn sm ghost" onClick={() => app.setDrawerOpen(!app.drawerOpen)}>
            <Icon name="logs" size={13} />
            {t('statusbar.logs')}
          </button>
        </span>
      </footer>

      {/* 迭代 119（#276）：分步引导——server 构建整特性不挂载（V1 范围仅 client，决策 #63 双构建裁剪同口径） */}
      {!isServerUi && <Guide open={guideOpen} tab={tab} onSwitchTab={switchTab} onFinish={finishGuide} />}

      {app.drawerOpen && (
        <div className="log-drawer">
          <div className="log-head">
            <span>{t('logsDrawer.title')}</span>
            <span className="spacer" />
            {/* 迭代 104（#225，决策 #161）：清空 = 销毁类操作——实心红 danger＋先弹确认（原灰色 ghost 直执行） */}
            <button className="btn sm danger" onClick={() => setConfirmingClearLogs(true)}>
              <Icon name="clear" size={13} />
              {t('logsDrawer.clear')}
            </button>
            <button className="btn sm ghost" style={{ color: '#8b96a3' }} onClick={() => app.setDrawerOpen(false)}>
              {t('logsDrawer.collapse')}
            </button>
          </div>
          <div className="log-body">
            {app.logs.map((l, i) => (
              <div key={i}>
                <span className="t">{l.time}</span>
                {l.msg}
              </div>
            ))}
          </div>
        </div>
      )}

      {/* 迭代 104（#225）：清空日志确认——文案含「不可恢复」，Esc / 遮罩默认取消（通用 Modal 既有语义） */}
      {confirmingClearLogs && (
        <Modal
          title={t('clearLogsConfirm.title')}
          onClose={() => setConfirmingClearLogs(false)}
          footer={
            <>
              <button className="btn" onClick={() => setConfirmingClearLogs(false)}>
                {t('action.cancel')}
              </button>
              <button
                className="btn danger"
                onClick={() => {
                  app.clearLogs()
                  setConfirmingClearLogs(false)
                }}
              >
                <Icon name="trash" size={13} />
                {t('clearLogsConfirm.confirm')}
              </button>
            </>
          }
        >
          <p>{t('clearLogsConfirm.body')}</p>
        </Modal>
      )}
    </div>
  )
}

function DesignerEmpty({ onNew }: { onNew: () => void }) {
  const { t } = useTranslation('shell')
  return (
    <div className="page">
      <div className="page-head">
        <div className="page-title">{t('designerEmpty.title')}</div>
      </div>
      <div className="empty" style={{ flex: 1 }}>
        <Icon name="designer" />
        <div className="empty-title">{t('designerEmpty.untitled')}</div>
        <div className="hint">{t('designerEmpty.hint')}</div>
        {/* data-guide：首次引导第 2 步锚点（引导经 switchTab 切入 designer 时必为空态， 拍板） */}
        <button className="btn primary" data-guide="designer-empty-new" onClick={onNew}>
          <Icon name="plus" size={13} />
          {t('designerEmpty.newTemplate')}
        </button>
      </div>
    </div>
  )
}

export default function App() {
  return (
    <AppProvider>
      <Shell />
    </AppProvider>
  )
}
