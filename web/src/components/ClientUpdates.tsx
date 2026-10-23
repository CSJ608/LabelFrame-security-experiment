import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import '../i18n'
import { beginHostOperation, reportHostActivity, setHostDirty } from '../lib/hostLifecycle'
import { clientUpdateReasonKey } from '../lib/clientUpdateReason'

interface Settings { mode: string; windowStart: string; windowEnd: string }
interface Status { phase: string; currentVersion: string; targetVersion?: string; reason?: string; downloadedBytes: number; totalBytes: number; transactionId?: string }
interface Snapshot { settings: Settings; status: Status }

async function request(path: string, method = 'GET', body?: unknown): Promise<unknown> {
  const response = await fetch(`/api/host/update${path}`, { method, headers: { 'Content-Type': 'application/json' },
    ...(body === undefined ? {} : { body: JSON.stringify(body) }), signal: AbortSignal.timeout(method === 'GET' ? 8000 : 240000) })
  if (!response.ok) {
    const error = await response.json().catch(() => ({})) as { message?: string }
    throw new Error(error.message ?? `HTTP ${response.status}`)
  }
  return response.json()
}

/** 本机自动升级设置、后台状态与安全安装入口。 */
export function ClientUpdates() {
  const { t, i18n } = useTranslation('settings')
  const reasonText = (message: string) => i18n.language.startsWith('zh') ? message : t(`autoUpdate.reasons.${clientUpdateReasonKey(message)}`)
  const [snapshot, setSnapshot] = useState<Snapshot | null>(null)
  const [draft, setDraft] = useState<Settings | null>(null)
  const [error, setError] = useState('')
  const [busy, setBusy] = useState(false)
  const [unsupported, setUnsupported] = useState(false)
  useEffect(() => {
    let disposed = false
    const load = async () => {
      try {
        const data = await request('') as Snapshot
        if (!data?.settings || !data?.status) throw new Error('unsupported')
        if (!disposed) { setSnapshot(data); setDraft(current => current ?? data.settings); setUnsupported(false) }
      } catch { if (!disposed) setUnsupported(true) }
    }
    void load()
    const timer = setInterval(() => void load(), 3000)
    return () => { disposed = true; clearInterval(timer); setHostDirty('client-update-settings', false) }
  }, [])
  const dirty = !!draft && !!snapshot && JSON.stringify(draft) !== JSON.stringify(snapshot.settings)
  useEffect(() => { setHostDirty('client-update-settings', dirty) }, [dirty])
  const run = async (action: 'save' | 'check' | 'install') => {
    setBusy(true); setError('')
    const finish = action === 'install' ? () => {} : beginHostOperation()
    try {
      await reportHostActivity()
      if (action === 'save') await request('/settings', 'PUT', draft)
      if (action === 'check') await request('/check', 'POST', {})
      if (action === 'install') await request('/install', 'POST', {})
      if (action !== 'install') setSnapshot(await request('') as Snapshot)
    } catch (reason) { setError(reason instanceof Error ? reason.message : t('autoUpdate.failed')) }
    finally { finish(); setBusy(false) }
  }
  if (unsupported) return <p className="hint">{t('autoUpdate.unsupported')}</p>
  if (!snapshot || !draft) return <p className="hint">{t('autoUpdate.loading')}</p>
  return <div className="stack" data-testid="client-auto-update" style={{ display: 'grid', gap: 12, marginBottom: 18 }}>
    <h3 className="settings-subtitle">{t('autoUpdate.title')}</h3>
    <p className="hint">{t('autoUpdate.hint')}</p>
    <label className="field" style={{ maxWidth: 420 }}>{t('autoUpdate.policy')}
      <select className="input" value={draft.mode} disabled={busy} onChange={event => setDraft({ ...draft, mode: event.target.value })}>
        <option value="notify">{t('autoUpdate.notify')}</option>
        <option value="download">{t('autoUpdate.download')}</option>
        <option value="maintenance">{t('autoUpdate.maintenance')}</option>
      </select>
    </label>
    {draft.mode === 'maintenance' && <>
      <div style={{ display: 'flex', gap: 12, flexWrap: 'wrap' }}>
        <label className="field">{t('autoUpdate.start')} <input className="input" type="time" value={draft.windowStart} onChange={event => setDraft({ ...draft, windowStart: event.target.value })} /></label>
        <label className="field">{t('autoUpdate.end')} <input className="input" type="time" value={draft.windowEnd} onChange={event => setDraft({ ...draft, windowEnd: event.target.value })} /></label>
      </div>
      <p className="hint">{t('autoUpdate.conditions')}</p>
    </>}
    <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap' }}>
      <button className="btn" disabled={busy || !dirty} onClick={() => void run('save')}>{t('autoUpdate.save')}</button>
      <button className="btn" disabled={busy || dirty} onClick={() => void run('check')}>{t('autoUpdate.check')}</button>
      <button className="btn primary" disabled={busy || dirty || !snapshot.status.targetVersion || ['installing', 'repair-required', 'recovering'].includes(snapshot.status.phase)}
        onClick={() => void run('install')}>{t('autoUpdate.install')}</button>
    </div>
    <div role="status" style={{ padding: 10, borderRadius: 6, background: 'var(--accent-soft)' }}>
      {t(`autoUpdate.phases.${snapshot.status.phase}`, { defaultValue: snapshot.status.phase })}
      {snapshot.status.targetVersion && ` · ${snapshot.status.currentVersion} → ${snapshot.status.targetVersion}`}
      {snapshot.status.totalBytes > 0 && ` · ${Math.floor(snapshot.status.downloadedBytes * 100 / snapshot.status.totalBytes)}%`}
      {snapshot.status.reason && <p className="hint">{reasonText(snapshot.status.reason)}</p>}
      {snapshot.status.transactionId && <p className="hint">{t('autoUpdate.transaction', { id: snapshot.status.transactionId })}</p>}
    </div>
    {error && <div role="alert" style={{ color: 'var(--danger)' }}>{reasonText(error)}</div>}
    <p className="hint">{t('autoUpdate.bootstrap')}</p>
    <h3 className="settings-subtitle">{t('autoUpdate.manual')}</h3>
  </div>
}

/** 全局持续提示可安装更新与需要处理的更新失败。 */
export function ClientUpdateNotice() {
  const { t } = useTranslation('settings')
  const [status, setStatus] = useState<Status | null>(null)
  useEffect(() => {
    let disposed = false
    const load = async () => {
      try {
        const snapshot = await request('') as Snapshot
        if (!disposed) setStatus(snapshot.status ?? null)
      } catch { /* 旧客户端不展示升级提示。 */ }
    }
    void load()
    const timer = setInterval(() => void load(), 10000)
    return () => { disposed = true; clearInterval(timer) }
  }, [])
  if (!status || !['available', 'ready', 'deferred', 'failed', 'repair-required'].includes(status.phase)) return null
  return <div className="notice" role="status" style={{ margin: '8px 16px' }}>
    {t(`autoUpdate.phases.${status.phase}`)}{status.targetVersion && ` · ${status.targetVersion}`} {' '}
    <a href="#/settings/settings.updates">{t('autoUpdate.view')}</a>
  </div>
}
