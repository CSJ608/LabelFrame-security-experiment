// 下载中心页（迭代 118 改版 · #272，升级迭代 59 决策 #119 设立的下载中心）：页内三 tab——
// 「快速访问」（默认）/「Windows 包管理」/「Android 包管理」；tab 状态由路由 sub 段承载（迭代 127 · #312，
// 决策 #177——升级 #169 的 #dc= 形态为 #/packages/<sub>，旧 #dc= 分享链接经 parseHash 兼容映射直达），
// 刷新 / 直链打开均还原当前 tab；视觉沿用仓库页内 tab 先例（btn + active 分段风格）。
// 快速访问 = 消费视图（拿包 + 连服务器），整区限宽居中：上区块标题「客户端」——Windows / Android 各一张
// 「最新上传」卡（货架语义按上架时间/修改时间倒序取第一条，删除后自动回退剩余最新一条，不做版本号解析 / 推荐标记）；
// 下区块「服务端信息」卡（与上排同款卡片视觉）：大二维码居中（内容 = 选中的裸地址 URL，无包装协议）+ 地址 + 复制。
// 连接地址候选来自 GET /api/server/ipv4-candidates（只读枚举本机 IPv4，私网优先）：默认取与 origin 匹配者，
// localhost / 无匹配回退首个候选（不产生 localhost 废码）；多网卡可切换候选，二维码 / 地址 / 复制同步更新。
// 管理退居专职 tab：Windows（client-packages）/ Android（pda-packages）上传 / 下载 / 删除（确认 Modal）
// 行为与数据接口不动（AC-06 回归）；client-packages 既有行为不动：客户端设置页「更新与安装包」仍走
// GET /api/client-packages 下载。措辞平台优先（Windows / Android），downloadCenter 域 zh-CN + en 全量 key 化（迭代 112 #246 起延续）。

