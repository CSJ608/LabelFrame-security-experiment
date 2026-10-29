// 全局 UI 状态：连接状态（服务端 healthz + serverMode）、本机打印服务可达性（localServiceUp）、
// 本机连接（transportConfig）、机器级配置、DataPrint 会话草稿、状态栏消息、日志。
// baseUrl 优先级 = 机器级配置（GET /api/host/config）> localStorage 兜底 > 默认 127.0.0.1:53961。
// 「本机打印服务」（localServiceUp）与「服务端地址」连通性（connected）各自独立探测（一词三义口径：
// 「服务端」不得兼指本机后台服务）。

import { createContext, useCallback, useContext, useEffect, useMemo, useRef, useState } from 'react'
import type { ReactNode } from 'react'
import { localApi, serverApi, setServerBaseUrl } from '../lib/api/client'
import type { TransportConfig } from '../lib/api/types'
import { getBaseUrl, setBaseUrl as persistBaseUrl } from '../lib/settings'
import { probeHealthz } from '../lib/api/client'
import { isServerUi } from '../lib/uiMode'
import i18next, { currentLocale, formatLogTime } from '../i18n'
import type { PrintDraft, StorageLike } from './draft'
import { applyDraftValue, createEmptyDraft, loadPrintDraft, savePrintDraft } from './draft'

export interface LogLine {
  time: string
  msg: string
}

/** 业务 API 模式：server = 服务端（模板 / 作业中心）；standalone = 单机降级（本机 WinHost 全套 API）。server 构建下恒为 'server'。 */
export type ServerMode = 'unknown' | 'server' | 'standalone'

interface AppContextValue {
  connected: boolean
  /* * 本机打印服务（WinHost）是否可达（页面来源 /healthz 探测；状态栏「本机打印服务」数据源）。 */
  localServiceUp: boolean
  /** 生效中的服务端地址（机器级配置 / localStorage 兜底 / 默认）。 */
  baseUrl: string
  /** 业务 API 模式（healthz 探测服务端地址得出）。 */
  serverMode: ServerMode
  /** 本机 Client 的 deviceId（机器级配置；旧客户端为 null，F5 回退第一台在线；server 构建恒 null）。 */
  hostDeviceId: string | null
  /** 本机设备名称（机器级配置 deviceName；server 构建恒 null）。 */
  hostDeviceName: string | null
  /** 本机 Client 枚举的 IPv4 列表（/api/host/config.ips；server 构建恒空）。 */
  hostIps: string[]
  /** 本机客户端版本（/api/host/config.version，「检查更新」比较口径；旧客户端为 null）。 */
  hostVersion: string | null
  /** 当前管理树的默认目标设备。 */
  defaultTargetDeviceId: string | null
  /** healthz 的传输模式（旧字段，兼容展示兜底）。 */
  transport: string | null
  /** GET /api/transport 结果（mode + params，本机连接），切换成功后立即更新。 */
  transportConfig: TransportConfig | null
  statusMsg: string
  /** 状态栏消息成败着色（null = 中性灰）；日志抽屉行不带色。 */
  statusLevel: 'ok' | 'err' | null
  logs: LogLine[]
  drawerOpen: boolean
  /** DataPrint 草稿；Server 仅内存，client 保留原会话存储。 */
  printDraft: PrintDraft
  setDrawerOpen: (open: boolean) => void
  log: (msg: string) => void
  setStatus: (msg: string, level?: 'ok' | 'err') => void
  /** 设置数据与打印默认目标设备。 */
  setDefaultTargetDeviceId: (id: string | null) => void
  /** 探测服务端连接（healthz，5s 超时）。 */
  checkConnection: () => Promise<boolean>
  /** 探测本机打印服务（页面来源 /healthz；client 构建状态栏「本机打印服务：运行中 / 未运行」数据源，server 构建不使用）。 */
  checkLocalService: () => Promise<boolean>
  /** 探测任意地址的 /healthz（设置页「测试连接」用输入值，不保存）。 */
  checkUrl: (url: string) => Promise<boolean>
  /** 保存服务端地址（机器级配置持久化 + 立即生效 + 重新探测）；旧客户端回退 localStorage。 */
  changeBaseUrl: (url: string) => Promise<boolean>
  /** 连接切换成功后立即用响应 config 更新全局状态（不依赖 healthz 轮询）。 */
  applyTransportConfig: (cfg: TransportConfig) => void
  clearLogs: () => void
  setDraftSelected: (name: string) => void
  setDraftValue: (template: string, key: string, value: string) => void
  setDraftDebug: (on: boolean) => void
  setDraftJobId: (jobId: string | null) => void
}

