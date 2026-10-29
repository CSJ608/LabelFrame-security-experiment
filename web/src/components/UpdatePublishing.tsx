import { useEffect, useRef, useState } from 'react'
import type { ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import { Modal } from './Modal'
import '../i18n'
import { requestTimeouts, serverRequest } from '../lib/api/client'

interface Package {
  version?: string; versionName?: string; versionCode?: number; architecture?: string; abis?: string[]; sha256: string
}
interface Release { id: string; package: Package; devices: string[]; paused: boolean }
interface Draft { fileName: string; sha256: string | null }
interface Report { status: { deviceId: string; phase: string; reason?: string }; receivedAt: string }
interface Pending { fileName?: string; package: Package; release?: Release }

const version = (p: Package) => p.version ?? `${p.versionName} (${p.versionCode})`

/** 为安装包列表提供客户端更新发布、暂停和确认交互。 */
export function UpdatePublishing({ platform, revision, children }: {
  platform: 'windows' | 'android'
  revision: string
  children: (actions: (fileName: string) => ReactNode) => ReactNode
}) {
  const { t } = useTranslation('downloadCenter')
  const [releases, setReleases] = useState<Release[]>([])
  const [drafts, setDrafts] = useState<Draft[]>([])
  const [reports, setReports] = useState<Report[]>([])
  const [pending, setPending] = useState<Pending | null>(null)
  const [busy, setBusy] = useState(false)
  const [ready, setReady] = useState(false)
  const [error, setError] = useState('')
  const [notice, setNotice] = useState('')
  const active = useRef<AbortController | null>(null)
  const base = `api/${platform === 'android' ? 'android' : 'client'}-updates`
  const title = t(`simplePublish.${platform}`)
  const scope = (release?: Release) => release?.devices.length ? t('simplePublish.selected', { count: release.devices.length }) : t('simplePublish.all')
  const request = async (path: string, signal: AbortSignal, method = 'GET', body?: unknown) => {
    return serverRequest(`${base}${path}`, {
      method, redirect: 'error', signal,
      headers: { 'Content-Type': 'application/json' },
      ...(body === undefined ? {} : { body: JSON.stringify(body) }),
    }, requestTimeouts.heavy)
  }
  const reload = async (signal: AbortSignal) => {
    const [nextReleases, nextDrafts, nextReports] = await Promise.all([
      request('', signal), request('/drafts', signal), request('/status', signal),
    ])
    if (signal.aborted) return
    setReleases(nextReleases as Release[]); setDrafts(nextDrafts as Draft[]); setReports(nextReports as Report[]); setReady(true)
  }
  const run = async (action?: (signal: AbortSignal) => Promise<void>) => {
    if (active.current) return
    const controller = new AbortController()
    active.current = controller
    setBusy(true); setError(''); setNotice('')
    try {
      if (action) await action(controller.signal)
      else await reload(controller.signal)
    } catch (reason) {
      if (!controller.signal.aborted) {
        setError(reason instanceof Error ? reason.message : t('publish.failed'))
        setReady(false); setPending(null)
      }
    } finally {
      if (active.current === controller) { active.current = null; setBusy(false) }
    }
  }
  useEffect(() => {
    setReady(false); setPending(null)
    void run()
    return () => { active.current?.abort(); active.current = null }
    // 请求绑定当前平台和草稿快照，离页或目录变化时取消旧请求。
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [base, revision])

  const pause = (release: Release) => void run(async signal => {
    await request(`/${encodeURIComponent(release.id)}/pause`, signal, 'PUT', { paused: true })
    await reload(signal)
    if (!signal.aborted) setNotice(t('simplePublish.pausedNotice'))
  })
  const confirm = () => {
    if (!pending) return
    const selected = pending
    void run(async signal => {
      if (selected.release) await request(`/${encodeURIComponent(selected.release.id)}/pause`, signal, 'PUT', { paused: false })
      else await request('/publish', signal, 'POST', {
        fileName: selected.fileName, devices: [], expectedSha256: selected.package.sha256,
        ...(platform === 'android' ? { allDevicesConfirmed: true, mode: 'confirm' } : {}),
      })
      if (!signal.aborted) setPending(null)
      await reload(signal)
      if (!signal.aborted) setNotice(t('simplePublish.publishedNotice'))
    })
  }
  const releaseActions = (release: Release) => <span key={release.id} style={{ display: 'inline-flex', alignItems: 'center', gap: 8 }}>
    <span className="hint">{t(release.paused ? 'simplePublish.paused' : 'simplePublish.published')}{release.devices.length > 0 && ` · ${scope(release)}`}</span>
    <button className="btn sm" disabled={busy || !ready} onClick={() => release.paused ? setPending({ package: release.package, release }) : pause(release)}>
      {t(release.paused ? 'simplePublish.resume' : 'simplePublish.pause')}
    </button>
  </span>
  const actions = (fileName: string) => {
    const hash = drafts.find(draft => draft.fileName === fileName)?.sha256
    const matches = hash ? releases.filter(release => release.package.sha256.toLowerCase() === hash.toLowerCase()) : []
    return <div style={{ display: 'flex', flexWrap: 'wrap', gap: 8 }}>
      {matches.map(releaseActions)}
      {matches.length === 0 && <button className="btn sm primary" disabled={busy || !ready || !fileName.toLowerCase().endsWith(platform === 'android' ? '.apk' : '.msi')}
        onClick={() => void run(async signal => {
          const pkg = await request('/preview', signal, 'POST', { fileName }) as Package
          if (!signal.aborted) setPending({ fileName, package: pkg })
        })}>{t('simplePublish.publish')}</button>}
    </div>
  }
  return <section aria-label={title}>
    <p className="hint">{t('simplePublish.hint')}</p>
    {error && <div role="alert" className="banner error">{error} {t('simplePublish.retryHint')}</div>}
    {notice && <div role="status" className="banner notice">{notice}</div>}
    <div style={{ display: 'flex', gap: 8, alignItems: 'center', marginBottom: 8 }}>
      <button className="btn sm" disabled={busy} onClick={() => void run()}>{t('simplePublish.refresh')}</button>
      {busy && <span role="status">{t('simplePublish.busy')}</span>}
    </div>
    {children(actions)}
    <p className="hint">{t('simplePublish.pauseHint')}</p>
    <details style={{ marginTop: 16 }}>
      <summary>{t('simplePublish.history')}</summary>
      <p className="hint">{t('simplePublish.historyHint')}</p>
      {releases.map(release => <div key={release.id} style={{ display: 'flex', flexWrap: 'wrap', gap: 12, padding: 8 }}>
        <span>{version(release.package)} · {scope(release)}</span>{releaseActions(release)}
      </div>)}
      <p className="hint">{t('simplePublish.reportHint')}</p>
      {reports.map(report => <div key={report.status.deviceId} style={{ padding: 8 }}>
        {report.status.deviceId} · {t(`${platform === 'android' ? 'androidPublish' : 'publish'}.phases.${report.status.phase}`, { defaultValue: report.status.phase })}
        <span className="hint"> · {new Date(report.receivedAt).toLocaleString()} · {report.status.reason}</span>
      </div>)}
    </details>
    {pending && <Modal title={title} onClose={() => { if (!busy) setPending(null) }} footer={<>
      <button className="btn" disabled={busy} onClick={() => setPending(null)}>{t('simplePublish.cancel')}</button>
      <button className="btn primary" disabled={busy} onClick={confirm}>{t('simplePublish.confirm')}</button>
    </>}>
      <p>{pending.fileName}</p>
      <p>{t('simplePublish.version')}: <strong>{version(pending.package)}</strong> · {pending.package.architecture ?? pending.package.abis?.join(', ')}</p>
      <p>{t('simplePublish.scope')}: {scope(pending.release)}</p>
      <p>{t(`simplePublish.${platform}Hint`)}</p>
    </Modal>}
  </section>
}
