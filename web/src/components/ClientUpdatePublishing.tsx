import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import '../i18n'
import { clientUpdateReasonKey } from '../lib/clientUpdateReason'

interface Release { id: string; fileName: string; package: { version: string; sha256: string; architecture: string }; devices: string[]; paused: boolean }
interface Report { status: { deviceId: string; currentVersion: string; targetVersion?: string; phase: string; reason?: string }; receivedAt: string }

/** 管理员显式发布、暂停与查看客户端自报更新状态。 */
export function ClientUpdatePublishing({ fileNames }: { fileNames: string[] }) {
  const { t, i18n } = useTranslation('downloadCenter')
  const reasonText = (message: string) => i18n.language.startsWith('zh') || message === t('publish.denied')
    ? message : t(`publish.reasons.${clientUpdateReasonKey(message)}`)
  const [token, setToken] = useState('')
  const [fileName, setFileName] = useState('')
  const [devices, setDevices] = useState('')
  const [releases, setReleases] = useState<Release[] | null>(null)
  const [reports, setReports] = useState<Report[]>([])
  const [error, setError] = useState('')
  const [busy, setBusy] = useState(false)
  const request = async (path: string, method = 'GET', body?: unknown) => {
    const response = await fetch(`/api/client-updates${path}`, { method, headers: { Authorization: `Bearer ${token}`, 'Content-Type': 'application/json' },
      ...(body === undefined ? {} : { body: JSON.stringify(body) }), signal: AbortSignal.timeout(90000) })
    if (!response.ok) {
      const data = await response.json().catch(() => ({})) as { message?: string }
      throw new Error(response.status === 403 ? t('publish.denied') : data.message ?? `HTTP ${response.status}`)
    }
    return response.json()
  }
  const run = async (action?: () => Promise<unknown>) => {
    setBusy(true); setError('')
    try {
      if (action) await action()
      setReleases(await request('') as Release[])
      setReports(await request('/status') as Report[])
    } catch (reason) { setError(reason instanceof Error ? reason.message : t('publish.failed')) }
    finally { setBusy(false) }
  }
  return <section style={{ marginTop: 20, padding: 16, border: '1px solid var(--line)', borderRadius: 8, display: 'grid', gap: 12 }} data-testid="client-update-publishing">
    <h3>{t('publish.title')}</h3>
    <p className="hint">{t('publish.hint')}</p>
    <div style={{ display: 'flex', alignItems: 'end', gap: 10, flexWrap: 'wrap' }}>
      <label className="field" style={{ width: 360 }}>{t('publish.token')} <input className="input" type="password" autoComplete="off" value={token} onChange={event => { setToken(event.target.value); setReleases(null); setReports([]) }} /></label>
      <button className="btn" disabled={busy || token.length < 32} onClick={() => void run()}>{t('publish.load')}</button>
    </div>
    {releases && <div style={{ display: 'grid', gap: 10, marginTop: 14 }}>
      <label className="field" style={{ maxWidth: 560 }}>{t('publish.draft')} <select className="input" value={fileName} onChange={event => setFileName(event.target.value)}>
        <option value="">{t('publish.choose')}</option>
        {fileNames.filter(name => /\.msi$/i.test(name)).map(name => <option key={name}>{name}</option>)}
      </select></label>
      <label className="field" style={{ maxWidth: 560 }}>{t('publish.devices')} <textarea className="input" rows={2} value={devices} onChange={event => setDevices(event.target.value)} placeholder={t('publish.scopeHint')} /></label>
      <button className="btn primary" style={{ justifySelf: 'start' }} disabled={busy || !fileName} onClick={() => void run(() => request('/publish', 'POST', {
        fileName, devices: devices.split(/[,，\n]/).map(device => device.trim()).filter(Boolean),
      }))}>{t('publish.action')}</button>
      <table className="table"><thead><tr><th>{t('publish.version')}</th><th>{t('publish.scope')}</th><th>{t('publish.state')}</th><th /></tr></thead>
        <tbody>{releases.map(release => <tr key={release.id}>
          <td title={release.package.sha256}>{release.package.version} · {release.package.architecture}</td>
          <td>{release.devices.length ? release.devices.join(', ') : t('publish.all')}</td>
          <td>{t(release.paused ? 'publish.paused' : 'publish.active')}</td>
          <td><button className="btn sm" disabled={busy} onClick={() => void run(() => request(`/${release.id}/pause`, 'PUT', { paused: !release.paused }))}>
            {t(release.paused ? 'publish.resume' : 'publish.pause')}</button></td>
        </tr>)}</tbody></table>
      <p className="hint">{t('publish.reportHint')}</p>
      <table className="table"><thead><tr><th>{t('publish.device')}</th><th>{t('publish.version')}</th><th>{t('publish.state')}</th><th>{t('publish.received')}</th></tr></thead>
        <tbody>{reports.map(report => <tr key={report.status.deviceId}><td>{report.status.deviceId}</td>
          <td>{report.status.currentVersion}{report.status.targetVersion && ` → ${report.status.targetVersion}`}</td>
          <td>{t(`publish.phases.${report.status.phase}`, { defaultValue: report.status.phase })}<div className="hint">{report.status.reason && reasonText(report.status.reason)}</div></td>
          <td>{new Date(report.receivedAt).toLocaleString()}</td></tr>)}</tbody></table>
    </div>}
    {error && <div role="alert" style={{ color: 'var(--danger)' }}>{reasonText(error)}</div>}
  </section>
}