const AppContext = createContext<AppContextValue | null>(null)

const MAX_LOGS = 300

/** 客户端兼容的目标设备存储键。 */
const DEFAULT_TARGET_KEY = 'labelframe.defaultTargetDeviceId'

function persistDefaultTargetDevice(id: string | null): void {
  try {
    const storage = typeof window !== 'undefined' ? window.localStorage : null
    if (!storage) return
    if (id) storage.setItem(DEFAULT_TARGET_KEY, id)
    else storage.removeItem(DEFAULT_TARGET_KEY)
  } catch {
    // 隐私模式等容错：忽略
  }
}

/** 会话存储（sessionStorage）：显式 window 访问 + 守卫（Node 26 实验性全局 / 隐私模式容错）。 */
function getSessionStorage(): StorageLike | undefined {
  try {
    return typeof window !== 'undefined' ? window.sessionStorage : undefined
  } catch {
    return undefined
  }
}

export function AppProvider({ children }: { children: ReactNode }) {
  const [baseUrl, setBaseUrlState] = useState(() => (isServerUi ? '' : getBaseUrl()))
  const [serverMode, setServerMode] = useState<ServerMode>('unknown')
  const [connected, setConnected] = useState(false)
  // 本机打印服务可达性独立于「服务端地址」连通性——
  // 状态栏呈现本机后台服务（页面来源），不再与服务端地址（remote Server）共用一个「connected」
  const [localServiceUp, setLocalServiceUp] = useState(false)
  const [hostDeviceId, setHostDeviceId] = useState<string | null>(null)
  const [hostDeviceName, setHostDeviceName] = useState<string | null>(null)
  const [hostIps, setHostIps] = useState<string[]>([])
  const [hostVersion, setHostVersion] = useState<string | null>(null)
  const [defaultTargetDeviceId, setDefaultTargetDeviceIdState] = useState<string | null>(null)
  const [transport, setTransport] = useState<string | null>(null)
  const [transportConfig, setTransportConfig] = useState<TransportConfig | null>(null)
  // 状态栏初始消息读 i18next 当前语言；后续消息由各页 setStatus 写入（语言切换后旧消息不回译，属既有语义）
  const [statusMsg, setStatusMsg] = useState(() => i18next.t('shell:statusbar.ready'))
  const [statusLevel, setStatusLevel] = useState<'ok' | 'err' | null>(null)
  const [logs, setLogs] = useState<LogLine[]>([])
  const [drawerOpen, setDrawerOpen] = useState(false)
  const [printDraft, setPrintDraft] = useState<PrintDraft>(() => isServerUi ? createEmptyDraft() : loadPrintDraft(getSessionStorage()))
  const pendingRef = useRef<Promise<boolean> | null>(null)
  // 探测串行化与终态一致性三件套：
  // - configReadyRef：client 构建机器级配置加载完成前不探测服务端（消除初始化探测与配置加载后探测交错的竞态双态）；
  // - baseUrlRef：生效服务端地址的同步镜像（state 更新异步，探测路径需读到最新值）；
  // - probeSeqRef：探测序号（后发优先）——地址切换或判空后，在途旧探测的结果按序号作废。
  const configReadyRef = useRef(isServerUi)
  const baseUrlRef = useRef(isServerUi ? '' : getBaseUrl())
  const probeSeqRef = useRef(0)

  // Server 管理草稿不得跨登录身份持久化。
  useEffect(() => {
    if (!isServerUi) savePrintDraft(getSessionStorage(), printDraft)
  }, [printDraft])

  const log = useCallback((msg: string) => {
    // 日志行时间戳跟随当前界面语言（读 i18next 单例；存量行保留落盘时格式）
    const time = formatLogTime(currentLocale(), new Date())
    setLogs((prev) => [...prev.slice(-(MAX_LOGS - 1)), { time, msg }])
  }, [])

  const setStatus = useCallback(
    (msg: string, level?: 'ok' | 'err') => {
      setStatusMsg(msg)
      setStatusLevel(level ?? null)
      log(msg)
    },
    [log],
  )

  const checkConnection = useCallback(async (): Promise<boolean> => {
    // 机器级配置就绪前不探测（此间不写终态，connected / serverMode 维持初值）
    if (!configReadyRef.current) return false
    // client 构建下服务端地址为空 = 未配置服务端——不做网络探测（空串 base 落同源会打到本机
    // WinHost /healthz 200，谎报「服务端已连接」），直接判「服务端未连接（单机模式可用）」；
    // server 构建不适用：base 恒为 '' 且同源即服务端自身，探测语义正确。
    if (!isServerUi && baseUrlRef.current === '') {
      probeSeqRef.current += 1 // 作废在途旧探测
      pendingRef.current = null
      setConnected(false)
      setServerMode('standalone')
      return false
    }
    if (pendingRef.current) return pendingRef.current
    const seq = ++probeSeqRef.current
    const probe = (async () => {
      try {
        await serverApi.healthz()
        if (seq === probeSeqRef.current) {
          setConnected(true)
          setServerMode('server')
        }
        return true
      } catch {
        if (seq === probeSeqRef.current) {
          setConnected(false)
          // server 构建无单机降级：探测失败仍保持 server 模式，仅置未连接
          setServerMode(isServerUi ? 'server' : 'standalone')
        }
        return false
      } finally {
        // 序号仍为最新时清理去重占用；已被接替（序号前移）时占用由接替方处理
        if (seq === probeSeqRef.current) pendingRef.current = null
      }
    })()
    pendingRef.current = probe
    return probe
  }, [])

  const checkUrl = useCallback((url: string): Promise<boolean> => probeHealthz(url), [])

  // 本机打印服务探测：页面来源 /healthz（client 构建页面由本机 WinHost 托管）；server 构建不调用
  const checkLocalService = useCallback(async (): Promise<boolean> => {
    try {
      await localApi.healthz()
      setLocalServiceUp(true)
      return true
    } catch {
      setLocalServiceUp(false)
      return false
    }
  }, [])

  // 启动：读机器级配置（serverUrl 优先）→ 本机连接配置 → 探测服务端。
  // server 构建由服务端托管、无本机 Client——跳过 localApi 探测直接探测服务端 healthz
  useEffect(() => {
    let on = true
    if (isServerUi) {
      void checkConnection()
      return () => {
        on = false
      }
    }
    void localApi
      .getHostConfig()
      .then((cfg) => {
        if (!on) return
        setServerBaseUrl(cfg.serverUrl)
        baseUrlRef.current = cfg.serverUrl
        setBaseUrlState(cfg.serverUrl)
        setHostDeviceId(cfg.deviceId ?? null)
        setHostDeviceName(cfg.deviceName ?? null)
        setHostIps(cfg.ips ?? [])
        setHostVersion(cfg.version ?? null)
        setStatus(i18next.t('shell:startup.hostConfigLoaded', { serverUrl: cfg.serverUrl }))
      })
      .catch(() => {
        if (!on) return
        // 旧客户端（无 /api/host/config）：回退 localStorage 兜底（getBaseUrl 已含默认值）
        setStatus(i18next.t('shell:startup.hostConfigFallback'))
      })
      .finally(() => {
        // 配置加载完成（或旧客户端回退兜底）后才放行探测——首次探测在此串行发起，
        // 此前任何 checkConnection 调用均为无探测空转
        if (on) {
          configReadyRef.current = true
          void checkConnection()
        }
      })
    void localApi
      .getTransport()
      .then((cfg) => {
        if (!on) return
        setTransportConfig(cfg)
        setTransport(cfg.mode)
      })
      .catch(() => {
        // 旧客户端无 /api/transport：忽略，保持现状
      })
    return () => {
      on = false
    }
  }, [checkConnection, setStatus])

  const changeBaseUrl = useCallback(
    async (url: string): Promise<boolean> => {
      const cleaned = url.trim().replace(/\/+$/, '')
      try {
        await localApi.setHostConfig({ serverUrl: cleaned })
        // 立即生效：内存更新（机器级配置为唯一事实来源，localStorage 仅兜底）→ 重新探测 → 页面随 baseUrl 重拉
        setServerBaseUrl(cleaned)
        baseUrlRef.current = cleaned
        setBaseUrlState(cleaned)
        persistBaseUrl(cleaned)
        setStatus(i18next.t('settings:serverAddress.statusSaved', { url: cleaned }), 'ok')
        // 释放旧地址在途探测的去重占用，按新地址立即发起新探测（旧结果按序号作废）
        pendingRef.current = null
        void checkConnection()
        return true
      } catch {
        // 旧客户端 / 无本机配置接口：回退浏览器本地保存
        setServerBaseUrl(cleaned)
        baseUrlRef.current = cleaned
        setBaseUrlState(cleaned)
        persistBaseUrl(cleaned)
        setStatus(i18next.t('settings:serverAddress.statusFallback'), 'err')
        pendingRef.current = null
        void checkConnection()
        return false
      }
    },
    [checkConnection, setStatus],
  )

  const applyTransportConfig = useCallback((cfg: TransportConfig) => {
    setTransportConfig(cfg)
    setTransport(cfg.mode)
  }, [])

  const clearLogs = useCallback(() => setLogs([]), [])

  /** 更新当前界面的默认目标设备。 */
  const setDefaultTargetDeviceId = useCallback((id: string | null) => {
    if (!isServerUi) persistDefaultTargetDevice(id)
    setDefaultTargetDeviceIdState(id)
  }, [])

  const setDraftSelected = useCallback((name: string) => {
    setPrintDraft((d) => ({ ...d, selectedName: name }))
  }, [])

  const setDraftValue = useCallback((template: string, key: string, value: string) => {
    setPrintDraft((d) => applyDraftValue(d, template, key, value))
  }, [])

  const setDraftDebug = useCallback((on: boolean) => {
    setPrintDraft((d) => ({ ...d, debugMode: on }))
  }, [])

  const setDraftJobId = useCallback((jobId: string | null) => {
    setPrintDraft((d) => ({ ...d, jobId }))
  }, [])

  const value = useMemo<AppContextValue>(
    () => ({
      connected,
      localServiceUp,
      baseUrl,
      serverMode,
      hostDeviceId,
      hostDeviceName,
      hostIps,
      hostVersion,
      defaultTargetDeviceId,
      transport,
      transportConfig,
      statusMsg,
      statusLevel,
      logs,
      drawerOpen,
      printDraft,
      setDrawerOpen,
      log,
      setStatus,
      setDefaultTargetDeviceId,
      checkConnection,
      checkLocalService,
      checkUrl,
      changeBaseUrl,
      applyTransportConfig,
      clearLogs,
      setDraftSelected,
      setDraftValue,
      setDraftDebug,
      setDraftJobId,
    }),
    [
      connected,
      localServiceUp,
      baseUrl,
      serverMode,
      hostDeviceId,
      hostDeviceName,
      hostIps,
      hostVersion,
      defaultTargetDeviceId,
      transport,
      transportConfig,
      statusMsg,
      statusLevel,
      logs,
      drawerOpen,
      printDraft,
      log,
      setStatus,
      setDefaultTargetDeviceId,
      checkConnection,
      checkLocalService,
      checkUrl,
      changeBaseUrl,
      applyTransportConfig,
      clearLogs,
      setDraftSelected,
      setDraftValue,
      setDraftDebug,
      setDraftJobId,
    ],
  )

  return <AppContext.Provider value={value}>{children}</AppContext.Provider>
}

// Provider 组件与配套 useApp 钩子同文件导出是 React Context 社区惯例（显式豁免 only-export-components）
// eslint-disable-next-line react/only-export-components
export function useApp(): AppContextValue {
  const ctx = useContext(AppContext)
  if (!ctx) throw new Error('useApp 必须在 AppProvider 内使用')
  return ctx
}
