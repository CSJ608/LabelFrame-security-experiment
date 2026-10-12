// 数据与打印：测试数据表单 / 打印测试 / Excel 导入映射 / 批量打印 / 作业进度与失败重试
// 会话草稿提升全局（模板 / 字段值 / 调试开关 / 作业进度保留；Excel 不保留）
// 调试模式独立开关——开：打印按钮改为后端渲染出图下载（单张 PNG / 批量 zip），不建作业不发驱动。
// 无字段模板 = 静态标签（合法模板），操作区照常渲染可打印测试（提交空数据单张）
// Excel 模板 / 导入维持无字段禁用（无列可生成 / 映射）。
// （决议 2 / C-3）：本页对服务端的状态一律用「已加入 / 未加入服务端」（设备注册，hostInList）
// 且随探测周期（10s）自动刷新——后台注册状态变化后无需切页（评审 B-9 / C-3）。
// （决议，评审 C-1）：「图片预览」名副其实——点击后页内弹层呈现当前字段值的渲染图
// （复用 render-image 端点实时取图，不建作业不自动下载）；下载由弹层内显式「下载」按钮触发，预览与下载分离。
// （评审 / B-6）：副标题改「填写数据并打印 / Excel 批量打印」（本页是日常主打印入口
// 不再自称「测试数据」）；作业进度「目标设备」显示设备名（无可解析名称回退设备 ID），与在线设备页 / 目标设备下拉同源。
// （C-4 / C-5）：接收工作台「打印」直达的预选草稿（同手动选择）；作业进度区指向「作业历史」的
// 纯文字指引改为可点击跳转（onOpenJobHistory → 切作业历史页，行可展开逐张 / 汇总明细）。
// 页面文案 key 化（dataPrint 域；jobStatus / action 等 common 词条经 fallbackNS 兜底）。
// requestId 改用 uuid 包 v4 生成——crypto.randomUUID 是安全上下文专属 API（仅 HTTPS /
// localhost 暴露），HTTP + 局域网 IP 访问 server UI 时为 undefined 直接抛 TypeError；uuid 的 v4 在
// randomUUID 不可用时回退 crypto.getRandomValues（不受安全上下文限制），两种上下文行为一致。

import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import type { ReactNode } from 'react'
import { Trans, useTranslation } from 'react-i18next'
import { v4 as uuidv4 } from 'uuid'
import i18next from '../i18n'
import { localApi, serverApi } from '../lib/api/client'
import { ApiError } from '../lib/api/types'
import type { DeviceView, JobView, SubmitJobRequest, TemplatePackage, TemplateSummary } from '../lib/api/types'
import { formatTransport, isNativePrintMode } from '../lib/transport'
import { deviceDisplayName } from '../lib/deviceDisplay'
import { useJobStatusLabel } from '../lib/jobStatus'
import { downloadBlob } from '../lib/download'
import { fromBackendElements } from '../lib/design/convert'
import { deriveFieldInfos } from '../lib/design/fields'
import { findDuplicateKeys, isMappingComplete, rowToData, suggestMapping } from '../lib/excel/mapping'
import type { MappingField } from '../lib/excel/mapping'
import { useApp } from '../state/AppContext'
import { mergeDraftValues } from '../state/draft'
import { isServerUi } from '../lib/uiMode'
import { Icon } from '../components/Icon'
import { Modal } from '../components/Modal'
import { NativePrintModeHint } from '../components/TransportPanel'

/* * 设备在线状态标签（common.device.* 词条；调用点多为拼接，读 i18next 单例当前语言）。 */
const deviceStatusLabel = (s: string) => i18next.t(s === 'Online' ? 'device.online' : 'device.offline')

/** 离线原因（选择器置灰时显示上次心跳时间）。 */
function formatLastSeen(iso?: string): string {
  if (!iso) return '—'
  const d = new Date(iso)
  if (Number.isNaN(d.getTime())) return '—'
  const p = (n: number) => String(n).padStart(2, '0')
  return `${p(d.getMonth() + 1)}-${p(d.getDate())} ${p(d.getHours())}:${p(d.getMinutes())}:${p(d.getSeconds())}`
}

const isTerminal = (s: string) => s === 'Completed' || s === 'Failed' || s === 'Cancelled' || s === 'Expired'

/** 中文顿号 / 英文逗号分隔（重复映射字段列表拼接，随当前界面语言）。 */
const localeSep = () => (i18next.language === 'en' ? ', ' : '、')

/* * 图片预览弹层状态（·）：ready 持有 blob 供弹层内「下载」按钮复用（下载不再重新请求）。 */
type ImagePreview = { status: 'loading' } | { status: 'ready'; url: string; blob: Blob; filename: string } | { status: 'error'; message: string }

/** 作业轮询（1.5s，终端状态停止）；API 跟随模式（服务端 / 单机降级）。
 *  参数用 Pick 而非 typeof serverApi：client 构建降级直连时传 localApi（无 client-packages 方法），仅需 getJob / retryJobItem。 */
function useJobPolling(jobId: string | null, biz: Pick<typeof serverApi, 'getJob' | 'retryJobItem'>) {
  const [job, setJob] = useState<JobView | null>(null)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    if (!jobId) return
    let stopped = false
    let timer: ReturnType<typeof setTimeout> | null = null
    const tick = async () => {
      try {
        const j = await biz.getJob(jobId)
        if (stopped) return
        setJob(j)
        setError(null)
        if (!isTerminal(j.status)) timer = setTimeout(() => void tick(), 1500)
      } catch (err) {
        if (stopped) return
        setError(err instanceof ApiError ? err.message : i18next.t('dataPrint:errors.getJob'))
        timer = setTimeout(() => void tick(), 2000)
      }
    }
    void tick()
    return () => {
      stopped = true
      if (timer) clearTimeout(timer)
    }
  }, [jobId, biz])

  const retry = useCallback(
    async (index: number): Promise<boolean> => {
      if (!jobId) return false
      try {
        const j = await biz.retryJobItem(jobId, index)
        setJob(j)
        return true
      } catch (err) {
        setError(err instanceof ApiError ? err.message : i18next.t('dataPrint:errors.retryFailed'))
        return false
      }
    },
    [jobId, biz],
  )

  return { job, error, retry }
}

