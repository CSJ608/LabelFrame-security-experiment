// 在线设备页（Server UI 专用）：GET /api/devices 每 5s 自动刷新
// 列表显示 deviceId / 名称 / lastIp / 在线状态 / 最近心跳；点击设备设为「数据与打印」默认目标
// （localStorage labelframe.defaultTargetDeviceId，AppContext 共享状态，跨页联动）。
// 文案 key 化（devices 域；zh-CN 值与原硬编码逐字一致）。

import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { serverApi } from '../lib/api/client'
import { ApiError } from '../lib/api/types'
import type { DeviceView } from '../lib/api/types'
import { useApp } from '../state/AppContext'
import { Icon } from '../components/Icon'

const POLL_MS = 5000

/** 最近心跳：本地时间 MM-dd HH:mm:ss。 */
function formatTime(iso?: string): string {
  if (!iso) return '—'
  const d = new Date(iso)
  if (Number.isNaN(d.getTime())) return '—'
  const p = (n: number) => String(n).padStart(2, '0')
  return `${p(d.getMonth() + 1)}-${p(d.getDate())} ${p(d.getHours())}:${p(d.getMinutes())}:${p(d.getSeconds())}`
}

export function Devices() {
  const app = useApp()
  const { t } = useTranslation('devices')
  // （决议 a 案）：初始 null = 首次拉取未完成（加载中文案行），[] = 确为空——
  // 与插件管理页同一模式，消除进页瞬间闪现「暂无设备」的误导。
  const [devices, setDevices] = useState<DeviceView[] | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [notice, setNotice] = useState<string | null>(null)
  const [running, setRunning] = useState(true)

  useEffect(() => {
    let stopped = false
    let timer: ReturnType<typeof setTimeout> | null = null

    const tick = async () => {
      try {
        // 全量轮询替换（在线窗口 30s + 2s 偏差，5s 轮询下最坏约 37s 翻转；不要求即时）
        const list = await serverApi.listDevices()
        if (!stopped) {
          setDevices(list)
          setError(null)
        }
      } catch (err) {
        if (!stopped) setError(err instanceof ApiError ? err.message : t('loadFailed'))
      } finally {
        if (!stopped) timer = setTimeout(() => void tick(), POLL_MS)
      }
    }

    void tick()
    return () => {
      stopped = true
      if (timer) clearTimeout(timer)
    }
  }, [running, t])

  /** 点击设备设为数据与打印默认目标（仅在线设备可设，避免与「仅在线可选」语义冲突）；页面内提示 + 状态栏/日志。 */
  const pick = (d: DeviceView) => {
    if (d.status !== 'Online') {
      const msg = t('pickOffline', { name: d.name || d.deviceId })
      setNotice(msg)
      app.setStatus(msg)
      return
    }
    const msg = t('picked', { name: d.name || d.deviceId })
    setNotice(msg)
    app.setDefaultTargetDeviceId(d.deviceId)
    app.setStatus(msg)
  }

  const isDefault = (d: DeviceView) => app.defaultTargetDeviceId === d.deviceId

  return (
    <div className="page">
      <div className="page-head">
        <div className="page-title">
          {t('page.title')}
          <small>{t('page.subtitle')}</small>
        </div>
        <div className="spacer" />
        <button
          className={'btn' + (running ? ' active' : '')}
          onClick={() => setRunning(!running)}
          title={running ? t('pauseAuto') : t('resumeAuto')}
        >
          <Icon name={running ? 'preview' : 'refresh'} size={13} />
          {running ? t('autoRefresh') : t('paused')}
        </button>
        <button className="btn" onClick={() => void serverApi.listDevices().then(setDevices).catch((err) => setError(err instanceof ApiError ? err.message : t('loadFailed')))} title={t('refreshNowTitle')}>
          <Icon name="refresh" size={13} />
          {t('action.refresh')}
        </button>
      </div>

      {error && <div className="banner error">{error}</div>}
      {notice && <div className="banner notice">{notice}</div>}

      <div style={{ flex: 1, overflow: 'auto', padding: 12 }}>
        {devices === null ? (
          <div className="empty">
            <Icon name="refresh" />
            <div className="empty-title">{t('loading')}</div>
          </div>
        ) : devices.length === 0 ? (
          <div className="empty">
            <Icon name="grid" />
            <div className="empty-title">{t('empty')}</div>
            <div className="hint">
              {/* 迭代 80（#128 决议 2「三名义」③）：设备出现在列表 = 已加入服务端（设备注册），不再用「连接服务端」 */}
              {t('emptyHintInstalled')}
              <br />
              {t('emptyHintOffline')}
            </div>
          </div>
        ) : (
          <table className="table">
            <thead>
              <tr>
                <th style={{ width: 210 }}>{t('columns.deviceId')}</th>
                <th>{t('columns.name')}</th>
                <th style={{ width: 150 }}>{t('columns.lastIp')}</th>
                <th style={{ width: 90 }}>{t('columns.status')}</th>
                <th style={{ width: 160 }}>{t('columns.lastSeen')}</th>
                <th style={{ width: 110 }}></th>
              </tr>
            </thead>
            <tbody>
              {devices.map((d) => (
                <tr
                  key={d.deviceId}
                  className={isDefault(d) ? 'selected' : undefined}
                  style={{ cursor: 'pointer' }}
                  onClick={() => pick(d)}
                  title={isDefault(d) ? t('defaultTitle') : t('pickTitle')}
                >
                  <td className="mono" style={{ fontSize: 12 }}>
                    {d.deviceId}
                  </td>
                  <td>
                    {isDefault(d) && (
                      <span className="badge info" style={{ marginRight: 6 }}>
                        {t('defaultBadge')}
                      </span>
                    )}
                    {d.name || '—'}
                  </td>
                  <td className="mono" style={{ fontSize: 12 }}>
                    {d.lastIp || '—'}
                  </td>
                  <td>
                    <span className={'badge ' + (d.status === 'Online' ? 'ok' : 'err')}>
                      <span className="status-dot" style={{ background: d.status === 'Online' ? 'var(--ok)' : 'var(--danger)' }} />
                      {d.status === 'Online' ? t('device.online') : t('device.offline')}
                    </span>
                  </td>
                  <td className="mono" style={{ fontSize: 12 }}>
                    {formatTime(d.lastSeenAt)}
                  </td>
                  <td>
                    {d.status === 'Online' ? (
                      <button className="btn sm" onClick={(ev) => { ev.stopPropagation(); pick(d) }}>
                        <Icon name="link" size={12} />
                        {t('setDefault')}
                      </button>
                    ) : (
                      <span className="hint" style={{ fontSize: 12 }}>—</span>
                    )}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </div>
    </div>
  )
}
