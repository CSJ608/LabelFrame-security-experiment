// 插件管理页（Server UI 专用）：服务端 plugin-packages 目录管理——
// 插件包列表（名称 / 版本 / pluginId / 大小 / 时间 / valid 状态，invalid 红标 + 原因）+ 上传（multipart，64MB 预检）+ 下载 + 删除（确认）。
// 与客户端设置页「插件管理」卡片共用 GET /api/plugin-packages 与下载 URL。

import { useCallback, useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
// i18n 初始化副作用导入：pluginLimits 在本页单测中被 mock（真实模块的 i18n 传递依赖被切断），
// 显式引入保证 useTranslation 在任何入口下都有已注册的语言包资源。
import '../i18n'
import { pluginPackageDownloadUrl, serverApi } from '../lib/api/client'
import { ApiError } from '../lib/api/types'
import type { PluginPackageInfo } from '../lib/api/types'
import { Icon } from '../components/Icon'
import { Modal } from '../components/Modal'
import { formatSize } from '../lib/download'
import { pluginPackageTooLarge } from '../lib/pluginLimits'

/** 修改时间：本地时间 MM-dd HH:mm:ss。 */
function formatTime(iso?: string): string {
  if (!iso) return '—'
  const d = new Date(iso)
  if (Number.isNaN(d.getTime())) return '—'
  const p = (n: number) => String(n).padStart(2, '0')
  return `${p(d.getMonth() + 1)}-${p(d.getDate())} ${p(d.getHours())}:${p(d.getMinutes())}:${p(d.getSeconds())}`
}

export function PluginPackages() {
  const { t } = useTranslation('pluginPackages')
  const [packages, setPackages] = useState<PluginPackageInfo[] | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [notice, setNotice] = useState<string | null>(null)
  const [uploading, setUploading] = useState(false)
  const [deleting, setDeleting] = useState<string | null>(null)
  // 删除确认（自研 Modal，不用原生 confirm）
  const [pendingRemove, setPendingRemove] = useState<PluginPackageInfo | null>(null)

  const load = useCallback(async () => {
    setError(null)
    try {
      setPackages(await serverApi.listPluginPackages())
    } catch (err) {
      setError(err instanceof ApiError ? err.message : t('loadFailed'))
    }
  }, [t])

  useEffect(() => {
    void load()
  }, [load])

  const upload = async (file: File) => {
    setNotice(null)
    const tooLarge = pluginPackageTooLarge(file.size)
    if (tooLarge) {
      setError(tooLarge)
      return
    }
    setUploading(true)
    setError(null)
    try {
      await serverApi.uploadPluginPackage(file)
      setNotice(t('uploadOk', { name: file.name }))
      void load()
    } catch (err) {
      setError(err instanceof ApiError ? err.message : t('uploadFailed'))
    } finally {
      setUploading(false)
    }
  }

  const remove = async (p: PluginPackageInfo) => {
    setDeleting(p.fileName)
    setError(null)
    setNotice(null)
    try {
      await serverApi.deletePluginPackage(p.fileName)
      setNotice(t('deleteOk', { name: p.fileName }))
      void load()
    } catch (err) {
      setError(err instanceof ApiError ? err.message : t('deleteFailed'))
    } finally {
      setDeleting(null)
    }
  }

  /** 删除确认 Modal 的「确认删除」：执行删除并立即关闭确认框（进行中状态由行内按钮呈现）。 */
  const confirmRemove = () => {
    if (!pendingRemove) return
    const p = pendingRemove
    setPendingRemove(null)
    void remove(p)
  }

  return (
    <div className="page">
      <div className="page-head">
        <div className="page-title">
          {t('page.title')}
          <small>{t('page.subtitle')}</small>
        </div>
        <div className="spacer" />
        <button className="btn" onClick={() => document.getElementById('pluginPkgFile')?.click()} disabled={uploading}>
          <Icon name="upload" size={13} />
          {uploading ? t('action.uploading') : t('upload')}
        </button>
        <input
          id="pluginPkgFile"
          type="file"
          style={{ display: 'none' }}
          onChange={(ev) => {
            const f = ev.target.files?.[0]
            if (f) void upload(f)
            ev.target.value = ''
          }}
        />
        <button className="btn" onClick={() => void load()} title={t('refreshTitle')}>
          <Icon name="refresh" size={13} />
          {t('action.refresh')}
        </button>
      </div>

      {error && <div className="banner error">{error}</div>}
      {notice && <div className="banner notice">{notice}</div>}

      <div style={{ flex: 1, overflow: 'auto', padding: 12 }}>
        {packages === null ? (
          <div className="empty">
            <Icon name="puzzle" />
            <div className="empty-title">{t('loading')}</div>
          </div>
        ) : packages.length === 0 ? (
          <div className="empty">
            <Icon name="puzzle" />
            <div className="empty-title">{t('empty')}</div>
            <div className="hint">
              {t('emptyHint')}
            </div>
          </div>
        ) : (
          <table className="table">
            <thead>
              <tr>
                <th>{t('columns.name')}</th>
                <th style={{ width: 90 }}>{t('columns.version')}</th>
                <th style={{ width: 140 }}>{t('columns.pluginId')}</th>
                <th style={{ width: 90 }}>{t('columns.size')}</th>
                <th style={{ width: 150 }}>{t('columns.modifiedAt')}</th>
                <th style={{ width: 210 }}>{t('columns.status')}</th>
                <th style={{ width: 150 }}></th>
              </tr>
            </thead>
            <tbody>
              {packages.map((p) => (
                <tr key={p.fileName} style={{ cursor: 'default' }}>
                  <td>
                    <div>{p.name ?? '—'}</div>
                    <div className="mono" style={{ fontSize: 11, color: 'var(--muted)' }}>
                      {p.fileName}
                    </div>
                  </td>
                  <td className="mono" style={{ fontSize: 12 }}>
                    {p.version ?? '—'}
                  </td>
                  <td className="mono" style={{ fontSize: 12 }}>
                    {p.pluginId ?? '—'}
                  </td>
                  <td className="mono" style={{ fontSize: 12 }}>
                    {formatSize(p.sizeBytes)}
                  </td>
                  <td className="mono" style={{ fontSize: 12 }}>
                    {formatTime(p.modifiedAt)}
                  </td>
                  <td>
                    {p.valid ? (
                      <span className="badge ok">{t('valid')}</span>
                    ) : (
                      <>
                        <span className="badge err">{t('invalid')}</span>{' '}
                        <span style={{ fontSize: 12, color: 'var(--danger)' }}>{p.invalidReason ?? t('parseFailed')}</span>
                      </>
                    )}
                  </td>
                  <td>
                    <div style={{ display: 'flex', gap: 6 }}>
                      <a className="btn sm" href={pluginPackageDownloadUrl(p.fileName)} title={t('downloadTitle', { name: p.fileName })}>
                        <Icon name="download" size={12} />
                        {t('action.download')}
                      </a>
                      <button
                        className="btn sm danger"
                        onClick={() => setPendingRemove(p)}
                        disabled={deleting === p.fileName}
                        title={t('deleteTitle')}
                      >
                        <Icon name="trash" size={12} />
                        {deleting === p.fileName ? t('action.deleting') : t('action.delete')}
                      </button>
                    </div>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </div>

      {pendingRemove && (
        <Modal
          title={t('deleteModal.title')}
          onClose={() => setPendingRemove(null)}
          footer={
            <>
              <button className="btn" onClick={() => setPendingRemove(null)}>
                {t('action.cancel')}
              </button>
              <button className="btn danger" onClick={confirmRemove} disabled={deleting !== null}>
                <Icon name="trash" size={13} />
                {t('action.confirmDelete')}
              </button>
            </>
          }
        >
          <p>
            {t('deleteModal.body', { name: pendingRemove.fileName })}
          </p>
        </Modal>
      )}
    </div>
  )
}