/* * 行内链接式按钮（· C-5）：提示文案中的页内跳转（如「作业历史」）——视觉为链接，语义 / 焦点行为为按钮。
 * children 改可选——词条内 <0>…</0> 标签经 Trans 注入文本（组件数组形式不静态传 children）。 */
function InlineLink({ onClick, children }: { onClick: () => void; children?: ReactNode }) {
  return (
    <button type="button" className="link-like" onClick={onClick}>
      {children}
    </button>
  )
}

function JobPanel({
  job,
  error,
  retry,
  debugMode,
  canRetry,
  resolveDeviceName,
  onOpenJobHistory,
}: {
  job: JobView | null
  error: string | null
  retry: (i: number) => Promise<boolean>
  debugMode: boolean
  /* * （G4）：server 构建无逐张 retry 端点——隐藏逐张失败重试表格（Server 作业本就无 items，强制隐藏兜底）。 */
  canRetry: boolean
  /* * （B-6）：目标设备显示名解析（设备名优先，无可解析名称回退设备 ID；单机模式无设备列表 = 恒回退 ID）。 */
  resolveDeviceName: (deviceId: string) => string
  /* * （C-5）：跳转「作业历史」页（进度区文字指引可点击——作业历史行可展开明细）。 */
  onOpenJobHistory: () => void
}) {
  const app = useApp()
  const { t } = useTranslation('dataPrint')
  const jobLabel = useJobStatusLabel()
  if (!job) {
    return (
      <div className="panel">
        <div className="panel-head">{t('jobPanel.title')}</div>
        <div className="panel-body">
          {debugMode ? <div className="hint">{t('jobPanel.debugDoneHint')}</div> : <div className="hint">{t('jobPanel.idleHint')}</div>}
          {error && <div className="error-text" style={{ marginTop: 6 }}>{error}</div>}
        </div>
      </div>
    )
  }
  const pct = job.totalItems > 0 ? Math.round((job.completedItems / job.totalItems) * 100) : 0
  const failed = job.items ? job.items.filter((i) => i.status === 'Failed').length : (job.failedItems ?? 0)
  return (
    <div className="panel">
      <div className="panel-head">
        {t('jobPanel.title')}
        <span className={'badge ' + (job.status === 'Completed' ? 'ok' : job.status === 'Failed' ? 'err' : job.status === 'Cancelled' ? 'neutral' : 'info')}>
          {jobLabel(job.status)}
        </span>
        <span className="spacer" style={{ flex: 1 }} />
        <span className="mono" style={{ color: 'var(--ink-3)', fontSize: 11 }}>ID {job.jobId.slice(0, 8)}</span>
      </div>
      <div className="panel-body" style={{ display: 'flex', flexDirection: 'column', gap: 10 }}>
        {debugMode && <div className="hint">{t('jobPanel.debugLastJobHint')}</div>}
        <div>
          <div style={{ display: 'flex', justifyContent: 'space-between', marginBottom: 4, fontSize: 12, color: 'var(--ink-2)' }}>
            <span>{t('jobPanel.progress', { completed: job.completedItems, total: job.totalItems })}</span>
            <span className="mono">{pct}%</span>
          </div>
          <div className="progress">
            <div className={failed > 0 ? 'fail' : job.status === 'Completed' ? 'done' : ''} style={{ width: pct + '%' }} />
          </div>
          {failed > 0 && (
            <div className="hint" style={{ marginTop: 6, color: 'var(--danger)' }}>
              {t('jobPanel.failedPrefix', { count: failed })}
              {job.items && canRetry ? (
                t('jobPanel.retryBelow')
              ) : (
                // （C-5）：指引可点击跳转——作业历史行可展开查看状态与失败原因
                <Trans t={t} i18nKey="jobPanel.checkJobHistory" components={[<InlineLink key={0} onClick={onOpenJobHistory} />]} />
              )}
            </div>
          )}
        </div>
        {job.targetDeviceId && (
          <div className="hint">
            {job.deviceStatus
              ? t('jobPanel.targetDeviceStatus', { name: resolveDeviceName(job.targetDeviceId), status: deviceStatusLabel(job.deviceStatus) })
              : t('jobPanel.targetDevice', { name: resolveDeviceName(job.targetDeviceId) })}
          </div>
        )}
        {job.printImageDir && (
          <div className="hint" style={{ wordBreak: 'break-all' }}>
            {t('jobPanel.simPrintDir', { dir: job.printImageDir, count: job.printImageCount ?? 0 })}
          </div>
        )}
        {job.errorMessage && <div className="hint" style={{ color: 'var(--danger)' }}>{t('jobPanel.errorLabel', { message: job.errorMessage })}</div>}
        {job.items && canRetry && (
          <table className="table">
            <thead>
              <tr>
                <th style={{ width: 50 }}>#</th>
                <th style={{ width: 90 }}>{t('column.status')}</th>
                <th>{t('job.failedReason')}</th>
                <th style={{ width: 90 }}></th>
              </tr>
            </thead>
            <tbody>
              {job.items.map((it) => (
                <tr key={it.index} style={{ cursor: 'default' }}>
                  <td className="mono">{it.index + 1}</td>
                  <td>
                    <span className={'badge ' + (it.status === 'Completed' ? 'ok' : it.status === 'Failed' ? 'err' : it.status === 'Cancelled' ? 'neutral' : 'info')}>
                      {jobLabel(it.status)}
                    </span>
                  </td>
                  <td style={{ color: 'var(--danger)', fontSize: 12 }}>{it.status === 'Failed' ? it.errorMessage || it.errorCode || t('job.unknownError') : ''}</td>
                  <td>
                    {it.status === 'Failed' && (
                      <button
                        className="btn sm"
                        onClick={() => {
                          void retry(it.index).then((ok) => ok && app.setStatus(t('jobPanel.retried', { index: it.index + 1 })))
                        }}
                      >
                        <Icon name="retry" size={12} />
                        {t('action.retry')}
                      </button>
                    )}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
        {!job.items && (
          <div className="hint">
            <Trans t={t} i18nKey="jobPanel.noItemsNote" components={[<InlineLink key={0} onClick={onOpenJobHistory} />]} />
          </div>
        )}
      </div>
    </div>
  )
}

export function DataPrint({ onOpenJobHistory }: { onOpenJobHistory: () => void }) {
  const app = useApp()
  const { printDraft } = app
  // 页面文案 key 化（dataPrint 域；jobStatus / action 等 common 词条经 fallbackNS 兜底）
  const { t } = useTranslation('dataPrint')
  const [templates, setTemplates] = useState<TemplateSummary[]>([])
  const [pkg, setPkg] = useState<TemplatePackage | null>(null)
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)

  // Excel 导入数据与列映射：页面局部状态（切页 / 刷新即丢弃，重新上传）
  const [mappingOpen, setMappingOpen] = useState(false)
  const [excel, setExcel] = useState<{ headers: string[]; rows: string[][]; file: string } | null>(null)
  const [mapping, setMapping] = useState<string[]>([])
  const [importing, setImporting] = useState(false)

  const [submitting, setSubmitting] = useState(false)

  // 图片预览弹层（·）：loading / ready（blob URL）/ error 三态
  // 代数计数用于关闭弹层后作废在途请求（防结果回来自动重开「幽灵弹层」）
  const [imagePreview, setImagePreview] = useState<ImagePreview | null>(null)
  const previewGenRef = useRef(0)
  const previewRef = useRef<ImagePreview | null>(null)
  previewRef.current = imagePreview
  useEffect(() => {
    // 卸载兜底：释放弹层持有的 blob URL（防泄漏），并作废在途预览请求
    return () => {
      previewGenRef.current += 1
      if (previewRef.current?.status === 'ready') URL.revokeObjectURL(previewRef.current.url)
    }
  }, [])

  // 目标设备（F5）：GET /api/devices 成功 = 服务端模式（显示选择、提交带 targetDeviceId）
  // 404 / 失败 = 单机 WinHost 降级（隐藏选择、提交不带 targetDeviceId）。
  // 默认选中本机设备（机器级配置 deviceId 匹配在线列表），未命中回退第一台在线。
  // （K2）：server 构建恒服务端模式——设备列表拉取成功即 'server'（失败也保持 'server'，无 standalone 分支）。
  // （A）：客户端构建目标设备固定本机——routeMode 判定本机路由——
  //   本机已注册且在线 → 'server'（作业经服务端投递，服务端历史可见）；本机未注册 / 离线 → 'direct'（降级本机直连，作业仅本机历史）。
  const [deviceMode, setDeviceMode] = useState<'loading' | 'server' | 'standalone'>('loading')
  const [routeMode, setRouteMode] = useState<'loading' | 'server' | 'direct'>('loading')
  /* * ：本机是否已注册到服务端（deviceId 出现在设备列表）——区分「未注册 / 离线」两种降级提示。 */
  const [hostInList, setHostInList] = useState(false)
  const [devices, setDevices] = useState<DeviceView[]>([])
  const [targetDeviceId, setTargetDeviceId] = useState('')

  /** 业务 API 跟随模式：服务端 = serverApi（模板 / 作业中心）；单机降级 = localApi（本机 WinHost 全套 API）。
   * server 构建恒 serverApi（Server UI 由服务端托管，无本机 Client）。 */
  const biz = isServerUi ? serverApi : deviceMode === 'server' ? serverApi : localApi
  /** 提交 / 作业轮询 API：client 构建服务端模式下降级直连（routeMode 'direct'）时走 localApi（本机 WinHost 直接打印）。 */
  const submitBiz = isServerUi ? serverApi : deviceMode === 'server' && routeMode === 'server' ? serverApi : localApi
  const { job, error: jobError, retry } = useJobPolling(printDraft.jobId, submitBiz)

  useEffect(() => {
    let cancelled = false
    let probedOnce = false
    const probeRoute = () => {
      if (isServerUi) {
        // （K1/K2/Y2）：server 构建不探测本机（无 getHostConfig / getTransport）
        // 进入页面拉取一次设备列表（无需轮询，提交前另有现拉校验）；
        // 默认目标优先级 = 用户点选（localStorage labelframe.defaultTargetDeviceId，须在线）> 第一台在线。
        serverApi
          .listDevices()
          .then((list) => {
            if (cancelled) return
            probedOnce = true
            setDevices(list)
            setDeviceMode('server')
            setRouteMode('server')
            setHostInList(true)
            const online = list.filter((d) => d.status === 'Online')
            const saved =
              app.defaultTargetDeviceId && online.some((d) => d.deviceId === app.defaultTargetDeviceId)
                ? app.defaultTargetDeviceId
                : ''
            setTargetDeviceId((prev) =>
              prev && online.some((d) => d.deviceId === prev) ? prev : saved || online[0]?.deviceId || '',
            )
          })
          .catch((err) => {
            if (cancelled) return
            setDeviceMode('server')
            setRouteMode('server')
            setHostInList(true)
            if (!probedOnce) setError(err instanceof ApiError ? err.message : i18next.t('dataPrint:errors.loadDevices'))
            probedOnce = true
          })
        return
      }
      void Promise.all([serverApi.listDevices().catch(() => null), localApi.getHostConfig().catch(() => null)]).then(
        ([list, cfg]) => {
          if (cancelled) return
          if (list) {
            setDevices(list)
            setDeviceMode('server')
            const online = list.filter((d) => d.status === 'Online')
            // 本机设备优先（hostConfig.deviceId 匹配），未命中回退第一台在线（少点一次；全部离线时留空由用户选择）；
            // 已选目标保留（仍在线时），不被周期刷新顶回
            const mine = cfg ? online.find((d) => d.deviceId === cfg.deviceId) : undefined
            setTargetDeviceId((prev) => prev || mine?.deviceId || online[0]?.deviceId || '')
            // （A）：客户端构建目标固定本机——本机已注册且在线 → 服务端路由
            // 未注册（无 deviceId 或不在列表）/ 离线 → 降级本机直连（提交走本机 WinHost，作业仅本机历史）
            const mineAny = cfg?.deviceId ? list.find((d) => d.deviceId === cfg.deviceId) : undefined
            setHostInList(Boolean(mineAny))
            setRouteMode(mineAny && mineAny.status === 'Online' ? 'server' : 'direct')
          } else {
            // 单机模式：旧 WinHost 无 /api/devices（404），或服务端不可达——隐藏设备选择，正常提交
            setDeviceMode('standalone')
            setRouteMode('direct')
            setHostInList(false)
          }
        },
      )
    }
    probeRoute()
    // （C-3）：client 构建设备列表 / routeMode / hostInList 随健康探测周期刷新（10s）——
    // 后台注册状态变化（含保存服务端地址后的热切换）后页面在探测周期内自动更新，无需切页；
    // baseUrl 变化（保存新地址）时立即重探。server 构建维持「进入拉取一次」契约（提交前另有现拉校验）
    const timer = isServerUi ? null : setInterval(probeRoute, 10000)
    return () => {
      cancelled = true
      if (timer) clearInterval(timer)
    }
  }, [app.baseUrl, app.defaultTargetDeviceId]) // eslint-disable-line react-hooks/exhaustive-deps

  const selectedName = printDraft.selectedName
  const debugMode = printDraft.debugMode

  /* * （B-6）：目标设备显示名——从本页已拉取的设备列表解析（GET /api/devices，与在线设备页 /
   *  目标设备下拉同源），无可解析名称（空 / 空白 / 不在列表，含单机模式无设备列表）回退设备 ID。 */
  const resolveDeviceName = useCallback(
    (deviceId: string) => deviceDisplayName(devices.find((d) => d.deviceId === deviceId)?.name, deviceId),
    [devices],
  )

  useEffect(() => {
    if (deviceMode === 'loading') return
    void biz
      .listTemplates()
      .then((list) => {
        setTemplates(list)
        if (list.length > 0 && !selectedName) app.setDraftSelected(list[0].name)
      })
      .catch((err) => setError(err instanceof ApiError ? err.message : i18next.t('dataPrint:errors.loadTemplates')))
  }, [deviceMode]) // eslint-disable-line react-hooks/exhaustive-deps

  // 模板详情：cancelled 守卫与同文件预览弹层 previewGenRef 同一竞态标准——
  // 快速切换模板时，前一个慢响应（setPkg / setError / setLoading）不得覆盖新选择（字段表单 / 打印数据用错模板）。
  useEffect(() => {
    if (!selectedName) return
    let cancelled = false
    setLoading(true)
    setPkg(null)
    setError(null)
    void biz
      .getTemplate(selectedName)
      .then((p) => {
        if (cancelled) return
        setPkg(p)
      })
      .catch((err) => {
        if (cancelled) return
        setError(err instanceof ApiError ? err.message : i18next.t('dataPrint:errors.loadTemplate'))
      })
      .finally(() => {
        if (!cancelled) setLoading(false)
      })
    return () => {
      cancelled = true
    }
  }, [selectedName, deviceMode]) // eslint-disable-line react-hooks/exhaustive-deps

  // 打印字段（键 + 显示名）：contract.fields 优先，空则从版式推导；
  // 标签与占位符一律 `displayName || key`（· 决议 1：存量模板未填显示名回退键名，不报错）
  const formFields = useMemo(() => {
    if (!pkg) return []
    const fromContract = (pkg.contract.fields ?? [])
      .map((f) => ({ key: f.key, displayName: f.displayName?.trim() || undefined }))
      .filter((f) => f.key)
    if (fromContract.length > 0) return fromContract
    return deriveFieldInfos(fromBackendElements(pkg.layout.elements))
  }, [pkg])

  // 列映射建议用字段（键 + 显示名）：表头为显示名（下载的 Excel 模板）时也能自动匹配（联调修复）
  const mappingFields = useMemo(() => {
    if (!pkg) return []
    const fromContract = (pkg.contract.fields ?? [])
      .map((f) => ({ key: f.key, displayName: f.displayName }))
      .filter((f) => f.key)
    if (fromContract.length > 0) return fromContract
    return deriveFieldInfos(fromBackendElements(pkg.layout.elements))
  }, [pkg])

  // 显示值 = { ...testData, ...用户 dirty 的 key }（按 key 存在性合并，用户清空不被顶回）
  const values = useMemo(() => {
    if (!pkg) return {}
    return mergeDraftValues(pkg.testData, printDraft.valuesByTemplate[pkg.name], printDraft.dirtyKeysByTemplate[pkg.name])
  }, [pkg, printDraft.valuesByTemplate, printDraft.dirtyKeysByTemplate])

  const setFieldValue = (key: string, value: string) => {
    if (pkg) app.setDraftValue(pkg.name, key, value)
  }

  /**
   * 拼提交请求：
   * - job（服务端模式）：templateName 引用服务端模板库 + targetDeviceId 定向投递（自包含 template 不携带）；
   *   - server 构建：targetDeviceId = 所选在线设备；
   * - client 构建（A）：目标固定本机——本机在线走服务端路由，targetDeviceId = 本机 deviceId
   * - job（降级直连 / 单机）：自包含 template（本机 WinHost 直接打印，旧 WinHost 无 templateName / targetDeviceId）；
   * - debug 出图：自包含 template（render-image 后端要求 contract + layout，不建作业）。
   * requestId 一律用 uuid v4（服务端契约仅要求唯一非空白字符串；见文件头注释）。
   */
  const buildRequest = useCallback(
    (labels: { data: Record<string, string> }[], kind: 'job' | 'debug'): SubmitJobRequest | null => {
      if (!pkg) return null
      if (kind === 'job') {
        if (isServerUi) {
          return {
            requestId: uuidv4(),
            templateName: pkg.name,
            targetDeviceId,
            labels,
          }
        }
        if (deviceMode === 'server' && routeMode === 'server') {
          return {
            requestId: uuidv4(),
            templateName: pkg.name,
            targetDeviceId: app.hostDeviceId ?? undefined,
            labels,
          }
        }
      }
      return {
        requestId: uuidv4(),
        template: { name: pkg.name, contract: pkg.contract, layout: pkg.layout },
        labels,
      }
    },
    [pkg, deviceMode, routeMode, targetDeviceId, app.hostDeviceId],
  )

  const submit = async (labels: { data: Record<string, string> }[]) => {
    if (isServerUi && !targetDeviceId) {
      app.setStatus(t('errors.pickDeviceFirst'))
      return
    }
    setSubmitting(true)
    try {
      // （K3，仅 server 构建）：提交时现拉 GET /api/devices 核对所选设备在线——
      // 不复用进入页面时的缓存列表（设备中途掉线后缓存校验形同虚设）；掉线提示并禁止提交、作业不排队。
      // client 构建保持现状（可选离线设备排队）。
      if (isServerUi) {
        try {
          const fresh = await serverApi.listDevices()
          const dev = fresh.find((d) => d.deviceId === targetDeviceId)
          if (!dev || dev.status !== 'Online') {
            setDevices(fresh)
            const msg = t('errors.deviceOffline')
            setError(msg)
            app.setStatus(msg)
            return
          }
        } catch (err) {
          const msg = err instanceof ApiError ? err.message : t('errors.deviceCheckFailed')
          setError(msg)
          app.setStatus(msg)
          return
        }
      }
      const req = buildRequest(labels, 'job')
      if (!req) return
      const j = await submitBiz.submitJob(req)
      app.setDraftJobId(j.jobId)
      app.setStatus(t('status.submitted', { count: labels.length, id: j.jobId.slice(0, 8) }))
    } catch (err) {
      const msg = err instanceof ApiError ? err.message : t('errors.submitFailed')
      setError(msg)
      app.setStatus(msg)
    } finally {
      setSubmitting(false)
    }
  }

  const downloadDebug = async (labels: { data: Record<string, string> }[], batch: boolean) => {
    const req = buildRequest(labels, 'debug')
    if (!req) return
    setSubmitting(true)
    try {
      const { blob, filename } = batch ? await biz.renderImages(req) : await biz.renderImage(req)
      downloadBlob(blob, filename)
      app.setStatus(t('status.imageDownloaded', { filename }))
    } catch (err) {
      app.setStatus(err instanceof ApiError ? err.message : t('errors.renderFailed'))
    } finally {
      setSubmitting(false)
    }
  }

  /** 测试打印 / 出图的单张数据：有字段模板提交当前表单值（含预填）；
   * 无字段模板（静态标签，·）提交空字典——即使模板包携带遗留 testData 也不外带
   *  后端受理 / 校验 / 渲染三层均支持空 data，静态内容按版式原样输出。 */
  const singleTestData = () => (formFields.length === 0 ? {} : { ...values })

  /* * 调试关：打印测试（单张）提交正常作业（无字段拦截守卫已随 移除）。 */
  const testPrint = () => {
    if (!pkg) return
    void submit([{ data: singleTestData() }])
  }

  /** 调试开：单张出图下载（后端渲染 PNG，不建作业不发驱动）。 */
  const debugSingle = () => {
    if (!pkg) return
    void downloadDebug([{ data: singleTestData() }], false)
  }

  /* * 调试关：图片预览（· 决议）——实时调用 render-image 取当前字段值的渲染图
   *  页内弹层呈现（不建作业、不自动下载）；下载由弹层内「下载」按钮显式触发，预览与下载分离。 */
  const previewImage = () => {
    if (!pkg) return
    const req = buildRequest([{ data: singleTestData() }], 'debug')
    if (!req) return
    const gen = ++previewGenRef.current
    setImagePreview({ status: 'loading' })
    biz
      .renderImage(req)
      .then(({ blob, filename }) => {
        if (gen !== previewGenRef.current) return // 弹层已关闭 / 已再次预览：丢弃本次结果
        setImagePreview({ status: 'ready', url: URL.createObjectURL(blob), blob, filename })
      })
      .catch((err: unknown) => {
        if (gen !== previewGenRef.current) return
        setImagePreview({ status: 'error', message: err instanceof ApiError ? err.message : t('errors.renderFailed') })
      })
  }

  /** 关闭预览弹层：释放 blob URL 并作废在途请求。 */
  const closeImagePreview = () => {
    previewGenRef.current += 1
    setImagePreview((prev) => {
      if (prev?.status === 'ready') URL.revokeObjectURL(prev.url)
      return null
    })
  }

  /** 弹层内显式「下载」：下载当前预览的图片（blob 已在弹层态持有，不重新请求）。 */
  const downloadImagePreview = () => {
    if (imagePreview?.status !== 'ready') return
    downloadBlob(imagePreview.blob, imagePreview.filename)
    app.setStatus(t('status.imageDownloaded', { filename: imagePreview.filename }))
  }

  const [excelTplBusy, setExcelTplBusy] = useState(false)

  /* * 下载 Excel 模板：按当前模板契约字段（显示名表头）+ testData（示例行）生成 xlsx，直接套用导入。 */
  const downloadExcelTemplate = async () => {
    if (!pkg) return
    setExcelTplBusy(true)
    try {
      const fields = pkg.contract.fields ?? []
      const columns = fields.map((f) => ({ key: f.key, displayName: f.displayName || f.key }))
      const sampleRow: Record<string, string> = {}
      for (const f of fields) {
        const v = pkg.testData?.[f.key]
        if (v !== undefined && v !== null) sampleRow[f.key] = v
      }
      const { blob, filename } = await biz.excelTemplate(columns, sampleRow)
      downloadBlob(blob, filename)
      app.setStatus(t('status.excelDownloaded', { filename }))
    } catch (err) {
      app.setStatus(err instanceof ApiError ? err.message : t('errors.excelTplFailed'))
    } finally {
      setExcelTplBusy(false)
    }
  }

  const pickExcel = async (file: File) => {
    setImporting(true)
    setError(null)
    try {
      const r = await biz.importExcel(file)
      if (r.headers.length === 0) {
        app.setStatus(t('status.excelNoHeaders'))
        return
      }
      setExcel({ headers: r.headers, rows: r.rows, file: file.name })
      setMapping(suggestMapping(r.headers, mappingFields))
      setMappingOpen(true)
    } catch (err) {
      app.setStatus(err instanceof ApiError ? err.message : t('errors.excelParseFailed'))
    } finally {
      setImporting(false)
    }
  }

  const confirmMapping = () => {
    if (!excel || !pkg) return
    const dup = findDuplicateKeys(mapping)
    if (dup.length > 0) {
      app.setStatus(t('status.dupMapped', { fields: dup.join(localeSep()) }))
      return
    }
    const labels = excel.rows.map((row) => ({ data: rowToData(excel.headers, row, mapping) }))
    setMappingOpen(false)
    if (debugMode) {
      app.setStatus(t('status.generatingImages', { count: labels.length }))
      void downloadDebug(labels, true)
    } else {
      app.setStatus(t('status.batchSubmitting', { count: labels.length }))
      void submit(labels)
    }
  }

  return (
    <div className="page">
      <div className="page-head">
        <div className="page-title">
          {t('title')}
          <small>{t('subtitle')}</small>
        </div>
        <div className="spacer" />
        {/* data-guide：首次引导第 3 步锚点（迭代 119 / #276） */}
        <select
          className="input"
          data-guide="dataprint-template-select"
          value={selectedName}
          onChange={(ev) => app.setDraftSelected(ev.target.value)}
          style={{ minWidth: 180 }}
        >
          {templates.length === 0 && <option value="">{t('tplNoneOption')}</option>}
          {selectedName && !templates.some((tp) => tp.name === selectedName) && <option value={selectedName}>{selectedName}</option>}
          {templates.map((tp) => (
            <option key={tp.name} value={tp.name}>
              {tp.name}
            </option>
          ))}
        </select>
          <button
            className="btn"
            onClick={() => void downloadExcelTemplate()}
            disabled={!pkg || formFields.length === 0 || excelTplBusy}
            title={!pkg || formFields.length === 0 ? t('excelTpl.noFieldsTitle') : t('excelTpl.title')}
          >
            <Icon name="download" size={13} />
            {excelTplBusy ? t('excelTpl.generating') : t('excelTpl.download')}
          </button>
        {/* data-guide：首次引导第 4 步锚点（迭代 119 / #276） */}
        <button
          className="btn"
          data-guide="dataprint-excel-import"
          onClick={() => document.getElementById('excelFile')?.click()}
          disabled={!pkg || importing || submitting}
        >
          <Icon name="upload" size={13} />
          {t('excelImport')}
        </button>
        <input
          id="excelFile"
          type="file"
          accept=".xlsx"
          style={{ display: 'none' }}
          onChange={(ev) => {
            const f = ev.target.files?.[0]
            if (f) void pickExcel(f)
            ev.target.value = ''
          }}
        />
      </div>

      {/* 连接状态徽标（迭代 18 F5）：本机连接（Client 传输方式）与「已加入服务端」（设备注册状态）各自含义。
          本页对服务端的状态 = 设备是否已加入服务端设备列表
          （hostInList，随探测周期刷新），不再用「已连接 / 未连接」（评审 #114 B-9 / C-3 一词三义与同屏矛盾）。
          迭代 20：server 构建隐藏（本机连接 = 打印机相关内容；加入状态在目标设备行显示） */}
      {!isServerUi && (
        <div
          style={{ display: 'flex', alignItems: 'center', gap: 14, padding: '6px 16px', borderBottom: '1px solid var(--line)', flexWrap: 'wrap' }}
          title={t('conn.badgesTitle')}
        >
          <span className="hint" style={{ display: 'inline-flex', alignItems: 'center', gap: 6 }}>
            {t('conn.local')}
            <span className="badge">{formatTransport(app.transportConfig) || app.transport || t('value.unknown')}</span>
          </span>
          <span className="hint" style={{ display: 'inline-flex', alignItems: 'center', gap: 6 }}>
            {t('conn.server')}
            <span className={'conn' + (deviceMode === 'server' && hostInList ? ' on' : ' off')} style={{ display: 'inline-flex', alignItems: 'center', gap: 4 }}>
              <span className={'status-dot' + (deviceMode === 'server' && hostInList ? ' on' : '')} />
              {routeMode === 'loading' ? t('conn.detecting') : deviceMode === 'server' && hostInList ? t('conn.joined') : t('conn.notJoined')}
            </span>
          </span>
        </div>
      )}

      {deviceMode === 'server' && (
        <div style={{ display: 'flex', alignItems: 'center', gap: 10, padding: '6px 16px', borderBottom: '1px solid var(--line)', flexWrap: 'wrap' }}>
          <span className="hint">{t('target.label')}</span>
          {isServerUi ? (
            <>
              <select
                className="input"
                aria-label={t('target.label')}
                value={targetDeviceId}
                onChange={(ev) => setTargetDeviceId(ev.target.value)}
                style={{ minWidth: 240 }}
                title={t('target.selectTitle')}
              >
                {devices.length === 0 && <option value="">{t('target.noneOption')}</option>}
                {devices.length > 0 && !targetDeviceId && <option value="">{t('target.pickOption')}</option>}
                {devices.map((d) => (
                  <option key={d.deviceId} value={d.deviceId} disabled={d.status !== 'Online'} title={d.status !== 'Online' ? t('target.offlineTitle', { time: formatLastSeen(d.lastSeenAt) }) : undefined}>
                    {t('target.optionLabel', { name: d.name, status: deviceStatusLabel(d.status) })}
                    {d.status !== 'Online' ? t('target.lastSeenSuffix', { time: formatLastSeen(d.lastSeenAt) }) : ''}
                  </option>
                ))}
              </select>
              {devices.length === 0 ? (
                <span className="badge warn">{t('target.noDevicesHint')}</span>
              ) : targetDeviceId ? (
                <span className="hint">{t('target.onlineOnlyHint')}</span>
              ) : (
                <span className="badge warn">{t('target.noOnlineBadge')}</span>
              )}
            </>
          ) : (
            // （A）：客户端构建目标设备固定本机——只显示本机标签，无设备选择器
            // 本机未注册 / 离线时降级本机直连并提示原因
            <>
              <span className="badge ok" style={{ display: 'inline-flex', alignItems: 'center', gap: 6 }}>
                <Icon name="printer" size={12} />
                {t('target.localBadge', { name: app.hostDeviceName || app.hostDeviceId || t('value.unknown') })}
              </span>
              {routeMode === 'server' ? (
                <span className="hint">{t('target.joinedServerHint')}</span>
              ) : (
                <span className="badge warn">{!hostInList ? t('target.notJoinedBadge') : t('target.offlineBadge')}</span>
              )}
            </>
          )}
        </div>
      )}

      {error && <div className="banner error">{error}</div>}

      <div className="grid-2" style={{ flex: 1, overflow: 'auto', padding: 12 }}>
        <div style={{ display: 'flex', flexDirection: 'column', gap: 12 }}>
          <div className="panel">
            <div className="panel-head">
              {t('testData.title')}
              <span className="hint" style={{ marginLeft: 6 }}>
                {pkg ? t('testData.fieldsFor', { name: pkg.name }) : t('testData.fieldsPlain')}
              </span>
            </div>
            <div className="panel-body" style={{ display: 'flex', flexDirection: 'column', gap: 8 }}>
              {loading ? (
                <div className="hint">{t('state.loading')}</div>
              ) : !pkg ? (
                <div className="hint">{t('testData.pickTemplateHint')}</div>
              ) : (
                <>
                  {formFields.length === 0 ? (
                    // 无字段模板 = 静态标签（合法模板），说明性提示替代旧「不允许」语义
                    // 操作区（调试开关 / 打印测试 / 出图预览）照常渲染，行为与有字段模板一致
                    <div className="hint">{t('testData.staticTemplateHint')}</div>
                  ) : (
                    formFields.map((f) => {
                      const label = f.displayName || f.key
                      return (
                        <label className="field" key={f.key} title={f.displayName && f.displayName !== f.key ? t('testData.fieldKeyTitle', { key: f.key }) : undefined}>
                          {label}
                          <input
                            className="input mono"
                            value={values[f.key] ?? ''}
                            placeholder={t('testData.fieldPlaceholder', { label })}
                            onChange={(ev) => setFieldValue(f.key, ev.target.value)}
                          />
                        </label>
                      )
                    })
                  )}
                  <label className="field" style={{ flexDirection: 'row', alignItems: 'center', gap: 6, marginTop: 2 }}>
                    <input type="checkbox" checked={debugMode} onChange={(ev) => app.setDraftDebug(ev.target.checked)} />
                    {t('testData.debugToggle')}
                  </label>
                  <div style={{ display: 'flex', gap: 8, marginTop: 4, flexWrap: 'wrap' }}>
                    <button
                      className="btn primary"
                      onClick={debugMode ? debugSingle : testPrint}
                      disabled={submitting || !pkg || (isServerUi && !targetDeviceId)}
                      title={
                        debugMode
                          ? t('printBtn.titleDebug')
                          : isServerUi
                            ? t('printBtn.titleServerUi')
                            : deviceMode === 'server'
                              ? routeMode === 'server'
                                ? t('printBtn.titleServerRoute')
                                : t('printBtn.titleDirectRoute')
                              : t('printBtn.titleStandalone')
                      }
                    >
                      <Icon name="printer" size={13} />
                      {submitting ? t('printBtn.working') : debugMode ? t('printBtn.debugSingle') : t('printBtn.testSingle')}
                    </button>
                    {!debugMode && (
                      <button className="btn" onClick={previewImage} disabled={submitting || !pkg} title={t('previewBtn.title')}>
                        <Icon name="preview" size={13} />
                        {t('previewBtn.label')}
                      </button>
                    )}
                    {excel && (
                      <button className="btn" onClick={() => setMappingOpen(true)} disabled={!excel}>
                        {t('remapBtn', { file: excel.file })}
                      </button>
                    )}
                  </div>
                  {/* 原生指令模式连接提示（迭代 78，DESIGN §5.4.2）：本机连接为 native 时出纸由打印机指令渲染，界面预览（图片口径）不代表实际效果 */}
                  {!isServerUi && isNativePrintMode(app.transportConfig) && <NativePrintModeHint />}
                  <div className="hint">
                    {debugMode
                      ? t('hint.debugMode')
                      : formFields.length === 0
                        ? t('hint.static')
                        : isServerUi
                        ? t('hint.serverUi')
                        : deviceMode === 'server'
                          ? routeMode === 'server'
                            ? t('hint.serverRoute')
                            : t('hint.directRoute')
                          : t('hint.standalone')}
                  </div>
                </>
              )}
            </div>
          </div>
        </div>

        <JobPanel
          job={job}
          error={jobError}
          retry={retry}
          debugMode={debugMode}
          canRetry={!isServerUi}
          resolveDeviceName={resolveDeviceName}
          onOpenJobHistory={onOpenJobHistory}
        />
      </div>

      {mappingOpen && excel && pkg && (
        <MappingModal
          headers={excel.headers}
          rows={excel.rows}
          keys={mappingFields}
          mapping={mapping}
          setMapping={setMapping}
          onCancel={() => setMappingOpen(false)}
          onConfirm={confirmMapping}
          debugMode={debugMode}
        />
      )}

      {imagePreview && <ImagePreviewModal preview={imagePreview} onDownload={downloadImagePreview} onClose={closeImagePreview} />}
    </div>
  )
}

function MappingModal({
  headers,
  rows,
  keys,
  mapping,
  setMapping,
  onCancel,
  onConfirm,
  debugMode,
}: {
  headers: string[]
  rows: string[][]
  keys: MappingField[]
  mapping: string[]
  setMapping: (m: string[]) => void
  onCancel: () => void
  onConfirm: () => void
  debugMode: boolean
}) {
  // 弹窗文案 key 化（dataPrint 域 mapping.*；取消按钮经 common 兜底）
  const { t } = useTranslation('dataPrint')
  const [suggested, setSuggested] = useState<string[]>([])
  useEffect(() => {
    setSuggested(suggestMapping(headers, keys))
  }, [headers, keys])
  const complete = isMappingComplete(mapping)
  const dup = findDuplicateKeys(mapping)

  return (
    <Modal
      title={t('mapping.title', { count: rows.length })}
      onClose={onCancel}
      width={640}
      footer={
        <>
          <button className="btn" onClick={onCancel}>
            {t('action.cancel')}
          </button>
          <button
            className="btn sm"
            onClick={() => setMapping(suggested)}
            disabled={suggested.every((k) => !k)}
            title={t('mapping.autoMatchTitle')}
          >
            {t('mapping.autoMatch')}
          </button>
          <button className="btn primary" onClick={onConfirm} disabled={!complete || dup.length > 0} title={debugMode ? t('mapping.confirmDebugTitle') : t('mapping.confirmTitle')}>
            <Icon name={debugMode ? 'download' : 'printer'} size={13} />
            {debugMode ? t('mapping.downloadImages', { count: rows.length }) : t('mapping.batchPrint', { count: rows.length })}
          </button>
        </>
      }
    >
      <div className="hint">
        {t('mapping.hint')}
        {debugMode && <span className="hint">{t('mapping.debugHint')}</span>}
        {dup.length > 0 && <span className="error-text">{t('mapping.dupError', { fields: dup.join(i18next.language === 'en' ? ', ' : '、') })}</span>}
      </div>
      <table className="table">
        <thead>
          <tr>
            <th style={{ width: 44 }}>{t('mapping.colColumn')}</th>
            <th>{t('mapping.excelColumn')}</th>
            <th style={{ width: 200 }}>{t('mapping.templateField')}</th>
            <th>{t('mapping.sampleColumn')}</th>
          </tr>
        </thead>
        <tbody>
          {headers.map((h, i) => (
            <tr key={i} style={{ cursor: 'default' }}>
              <td className="mono" style={{ color: 'var(--ink-3)' }}>{i + 1}</td>
              <td style={{ fontWeight: 600 }}>{h || t('mapping.emptyColumn', { index: i + 1 })}</td>
              <td>
                <select className="input" style={{ width: '100%' }} value={mapping[i] ?? ''} onChange={(ev) => setMapping(mapping.map((m, j) => (j === i ? ev.target.value : m)))}>
                  <option value="">{t('mapping.noMap')}</option>
                  {keys.map((f) => {
                    const key = typeof f === 'string' ? f : f.key
                    const label = typeof f === 'string' || !f.displayName ? key : t('mapping.fieldOption', { displayName: f.displayName, key })
                    return (
                      <option key={key} value={key}>
                        {label}
                      </option>
                    )
                  })}
                </select>
              </td>
              <td className="mono" style={{ color: 'var(--ink-2)', maxWidth: 160, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>
                {rows[0]?.[i] ?? ''}
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </Modal>
  )
}

/**
 * 图片预览弹层（· 决议）：与工作台缩略图灯箱同交互（居中大图，Esc / 点背景 / 点 × 关闭
 * 点卡片本体不关闭）；底部操作行提供显式「下载」按钮——预览与下载是两个独立动作，不再混用同一次点击。
 * 复用工作台灯箱样式（preview-modal*），仅操作行与图片高度为本弹层专有（不影响工作台）。
 */
function ImagePreviewModal({
  preview,
  onDownload,
  onClose,
}: {
  preview: ImagePreview
  onDownload: () => void
  onClose: () => void
}) {
  useEffect(() => {
    const onKey = (ev: KeyboardEvent) => {
      if (ev.key === 'Escape') onClose()
    }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [onClose])

  // 弹窗文案 key 化（dataPrint 域 imagePreview.*）
  const { t } = useTranslation('dataPrint')
  return (
    <div className="preview-modal" onClick={onClose}>
      <div className="preview-modal-card" role="dialog" aria-label={t('imagePreview.title')} onClick={(ev) => ev.stopPropagation()}>
        <div className="preview-modal-title">
          {t('imagePreview.title')}
          <span className="spacer" style={{ flex: 1 }} />
          <button className="preview-modal-close" onClick={onClose} title={t('imagePreview.closeTitle')} aria-label={t('imagePreview.closeAria')}>
            <Icon name="x" size={13} />
          </button>
        </div>
        {preview.status === 'loading' ? (
          <div className="preview-modal-state">
            <Icon name="refresh" size={13} />
            {t('imagePreview.generating')}
          </div>
        ) : preview.status === 'error' ? (
          <div className="preview-modal-state err">
            <Icon name="alert" size={13} />
            {t('imagePreview.unavailable')}
            <small>{preview.message}</small>
          </div>
        ) : (
          <>
            <img
              className="preview-modal-img"
              style={{ maxHeight: 'calc(84vh - 96px)' }}
              src={preview.url}
              alt={t('imagePreview.imgAlt')}
            />
            <div className="preview-modal-actions">
              <button className="btn sm" onClick={onDownload} title={t('imagePreview.downloadTitle')}>
                <Icon name="download" size={12} />
                {t('imagePreview.download')}
              </button>
              <span className="hint" style={{ fontSize: 10 }}>
                {t('imagePreview.footHint')}
              </span>
            </div>
          </>
        )}
      </div>
    </div>
  )
}
