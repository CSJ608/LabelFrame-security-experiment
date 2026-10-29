// 下载中心页：页内三 tab——「快速访问」（默认）/「Windows 包管理」/「Android 包管理」。
// tab 状态由路由 sub 段承载（#/packages/<sub>，旧 #dc= 分享链接经 parseHash 兼容映射直达），刷新 / 直链均还原当前 tab。
// 快速访问 = 消费视图：Windows / Android 各一张「最新上传」卡（按上架 / 修改时间倒序取第一条，删除自动回退，
// 不做版本号解析）；「服务端信息」卡：大二维码（内容 = 选中裸地址 URL）+ 地址 + 复制。地址解析通告优先：
// GET /api/server/info 的 publicBaseUrl 非空时直接采用（候选切换整块不渲染）；无通告（未配置 / 旧版服务端 /
// 请求失败）时按 GET /api/server/ipv4-candidates（私网优先）推导——默认取与 origin 匹配者，localhost /
// 无匹配回退首个候选（不产生 localhost 废码），并携带 pathname 推导的部署子路径前缀；多网卡可切换，
// 二维码 / 地址 / 复制同步更新。
// 管理退居专职 tab：Windows（client-packages）/ Android（pda-packages）上传 / 下载 / 删除（确认 Modal）；
// 客户端设置页「更新与安装包」仍走 GET /api/client-packages 下载。

