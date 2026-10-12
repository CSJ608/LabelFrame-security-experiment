// 工作台：模板管理（搜索 / 分组过滤 / 新建 / 编辑 / 删除 / 导出 / 导入）
// 业务 API 跟随模式——服务端 = serverApi（模板中心）；单机降级 = localApi（本机 WinHost 模板库）。
// 模板名搜索（子串、大小写不敏感，与分组过滤叠加生效）。
// 预览缩略图（列表加载后按需拉取全部预览，会话内缓存随列表刷新失效；点击居中灯箱放大）。
// 整体信息架构重构（用户定稿方案 A）——表格形态收敛为卡片网格（缩略图主视觉）
//   自适应多列卡片，名称 / 分组 / 日期 / 操作收于卡片下部；既有能力全部保留。
// （C-4，决议 1）：卡片操作区新增「打印」——经 onOpenPrint 跳「数据与打印」页并预选该模板
//   （字段草稿行为与手动选择一致）；双击卡片仍进设计器（现状不变）。
// （修订①）：卡片主操作 = 「编辑」（主色），「打印」降常驻普通按钮
//   低频「导出 / 删除」收进 ⋯ 溢出菜单（锚定 Popover：portal + fixed 防 .wb-card overflow 裁剪）——
//   修复四按钮平铺最小需宽约 216px 超出最窄卡片可用宽度 176px 致「删除」被裁的回归。

import { useCallback, useEffect, useState } from 'react'
import { Trans, useTranslation } from 'react-i18next'
import { localApi, serverApi } from '../lib/api/client'
import { ApiError } from '../lib/api/types'
import type { TemplateSummary } from '../lib/api/types'
import { useApp } from '../state/AppContext'
import type { DesignerRequest } from '../state/types'
import { formatDate, formatDateTime, toAppLocale } from '../i18n'
import { Icon } from '../components/Icon'
import { Modal } from '../components/Modal'
import { MenuItem, Popover } from '../components/Popover'
import { useTemplatePreviewCache } from './useTemplatePreview'
import type { TemplatePreviewEntry } from './useTemplatePreview'
import { TemplatePreviewModal } from './WorkbenchPreview'

/* * 预览缩略图呈现（卡片形态）：加载中骨架 / 失败占位 / 成功图片（点击放大）。 */
function PreviewThumb({
  name,
  entry,
  onEnlarge,
}: {
  name: string
  entry: TemplatePreviewEntry | undefined
  onEnlarge: (name: string) => void
}) {
  // 页面文案 key 化（workbench 域；common 词条经 fallbackNS 免前缀）
  const { t } = useTranslation('workbench')
  if (!entry || entry.status === 'loading') {
    return <div className="preview-thumb-skel card" title={t('preview.generating')} />
  }
  if (entry.status === 'error') {
    return (
      <div className="preview-thumb-err card" title={t('preview.unavailable', { message: entry.message })}>
        <Icon name="alert" size={12} />
      </div>
    )
  }
  return (
    <img
      className="preview-thumb card"
      src={entry.url}
      alt={t('preview.thumbAlt', { name })}
      title={t('preview.enlargeHint')}
      onClick={() => onEnlarge(name)}
    />
  )
}

