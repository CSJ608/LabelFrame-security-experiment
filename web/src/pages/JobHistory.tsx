// 作业历史页：服务端 / 本机作业列表（GET /api/jobs?limit=100），终态 / 进行中徽标；单机降级显示本机作业列表，空态文案按模式区分。
// 自动轮询：存在进行中作业时 1.5s 轮询，列表全终态即停；页面隐藏暂停、恢复可见立即拉取；失败保留既有列表 2s 退避。
// 「目标设备」列显示设备名（无可解析名称回退设备 ID）；编号收敛——仅「作业编号」可见可复制，「请求编号」收进悬停提示。
// 行可展开明细：本机直连作业逐张明细；服务端下发作业（无 items）显示汇总并如实注明。

import { Fragment, useCallback, useEffect, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { localApi, serverApi } from '../lib/api/client'
import { ApiError } from '../lib/api/types'
import type { JobView } from '../lib/api/types'
import { copyText } from '../lib/clipboard'
import { deviceDisplayName } from '../lib/deviceDisplay'
import { useJobStatusLabel } from '../lib/jobStatus'
import { useApp } from '../state/AppContext'
import { isServerUi } from '../lib/uiMode'
import { Icon } from '../components/Icon'

const isTerminal = (s: string) => s === 'Completed' || s === 'Failed' || s === 'Cancelled' || s === 'Expired'

/* * ：轮询节奏（对齐 DataPrint useJobPolling——1.5s 常规 / 2s 失败退避）。 */
const POLL_INTERVAL_MS = 1500
const POLL_ERROR_RETRY_MS = 2000

/** 时间列：本地时间 MM-dd HH:mm:ss。 */
function formatTime(iso?: string): string {
  if (!iso) return '—'
  const d = new Date(iso)
  if (Number.isNaN(d.getTime())) return '—'
  const p = (n: number) => String(n).padStart(2, '0')
  return `${p(d.getMonth() + 1)}-${p(d.getDate())} ${p(d.getHours())}:${p(d.getMinutes())}:${p(d.getSeconds())}`
}

/** 状态徽标样式类（主行 / 明细同口径）。 */
function statusBadgeClass(status: string): string {
  return 'badge ' + (status === 'Completed' ? 'ok' : status === 'Failed' ? 'err' : isTerminal(status) ? 'neutral' : 'info')
}

/** 行展开明细：本机直连作业（WinHost 返回逐张 items）逐张表（每张状态 / 失败原因，errorMessage 回退
 * errorCode；Log 模拟打印附出图目录与张数）；服务端下发作业（无 items）汇总呈现＋如实占位说明。
 */
function JobDetail({ job }: { job: JobView }) {
  const { t } = useTranslation('jobHistory')
  const jobLabel = useJobStatusLabel()
  if (job.items && job.items.length > 0) {
    return (
      <div style={{ display: 'flex', flexDirection: 'column', gap: 8 }}>
        <div className="hint">{t('detail.itemsTitle', { count: job.items.length })}</div>
        <table className="table">
          <thead>
            <tr>
              <th style={{ width: 50 }}>#</th>
              <th style={{ width: 90 }}>{t('column.status')}</th>
              <th>{t('job.failedReason')}</th>
            </tr>
          </thead>
          <tbody>
            {job.items.map((it) => (
              <tr key={it.index} style={{ cursor: 'default' }}>
                <td className="mono">{it.index + 1}</td>
                <td>
                  <span className={statusBadgeClass(it.status)}>{jobLabel(it.status)}</span>
                </td>
                {/* 失败原因与数据与打印进度区同口径：仅失败张呈现原因（errorMessage 优先，回退 errorCode） */}
                <td style={{ fontSize: 12, color: it.status === 'Failed' ? 'var(--danger)' : undefined }}>
                  {it.status === 'Failed' ? it.errorMessage || it.errorCode || t('job.unknownError') : '—'}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
        {job.printImageDir && (
          <div className="hint" style={{ wordBreak: 'break-all' }}>
            {t('detail.simPrintDir', { dir: job.printImageDir, count: job.printImageCount ?? 0 })}
          </div>
        )}
      </div>
    )
  }
  const failedCount = job.failedItems ?? 0
  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 6 }}>
      <div style={{ fontSize: 12.5, display: 'flex', alignItems: 'center', gap: 10, flexWrap: 'wrap' }}>
        <span className={statusBadgeClass(job.status)}>{jobLabel(job.status)}</span>
        <span>
          {t('detail.progress', { completed: job.completedItems, total: job.totalItems })}
          {failedCount > 0 && <span style={{ color: 'var(--danger)' }}>{t('detail.failedPart', { count: failedCount })}</span>}
        </span>
      </div>
      <div style={{ fontSize: 12.5 }}>
        {t('detail.failedReasonLabel')}
        {job.errorMessage ? (
          <span style={{ color: 'var(--danger)' }}>{job.errorMessage}</span>
        ) : (
          <span className="hint">{t('detail.noError')}</span>
        )}
      </div>
      {/* 如实占位，不伪装有逐张数据（设备侧出图回传属未来能力） */}
      <div className="hint">{t('detail.serverJobNote')}</div>
    </div>
  )
}

export function JobHistory() {
  const app = useApp()
  const { serverMode } = app
  const { t } = useTranslation('jobHistory')
  const jobLabel = useJobStatusLabel()
  // 业务 API 跟随模式：服务端 = serverApi；单机降级 = localApi（本机 WinHost 作业列表）
  const biz = serverMode === 'server' ? serverApi : localApi
  const [jobs, setJobs] = useState<JobView[] | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [loading, setLoading] = useState(false)
  /* * ：当前展开明细的作业（单展开——切行即收起上一行，明细随轮询刷新保持最新）。 */
  const [expandedJobId, setExpandedJobId] = useState<string | null>(null)
  /** 轮询定时器（存在进行中作业时续排；全终态即停）。 */
  const timerRef = useRef<ReturnType<typeof setTimeout> | null>(null)
  /** 最近一次成功列表（失败退避判断是否仍有进行中作业需要重试）。 */
  const jobsRef = useRef<JobView[] | null>(null)

  // 作业历史可见性——客户端构建在服务端模式下传本机 deviceId（只看自己的作业）
  // 服务端构建不传（看全部）；单机降级看本机历史本就不传。
  const deviceFilter = isServerUi || serverMode !== 'server' ? undefined : (app.hostDeviceId ?? undefined)

  /* * 设备名解析表（deviceId → name，与在线设备页 / 目标设备下拉同源）：服务端模式拉取一次
   *  （设备名稳定，轮询不重复拉）；失败静默回退设备 ID（辅助显示，不阻断列表、不出横幅）；
   *  单机降级不拉取（本机历史作业无 targetDeviceId，显示「本机」）。 */
  const [deviceNames, setDeviceNames] = useState<Record<string, string>>({})
  useEffect(() => {
    if (serverMode !== 'server') return
    let cancelled = false
    serverApi
      .listDevices()
      .then((list) => {
        if (!cancelled) setDeviceNames(Object.fromEntries(list.map((d) => [d.deviceId, d.name])))
      })
      .catch(() => {})
    return () => {
      cancelled = true
    }
  }, [serverMode])

  const clearTimer = useCallback(() => {
    if (timerRef.current) {
      clearTimeout(timerRef.current)
      timerRef.current = null
    }
  }, [])

  const load = useCallback(
    async (opts?: { silent?: boolean }) => {
      // 周期轮询在页面隐藏时暂停（恢复可见由 visibilitychange 立即拉取，不走此分支）
      if (opts?.silent && document.hidden) return
      if (!opts?.silent) setLoading(true)
      /** 本轮结束后是否续排下一轮轮询。 */
      let keepPolling = false
      let failed = false
      try {
        const list = await biz.getJobs(100, deviceFilter)
        jobsRef.current = list
        setJobs(list)
        setError(null)
        keepPolling = list.some((j) => !isTerminal(j.status))
      } catch (err) {
        // 轮询失败不清空既有列表（瞬时错误只出横幅）；已知存在进行中作业则退避重试
        failed = true
        setJobs((prev) => prev ?? [])
        setError(err instanceof ApiError ? err.message : t('errors.load'))
        keepPolling = jobsRef.current?.some((j) => !isTerminal(j.status)) ?? false
      } finally {
        if (!opts?.silent) setLoading(false)
      }
      clearTimer()
      if (keepPolling) {
        timerRef.current = setTimeout(() => void load({ silent: true }), failed ? POLL_ERROR_RETRY_MS : POLL_INTERVAL_MS)
      }
    },
    [biz, deviceFilter, clearTimer, t],
  )

  useEffect(() => {
    if (serverMode === 'unknown') return
    jobsRef.current = null
    void load()
    // 页面隐藏暂停轮询；恢复可见立即拉取一次（是否续排由结果决定）
    const onVisibility = () => {
      if (document.visibilityState === 'visible') {
        clearTimer()
        void load({ silent: true })
      }
    }
    document.addEventListener('visibilitychange', onVisibility)
    return () => {
      document.removeEventListener('visibilitychange', onVisibility)
      clearTimer()
    }
  }, [serverMode, load, clearTimer])

  return (
    <div className="page">
      <div className="page-head">
        <div className="page-title">
          {t('title')}
          <small>{t('subtitle')}</small>
        </div>
        <div className="spacer" />
        <button className="btn" onClick={() => void load()} disabled={loading || serverMode === 'unknown'} title={t('reloadTitle')}>
          <Icon name="refresh" size={13} />
          {loading ? t('action.refreshing') : t('action.refresh')}
        </button>
      </div>

      {error && <div className="banner error">{error}</div>}

      <div style={{ flex: 1, overflow: 'auto', padding: 12 }}>
        {serverMode === 'unknown' ? (
          <div className="empty">
            <Icon name="data" />
            <div className="empty-title">{t('state.connectingServer')}</div>
            <div className="hint">{t('empty.standaloneHint')}</div>
          </div>
        ) : !jobs || jobs.length === 0 ? (
          <div className="empty">
            <Icon name="data" />
            <div className="empty-title">{t('empty.title')}</div>
            <div className="hint">{serverMode === 'server' ? t('empty.serverRetention') : t('empty.localRetention')}</div>
          </div>
        ) : (
          <table className="table">
            <thead>
              <tr>
                {/* 行展开明细开关列（无标题——箭头自明） */}
                <th style={{ width: 30 }} aria-hidden="true" />
                <th style={{ width: 150 }}>{t('column.time')}</th>
                <th style={{ width: 150 }}>{t('column.jobId')}</th>
                <th style={{ width: 140 }}>{t('column.targetDevice')}</th>
                <th style={{ width: 90 }}>{t('column.status')}</th>
                <th style={{ width: 110 }}>{t('column.completedFailed')}</th>
                <th>{t('job.failedReason')}</th>
              </tr>
            </thead>
            <tbody>
              {jobs.map((j) => {
                const expanded = expandedJobId === j.jobId
                return (
                  <Fragment key={j.jobId}>
                    <tr
                      style={{ cursor: 'pointer' }}
                      title={t('row.expandTitle')}
                      onClick={() => setExpandedJobId((prev) => (prev === j.jobId ? null : j.jobId))}
                    >
                      <td>
                        {/* 展开开关（无独立 onClick——点击冒泡至行统一切换）；键盘可达：聚焦后 Enter / Space 触发点击同样冒泡 */}
                        <button
                          type="button"
                          className="btn sm ghost"
                          aria-expanded={expanded}
                          title={expanded ? t('row.collapseDetail') : t('row.expandDetail')}
                        >
                          <Icon
                            name="chevron"
                            size={12}
                            style={{ transform: expanded ? 'rotate(180deg)' : undefined, transition: 'transform 120ms' }}
                          />
                        </button>
                      </td>
                      <td className="mono">{formatTime(j.createdAt)}</td>
                      {/* 编号收敛：仅「作业编号」可见 + 一键复制（报障口径统一）；
                          「请求编号」收进悬停提示（title 携带两个完整编号）。 */}
                      <td>
                        <button
                          type="button"
                          className="btn sm ghost mono"
                          style={{ fontSize: 12 }}
                          title={t('row.copyTitle', { jobId: j.jobId, requestId: j.requestId })}
                          onClick={(ev) => {
                            ev.stopPropagation() // 复制不触发行展开切换
                            void copyText(j.jobId).then((ok) =>
                              app.setStatus(ok ? t('row.copied', { jobId: j.jobId }) : t('row.copyFailed')),
                            )
                          }}
                        >
                          {j.jobId.slice(0, 8)}
                          <Icon name="copy" size={12} />
                        </button>
                      </td>
                      <td className="mono" style={{ fontSize: 12 }}>
                        {j.targetDeviceId ? deviceDisplayName(deviceNames[j.targetDeviceId], j.targetDeviceId) : t('device.local')}
                      </td>
                      <td>
                        <span className={statusBadgeClass(j.status)}>{jobLabel(j.status)}</span>
                      </td>
                      <td className="mono" style={{ fontSize: 12 }}>
                        {j.completedItems}/{j.totalItems}
                        {(j.failedItems ?? 0) > 0 && (
                          <span style={{ color: 'var(--danger)' }}>{t('detail.failedCountCell', { count: j.failedItems ?? 0 })}</span>
                        )}
                      </td>
                      <td style={{ color: 'var(--danger)', fontSize: 12 }}>{j.errorMessage ?? ''}</td>
                    </tr>
                    {expanded && (
                      <tr>
                        <td colSpan={7} style={{ background: '#fafbfc', padding: '10px 14px 14px 46px' }}>
                          <JobDetail job={j} />
                        </td>
                      </tr>
                    )}
                  </Fragment>
                )
              })}
            </tbody>
          </table>
        )}
      </div>
    </div>
  )
}