import { useCallback, useEffect, useState } from 'react'
import type { ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import qrcode from 'qrcode-generator'
// i18n 初始化副作用导入：api client 在测试中被 mock、组件树不传递依赖 i18n/index——
// 显式引入保证 useTranslation 在任何入口（含单测）下都有已注册的语言包。
import '../i18n'
import { clientPackageDownloadUrl, pdaPackageDownloadUrl, serverApi } from '../lib/api/client'
import { ApiError } from '../lib/api/types'
import type { ClientPackageInfo, PdaPackageInfo } from '../lib/api/types'
import { Icon } from '../components/Icon'
import { Modal } from '../components/Modal'
import { UpdatePublishing } from '../components/UpdatePublishing'
import { formatSize } from '../lib/download'
import { copyText } from '../lib/clipboard'
import { deriveBasePath, joinAddress, pickDisplayAddress } from '../lib/connection'

/** 修改时间：本地时间 MM-dd HH:mm:ss。 */
function formatTime(iso?: string): string {
  if (!iso) return '—'
  const d = new Date(iso)
  if (Number.isNaN(d.getTime())) return '—'
  const p = (n: number) => String(n).padStart(2, '0')
  return `${p(d.getMonth() + 1)}-${p(d.getDate())} ${p(d.getHours())}:${p(d.getMinutes())}:${p(d.getSeconds())}`
}

/** 条目通用展示信息（客户端 / PDA 共用行渲染；时间倒序「最新在上」由本页保证——服务端列表亦按修改时间倒序）。 */
interface PackageRow {
  fileName: string
  sizeBytes: number
  modifiedAt: string
  downloadHref: string
}

// ── 页内 tab：#/packages/<sub> 路由 sub 段承载，刷新 / 直链还原 ──

/** 页内 tab 标识：quick = 快速访问（默认消费视图）；windows / android = 平台包管理。 */
type DcTab = 'quick' | 'windows' | 'android'

const DC_TABS: readonly DcTab[] = ['quick', 'windows', 'android']

/** 路由 sub 段 → 页内 tab（缺省 / 未知 sub 回退 quick——quick 为缺省值不写 sub 段）。 */
function tabFromSub(sub: string): DcTab {
  return sub === 'windows' || sub === 'android' ? sub : 'quick'
}

/** 二维码图片（qrcode-generator 生成 GIF data URL，无需 canvas；生成失败不渲染，不阻塞页面）。 */
function QrCodeImage({ text, size = 84, alt }: { text: string; size?: number; alt: string }) {
  let src = ''
  try {
    const qr = qrcode(0, 'M')
    qr.addData(text)
    qr.make()
    src = qr.createDataURL(3, 6)
  } catch {
    return null
  }
  return (
    <img
      src={src}
      width={size}
      height={size}
      alt={alt}
      title={text}
      style={{ display: 'block', background: '#fff', border: '1px solid var(--line)', borderRadius: 4, imageRendering: 'pixelated' }}
    />
  )
}

/** 分区头：标题 + 说明 + 分区专属上传按钮（隐藏 file input 由按钮触发）。 */
function SectionHead({
  title,
  hint,
  uploading,
  uploadLabel,
  inputId,
  onPick,
  uploadingLabel,
}: {
  title: string
  hint: string
  uploading: boolean
  uploadLabel: string
  inputId: string
  onPick: (file: File) => void
  uploadingLabel: string
}) {
  return (
    <div style={{ display: 'flex', alignItems: 'center', gap: 8, margin: '0 0 8px' }}>
      <div className="page-title" style={{ fontSize: 16, margin: 0, flex: 1 }}>
        {title}
        <small>{hint}</small>
      </div>
      <button className="btn sm" onClick={() => document.getElementById(inputId)?.click()} disabled={uploading}>
        <Icon name="upload" size={12} />
        {uploading ? uploadingLabel : uploadLabel}
      </button>
      <input
        id={inputId}
        type="file"
        accept={inputId === 'pdaPkgFile' ? '.apk' : undefined}
        style={{ display: 'none' }}
        onChange={(ev) => {
          const f = ev.target.files?.[0]
          if (f) onPick(f)
          ev.target.value = ''
        }}
      />
    </div>
  )
}

/** 分区条目表（列：文件名 / 大小 / 修改时间 / 二维码 / 操作）。 */
function PackageTable({
  rows,
  qrOrigin,
  deleting,
  onRemove,
  updateActions,
}: {
  rows: PackageRow[]
  /** 二维码前缀（本页 origin）：二维码内容 = origin + 下载相对路径 = 局域网可访问的完整 URL。 */
  qrOrigin: string
  deleting: string | null
  onRemove?: (row: PackageRow) => void
  updateActions?: (fileName: string) => ReactNode
}) {
  const { t } = useTranslation('downloadCenter')
  return (
    <table className="table">
      <thead>
        <tr>
          <th>{t('columns.fileName')}</th>
          <th style={{ width: 100 }}>{t('columns.size')}</th>
          <th style={{ width: 150 }}>{t('columns.modifiedAt')}</th>
          <th style={{ width: 104 }}>{t('columns.qrCode')}</th>
          {updateActions && <th>{t('simplePublish.state')}</th>}
          <th style={{ width: 170 }}></th>
        </tr>
      </thead>
      <tbody>
        {rows.map((p) => (
          <tr key={p.downloadHref} style={{ cursor: 'default' }}>
            <td className="mono" style={{ fontSize: 12, wordBreak: 'break-all' }}>
              {p.fileName}
            </td>
            <td className="mono" style={{ fontSize: 12 }}>
              {formatSize(p.sizeBytes)}
            </td>
            <td className="mono" style={{ fontSize: 12 }}>
              {formatTime(p.modifiedAt)}
            </td>
            <td>
              <QrCodeImage text={`${qrOrigin}${p.downloadHref}`} alt={t('qrAlt')} />
            </td>
            {updateActions && <td>{updateActions(p.fileName)}</td>}
            <td>
              <div style={{ display: 'flex', gap: 8 }}>
                <a className="btn sm" href={p.downloadHref} title={t('downloadTitle', { name: p.fileName })}>
                  <Icon name="download" size={12} />
                  {t('action.download')}
                </a>
                {onRemove && <button
                  className="btn sm danger"
                  onClick={() => onRemove(p)}
                  disabled={deleting === p.fileName}
                  title={t('deleteTitle')}
                >
                  <Icon name="trash" size={12} />
                  {deleting === p.fileName ? t('action.deleting') : t('action.delete')}
                </button>}
              </div>
            </td>
          </tr>
        ))}
      </tbody>
    </table>
  )
}

/** 快速访问首屏「最新上传」卡（Windows / Android 各一）：货架语义——取修改时间倒序第一条（换货即生效、
 * 删除自动回退）；卡内文件名 / 大小 / 上传时间 / 下载二维码（origin + 下载路径）/ 下载按钮 /「全部版本 →」；
 * 空态引导跳转对应管理 tab 上传。
 */
function LatestCard({
  platform,
  loading,
  row,
  qrOrigin,
  manageTabLabel,
  onGoManage,
}: {
  platform: string
  loading: boolean
  row: PackageRow | null
  qrOrigin: string
  manageTabLabel: string
  onGoManage: () => void
}) {
  const { t } = useTranslation('downloadCenter')
  return (
    <div
      className="panel panel-body"
      style={{
        display: 'flex',
        flexDirection: 'column',
        gap: 12,
      }}
    >
      <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
        <span style={{ fontSize: 13, fontWeight: 600 }}>{platform}</span>
        {row && <span className="badge info">{t('quick.latest')}</span>}
      </div>
      {loading ? (
        <div className="empty" style={{ padding: '32px 0' }}>
          <div className="hint">{t('quick.loading')}</div>
        </div>
      ) : !row ? (
        <div className="empty" style={{ padding: '24px 0' }}>
          <Icon name="download" />
          <div className="empty-title">{t('quick.emptyTitle')}</div>
          <div className="hint">{t('quick.emptyHint', { tab: manageTabLabel })}</div>
          <button className="btn" onClick={onGoManage}>
            <Icon name="upload" size={12} />
            {t('quick.goUpload')}
          </button>
        </div>
      ) : (
        <>
          <div className="mono" style={{ fontSize: 13, fontWeight: 600, wordBreak: 'break-all' }}>
            {row.fileName}
          </div>
          <div className="mono hint">
            {formatSize(row.sizeBytes)} · {t('quick.uploadedAt', { time: formatTime(row.modifiedAt) })}
          </div>
          <div style={{ display: 'flex', justifyContent: 'center' }}>
            <QrCodeImage text={`${qrOrigin}${row.downloadHref}`} size={150} alt={t('qrAlt')} />
          </div>
          <div style={{ display: 'flex', gap: 8, justifyContent: 'center', alignItems: 'center' }}>
            <a className="btn primary" href={row.downloadHref} title={t('downloadTitle', { name: row.fileName })}>
              <Icon name="download" size={12} />
              {t('action.download')}
            </a>
            <button className="btn" onClick={onGoManage} title={manageTabLabel}>
              {t('quick.allVersions')}
            </button>
          </div>
        </>
      )}
    </div>
  )
}

/** 当前页面 origin（非浏览器环境回退空串）。 */
function pageOrigin(): string {
  return typeof window !== 'undefined' ? window.location.origin : ''
}

/** 当前部署子路径前缀（hash 路由下 pathname 即部署前缀；根部署与非浏览器环境回退空串）。 */
function pageBasePath(): string {
  return typeof window !== 'undefined' ? deriveBasePath(window.location.pathname) : ''
}

/** 候选 IP 按当前 origin 的协议与端口 + 部署前缀拼装完整地址（默认端口时省略端口段）。 */
function candidateUrl(ip: string, basePath: string): string {
  try {
    const u = new URL(pageOrigin())
    return joinAddress(u.protocol, ip, u.port, basePath)
  } catch {
    return ip
  }
}

/** 连接信息卡：大二维码居中（内容 = 选中裸地址 URL——PDA 扫码枪键盘直填即得地址）+ 地址文本 + 复制按钮；
 * 地址解析通告优先：GET /api/server/info 的 publicBaseUrl 非空时直接采用（多网卡候选切换整块不渲染）；
 * 无通告时走 GET /api/server/ipv4-candidates（私网优先）推导并携带部署子路径前缀，
 * 默认选择规则见 lib/connection.ts pickDisplayAddress / pickDefaultAddress。
 */
function ConnectionCard({ publicOnly = false }: { publicOnly?: boolean }) {
  const { t } = useTranslation('downloadCenter')
  const [candidates, setCandidates] = useState<string[] | null>(publicOnly ? [] : null)
  const [publicBaseUrl, setPublicBaseUrl] = useState<string | null>(null)
  const [selected, setSelected] = useState<string>(pageOrigin())
  const [copied, setCopied] = useState(false)
  const [copyFailed, setCopyFailed] = useState(false)

  useEffect(() => {
    let cancelled = false
    if (!publicOnly) serverApi
      .listServerIpv4Candidates()
      .then((res) => {
        if (!cancelled) setCandidates(Array.isArray(res.candidates) ? res.candidates : [])
      })
      .catch(() => {
        // 旧版服务端（无该端点）/ 请求失败：候选置空 → 展示回退当前 origin（不阻塞首屏）
        if (!cancelled) setCandidates([])
      })
    // 与候选并行拉服务端自描述：publicBaseUrl 通告优先于前端推导
    serverApi
      .getServerInfo()
      .then((info) => {
        if (cancelled) return
        const announced = typeof info.publicBaseUrl === 'string' ? info.publicBaseUrl.trim() : ''
        setPublicBaseUrl(announced || null)
      })
      .catch(() => {
        // 旧版服务端（无该字段）/ 请求失败：视为无通告，不影响候选拉取与首屏
        if (!cancelled) setPublicBaseUrl(null)
      })
    return () => {
      cancelled = true
    }
  }, [publicOnly])

  const basePath = pageBasePath()

  useEffect(() => {
    // 通告与候选都未返回时保持初始 origin（无更优信息不猜测）；任一返回即解析（通告优先在 pickDisplayAddress 内）
    if (publicBaseUrl === null && candidates === null) return
    setSelected(pickDisplayAddress(publicBaseUrl, pageOrigin(), candidates, basePath))
  }, [publicBaseUrl, candidates, basePath])

  const copy = async () => {
    if (await copyText(selected)) {
      setCopied(true)
      window.setTimeout(() => setCopied(false), 2000)
    } else {
      // 剪贴板不可用（非安全上下文 / 权限拒绝）：按钮旁红字提示手动复制
      setCopyFailed(true)
      window.setTimeout(() => setCopyFailed(false), 3000)
    }
  }

  return (
    <div className="panel panel-body">
      <div style={{ display: 'flex', justifyContent: 'center', marginBottom: 12 }}>
        <QrCodeImage text={selected} size={176} alt={t('connQrAlt')} />
      </div>
      <div style={{ display: 'flex', justifyContent: 'center', alignItems: 'center', gap: 8, flexWrap: 'wrap' }}>
        <span className="hint">{t('connection.address')}</span>
        <span className="mono" style={{ fontSize: 13, wordBreak: 'break-all' }}>
          {selected}
        </span>
        <button className="btn sm" onClick={() => void copy()} title={t('connection.copyTitle')}>
          <Icon name={copied ? 'check' : 'copy'} size={12} />
          {copied ? t('connection.copied') : t('connection.copy')}
        </button>
        {copyFailed && <span className="error-text">{t('connection.copyFailed')}</span>}
      </div>
      {!publicBaseUrl && candidates !== null && candidates.length > 1 && (
        <div style={{ display: 'flex', justifyContent: 'center', alignItems: 'center', gap: 8, marginTop: 12, flexWrap: 'wrap' }}>
          <span className="hint">{t('connection.candidates')}</span>
          {candidates.map((ip) => (
            <button
              key={ip}
              className={'btn sm' + (selected === candidateUrl(ip, basePath) ? ' active' : '')}
              onClick={() => setSelected(candidateUrl(ip, basePath))}
              title={candidateUrl(ip, basePath)}
            >
              {ip}
            </button>
          ))}
        </div>
      )}
    </div>
  )
}

/** 无管理状态与写操作的公开下载页。 */
export function PublicDownloads() {
  const { t } = useTranslation('downloadCenter')
  const [rows, setRows] = useState<PackageRow[] | null>(null)
  const [error, setError] = useState(false)
  const [revision, setRevision] = useState(0)
  useEffect(() => {
    let active = true
    void Promise.all([serverApi.listClientPackages(), serverApi.listPdaPackages()])
      .then(([clients, pdas]) => {
        if (!active) return
        setRows([
          ...clients.map(p => ({ ...p, downloadHref: clientPackageDownloadUrl(p.fileName) })),
          ...pdas.map(p => ({ ...p, downloadHref: pdaPackageDownloadUrl(p.fileName) })),
        ])
      }).catch(() => { if (active) setError(true) })
    return () => { active = false }
  }, [revision])
  return <div className="page">
    <div className="page-head"><h1 className="page-title">{t('page.title')}</h1>
      <button className="btn" onClick={() => { setError(false); setRevision(v => v + 1) }}>{t('action.refresh')}</button>
    </div>
    <div style={{ overflow: 'auto', padding: 16 }}>
      {error && <p role="alert">{t('loadFailed')}</p>}
      {rows === null ? <p>{t('quick.loading')}</p> : <PackageTable rows={rows} qrOrigin={pageOrigin()} deleting={null} />}
      <h2>{t('connection.title')}</h2>
      <ConnectionCard publicOnly />
    </div>
  </div>
}

export function DownloadCenter({ sub = '', onSubChange }: { sub?: string; onSubChange?: (next: string) => void }) {
  const { t } = useTranslation('downloadCenter')
  // 页内 tab 状态源 = 路由 sub 段（App 注入，缺省 '' → quick）；切 tab 经 onSubChange 写 sub（replaceState 不入栈，由路由层执行）。
  const tab = tabFromSub(sub)
  const [clientPackages, setClientPackages] = useState<ClientPackageInfo[] | null>(null)
  const [pdaPackages, setPdaPackages] = useState<PdaPackageInfo[] | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [notice, setNotice] = useState<string | null>(null)
  const [uploadingClient, setUploadingClient] = useState(false)
  const [uploadingPda, setUploadingPda] = useState(false)
  const [deleting, setDeleting] = useState<string | null>(null)
  // 删除确认（自研 Modal，不用原生 confirm）
  const [pendingRemove, setPendingRemove] = useState<{ kind: 'client' | 'pda'; row: PackageRow } | null>(null)

  // 切页内 tab = 写路由 sub 段（quick 为缺省值写空段，保持 URL 干净）；hash 写入与不入栈语义在路由层
  const switchTo = useCallback(
    (next: DcTab) => {
      onSubChange?.(next === 'quick' ? '' : next)
    },
    [onSubChange],
  )

  const load = useCallback(async () => {
    setError(null)
    try {
      const [clients, pdas] = await Promise.all([serverApi.listClientPackages(), serverApi.listPdaPackages()])
      setClientPackages(clients)
      setPdaPackages(pdas)
    } catch (err) {
      setError(err instanceof ApiError ? err.message : t('loadFailed'))
    }
  }, [t])

  useEffect(() => {
    void load()
  }, [load])

  const uploadClient = async (file: File) => {
    setUploadingClient(true)
    setNotice(null)
    setError(null)
    try {
      await serverApi.uploadClientPackage(file)
      setNotice(t('windows.uploadOk', { name: file.name }))
      void load()
    } catch (err) {
      setError(err instanceof ApiError ? err.message : t('uploadFailed'))
    } finally {
      setUploadingClient(false)
    }
  }

  const uploadPda = async (file: File) => {
    setUploadingPda(true)
    setNotice(null)
    setError(null)
    try {
      await serverApi.uploadPdaPackage(file)
      setNotice(t('android.uploadOk', { name: file.name }))
      void load()
    } catch (err) {
      setError(err instanceof ApiError ? err.message : t('uploadFailed'))
    } finally {
      setUploadingPda(false)
    }
  }

  const removeClient = async (p: PackageRow) => {
    setDeleting(p.fileName)
    setError(null)
    setNotice(null)
    try {
      await serverApi.deleteClientPackage(p.fileName)
      setNotice(t('windows.deleteOk', { name: p.fileName }))
      void load()
    } catch (err) {
      setError(err instanceof ApiError ? err.message : t('deleteFailed'))
    } finally {
      setDeleting(null)
    }
  }

  const removePda = async (p: PackageRow) => {
    setDeleting(p.fileName)
    setError(null)
    setNotice(null)
    try {
      await serverApi.deletePdaPackage(p.fileName)
      setNotice(t('android.deleteOk', { name: p.fileName }))
      void load()
    } catch (err) {
      setError(err instanceof ApiError ? err.message : t('deleteFailed'))
    } finally {
      setDeleting(null)
    }
  }

  /** 删除确认 Modal 的「确认删除」：按分区派发并立即关闭确认框（进行中状态由行内按钮呈现）。 */
  const confirmRemove = () => {
    if (!pendingRemove) return
    const { kind, row } = pendingRemove
    setPendingRemove(null)
    if (kind === 'client') void removeClient(row)
    else void removePda(row)
  }

  // 时间排序（「最新上传」货架语义：按上架时间展示）
  const byNewest = <T extends { modifiedAt?: string }>(list: T[] | null): T[] | null =>
    list === null ? null : [...list].sort((a, b) => (b.modifiedAt ?? '').localeCompare(a.modifiedAt ?? ''))

  const clientRows: PackageRow[] | null =
    byNewest(clientPackages)?.map((p) => ({
      fileName: p.fileName,
      sizeBytes: p.sizeBytes,
      modifiedAt: p.modifiedAt,
      downloadHref: clientPackageDownloadUrl(p.fileName),
    })) ?? null
  const pdaRows: PackageRow[] | null =
    byNewest(pdaPackages)?.map((p) => ({
      fileName: p.fileName,
      sizeBytes: p.sizeBytes,
      modifiedAt: p.modifiedAt,
      downloadHref: pdaPackageDownloadUrl(p.fileName),
    })) ?? null

  // 二维码前缀 = 管理员浏览器正在访问的地址（PDA 与服务器同网即可直达）
  const qrOrigin = pageOrigin()

  return (
    <div className="page">
      <div className="page-head">
        <div className="page-title">
          {t('page.title')}
          <small>{t('page.subtitle')}</small>
        </div>
        <div className="spacer" />
        <button className="btn" onClick={() => void load()} title={t('refreshTitle')}>
          <Icon name="refresh" size={12} />
          {t('action.refresh')}
        </button>
      </div>

      {error && <div className="banner error">{error}</div>}
      {notice && <div className="banner notice">{notice}</div>}

      <div style={{ flex: 1, overflow: 'auto', padding: 12 }}>
        {/* 页内三 tab：分段按钮（btn + active 既有样式，不新造设计语言） */}
        <div style={{ display: 'flex', gap: 8, marginBottom: 12 }}>
          {DC_TABS.map((id) => (
            <button key={id} className={'btn' + (tab === id ? ' active' : '')} onClick={() => switchTo(id)}>
              {t(`tabs.${id}`)}
            </button>
          ))}
        </div>

        {tab === 'quick' && (
          <div style={{ maxWidth: 720, margin: '0 auto' }}>
            <div style={{ fontSize: 13, fontWeight: 600, margin: '0 0 8px' }}>
              {t('section.client')}
            </div>
            <div className="grid-2" style={{ gap: 12 }}>
              <LatestCard
                platform={t('quick.windowsName')}
                loading={clientRows === null}
                row={clientRows?.[0] ?? null}
                qrOrigin={qrOrigin}
                manageTabLabel={t('tabs.windows')}
                onGoManage={() => switchTo('windows')}
              />
              <LatestCard
                platform={t('quick.androidName')}
                loading={pdaRows === null}
                row={pdaRows?.[0] ?? null}
                qrOrigin={qrOrigin}
                manageTabLabel={t('tabs.android')}
                onGoManage={() => switchTo('android')}
              />
            </div>
            <div style={{ fontSize: 13, fontWeight: 600, margin: '16px 0 8px' }}>
              {t('connection.title')}
            </div>
            <ConnectionCard />
          </div>
        )}

        {tab === 'windows' && (
          <>
            {/* ── Windows 包管理（client-packages 既有数据与接口并入展示，行为不动）── */}
            <SectionHead
              title={t('windows.title')}
              hint={t('windows.hint')}
              uploading={uploadingClient}
              uploadLabel={t('windows.uploadLabel')}
              inputId="clientPkgFile"
              onPick={(f) => void uploadClient(f)}
              uploadingLabel={t('action.uploading')}
            />
            <UpdatePublishing platform="windows" revision={JSON.stringify(clientRows)}>{actions => clientRows === null ? (
              <div className="empty">
                <Icon name="download" />
                <div className="empty-title">{t('windows.loading')}</div>
              </div>
            ) : clientRows.length === 0 ? (
              <div className="empty">
                <Icon name="download" />
                <div className="empty-title">{t('windows.empty')}</div>
                <div className="hint">{t('windows.emptyHint')}</div>
              </div>
            ) : (
              <PackageTable rows={clientRows} qrOrigin={qrOrigin} deleting={deleting} onRemove={(row) => setPendingRemove({ kind: 'client', row })} updateActions={actions} />
            )}</UpdatePublishing>
          </>
        )}

        {tab === 'android' && (
          <>
            {/* ── Android 包管理（pda-packages 目录，上传 / 下载 / 删除行为不动）── */}
            <SectionHead
              title={t('android.title')}
              hint={t('android.hint')}
              uploading={uploadingPda}
              uploadLabel={t('android.uploadLabel')}
              inputId="pdaPkgFile"
              onPick={(f) => void uploadPda(f)}
              uploadingLabel={t('action.uploading')}
            />
            <details style={{ marginBottom: 12 }}>
              <summary>{t('simplePublish.manualInstall')}</summary>
              <span style={{ lineHeight: 1.7 }}>{t('android.installHint')}</span>
            </details>
            <UpdatePublishing platform="android" revision={JSON.stringify(pdaRows)}>{actions => pdaRows === null ? (
              <div className="empty">
                <Icon name="download" />
                <div className="empty-title">{t('android.loading')}</div>
              </div>
            ) : pdaRows.length === 0 ? (
              <div className="empty">
                <Icon name="download" />
                <div className="empty-title">{t('android.empty')}</div>
                <div className="hint">{t('android.emptyHint')}</div>
              </div>
            ) : (
              <PackageTable rows={pdaRows} qrOrigin={qrOrigin} deleting={deleting} onRemove={(row) => setPendingRemove({ kind: 'pda', row })} updateActions={actions} />
            )}</UpdatePublishing>
          </>
        )}
      </div>

      {pendingRemove && (
        <Modal
          title={pendingRemove.kind === 'client' ? t('deleteModal.titleWindows') : t('deleteModal.titleAndroid')}
          onClose={() => setPendingRemove(null)}
          footer={
            <>
              <button className="btn" onClick={() => setPendingRemove(null)}>
                {t('action.cancel')}
              </button>
              <button className="btn danger" onClick={confirmRemove} disabled={deleting !== null}>
                <Icon name="trash" size={12} />
                {t('action.confirmDelete')}
              </button>
            </>
          }
        >
          <p>
            {pendingRemove.kind === 'client'
              ? t('deleteModal.bodyWindows', { name: pendingRemove.row.fileName })
              : t('deleteModal.bodyAndroid', { name: pendingRemove.row.fileName })}
          </p>
        </Modal>
      )}
    </div>
  )
}
