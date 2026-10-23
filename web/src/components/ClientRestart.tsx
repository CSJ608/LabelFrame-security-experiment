import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { readHostLifecycle, restartHost } from '../lib/hostLifecycle'
import { Icon } from './Icon'

/** 客户端重启入口与持续的插件待生效提示。 */
export function ClientRestart({ contextual = false }: { contextual?: boolean }) {
  const { t } = useTranslation('settings')
  const [required, setRequired] = useState(false)
  const [supported, setSupported] = useState(false)
  const [restarting, setRestarting] = useState(false)
  const [error, setError] = useState('')

  useEffect(() => {
    let active = true
    const refresh = async () => {
      const state = await readHostLifecycle()
      if (active && state) {
        setRequired(state.restartRequired)
        setSupported(true)
      }
    }
    void refresh()
    const timer = setInterval(() => void refresh(), 2000)
    window.addEventListener('labelframe-plugins-changed', refresh)
    return () => { active = false; clearInterval(timer); window.removeEventListener('labelframe-plugins-changed', refresh) }
  }, [])

  if (contextual && !required) return null
  return (
    <div className={contextual ? 'settings-subsection' : 'settings-section-body'}>
      <div className="hint">{t(contextual ? 'restart.pending' : 'restart.hint')}</div>
      <div style={{ display: 'flex', gap: 10, alignItems: 'center', marginTop: 8 }}>
        <button className={contextual ? 'btn primary' : 'btn'} disabled={!supported || restarting} onClick={async () => {
          setError('')
          setRestarting(true)
          try { await restartHost() }
          catch (err) { setError(err instanceof Error ? err.message : t('restart.failed')); setRestarting(false) }
        }}>
          <Icon name="refresh" size={13} /> {t(restarting ? 'restart.running' : contextual ? 'restart.now' : 'restart.action')}
        </button>
        {!supported && <span className="hint">{t('restart.unsupported')}</span>}
      </div>
      {error && <div role="alert" className="badge err">{error}</div>}
    </div>
  )
}