export function Workbench({
  onOpenDesigner,
  onOpenPrint,
}: {
  onOpenDesigner: (req: DesignerRequest) => void
  /* * （C-4）：卡片「打印」直达——跳「数据与打印」页并预选该模板（预选写草稿由 App 侧统一完成）。 */
  onOpenPrint: (name: string) => void
}) {
  const app = useApp()
  const { serverMode } = app
  // 卡片日期格式化跟随当前界面语言（原硬编码 toLocaleString（'zh-CN'））——
  // 页面文案全面 key 化（workbench 域），useTranslation 同时订阅语言变化触发重渲染
  const { t, i18n } = useTranslation('workbench')
  const locale = toAppLocale(i18n.language)
  /** 业务 API 跟随模式（unknown 时不拉取，待探测完成）。 */
  const biz = serverMode === 'server' ? serverApi : localApi
  const [templates, setTemplates] = useState<TemplateSummary[]>([])
  const [groups, setGroups] = useState<string[]>([])
  const [group, setGroup] = useState('')
  const [search, setSearch] = useState('')
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [deleting, setDeleting] = useState<TemplateSummary | null>(null)
  const [busy, setBusy] = useState<string | null>(null)
  /* * ：当前打开 ⋯ 溢出菜单的卡片（模板 + 触发按钮锚点）；同一时刻至多一个。 */
  const [menu, setMenu] = useState<{ tpl: TemplateSummary; anchor: HTMLElement } | null>(null)

  // 预览缩略图（修订）：缓存随 biz 模式切换后的重新 load 失效
  const preview = useTemplatePreviewCache(biz.previewTemplate)
  /** 当前放大查看的模板名（居中灯箱）。 */
  const [enlarged, setEnlarged] = useState<string | null>(null)
  useEffect(() => {
    if (!enlarged) return
    const onKey = (ev: KeyboardEvent) => {
      if (ev.key === 'Escape') setEnlarged(null)
    }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [enlarged])

  const load = useCallback(async () => {
    setLoading(true)
    setError(null)
    try {
      const list = await biz.listTemplates()
      setTemplates(list)
      setGroups([...new Set(list.map((t) => t.group))].sort())
      // 列表刷新 = 新周期：模板可能已修改，预览缓存整体失效（释放 blob URL）后按需重拉全部
      preview.invalidate()
      for (const t of list) {
        preview.ensure(t.name)
      }
    } catch (err) {
      setError(err instanceof ApiError ? err.message : t('errors.loadList'))
    } finally {
      setLoading(false)
    }
  }, [biz, preview, t])

  useEffect(() => {
    if (serverMode === 'unknown') return
    void load()
  }, [serverMode, load])

  // 名称搜索（子串、大小写不敏感）与分组过滤叠加：两者都为空时即完整列表
  const keyword = search.trim().toLowerCase()
  const filtered = templates.filter((t) => {
    if (group && t.group !== group) return false
    if (keyword && !t.name.toLowerCase().includes(keyword)) return false
    return true
  })

  const doDelete = async () => {
    if (!deleting) return
    setBusy('delete')
    try {
      await biz.deleteTemplate(deleting.name)
      app.setStatus(t('status.deleted', { name: deleting.name }))
      setDeleting(null)
      void load()
    } catch (err) {
      app.setStatus(err instanceof ApiError ? err.message : t('status.deleteFailed'))
    } finally {
      setBusy(null)
    }
  }

  const doExport = async (tpl: TemplateSummary) => {
    setBusy(tpl.name)
    try {
      const { blob, filename } = await biz.exportTemplate(tpl.name)
      const url = URL.createObjectURL(blob)
      const a = document.createElement('a')
      a.href = url
      a.download = filename
      document.body.appendChild(a)
      a.click()
      a.remove()
      URL.revokeObjectURL(url)
      app.setStatus(t('status.exported', { name: tpl.name }))
    } catch (err) {
      app.setStatus(err instanceof ApiError ? err.message : t('status.exportFailed'))
    } finally {
      setBusy(null)
    }
  }

  const doImport = async (file: File) => {
    setBusy('import')
    try {
      const name = await biz.importTemplate(file)
      app.setStatus(t('status.imported', { name }))
      void load()
    } catch (err) {
      app.setStatus(err instanceof ApiError ? err.message : t('status.importFailed'))
    } finally {
      setBusy(null)
    }
  }

  return (
    <div className="page">
      <div className="page-head">
        <div className="page-title">
          {t('title')}
          <small>{t('subtitle')}</small>
        </div>
        <div className="spacer" />
        <button className="btn" onClick={() => void load()} disabled={loading || serverMode === 'unknown'} title={t('reloadListTitle')}>
          <Icon name="refresh" size={13} />
          {loading ? t('action.refreshing') : t('action.refresh')}
        </button>
        <div style={{ position: 'relative', display: 'inline-flex', alignItems: 'center' }}>
          <Icon name="search" size={13} style={{ position: 'absolute', left: 7, color: 'var(--ink-3)', pointerEvents: 'none' }} />
          <input
            className="input"
            value={search}
            onChange={(ev) => setSearch(ev.target.value)}
            placeholder={t('searchPlaceholder')}
            title={t('searchTitle')}
            spellCheck={false}
            style={{ width: 170, paddingLeft: 26 }}
          />
        </div>
        <select className="input" value={group} onChange={(ev) => setGroup(ev.target.value)} title={t('filterByGroupTitle')}>
          <option value="">{t('allGroups')}</option>
          {groups.map((g) => (
            <option key={g} value={g}>
              {g}
            </option>
          ))}
        </select>
        <button className="btn" onClick={() => document.getElementById('importFile')?.click()} disabled={busy !== null}>
          <Icon name="upload" size={13} />
          {t('importTemplate')}
        </button>
        <input
          id="importFile"
          type="file"
          accept=".lfpkg,application/zip"
          style={{ display: 'none' }}
          onChange={(ev) => {
            const f = ev.target.files?.[0]
            if (f) void doImport(f)
            ev.target.value = ''
          }}
        />
        {/* data-guide：首次引导第 1 步锚点（迭代 119 / #276） */}
        <button className="btn primary" data-guide="workbench-new" onClick={() => onOpenDesigner({ kind: 'new' })}>
          <Icon name="plus" size={13} />
          {t('newTemplate')}
        </button>
      </div>

      {/* 迭代 93（#151 F-17）：页头刷新按钮（与作业历史同款）+ 失败横幅旁重试入口——此前列表加载失败只能切页重试 */}
      {error && (
        <div className="banner error">
          {error}
          <button className="btn sm" onClick={() => void load()} disabled={loading}>
            {t('action.retry')}
          </button>
        </div>
      )}

      <div style={{ flex: 1, overflow: 'auto', padding: 12 }}>
        {loading || serverMode === 'unknown' ? (
          <div className="empty">
            <Icon name="refresh" />
            <div className="empty-title">{serverMode === 'unknown' ? t('state.connectingServer') : t('state.loading')}</div>
          </div>
        ) : templates.length === 0 ? (
          <div className="empty">
            <Icon name="workbench" />
            <div className="empty-title">{t('empty.title')}</div>
            <div className="hint">{t('empty.hint')}</div>
          </div>
        ) : filtered.length === 0 ? (
          <div className="empty">
            <Icon name="search" />
            <div className="empty-title">{t('noMatch.title')}</div>
            <div className="hint">{t('noMatch.hint')}</div>
          </div>
        ) : (
          <div className="wb-grid">
            {filtered.map((tpl) => (
              <div key={tpl.name} className="wb-card" onDoubleClick={() => onOpenDesigner({ kind: 'edit', name: tpl.name })} title={t('card.openDesignerTitle')}>
                <div className="wb-card-thumb">
                  <PreviewThumb name={tpl.name} entry={preview.get(tpl.name)} onEnlarge={setEnlarged} />
                </div>
                <div className="wb-card-body">
                  <div className="wb-card-name" title={tpl.name}>{tpl.name}</div>
                  <div className="wb-card-meta">
                    <span className="badge neutral">{tpl.group}</span>
                    <span className="mono" title={formatDateTime(locale, new Date(tpl.updatedAt))}>
                      {formatDate(locale, new Date(tpl.updatedAt))}
                    </span>
                  </div>
                </div>
                <div className="wb-card-foot">
                  {/* 迭代 104（#225，修订决策 #152①）：主操作 = 「编辑」（主色列首）＋「打印」常驻；
                      低频「导出 / 删除」收进 ⋯ 溢出菜单——卡片脚最窄可容两按钮一图标，删除不再被裁 */}
                  <button className="btn sm primary" onClick={() => onOpenDesigner({ kind: 'edit', name: tpl.name })}>
                    <Icon name="edit" size={12} />
                    {t('card.edit')}
                  </button>
                  <button
                    className="btn sm"
                    onClick={() => onOpenPrint(tpl.name)}
                    title={t('card.printTitle')}
                  >
                    <Icon name="printer" size={12} />
                    {t('card.print')}
                  </button>
                  <button
                    className="btn sm wb-more"
                    aria-haspopup="menu"
                    aria-expanded={menu?.tpl.name === tpl.name}
                    aria-label={t('card.moreAria', { name: tpl.name })}
                    title={t('card.moreTitle')}
                    onClick={(ev) => {
                      const anchor = ev.currentTarget
                      setMenu((cur) => (cur?.tpl.name === tpl.name ? null : { tpl, anchor }))
                    }}
                  >
                    <Icon name="more" size={14} />
                  </button>
                </div>
              </div>
            ))}
          </div>
        )}
      </div>

      {/* 迭代 104（#225）：⋯ 溢出菜单（导出 / 删除）——portal 到 body + fixed 锚定，防 .wb-card overflow 裁剪；
          「删除」为 danger 菜单项，点击仍走下方确认 Modal（决策 #161：销毁操作必须先确认） */}
      {menu && (
        <Popover anchor={menu.anchor} ariaLabel={t('card.moreAria', { name: menu.tpl.name })} onClose={() => setMenu(null)}>
          <MenuItem
            disabled={busy !== null}
            onClick={() => {
              const tpl = menu.tpl
              setMenu(null)
              void doExport(tpl)
            }}
          >
            <Icon name="download" size={13} />
            {t('menu.export')}
          </MenuItem>
          <MenuItem
            danger
            onClick={() => {
              setDeleting(menu.tpl)
              setMenu(null)
            }}
          >
            <Icon name="trash" size={13} />
            {t('menu.delete')}
          </MenuItem>
        </Popover>
      )}

      {enlarged && (() => {
        const entry = preview.get(enlarged)
        return entry ? <TemplatePreviewModal name={enlarged} entry={entry} onClose={() => setEnlarged(null)} /> : null
      })()}

      {deleting && (
        <Modal
          title={t('deleteConfirm.title')}
          onClose={() => setDeleting(null)}
          footer={
            <>
              <button className="btn" onClick={() => setDeleting(null)}>
                {t('action.cancel')}
              </button>
              <button className="btn danger" onClick={() => void doDelete()} disabled={busy === 'delete'}>
                <Icon name="trash" size={13} />
                {t('deleteConfirm.confirm')}
              </button>
            </>
          }
        >
          <p>
            <Trans t={t} i18nKey="deleteConfirm.body" values={{ name: deleting.name }} components={[<b key={0} />]} />
          </p>
        </Modal>
      )}
    </div>
  )
}
