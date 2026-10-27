import { useEffect, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import '../i18n'

interface Release {
  id: string
  package: { versionName: string; versionCode: number; abis: string[]; sha256: string }
  devices: string[]
  paused: boolean
  publishedAt: string
}
interface Report {
  status: { deviceId: string; currentVersionCode: number; targetVersionCode?: number; phase: string; reason?: string; transactionId?: string }
  receivedAt: string
}

/** 管理 Android APK 的确认安装发布、设备批次与自报状态。 */
export function AndroidUpdatePublishing({ fileNames }: { fileNames: string[] }) {
  const { t } = useTranslation('downloadCenter')
  const [token, setToken] = useState('')
  const [fileName, setFileName] = useState('')
  const [devices, setDevices] = useState('')
  const [all, setAll] = useState(false)
  const [releases, setReleases] = useState<Release[] | null>(null)
  const [reports, setReports] = useState<Report[]>([])
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  const [refreshRequired, setRefreshRequired] = useState(false)
  const [now, setNow] = useState(Date.now)
  const generation = useRef(0)
  const active = useRef<AbortController | null>(null)
  useEffect(() => () => { generation.current++; active.current?.abort() }, [])
  useEffect(() => { const timer = setInterval(() => setNow(Date.now()), 30000); return () => clearInterval(timer) }, [])
  const scope = [...new Set(devices.split(/[,，\n]/).map(value => value.trim()).filter(Boolean))]
  const validScope = all || (scope.length > 0 && scope.length <= 1000 && scope.every(value => value.length <= 128 && [...value].every(c => c.charCodeAt(0) >= 32 && (c.charCodeAt(0) < 127 || c.charCodeAt(0) > 159))))
  const run = async (path?: string, method = 'POST', body?: unknown) => {
    if (active.current) return
    const version = generation.current
    const controller = new AbortController()
    active.current = controller
    setBusy(true); setError('')
    const request = async (suffix: string, verb = 'GET', data?: unknown) => {
      const response = await fetch(`api/android-updates${suffix}`, {
        method: verb, redirect: 'error', signal: AbortSignal.any([controller.signal, AbortSignal.timeout(120000)]),
        headers: { Authorization: `Bearer ${token}`, 'Content-Type': 'application/json' },
        ...(data === undefined ? {} : { body: JSON.stringify(data) }),
      })
      if (!response.ok) {
        const result = await response.json().catch(() => ({})) as { message?: string }
        throw new Error(response.status === 403 ? t('publish.denied') : result.message ?? `HTTP ${response.status}`)
      }
      return response.json()
    }
    try {
      if (path) await request(path, method, body)
      const [nextReleases, nextReports] = await Promise.all([request(''), request('/status')])
      if (generation.current === version) { setReleases(nextReleases as Release[]); setReports(nextReports as Report[]); setRefreshRequired(false) }
    } catch (reason) {
      if (generation.current === version) {
        setError(reason instanceof Error ? reason.message : t('publish.failed'))
        if (path) setRefreshRequired(true)
      }
    } finally {
      if (active.current === controller) active.current = null
      if (generation.current === version) setBusy(false)
    }
  }
  return <section data-testid="android-update-publishing" style={{ marginTop: 20, padding: 16, border: '1px solid var(--line)', borderRadius: 8, display: 'grid', gap: 12 }}>
    <h3>{t('androidPublish.title')}</h3>
    <p className="hint">{t('androidPublish.hint')}</p>
    <label className="field" style={{ maxWidth: 560 }}>{t('publish.token')}
      <input className="input" type="password" autoComplete="off" value={token} onChange={event => {
        generation.current++; active.current?.abort(); active.current = null
        setToken(event.target.value); setReleases(null); setReports([]); setError(''); setBusy(false); setAll(false); setRefreshRequired(false)
      }} />
    </label>
    <button className="btn" style={{ justifySelf: 'start' }} disabled={busy || token.length < 32} onClick={() => void run()}>{t('publish.load')}</button>
    {releases && <>
      <label className="field">{t('publish.draft')}<select className="input" disabled={busy} value={fileName} onChange={event => setFileName(event.target.value)}>
        <option value="">{t('publish.choose')}</option>
        {fileNames.filter(name => /\.apk$/i.test(name)).map(name => <option key={name}>{name}</option>)}
      </select></label>
      <label className="field">{t('publish.devices')}<textarea className="input" rows={2} disabled={busy || all} value={devices} onChange={event => setDevices(event.target.value)} placeholder={t('androidPublish.scopeHint')} /></label>
      <label><input type="checkbox" checked={all} disabled={busy} onChange={event => setAll(event.target.checked)} /> {t('androidPublish.allConfirm')}</label>
      <button className="btn primary" style={{ justifySelf: 'start' }} disabled={busy || refreshRequired || !fileNames.includes(fileName) || !/\.apk$/i.test(fileName) || !validScope}
        onClick={() => void run('/publish', 'POST', { fileName, devices: all ? [] : scope, allDevicesConfirmed: all, mode: 'confirm' })}>{t('publish.action')}</button>
      <p className="hint">{t('androidPublish.pauseHint')}</p>
      <div style={{ overflowX: 'auto' }}><table className="table"><thead><tr><th>{t('publish.version')}</th><th>{t('publish.scope')}</th><th>{t('publish.state')}</th><th>{t('androidPublish.published')}</th><th /></tr></thead>
        <tbody>{releases.map(release => <tr key={release.id}>
          <td title={release.package.sha256}>{release.package.versionName} ({release.package.versionCode}) · {release.package.abis.join(', ')}<div className="hint">{release.id}</div></td>
          <td>{release.devices.length ? release.devices.join(', ') : t('publish.all')}</td>
          <td>{t(release.paused ? 'publish.paused' : 'publish.active')}</td><td>{new Date(release.publishedAt).toLocaleString()}</td>
          <td><button className="btn sm" disabled={busy || refreshRequired} onClick={() => void run(`/${encodeURIComponent(release.id)}/pause`, 'PUT', { paused: !release.paused })}>{t(release.paused ? 'publish.resume' : 'publish.pause')}</button></td>
        </tr>)}</tbody></table></div>
      <p className="hint">{t('androidPublish.reportHint')}</p>
      <div style={{ overflowX: 'auto' }}><table className="table"><thead><tr><th>{t('publish.device')}</th><th>{t('publish.version')}</th><th>{t('publish.state')}</th><th>{t('publish.received')}</th></tr></thead>
        <tbody>{reports.map(report => <tr key={report.status.deviceId}>
          <td>{report.status.deviceId}</td><td>{report.status.currentVersionCode}{report.status.targetVersionCode != null && ` → ${report.status.targetVersionCode}`}</td>
          <td>{t(`androidPublish.phases.${report.status.phase}`, { defaultValue: report.status.phase })}<div className="hint">{report.status.reason}</div><div className="hint">{report.status.transactionId}</div></td>
          <td>{new Date(report.receivedAt).toLocaleString()}<div className="hint">{now - Date.parse(report.receivedAt) > 300000 ? t('androidPublish.stale') : t('androidPublish.reported')}</div></td>
        </tr>)}</tbody></table></div>
    </>}
    {error && <div role="alert" style={{ color: 'var(--danger)' }}>{error}</div>}
    {refreshRequired && <p className="hint">{t('androidPublish.refreshRequired')}</p>}
  </section>
}
