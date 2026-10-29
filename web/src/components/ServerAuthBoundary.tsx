import { useEffect, useRef, useState } from 'react'
import type { ReactNode } from 'react'
import { flushSync } from 'react-dom'
import { useTranslation } from 'react-i18next'
import { activateAdminBoundary, clearAdminStorage, invalidateAdmin, loginAdmin, logoutAdmin, onAdminInvalidated, readAdminSession, watchAdminTabs } from '../lib/adminSession'
import { PublicDownloads } from '../pages/DownloadCenter'
import { Help } from '../pages/Help'
import { formatHash, parseHash } from '../lib/router'
import { SERVER_TABS } from '../lib/tabs'
import i18next, { changeLocale, toAppLocale } from '../i18n'

/** 在 Server 管理状态树外提供公开页与会话入口。 */
export function ServerAuthBoundary({ children }: { children: ReactNode }) {
  const { t, i18n } = useTranslation('shell')
  const [admin, setAdmin] = useState(false)
  const [checking, setChecking] = useState(true)
  const [configured, setConfigured] = useState<boolean | null>(null)
  const [password, setPassword] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  const [hash, setHash] = useState(window.location.hash)
  const epoch = useRef(0)
  const allowed = SERVER_TABS.map(tab => tab.id)
  const route = parseHash(hash, allowed)
  const publicDownload = !hash || hash === '#/' || (route.page === 'packages' && !['windows', 'android'].includes(route.sub))
  const help = route.page === 'help'

  useEffect(() => {
    activateAdminBoundary()
    clearAdminStorage()
    let mounted = true
    const unsubscribe = onAdminInvalidated(() => {
      epoch.current++
      setHash(window.location.hash)
      setAdmin(false); setPassword(''); setChecking(false); setBusy(false)
    })
    const stopTabs = watchAdminTabs()
    const hide = () => invalidateAdmin(false)
    window.addEventListener('pagehide', hide)
    const changed = () => setHash(window.location.hash)
    window.addEventListener('hashchange', changed)
    const current = epoch.current
    void readAdminSession().then(session => {
      if (!mounted || current !== epoch.current) return
      setConfigured(session.configured); setAdmin(session.authenticated)
    }).catch(() => {
      if (mounted && current === epoch.current) setError(i18next.t('shell:auth.sessionFailed'))
    }).finally(() => { if (mounted && current === epoch.current) setChecking(false) })
    return () => {
      mounted = false
      unsubscribe(); stopTabs(); window.removeEventListener('hashchange', changed); window.removeEventListener('pagehide', hide)
      invalidateAdmin(false)
    }
  }, [])

  useEffect(() => {
    if (!admin) return
    let pending = false
    const verify = () => {
      if (pending) return
      pending = true
      const current = epoch.current
      void readAdminSession().then(session => {
        if (current === epoch.current && !session.authenticated) invalidateAdmin()
      }).catch(() => { if (current === epoch.current) invalidateAdmin() })
        .finally(() => { pending = false })
    }
    window.addEventListener('focus', verify)
    return () => window.removeEventListener('focus', verify)
  }, [admin])

  const login = async () => {
    if (busy) return
    const current = epoch.current
    const supplied = password
    setPassword(''); setBusy(true); setError('')
    try {
      const session = await loginAdmin(supplied)
      if (current === epoch.current) {
        setConfigured(session.configured); setAdmin(session.authenticated)
        if (!session.authenticated) setError(i18next.t('shell:auth.loginFailed'))
      }
    } catch {
      if (current === epoch.current) setError(i18next.t('shell:auth.loginFailed'))
    } finally { if (current === epoch.current) setBusy(false) }
  }
  const logout = async () => {
    let revocation!: Promise<void>
    // 必须先卸载敏感树，再等待网络撤销结果。
    flushSync(() => { revocation = logoutAdmin(); setBusy(true); setError('') })
    try { await revocation }
    catch { setError(i18next.t('shell:auth.logoutUnconfirmed')) }
    finally { setBusy(false) }
  }

  if (admin) return <div style={{ height: '100%', display: 'flex', flexDirection: 'column' }}>
    <div style={{ display: 'flex', justifyContent: 'flex-end', padding: 6 }}>
      <button className="btn" onClick={() => void logout()}>{t('auth.logout')}</button>
    </div>
    <div style={{ flex: 1, minHeight: 0 }}>{children}</div>
  </div>

  return <div className="app">
    <header style={{ display: 'flex', flexWrap: 'wrap', gap: 12, padding: 12, alignItems: 'center' }}>
      <strong>{t('appTitle')}</strong>
      <a className="btn" href="#/packages">{t('nav.packages')}</a>
      <a className="btn" href="#/help">{t('nav.help')}</a>
      <a className="btn" href="#/workbench">{t('auth.login')}</a>
      <button className="btn" onClick={() => changeLocale(toAppLocale(i18n.language) === 'en' ? 'zh-CN' : 'en')}>{t('auth.language')}</button>
    </header>
    {error && <div role="alert" className="banner error">{error}</div>}
    {publicDownload ? <PublicDownloads /> : help ? <Help onOpenInUi={(page, anchor) => { window.location.hash = formatHash(page, anchor) }} /> :
      <main className="page" style={{ padding: 24, overflow: 'auto' }}>
        <div className="panel panel-body" style={{ width: '100%', maxWidth: 480, margin: '24px auto', display: 'grid', gap: 16 }}>
        <h1>{t('auth.login')}</h1>
        <p>{t('auth.required')}</p>
        <p className="hint">{t('auth.transport')}</p>
        {configured === false && <p role="alert">{t('auth.notConfigured')}</p>}
        <form style={{ display: 'grid', gap: 12 }} onSubmit={event => { event.preventDefault(); void login() }}>
          <label className="field">{t('auth.password')}<input className="input" type="password" autoComplete="current-password" maxLength={1024}
            value={password} onChange={event => setPassword(event.target.value)} disabled={busy || checking} /></label>
          <button className="btn primary" disabled={busy || checking || configured === false || !password}>
            {checking || busy ? t('auth.wait') : t('auth.login')}
          </button>
        </form>
        </div>
      </main>}
  </div>
}