import { useCallback, useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import qrcode from 'qrcode-generator'
// i18n 初始化副作用导入：本页组件树不传递依赖 i18n/index（api client 在测试中被 mock），
// 显式引入保证 useTranslation 在任何入口（含单测）下都有已注册的语言包资源。
import '../i18n'
import { clientPackageDownloadUrl, pdaPackageDownloadUrl, serverApi } from '../lib/api/client'
import { ApiError } from '../lib/api/types'
import type { ClientPackageInfo, PdaPackageInfo } from '../lib/api/types'
import { Icon } from '../components/Icon'
import { Modal } from '../components/Modal'
import { formatSize } from '../lib/download'
import { copyText } from '../lib/clipboard'
import { joinAddress, pickDefaultAddress } from '../lib/connection'

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

// ── 页内 tab（迭代 118 · #272 立；迭代 127 · #312 改路由 sub 段承载）：#/packages/<sub>，刷新 / 直链还原 ──

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
      style={{ display: 'block', background: '#fff', border: '1px solid var(--border)', borderRadius: 4, imageRendering: 'pixelated' }}
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
      <div className="page-title" style={{ fontSize: 15, margin: 0, flex: 1 }}>
        {title}
        <small>{hint}</small>
      </div>
      <button className="btn sm" onClick={() => document.getElementById(inputId)?.click()} disabled={uploading}>
        <Icon name="upload" size={13} />
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
}: {
  rows: PackageRow[]
  /** 二维码前缀（本页 origin）：二维码内容 = origin + 下载相对路径 = 局域网可访问的完整 URL。 */
  qrOrigin: string
  deleting: string | null
  onRemove: (row: PackageRow) => void
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
          <th style={{ width: 170 }}></th>
        </tr>
      </thead>
      <tbody>
        {rows.map((p) => (
          <tr key={p.fileName} style={{ cursor: 'default' }}>
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
            <td>
              <div style={{ display: 'flex', gap: 6 }}>
                <a className="btn sm" href={p.downloadHref} title={t('downloadTitle', { name: p.fileName })}>
                  <Icon name="download" size={12} />
                  {t('action.download')}
                </a>
                <button
                  className="btn sm danger"
                  onClick={() => onRemove(p)}
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
  )
}

/** 「最新上传」货架徽标（accent 浅底小标签）。 */
function LatestBadge({ label }: { label: string }) {
  return (
    <span
      style={{
        padding: '1px 8px',
        fontSize: 11,
        lineHeight: '18px',
        color: 'var(--accent)',
        background: 'var(--accent-soft)',
        borderRadius: 999,
        whiteSpace: 'nowrap',
      }}
    >
      {label}
    </span>
  )
}

/**
 * 快速访问首屏「最新上传」卡（Windows / Android 各一，#272）：
 * 货架语义——取修改时间倒序第一条（管理员换货即生效、删除自动回退剩余最新一条）；
 * 卡内：文件名 / 大小 / 上传时间 / 下载二维码（origin + 下载路径）/ 下载按钮 /「全部版本 →」跳对应管理 tab；
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
      style={{
        padding: 14,
        background: 'var(--panel)',
        border: '1px solid var(--line)',
        borderRadius: 'var(--radius)',
        boxShadow: 'var(--shadow-1)',
        display: 'flex',
        flexDirection: 'column',
        gap: 10,
      }}
    >
      <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
        <span style={{ fontSize: 13, fontWeight: 600 }}>{platform}</span>
        {row && <LatestBadge label={t('quick.latest')} />}
      </div>
      {loading ? (
        <div className="hint" style={{ padding: '36px 0', textAlign: 'center' }}>
          {t('quick.loading')}
        </div>
      ) : !row ? (
        <div style={{ display: 'flex', flexDirection: 'column', alignItems: 'center', gap: 8, padding: '22px 0' }}>
          <Icon name="download" />
          <div className="empty-title">{t('quick.emptyTitle')}</div>
          <div className="hint">{t('quick.emptyHint', { tab: manageTabLabel })}</div>
          <button className="btn" onClick={onGoManage}>
            <Icon name="upload" size={13} />
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
              <Icon name="download" size={13} />
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

/** 候选 IP 按当前 origin 的协议与端口拼装完整地址（默认端口时省略端口段）。 */
function candidateUrl(ip: string): string {
  try {
    const u = new URL(pageOrigin())
    return joinAddress(u.protocol, ip, u.port)
  } catch {
    return ip
  }
}

/**
 * 连接信息卡（快速访问首屏下排通栏，浅底与上排区分，#272）：
 * 第一行大二维码居中（内容 = 选中裸地址 URL，无包装协议——PDA 扫码枪键盘直填即得地址）；
 * 第二行等宽地址文本 + 复制按钮（复制当前选中地址）；多网卡候选可切换，切换后二维码 / 地址 / 复制同步更新。
 * 地址候选 = GET /api/server/ipv4-candidates（私网优先排序）；默认选择规则见 lib/connection.ts pickDefaultAddress。
 */
function ConnectionCard() {
  const { t } = useTranslation('downloadCenter')
  const [candidates, setCandidates] = useState<string[] | null>(null)
  const [selected, setSelected] = useState<string>(pageOrigin())
  const [copied, setCopied] = useState(false)

  useEffect(() => {
    let cancelled = false
    serverApi
      .listServerIpv4Candidates()
      .then((res) => {
        if (!cancelled) setCandidates(Array.isArray(res.candidates) ? res.candidates : [])
      })
      .catch(() => {
        // 旧版服务端（无该端点）/ 请求失败：候选置空 → 展示回退当前 origin（不阻塞首屏）
        if (!cancelled) setCandidates([])
      })
    return () => {
      cancelled = true
    }
  }, [])

  useEffect(() => {
    if (candidates === null) return
    setSelected(pickDefaultAddress(pageOrigin(), candidates))
  }, [candidates])

  const copy = async () => {
    if (await copyText(selected)) {
      setCopied(true)
      window.setTimeout(() => setCopied(false), 2000)
    }
  }

  return (
    <div
      style={{
        padding: 14,
        background: 'var(--panel)',
        border: '1px solid var(--line)',
        borderRadius: 'var(--radius)',
        boxShadow: 'var(--shadow-1)',
      }}
    >
      <div style={{ display: 'flex', justifyContent: 'center', marginBottom: 10 }}>
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
      </div>
      {candidates !== null && candidates.length > 1 && (
        <div style={{ display: 'flex', justifyContent: 'center', alignItems: 'center', gap: 6, marginTop: 10, flexWrap: 'wrap' }}>
          <span className="hint">{t('connection.candidates')}</span>
          {candidates.map((ip) => (
            <button
              key={ip}
              className={'btn sm' + (selected === candidateUrl(ip) ? ' active' : '')}
              onClick={() => setSelected(candidateUrl(ip))}
              title={candidateUrl(ip)}
            >
              {ip}
            </button>
          ))}
        </div>
      )}
    </div>
  )
}

export function DownloadCenter({ sub = '', onSubChange }: { sub?: string; onSubChange?: (next: string) => void }) {
  const { t } = useTranslation('downloadCenter')
  // 迭代 127（#312，决策 #177）：页内 tab 状态源 = 路由 sub 段（App 经 props 注入，缺省 '' → quick）；
  // 切 tab 经 onSubChange 写 sub（replaceState 不入栈，由路由层执行），旧 #dc= 自有 hash 三件已删。
  const tab = tabFromSub(sub)
  const [clientPackages, setClientPackages] = useState<ClientPackageInfo[] | null>(null)
  const [pdaPackages, setPdaPackages] = useState<PdaPackageInfo[] | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [notice, setNotice] = useState<string | null>(null)
  const [uploadingClient, setUploadingClient] = useState(false)
  const [uploadingPda, setUploadingPda] = useState(false)
  const [deleting, setDeleting] = useState<string | null>(null)
  // 迭代 93（#151 F-04）：删除确认改自研 Modal（复用工作台删除确认模式），替代原生 confirm 弹窗
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

  // 时间排序（迭代 59 决议 3 → 迭代 118 收口为「最新上传」货架语义：按上架时间展示，latest.json 对齐事项就此关闭）
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
          <Icon name="refresh" size={13} />
          {t('action.refresh')}
        </button>
      </div>

      {error && <div className="banner error">{error}</div>}
      {notice && <div className="banner notice">{notice}</div>}

      <div style={{ flex: 1, overflow: 'auto', padding: 12 }}>
        {/* 页内三 tab（#272）：分段按钮（btn + active 既有样式，不新造设计语言） */}
        <div style={{ display: 'flex', gap: 6, marginBottom: 12 }}>
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
            {clientRows === null ? (
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
              <PackageTable rows={clientRows} qrOrigin={qrOrigin} deleting={deleting} onRemove={(row) => setPendingRemove({ kind: 'client', row })} />
            )}
          </>
        )}

        {tab === 'android' && (
          <>
            {/* ── Android 包管理（pda-packages 目录，迭代 59 决策 #119；上传 / 下载 / 删除行为不动）── */}
            <SectionHead
              title={t('android.title')}
              hint={t('android.hint')}
              uploading={uploadingPda}
              uploadLabel={t('android.uploadLabel')}
              inputId="pdaPkgFile"
              onPick={(f) => void uploadPda(f)}
              uploadingLabel={t('action.uploading')}
            />
            <div
              style={{
                padding: '8px 12px',
                marginBottom: 8,
                background: 'var(--accent-soft)',
                color: 'var(--accent)',
                fontSize: 12,
                lineHeight: 1.7,
                borderRadius: 4,
              }}
            >
              <Icon name="alert" size={12} style={{ marginRight: 4, verticalAlign: '-2px' }} />
              {t('android.installHint')}
            </div>
            {pdaRows === null ? (
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
              <PackageTable rows={pdaRows} qrOrigin={qrOrigin} deleting={deleting} onRemove={(row) => setPendingRemove({ kind: 'pda', row })} />
            )}
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
                <Icon name="trash" size={13} />
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
