// 应用壳：左侧主导航（tab 状态为渲染真源，hash 路由同步 URL）+ 底部状态栏 + 日志抽屉。
// 双构建：server 构建菜单移除设置与打印机相关内容、新增在线设备等专属页。
// 文案全部 key 化（本文件已圈入 oxlint 裸中文 JSX 防线——新增文案必须走 t() 词条，不能直接写中文）。

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
import { startHostActivity } from './lib/hostLifecycle'
import { ClientUpdateNotice } from './components/ClientUpdates'

// 路由 page 段白名单 = 本构建 tab 集（受限页集不可经 hash 越权进入）
const ROUTE_ALLOWED: readonly TabId[] = (isServerUi ? SERVER_TABS : CLIENT_TABS).map((t) => t.id)

/** 状态栏多 IP 过长省略显示（title 给全量）。 */
function truncateIps(ips: string[], max = 28): string {
  const s = ips.join(', ')
  return s.length > max ? s.slice(0, max) + '…' : s
}

function Shell() {
  const { t, i18n } = useTranslation('shell')
  // tab 状态为渲染真源，初始值读当前 hash（直达 / 刷新保持）
  const [tab, setTab] = useState<TabId>(() => parseHash(window.location.hash, ROUTE_ALLOWED).page)
  const [designerReq, setDesignerReq] = useState<DesignerRequest | null>(null)
  // 日志「清空」确认弹窗开关
  const [confirmingClearLogs, setConfirmingClearLogs] = useState(false)
  // 首次引导开关
  const [guideOpen, setGuideOpen] = useState(false)
  // 设计器离开守卫：dirty 时挂起切页弹三选；返回 false=挂起（路由 pre-revert）、
  // true=已直接放行（setTab 已执行，不得回退——否则非 dirty 设计器放行后浏览器后退失效）
  const designerLeaveRef = useRef<((leave: () => void) => boolean) | null>(null)
  const registerDesignerLeave = useCallback((fn: ((leave: () => void) => boolean) | null) => {
    designerLeaveRef.current = fn
  }, [])
  const app = useApp()
  useEffect(() => startHostActivity(), [])
  // 解构出三个成员再入依赖（exhaustive-deps 按 app 聚合对象报缺依赖；三者均为 useCallback 稳定引用）
  const { checkConnection, checkLocalService, baseUrl } = app

  useEffect(() => {
    void checkConnection()
    if (!isServerUi) void checkLocalService()
    // 周期探测（10s）：后端重启后状态自动恢复
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

  // client 构建首见自动启动引导：延迟约 300ms 等壳层首个渲染帧稳定（避开启动链首次重渲染闪烁）；
  // 存储异常视为未看过（引导每次出现为可接受降级）
  useEffect(() => {
    if (isServerUi || isGuideSeen()) return
    const timer = setTimeout(() => setGuideOpen(true), 300)
    return () => clearTimeout(timer)
  }, [])

  // 引导退出（跳过 / Esc / 完成共用）：写首见标记后关闭（重放路径重复写入幂等）
  const finishGuide = useCallback(() => {
    markGuideSeen()
    setGuideOpen(false)
  }, [])

  const closeDesigner = () => {
    setDesignerReq(null)
    setTab('workbench')
  }

  // 导航切 tab 统一入口：设计器 dirty 时经其守卫挂起（弹三选），确认后执行切换
  const switchTab = (id: TabId): boolean => {
    if (id === tab) return true
    if (tab === 'designer' && designerReq && designerLeaveRef.current) {
      return designerLeaveRef.current(() => setTab(id))
    }
    setTab(id)
    return true
  }

  const route = useHashRoute({ allowed: ROUTE_ALLOWED, page: tab, requestPage: switchTab })

  // 工作台卡片「打印」直达：先把该模板写入打印草稿（与手动选择同入口），再切到「数据与打印」页
  const openPrintFromTemplate = (name: string) => {
    app.setDraftSelected(name)
    switchTab('data')
  }

  // 帮助文章「去界面查看」深链 = hash 赋值导航（#/page/anchor 经 hashchange 既有通道落位，router 零改动）
  const openHelpInUi = (target: TabId, anchor: string) => {
    window.location.hash = formatHash(target, anchor)
  }

  // 深链自动开草稿：设计器空态且路由 sub 为已登记的深链示例锚点时自动新建草稿（不落库零写请求）；
  // server 构建同语义（设计器在 server 构建功能完整、深链可达）
  useEffect(() => {
    if (tab !== 'designer' || designerReq) return
    // Object.hasOwn 防 'toString' 等原型链属性名经 sub 段合法进入后 in 判定为真、取值非数组致渲染崩溃
    if (!route.sub || !Object.hasOwn(HELP_LINK_ELEMENTS, route.sub)) return
    openDesigner({ kind: 'new' })
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [tab, designerReq, route.sub])

  const tabs = (isServerUi ? SERVER_TABS : CLIENT_TABS).map(({ id, labelKey, icon }) => ({
    id,
    label: t(labelKey),
    icon,
  }))

  // 语言切换器状态：当前语言短名（「中」/「EN」，语言自名不随语言翻译），点击经 changeLocale 单点互切
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
          {/* client 构建指向「本机打印服务」（页面来源的本机后台服务），
              不再沿用「服务端」一词；server 构建维持服务端自身连通 */}
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
            {/* 语言切换器（连接状态点旁）——server 构建无设置页的一步切换入口；
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
          {/* 数据与打印页的「作业历史」指引可点击跳转 */}
          {tab === 'data' && <DataPrint onOpenJobHistory={() => switchTab('jobs')} />}
          {tab === 'devices' && <Devices />}
          {tab === 'jobs' && <JobHistory />}
          {tab === 'packages' && <DownloadCenter sub={route.sub} onSubChange={route.setSub} />}
          {tab === 'plugin-packages' && <PluginPackages />}
          {tab === 'settings' && <Settings helpAnchor={route.sub} />}
          {/* 帮助页（文档底座）——双构建均挂载；
              内容按构建页集适配（lib/help.ts BUILD_HELP_PAGES） */}
          {tab === 'help' && <Help onOpenInUi={openHelpInUi} />}
        </main>
      </div>

      {!isServerUi && <ClientUpdateNotice />}
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
            // Server UI 状态栏显示服务端地址（页面 origin）与 UI 模式
            <span className="mono" title={window.location.origin}>
              {t('statusbar.serverAdminOrigin', { origin: window.location.origin })}
            </span>
          ) : (
            <>
              <span className="mono">{app.baseUrl}</span>
              {/* 本机 IP（/api/host/config.ips，多 IP 逗号分隔全部）；
                  随「本机打印服务」运行状态显示（本机事实不依赖服务端地址连通性） */}
              {app.localServiceUp && app.hostIps.length > 0 && (
                <span className="mono" title={app.hostIps.join(', ')}>
                  {t('statusbar.localIps', { ips: truncateIps(app.hostIps) })}
                </span>
              )}
              {/* 本机设备名称（/api/host/config.deviceName，与本机 IP 并列） */}
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

      {/* 分步引导：server 构建整特性不挂载 */}
      {!isServerUi && <Guide open={guideOpen} tab={tab} onSwitchTab={switchTab} onFinish={finishGuide} />}

      {app.drawerOpen && (
        <div className="log-drawer">
          <div className="log-head">
            <span>{t('logsDrawer.title')}</span>
            <span className="spacer" />
            {/* 清空 = 销毁类操作：danger 样式 + 先弹确认 */}
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

      {/* 清空日志确认：文案含「不可恢复」，Esc / 遮罩默认取消 */}
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
        {/* data-guide：首次引导第 2 步锚点 */}
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
