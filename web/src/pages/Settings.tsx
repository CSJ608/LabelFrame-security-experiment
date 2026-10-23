// 设置页：分节左菜单＋右内容（帮助页文档站版式同构）——左列四个分节导航（常规设置 / 打印设置 /
// 更新与安装包 / 插件管理），右区仅渲染选中分节；分节内 h3 子标题分层不折叠（打印设置：状态总览卡
// 置顶 → 连接配置 → 作业配置 → 测试打印）。常规设置＝界面语言＋服务端地址（机器级配置，保存即生效）。

import { useCallback, useEffect, useState } from 'react'
import { ClientRestart } from '../components/ClientRestart'
import { ClientUpdates } from '../components/ClientUpdates'
import { beginHostOperation, setHostDirty } from '../lib/hostLifecycle'
import { useTranslation } from 'react-i18next'
import { clientPackageDownloadUrl, localApi, serverApi } from '../lib/api/client'
import { ApiError } from '../lib/api/types'
import type { ClientPackageInfo, InstalledPluginInfo, PluginPackageInfo, PrinterStatus, PrintSettings } from '../lib/api/types'
import { formatSize } from '../lib/download'
import { checkForUpdate } from '../lib/update'
import { formatTransport } from '../lib/transport'
import { printerStatusMessage } from '../lib/printerStatusText'
import { pluginPackageTooLarge } from '../lib/pluginLimits'
import { pluginPackageName } from '../lib/pluginText'
import { APP_LOCALES, changeLocale, toAppLocale } from '../i18n'
import type { AppLocale } from '../i18n'
import { useApp } from '../state/AppContext'
import { Icon } from '../components/Icon'
import { Modal } from '../components/Modal'
import { HelpDot } from '../components/HelpDot'
import { TransportPanel } from '../components/TransportPanel'
import { useHelpDeepLink } from '../lib/helpLink'
import { isServerUi } from '../lib/uiMode'

/** 分节清单：顺序即左菜单顺序；titleKey 复用各面板头词条（菜单与面板标题单源）。 */
const SETTINGS_SECTIONS = [
  { id: 'general', titleKey: 'general.title' },
  { id: 'printing', titleKey: 'printing.title' },
  { id: 'updates', titleKey: 'updates.title' },
  { id: 'plugins', titleKey: 'plugins.title' },
] as const

type SettingsSectionId = (typeof SETTINGS_SECTIONS)[number]['id']

/* * 深链锚点 → 分节映射：锚点元素（「?」文档点）仅当选中分节渲染——深链到达须先切到对应分节，
 * 轮询才能命中（未登记锚点不受影响，走通用超时降级）。 */
const ANCHOR_TO_SECTION: Record<string, SettingsSectionId> = {
  'settings.language': 'general',
  'settings.serverAddress': 'general',
  'settings.transport': 'printing',
  'settings.batch': 'printing',
  'settings.printer': 'printing',
  'settings.updates': 'updates',
  'settings.plugins': 'plugins',
}

export function Settings({ helpAnchor }: { helpAnchor?: string | null }) {
  const app = useApp()
  const { t, i18n } = useTranslation('settings')
  // 文档点「?」：dot 在 server 构建返回 null——settings 为 client-only 页（server 构建本不可达，
  // 门仅防手造 URL 等边缘）；深链 helpAnchor 页内轮询（useHelpDeepLink）锚点常在即刻命中。
  const [openDot, setOpenDot] = useState<string | null>(null)
  useHelpDeepLink(helpAnchor, {
    onFound: () => {
      setOpenDot(helpAnchor ?? null)
      app.setStatus('')
    },
    onTimeout: () => app.setStatus(t('help:deepLink.timeout')),
    onPending: () => app.setStatus(t('help:deepLink.pending')),
  })
  const toggleDot = (anchor: string) => setOpenDot((prev) => (prev === anchor ? null : anchor))
  const dot = (anchor: string) =>
    isServerUi ? null : (
      <HelpDot
        anchor={anchor}
        topicKey={`topic.${anchor}`}
        open={openDot === anchor}
        onToggle={() => toggleDot(anchor)}
        onClose={() => setOpenDot(null)}
      />
    )
  const [url, setUrl] = useState(app.baseUrl)
  // 分节导航：右区仅渲染选中分节（默认打印设置——最高频调整项）；帮助深链锚点映射分节，到达即自动选中
  const [section, setSection] = useState<SettingsSectionId>(() => (helpAnchor && ANCHOR_TO_SECTION[helpAnchor]) || 'printing')
  useEffect(() => {
    if (!helpAnchor) return
    const target = ANCHOR_TO_SECTION[helpAnchor]
    if (target) setSection(target)
  }, [helpAnchor])
  const [testing, setTesting] = useState(false)
  const [saving, setSaving] = useState(false)
  const [testResult, setTestResult] = useState<{ ok: boolean; msg: string } | null>(null)
  const [saveResult, setSaveResult] = useState<{ ok: boolean; msg: string } | null>(null)
  const [printer, setPrinter] = useState<PrinterStatus | null>(null)
  const [printerLoading, setPrinterLoading] = useState(false)
  const [testPrinting, setTestPrinting] = useState(false)
  // 测试打印结果（ok 标志判别——本地化后不得依赖文案前缀匹配）
  const [printResult, setPrintResult] = useState<{ ok: boolean; msg: string } | null>(null)

  // 更新与安装包——服务端可达时拉取安装包列表；不可达提示需先连接服务端
  const [packages, setPackages] = useState<ClientPackageInfo[] | null>(null)
  const [packagesError, setPackagesError] = useState<string | null>(null)

  // 插件管理——服务端可用插件区（四态：加载中 / 空 / 错误[含旧 Server 404] / 单机模式）
  const [pluginPackages, setPluginPackages] = useState<PluginPackageInfo[] | null>(null)
  const [pluginPackagesError, setPluginPackagesError] = useState<string | null>(null)
  const [pluginPackagesOldServer, setPluginPackagesOldServer] = useState(false)
  // 已安装插件区（始终渲染，不依赖服务端；旧 WinHost 404 → 版本提示）
  const [installedPlugins, setInstalledPlugins] = useState<InstalledPluginInfo[] | null>(null)
  const [installedPluginsError, setInstalledPluginsError] = useState<string | null>(null)
  const [installedOldWinHost, setInstalledOldWinHost] = useState(false)
  const [installedPluginsLoading, setInstalledPluginsLoading] = useState(false)
  // 行内操作态与提示
  const [installing, setInstalling] = useState<string | null>(null)
  const [uninstalling, setUninstalling] = useState<string | null>(null)
  const [pluginNotice, setPluginNotice] = useState<string | null>(null)
  const [pluginError, setPluginError] = useState<string | null>(null)
  // 插件覆盖安装 / 卸载确认（自研 Modal，不用原生 confirm）
  const [confirmOverwrite, setConfirmOverwrite] = useState<PluginPackageInfo | null>(null)
  const [confirmUninstall, setConfirmUninstall] = useState<InstalledPluginInfo | null>(null)

  // 打印批次——WinHost /api/host/print-settings（用户级持久化，保存即生效）；旧 WinHost 404 → 版本提示
  const [printSettings, setPrintSettings] = useState<PrintSettings | null>(null)
  const [savedPrintSettings, setSavedPrintSettings] = useState('')
  const [printSettingsError, setPrintSettingsError] = useState<string | null>(null)
  const [printSettingsOldWinHost, setPrintSettingsOldWinHost] = useState(false)
  const [batchSaving, setBatchSaving] = useState(false)
  const [batchSaveResult, setBatchSaveResult] = useState<{ ok: boolean; msg: string } | null>(null)

  useEffect(() => {
    if (!app.connected) {
      setPackages(null)
      setPackagesError(null)
      return
    }
    let on = true
    serverApi
      .listClientPackages()
      .then((list) => {
        if (on) {
          setPackages(list)
          setPackagesError(null)
        }
      })
      .catch((err) => {
        if (on) {
          setPackages([])
          setPackagesError(err instanceof ApiError ? err.message : t('updates.loadFailed'))
        }
      })
    return () => {
      on = false
    }
  }, [app.connected, t])

  // 可用插件区：服务端可达时拉取（与「更新与安装包」同构；404 = 旧 Server 无此端点）
  useEffect(() => {
    if (!app.connected) {
      setPluginPackages(null)
      setPluginPackagesError(null)
      setPluginPackagesOldServer(false)
      return
    }
    let on = true
    serverApi
      .listPluginPackages()
      .then((list) => {
        if (on) {
          setPluginPackages(list)
          setPluginPackagesError(null)
          setPluginPackagesOldServer(false)
        }
      })
      .catch((err) => {
        if (on) {
          setPluginPackages([])
          setPluginPackagesError(err instanceof ApiError ? err.message : t('plugins.loadFailed'))
          setPluginPackagesOldServer(err instanceof ApiError && err.code === 'HTTP_404')
        }
      })
    return () => {
      on = false
    }
  }, [app.connected, t])

  // 已安装插件区：挂载即拉（单机模式下也可查看 / 卸载）；安装 / 卸载成功后刷新
  const refreshInstalledPlugins = useCallback(async () => {
    setInstalledPluginsLoading(true)
    try {
      setInstalledPlugins(await localApi.listInstalledPlugins())
      setInstalledPluginsError(null)
      setInstalledOldWinHost(false)
    } catch (err) {
      setInstalledPlugins([])
      setInstalledPluginsError(err instanceof ApiError ? err.message : t('plugins.installedLoadFailed'))
      setInstalledOldWinHost(err instanceof ApiError && err.code === 'HTTP_404')
    } finally {
      setInstalledPluginsLoading(false)
    }
  }, [t])

  useEffect(() => {
    void refreshInstalledPlugins()
  }, [refreshInstalledPlugins])

  // 打印批次：挂载即拉（WinHost 专属端点；404 = 旧客户端无此端点 → 版本提示，不渲染表单）
  useEffect(() => {
    let on = true
    localApi
      .getPrintSettings()
      .then((s) => {
        if (on) {
          setPrintSettings(s)
          setSavedPrintSettings(JSON.stringify(s))
          setPrintSettingsError(null)
          setPrintSettingsOldWinHost(false)
        }
      })
      .catch((err) => {
        if (on) {
          setPrintSettings(null)
          setPrintSettingsError(err instanceof ApiError ? err.message : t('batch.loadFailed'))
          setPrintSettingsOldWinHost(err instanceof ApiError && err.code === 'HTTP_404')
        }
      })
    return () => {
      on = false
    }
  }, [t])

  /** 安装：下载 blob → 保留原始文件名 multipart 提交本机 WinHost → 提示重启生效 + 刷新已安装列表。
   *  覆盖安装（已安装同 pluginId）先经自研 Modal 确认——确认后带 overwrite 直装。 */
  const installPlugin = async (p: PluginPackageInfo, opts?: { overwrite?: boolean }) => {
    setPluginError(null)
    setPluginNotice(null)
    // 覆盖安装确认（已安装同 pluginId；已安装列表加载失败则不判重，后端覆盖语义兜底）
    if (!opts?.overwrite && p.pluginId) {
      const existing = installedPlugins?.find((i) => i.pluginId === p.pluginId)
      if (existing) {
        setConfirmOverwrite(p)
        return
      }
    }
    setInstalling(p.fileName)
    try {
      const { blob, filename } = await serverApi.downloadPluginPackage(p.fileName)
      const file = new File([blob], filename)
      const res = await localApi.installPlugin(file)
      setPluginNotice(res.message) // 后端 message 已含「重启客户端后生效」
      window.dispatchEvent(new Event('labelframe-plugins-changed'))
      void refreshInstalledPlugins()
    } catch (err) {
      setPluginError(err instanceof ApiError ? err.message : t('plugins.installFailed'))
    } finally {
      setInstalling(null)
    }
  }

  /** 卸载：本机 WinHost 删目录 → 提示重启生效 + 刷新（确认由自研 Modal 承担）。 */
  const uninstallPlugin = async (plugin: InstalledPluginInfo) => {
    setUninstalling(plugin.pluginId)
    setPluginError(null)
    setPluginNotice(null)
    try {
      const res = await localApi.uninstallPlugin(plugin.pluginId)
      setPluginNotice(res.message) // 后端 message 已含「重启客户端后生效」
      window.dispatchEvent(new Event('labelframe-plugins-changed'))
      void refreshInstalledPlugins()
    } catch (err) {
      setPluginError(err instanceof ApiError ? err.message : t('plugins.uninstallFailed'))
    } finally {
      setUninstalling(null)
    }
  }

  const refreshPrinter = useCallback(async () => {
    setPrinterLoading(true)
    try {
      const s = await localApi.getPrinterStatus()
      setPrinter(s)
    } catch (err) {
      setPrinter(null)
      setPrintResult({ ok: false, msg: err instanceof ApiError ? err.message : t('printer.statusFailed') })
    } finally {
      setPrinterLoading(false)
    }
  }, [t])

  useEffect(() => {
    void refreshPrinter()
  }, [refreshPrinter])

  /** 测试连接：探测输入框地址的 /healthz，不保存不生效。 */
  const testConnection = async () => {
    setTesting(true)
    setTestResult(null)
    const ok = await app.checkUrl(url)
    setTestResult(ok ? { ok: true, msg: t('serverAddress.testOk') } : { ok: false, msg: t('serverAddress.testFail') })
    setTesting(false)
  }

  /** 保存：机器级配置持久化 + 立即生效（无需重启），旧客户端回退浏览器本地保存。 */
  const saveAddress = async () => {
    setSaving(true)
    setSaveResult(null)
    const ok = await app.changeBaseUrl(url)
    setSaveResult(ok ? { ok: true, msg: t('serverAddress.saveOk') } : { ok: false, msg: t('serverAddress.saveFallback') })
    setSaving(false)
  }

  /** 保存批次设置：POST /api/host/print-settings 持久化并立即生效（无需重启）；失败展示后端 message。 */
  const savePrintSettings = async () => {
    if (!printSettings) return
    setBatchSaving(true)
    setBatchSaveResult(null)
    try {
      await localApi.setPrintSettings(printSettings)
      setSavedPrintSettings(JSON.stringify(printSettings))
      setBatchSaveResult({ ok: true, msg: t('batch.saveOk') })
    } catch (err) {
      setBatchSaveResult({ ok: false, msg: err instanceof ApiError ? err.message : t('batch.saveFailed') })
    } finally {
      setBatchSaving(false)
    }
  }

  const busy = Boolean(installing || uninstalling || saving || testing || testPrinting || batchSaving)
  useEffect(() => {
    setHostDirty('settings', url !== app.baseUrl || (savedPrintSettings !== '' && JSON.stringify(printSettings) !== savedPrintSettings))
  }, [url, app.baseUrl, printSettings, savedPrintSettings])
  useEffect(() => () => setHostDirty('settings', false), [])
  useEffect(() => {
    if (busy) return beginHostOperation()
  }, [busy])

  const doTestPrint = async () => {
    setTestPrinting(true)
    setPrintResult(null)
    try {
      await localApi.testPrinter()
      setPrintResult({ ok: true, msg: t('printer.sent') })
      void refreshPrinter()
    } catch (err) {
      setPrintResult({ ok: false, msg: err instanceof ApiError ? err.message : t('printer.sendFailed') })
    } finally {
      setTestPrinting(false)
    }
  }

  return (
    <div className="page">
      <div className="page-head">
        <div className="page-title">
          {t('page.title')}
          <small>{t('page.subtitle')}</small>
        </div>
      </div>

      {/* 分节左菜单＋右内容：菜单项标题与各面板头单源；右区单分节滚动（长内容如插件管理不再撑高整页） */}
      <div className="settings-layout">
        <nav className="settings-nav" aria-label={t('navRegion')}>
          {SETTINGS_SECTIONS.map((s) => (
            <button
              key={s.id}
              type="button"
              className={'settings-nav-item' + (section === s.id ? ' active' : '')}
              aria-current={section === s.id ? 'true' : undefined}
              onClick={() => setSection(s.id)}
            >
              {t(s.titleKey)}
            </button>
          ))}
        </nav>
        <div className="settings-main">
        {/* 常规设置：界面语言（切换即时生效 + localStorage 持久化，首启跟随浏览器语言）＋
            服务端地址（机器级配置，保存即生效）——两子节分层呈现 */}
        {section === 'general' && (
        <section className="settings-section" data-testid="general-panel">
          <h2 className="settings-section-title">{t('general.title')}</h2>
          <div className="settings-subsection">
            <h3 className="settings-subtitle">
              {t('language.title')}
              {dot('settings.language')}
            </h3>
            <div className="settings-section-body">
              <label className="field" style={{ maxWidth: 260 }}>
                {t('language.label')}
                <select
                  className="input"
                  value={toAppLocale(i18n.language)}
                  onChange={(ev) => changeLocale(ev.target.value as AppLocale)}
                >
                  {APP_LOCALES.map((loc) => (
                    <option key={loc} value={loc}>
                      {t(loc === 'zh-CN' ? 'language.zhOption' : 'language.enOption')}
                    </option>
                  ))}
                </select>
              </label>
            </div>
          </div>
          <div className="settings-subsection">
            <h3 className="settings-subtitle">
              {t('serverAddress.title')}
              {dot('settings.serverAddress')}
            </h3>
            <div className="settings-section-body">
              <label className="field">
                {t('serverAddress.label')}
                <input className="input mono" value={url} onChange={(ev) => setUrl(ev.target.value)} placeholder="http://127.0.0.1:53961" spellCheck={false} />
              </label>
              <div style={{ display: 'flex', alignItems: 'center', gap: 10, flexWrap: 'wrap' }}>
                <button className="btn" onClick={() => void testConnection()} disabled={testing}>
                  <Icon name="link" size={13} />
                  {testing ? t('serverAddress.testing') : t('serverAddress.test')}
                </button>
                <button className="btn primary" onClick={() => void saveAddress()} disabled={saving}>
                  <Icon name="save" size={13} />
                  {saving ? t('serverAddress.saving') : t('serverAddress.save')}
                </button>
                <span className={'conn' + (app.connected ? ' on' : ' off')} style={{ display: 'inline-flex', alignItems: 'center', gap: 6 }}>
                  <span className={'status-dot' + (app.connected ? ' on' : '')} />
                  {app.connected ? t('serverAddress.connected') : t('serverAddress.disconnected')}
                </span>
              </div>
              {testResult && (
                <div className={testResult.ok ? 'badge ok' : 'badge err'} style={{ alignSelf: 'flex-start' }}>
                  {testResult.msg}
                </div>
              )}
              {saveResult && (
                <div className={saveResult.ok ? 'badge ok' : 'badge err'} style={{ alignSelf: 'flex-start' }}>
                  {saveResult.msg}
                </div>
              )}
            </div>
          </div>
          <div className="settings-subsection">
            <h3 className="settings-subtitle">{t('restart.title')}</h3>
            <ClientRestart />
          </div>
        </section>
        )}

        {section === 'printing' && (
        <section className="settings-section" data-testid="printing-panel">
          <h2 className="settings-section-title">{t('printing.title')}</h2>

          {/* 状态总览卡置顶：当前连接（本页唯一连接徽标，随本机打印服务运行状态着色——本机事实，
              不随服务端地址连通性）＋打印机实时状态合一，先看状态再调配置 */}
          <div className="settings-status-card" data-testid="print-status-card">
            <div style={{ display: 'flex', gap: 8, alignItems: 'center', flexWrap: 'wrap' }}>
              <span className="hint">{t('transportForm.currentConnection')}</span>
              <span
                className={'badge ' + (app.localServiceUp ? 'ok' : '')}
                style={{ maxWidth: 300, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}
              >
                {formatTransport(app.transportConfig) || app.transport || t('value.unknown')}
              </span>
              {printer ? (
                <>
                  <span className={'badge ' + (printer.isOnline ? 'ok' : 'err')}>
                    <span className="status-dot" style={{ background: printer.isOnline ? 'var(--ok)' : 'var(--danger)' }} />
                    {printer.isOnline ? t('device.online') : t('device.offline')}
                  </span>
                  {printer.isPaperOut && <span className="badge warn">{t('printer.paperOut')}</span>}
                  {printer.isPaused && <span className="badge warn">{t('printer.paused')}</span>}
                </>
              ) : (
                <span className="hint">{printerLoading ? t('printer.reading') : t('printer.none')}</span>
              )}
              <span className="spacer" style={{ flex: 1 }} />
              <button className="btn sm" onClick={() => void refreshPrinter()} disabled={printerLoading}>
                <Icon name="refresh" size={12} />
                {t('action.refresh')}
              </button>
            </div>
            {printer && <div className="hint">{printerStatusMessage(printer.message) || t('printer.noExtraInfo')}</div>}
          </div>

          {/* 连接配置：内容常显——子节已隔离上下文，不再需要折叠收纳 */}
          <div className="settings-subsection">
            <h3 className="settings-subtitle">
              {t('printing.connectionTitle')}
              {dot('settings.transport')}
            </h3>
            <TransportPanel />
          </div>

          {/* 作业配置：批次作业＋打印 DPI 同区，一个保存按钮（保存即生效、持久化） */}
          <div className="settings-subsection">
            <h3 className="settings-subtitle">
              {t('printing.jobsTitle')}
              {dot('settings.batch')}
            </h3>
            <div className="settings-section-body">
            {printSettingsOldWinHost ? (
              <div className="hint">{t('batch.oldClient')}</div>
            ) : printSettings === null ? (
              <div className="hint">{printSettingsError ?? t('batch.loading')}</div>
            ) : (
              <>
                <label className="field" style={{ flexDirection: 'row', alignItems: 'center', gap: 6 }}>
                  <input
                    type="checkbox"
                    checked={printSettings.batchEnabled}
                    onChange={(ev) => setPrintSettings({ ...printSettings, batchEnabled: ev.target.checked })}
                  />
                  {t('batch.enable')}
                </label>
                <div style={{ display: 'flex', gap: 10, flexWrap: 'wrap' }}>
                  <label className="field" style={{ maxWidth: 150 }}>
                    {t('batch.batchSize')}
                    <input
                      className="input mono"
                      type="number"
                      min={1}
                      value={printSettings.batchSize}
                      disabled={!printSettings.batchEnabled}
                      onChange={(ev) => setPrintSettings({ ...printSettings, batchSize: Number(ev.target.value) })}
                    />
                  </label>
                  <label className="field" style={{ maxWidth: 190 }}>
                    {t('batch.interval')}
                    <input
                      className="input mono"
                      type="number"
                      min={0}
                      value={printSettings.batchIntervalMs}
                      disabled={!printSettings.batchEnabled}
                      onChange={(ev) => setPrintSettings({ ...printSettings, batchIntervalMs: Number(ev.target.value) })}
                    />
                  </label>
                </div>
                <div style={{ display: 'flex', gap: 10, flexWrap: 'wrap', alignItems: 'flex-end' }}>
                  <label className="field" style={{ maxWidth: 190 }}>
                    {t('batch.dpi')}
                    <select
                      className="input"
                      value={printSettings.dpi}
                      onChange={(ev) => setPrintSettings({ ...printSettings, dpi: Number(ev.target.value) })}
                    >
                      <option value={203}>203 dpi</option>
                      <option value={300}>300 dpi</option>
                      <option value={600}>600 dpi</option>
                    </select>
                  </label>
                  <div className="hint" style={{ flex: 1, minWidth: 220 }}>{t('batch.dpiHint')}</div>
                </div>
                <div style={{ display: 'flex', alignItems: 'center', gap: 10, flexWrap: 'wrap' }}>
                  <button className="btn primary" onClick={() => void savePrintSettings()} disabled={batchSaving}>
                    <Icon name="save" size={13} />
                    {batchSaving ? t('batch.saving') : t('action.save')}
                  </button>
                  {batchSaveResult && (
                    <span className={batchSaveResult.ok ? 'badge ok' : 'badge err'}>{batchSaveResult.msg}</span>
                  )}
                </div>
                <div className="hint">{t('batch.hint', { size: printSettings.batchSize, interval: printSettings.batchIntervalMs })}</div>
              </>
            )}
          </div>
          </div>

          {/* 测试打印：整页收尾动作——状态与配置都就绪后真实出纸验证整链 */}
          <div className="settings-subsection">
            <h3 className="settings-subtitle">
              {t('printing.testPrintTitle')}
              {dot('settings.printer')}
            </h3>
            <div className="settings-section-body">
              <div style={{ display: 'flex', gap: 8, alignItems: 'center', flexWrap: 'wrap' }}>
                <button className="btn" onClick={() => void doTestPrint()} disabled={testPrinting}>
                  <Icon name="test" size={13} />
                  {testPrinting ? t('printer.sending') : t('printer.test')}
                </button>
                {printResult && <span className={printResult.ok ? 'badge ok' : 'badge err'}>{printResult.msg}</span>}
              </div>
            </div>
          </div>
        </section>
        )}

        {section === 'updates' && (
        <section className="settings-section">
          <div className="settings-section-head">
            <h2 className="settings-section-title">
              {t('updates.title')}
              {dot('settings.updates')}
            </h2>
            <span className="spacer" style={{ flex: 1 }} />
            <span className={'conn' + (app.connected ? ' on' : ' off')} style={{ display: 'inline-flex', alignItems: 'center', gap: 6 }}>
              <span className={'status-dot' + (app.connected ? ' on' : '')} />
              {app.connected ? t('serverAddress.connected') : t('updates.standalone')}
            </span>
          </div>
          <div className="settings-section-body">
            {/* 「检查更新」结论——本机版本 × 服务端 client-packages 最高包版本；unknown 不显示（无误导） */}
            <ClientUpdates />
            {(() => {
              const update = checkForUpdate(app.hostVersion, (packages ?? []).map((p) => p.fileName))
              if (update.kind === 'unknown') {
                return null
              }
              if (update.kind === 'update-available') {
                return (
                  <div
                    data-testid="update-available"
                    style={{
                      display: 'flex',
                      flexDirection: 'column',
                      gap: 4,
                      padding: '8px 12px',
                      borderRadius: 6,
                      background: 'rgba(255, 159, 67, 0.12)',
                      border: '1px solid rgba(255, 159, 67, 0.4)',
                    }}
                  >
                    <span style={{ fontWeight: 600 }}>
                      <Icon name="alert" size={13} /> {t('updates.updateAvailable', { latest: update.latestVersion, local: update.localVersion })}
                    </span>
                    <span className="hint">{t('updates.updateHint')}</span>
                  </div>
                )
              }
              return (
                <div data-testid="update-uptodate" className="badge ok" style={{ alignSelf: 'flex-start' }}>
                  {t('updates.upToDate', { version: update.localVersion })}
                </div>
              )
            })()}
            {!app.connected ? (
              <div className="hint">{t('updates.offlineHint')}</div>
            ) : packages === null ? (
              <div className="hint">{t('updates.loading')}</div>
            ) : packages.length === 0 ? (
              <div className="hint">
                {packagesError ? t('updates.loadFailedWith', { reason: packagesError }) : t('updates.empty')}
              </div>
            ) : (
              <>
                <table className="table">
                  <thead>
                    <tr>
                      <th>{t('updates.columns.fileName')}</th>
                      <th style={{ width: 110 }}>{t('updates.columns.size')}</th>
                      <th style={{ width: 140 }}>{t('updates.columns.modifiedAt')}</th>
                      <th style={{ width: 90 }}></th>
                    </tr>
                  </thead>
                  <tbody>
                    {packages.map((p) => (
                      <tr key={p.fileName} style={{ cursor: 'default' }}>
                        <td className="mono" style={{ fontSize: 12, wordBreak: 'break-all' }}>
                          {p.fileName}
                        </td>
                        <td className="mono" style={{ fontSize: 12 }}>
                          {formatSize(p.sizeBytes)}
                        </td>
                        <td className="mono" style={{ fontSize: 12 }}>
                          {formatPackageTime(p.modifiedAt)}
                        </td>
                        <td>
                          <a className="btn sm" href={clientPackageDownloadUrl(p.fileName)} title={t('updates.downloadTitle', { name: p.fileName })}>
                            <Icon name="download" size={12} />
                            {t('action.download')}
                          </a>
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
                <div className="hint">{t('updates.sourceHint', { base: app.baseUrl })}</div>
              </>
            )}
          </div>
        </section>
        )}

        {section === 'plugins' && (
        <section className="settings-section">
          <div className="settings-section-head">
            <h2 className="settings-section-title">
              {t('plugins.title')}
              {dot('settings.plugins')}
            </h2>
            <span className="spacer" style={{ flex: 1 }} />
            <button className="btn sm" onClick={() => void refreshInstalledPlugins()} disabled={installedPluginsLoading}>
              <Icon name="refresh" size={12} />
              {t('action.refresh')}
            </button>
          </div>
          <div className="settings-section-body">
            {pluginError && (
              <div className="badge err" style={{ alignSelf: 'flex-start' }}>
                {pluginError}
              </div>
            )}
            <ClientRestart contextual />
            {pluginNotice && (
              <div className="badge ok" style={{ alignSelf: 'flex-start' }}>
                {pluginNotice}
              </div>
            )}

            <div style={{ fontSize: 13, fontWeight: 600 }}>{t('plugins.availableTitle')}</div>
            {!app.connected ? (
              <div className="hint">{t('plugins.offlineHint')}</div>
            ) : pluginPackages === null ? (
              <div className="hint">{t('plugins.loading')}</div>
            ) : pluginPackages.length === 0 ? (
              <div className="hint">
                {pluginPackagesOldServer
                  ? t('plugins.oldServer')
                  : pluginPackagesError
                    ? t('plugins.loadFailedWith', { reason: pluginPackagesError })
                    : t('plugins.empty')}
              </div>
            ) : (
              <table className="table">
                <thead>
                  <tr>
                    <th>{t('plugins.columns.plugin')}</th>
                    <th style={{ width: 90 }}>{t('plugins.columns.version')}</th>
                    <th style={{ width: 90 }}>{t('plugins.columns.size')}</th>
                    <th style={{ width: 190 }}>{t('plugins.columns.status')}</th>
                    <th style={{ width: 100 }}></th>
                  </tr>
                </thead>
                <tbody>
                  {pluginPackages.map((p) => (
                    <tr key={p.fileName} style={{ cursor: 'default' }}>
                      <td>
                        <div>{pluginPackageName(p.pluginId, p.name) || '—'}</div>
                        <div className="mono" style={{ fontSize: 11, color: 'var(--muted)' }}>
                          {p.pluginId ?? p.fileName}
                        </div>
                      </td>
                      <td className="mono" style={{ fontSize: 12 }}>
                        {p.version ?? '—'}
                      </td>
                      <td className="mono" style={{ fontSize: 12 }}>
                        {formatSize(p.sizeBytes)}
                      </td>
                      <td>
                        {p.valid ? (
                          <span className="badge ok">{t('plugins.valid')}</span>
                        ) : (
                          <>
                            <span className="badge err">{t('plugins.invalid')}</span>{' '}
                            <span style={{ fontSize: 12, color: 'var(--danger)' }}>{p.invalidReason ?? t('plugins.parseFailed')}</span>
                          </>
                        )}
                      </td>
                      <td>
                        {(() => {
                          const tooLarge = pluginPackageTooLarge(p.sizeBytes)
                          return (
                            <button
                              className="btn sm"
                              onClick={() => void installPlugin(p)}
                              disabled={installing === p.fileName || !p.valid || tooLarge !== null}
                              title={
                                tooLarge ?? (!p.valid ? (p.invalidReason ?? t('plugins.invalidTitle')) : t('plugins.installTitle'))
                              }
                            >
                              <Icon name="download" size={12} />
                              {installing === p.fileName ? t('plugins.installing') : t('plugins.install')}
                            </button>
                          )
                        })()}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            )}

            <div style={{ fontSize: 13, fontWeight: 600, marginTop: 4 }}>{t('plugins.installedTitle')}</div>
            {installedPluginsError ? (
              <div className="hint">
                {installedOldWinHost ? t('plugins.installedOld') : t('plugins.installedLoadFailedWith', { reason: installedPluginsError })}
              </div>
            ) : installedPlugins === null ? (
              <div className="hint">{t('plugins.installedLoading')}</div>
            ) : installedPlugins.length === 0 ? (
              <div className="hint">{t('plugins.installedEmpty')}</div>
            ) : (
              <table className="table">
                <thead>
                  <tr>
                    <th>{t('plugins.columns.plugin')}</th>
                    <th style={{ width: 90 }}>{t('plugins.columns.version')}</th>
                    <th style={{ width: 190 }}>{t('plugins.columns.status')}</th>
                    <th style={{ width: 100 }}></th>
                  </tr>
                </thead>
                <tbody>
                  {installedPlugins.map((pl) => (
                    <tr key={pl.pluginId + (pl.packageDir ?? '')} style={{ cursor: 'default' }}>
                      <td>
                        <div>{pluginPackageName(pl.pluginId, pl.name)}</div>
                        <div className="mono" style={{ fontSize: 11, color: 'var(--muted)' }}>
                          {pl.pluginId}
                        </div>
                      </td>
                      <td className="mono" style={{ fontSize: 12 }}>
                        {pl.version === '?' ? '—' : pl.version}
                      </td>
                      <td>
                        {pl.source === 'manual' ? (
                          <span className="badge">{t('plugins.manual')}</span>
                        ) : pl.loaded ? (
                          <span className="badge ok">{t('plugins.loaded')}</span>
                        ) : pl.loadError ? (
                          <>
                            <span className="badge err">{t('plugins.loadFailed')}</span>{' '}
                            <span style={{ fontSize: 12, color: 'var(--danger)' }}>{pl.loadError}</span>
                          </>
                        ) : (
                          <span className="badge warn">{t('plugins.pendingRestart')}</span>
                        )}
                      </td>
                      <td>
                        {pl.source === 'package' && (
                          <button
                            className="btn sm danger"
                            onClick={() => setConfirmUninstall(pl)}
                            disabled={uninstalling === pl.pluginId}
                            title={t('plugins.uninstallTitle')}
                          >
                            <Icon name="trash" size={12} />
                            {uninstalling === pl.pluginId ? t('plugins.uninstalling') : t('plugins.uninstall')}
                          </button>
                        )}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            )}
          </div>
        </section>
        )}
        </div>
      </div>

      {confirmOverwrite && (() => {
        const existing = installedPlugins?.find((i) => i.pluginId === confirmOverwrite.pluginId)
        return (
          <Modal
            title={t('plugins.overwriteModal.title')}
            onClose={() => setConfirmOverwrite(null)}
            footer={
              <>
                <button className="btn" onClick={() => setConfirmOverwrite(null)}>
                  {t('action.cancel')}
                </button>
                <button
                  className="btn primary"
                  onClick={() => {
                    const p = confirmOverwrite
                    setConfirmOverwrite(null)
                    void installPlugin(p, { overwrite: true })
                  }}
                >
                  <Icon name="download" size={13} />
                  {t('plugins.overwriteModal.confirm')}
                </button>
              </>
            }
          >
            <p>
              {t('plugins.overwriteModal.body', {
                existing: existing ? `${existing.name} ${existing.version}` : t('plugins.overwriteModal.sameIdPlugin'),
                target: `${confirmOverwrite.name ?? confirmOverwrite.fileName} ${confirmOverwrite.version ?? '?'}`,
              })}
            </p>
          </Modal>
        )
      })()}

      {confirmUninstall && (
        <Modal
          title={t('plugins.uninstallModal.title')}
          onClose={() => setConfirmUninstall(null)}
          footer={
            <>
              <button className="btn" onClick={() => setConfirmUninstall(null)}>
                {t('action.cancel')}
              </button>
              <button
                className="btn danger"
                onClick={() => {
                  const pl = confirmUninstall
                  setConfirmUninstall(null)
                  void uninstallPlugin(pl)
                }}
              >
                <Icon name="trash" size={13} />
                {t('plugins.uninstallModal.confirm')}
              </button>
            </>
          }
        >
          <p>
            {t('plugins.uninstallModal.body', { name: `${confirmUninstall.name} ${confirmUninstall.version}` })}
          </p>
        </Modal>
      )}
    </div>
  )
}

/** 修改时间：本地时间 MM-dd HH:mm。 */
function formatPackageTime(iso?: string): string {
  if (!iso) return '—'
  const d = new Date(iso)
  if (Number.isNaN(d.getTime())) return '—'
  const p = (n: number) => String(n).padStart(2, '0')
  return `${p(d.getMonth() + 1)}-${p(d.getDate())} ${p(d.getHours())}:${p(d.getMinutes())}`
}
