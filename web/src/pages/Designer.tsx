// 设计器（核心页面壳）：顶栏（模板名 / 分组 / 纸张 / 预览 / 保存）+ 左栏（控件 / 字段 / 图层）
// + 画布 + 右栏（属性 / 测试数据 Tab）。stateRef 为同步真相（事件回调内先更新再 setState），
// 历史为快照式；dirty（undoCount 与最近成功保存点不一致）时离开走三选 Modal，一致直接离开不弹窗；
// Ctrl+S 就地保存（成功留在设计器并推进保存点）。
// 编排拆分：元素操作 / 深链演示 / 图片资源 / 保存 / 离开保护下沉 pages/designer/* hooks，机制层 lib/design/ 不动。

import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { setHostDirty } from '../lib/hostLifecycle'
import { useTranslation } from 'react-i18next'
import { localApi, serverApi } from '../lib/api/client'
import { isServerUi } from '../lib/uiMode'
import { ApiError } from '../lib/api/types'
import type { TemplatePackage } from '../lib/api/types'
import type { DesignerRequest } from '../state/types'
import { useApp } from '../state/AppContext'
import { CanvasViewport } from './designer/CanvasViewport'
import type { DesignState } from './designer/CanvasViewport'
import { SidePanel } from './designer/SidePanel'
import { PropsPanel } from './designer/PropsPanel'
import { Icon } from '../components/Icon'
import { Modal } from '../components/Modal'
import { ErrorAlert } from '../components/ErrorAlert'
import { MenuItem, Popover } from '../components/Popover'
import type { DesignElement, ImageAsset } from '../lib/design/types'
import { createHistory } from '../lib/design/history'
import { fromBackendElements, applyContractDisplayNames } from '../lib/design/convert'
import { deriveFieldInfos } from '../lib/design/fields'
import { HELP_LINK_ELEMENTS } from '../lib/help'
import i18next from '../i18n'
import { shortcutGroups } from './designer/shortcuts'
import { snap, parse } from './designer/designState'
import type { DesignerHistory } from './designer/designState'
import { IMAGE_ACCEPT, base64ToDataUrl, decodeImageSize } from './designer/images'
import { useDesignerActions } from './designer/useDesignerActions'
import { useHelpLinkDraft, buildHelpLinkElements } from './designer/useHelpLinkDraft'
import { useImageAssets } from './designer/useImageAssets'
import { useTemplateSave } from './designer/useTemplateSave'
import { useLeaveGuard } from './designer/useLeaveGuard'

// 测试引用面保持稳定（Designer.test.tsx 自本模块导入）
export { sanitizeImageKey } from './designer/images'

interface DesignerProps {
  request: DesignerRequest
  onClose: () => void
  /* * 向 Shell 注册离开守卫（导航 tab 切换拦截）；卸载自动注销。返回 false=已挂起三选待确认、
   * true=已直接放行——路由据此判定是否 pre-revert（非 dirty 放行时不得回退，否则浏览器后退在设计器上失效）。 */
  registerLeaveGuard?: (fn: ((leave: () => void) => boolean) | null) => void
  /* * 帮助深链锚点（= 路由 sub 段，Shell 直传）；页内轮询入 DOM 后自动弹文档点气泡。 */
  helpAnchor?: string | null
}

export function Designer({ request, onClose, registerLeaveGuard, helpAnchor }: DesignerProps) {
  const app = useApp()
  const { t } = useTranslation('designer')
  const { serverMode } = app
  /* * 业务 API 跟随模式（unknown 时不发请求，待探测完成）。 */
  const biz = serverMode === 'server' ? serverApi : localApi
  const [state, setState] = useState<DesignState | null>(null)
  const [selected, setSelected] = useState<string[]>([])
  const [viewMode, setViewMode] = useState<'fit' | 'preview'>('fit')
  // 打印 DPI 口径全局唯一（设置页打印设置）：预览缩放取客户端当前生效值，失败（旧宿主 404）保持 203
  const [printDpi, setPrintDpi] = useState(203)
  const [zoom, setZoom] = useState(1)
  const [gridOn, setGridOn] = useState(true)
  const [pendingType, setPendingType] = useState<string | null>(null)
  const [name, setName] = useState('')
  const [group, setGroup] = useState(() => i18next.t('designer:defaultGroup'))
  const [rightTab, setRightTab] = useState<'props' | 'data'>('props')
  const [shortcutsOpen, setShortcutsOpen] = useState(false)
  const [moreAnchor, setMoreAnchor] = useState<HTMLElement | null>(null)
  const [loadError, setLoadError] = useState<string | null>(null)
  const [saving, setSaving] = useState(false)
  const [confirmOverwrite, setConfirmOverwrite] = useState(false)

  const stateRef = useRef<DesignState | null>(null)
  const historyRef = useRef<DesignerHistory | null>(null)
  const initialNameRef = useRef('')
  const contractNameRef = useRef('')
  const contractVersionRef = useRef('1')
  const selectedRef = useRef<string[]>([])
  // 编辑目标的模板稳定标识：详情响应优先，回退工作台 request 透传（旧后端缺失保持 undefined）
  const uidRef = useRef<string | undefined>(undefined)
  // 最近一次成功保存时的 undoCount（0＝尚未保存过）：离开守卫 dirty 判定基准
  const savedMarkRef = useRef(0)
  const savedMetadataRef = useRef({ name: '', group: i18next.t('designer:defaultGroup') })
  useEffect(() => {
    setHostDirty('designer', (historyRef.current?.undoCount ?? 0) !== savedMarkRef.current
      || name !== savedMetadataRef.current.name || group !== savedMetadataRef.current.group)
  })
  useEffect(() => () => setHostDirty('designer', false), [])
  // 离开保护挂起的离开动作：「放弃更改」与「保存并离开」成功后执行；
  // 各终止路径（继续编辑 / 保存失败）就地作废；取消 / 空名类拦截仅离开发起时作废（Ctrl+S 就地发起不动挂起导航，失败仍作废）。
  const pendingLeaveRef = useRef<(() => void) | null>(null)
  // 加载单次闩锁 + 挂载标记（见下方加载 effect 注释）
  const initedRef = useRef(false)
  const mountedRef = useRef(true)
  useEffect(() => {
    mountedRef.current = true
    return () => {
      mountedRef.current = false
    }
  }, [])

  const commit = useCallback((next: DesignState) => {
    stateRef.current = next
    if (historyRef.current) historyRef.current = historyRef.current.commit(next)
    setState(next)
  }, [])

  const applyElements = useCallback(
    (updater: (els: DesignElement[]) => DesignElement[]) => {
      const prev = stateRef.current
      if (!prev) return
      const next = { ...prev, elements: updater(prev.elements) }
      stateRef.current = next
      setState(next)
    },
    [],
  )

  const commitNow = useCallback(() => {
    if (historyRef.current && stateRef.current) {
      historyRef.current = historyRef.current.commit(stateRef.current)
    }
  }, [])

  const { setDemoAnchor, demoPatch, demoActive, autoOpenAnchor, setAutoOpenAnchor, deepLinkDraftRef } = useHelpLinkDraft(helpAnchor, {
    request,
    stateRef,
    historyRef,
    selectedRef,
    setState,
    setSelected,
  })

  // Ctrl+S 就地保存入口：latest-ref 注入 useDesignerActions——保存 hook 依赖图片 hook 的 ref、
  // 图片 hook 又依赖动作 hook 的回调（声明顺序环，直接传函数则每次渲染换键监听重挂）。
  const saveRef = useRef<((opts?: { stay?: boolean }) => void) | null>(null)
  const saveInPlace = useCallback(() => saveRef.current?.({ stay: true }), [])

  const {
    handleSelect,
    addElementAt,
    changeElement,
    deleteElements,
    alignSelected,
    moveLayer,
    layerToTop,
    layerToBottom,
    doExportDesign,
    doImportDesign,
    importError,
    clearImportError,
  } = useDesignerActions({ stateRef, historyRef, selectedRef, setState, setSelected, setPendingType, commit, viewMode, pendingType, demoActive, saveInPlace })

  const { imageAssets, setImageAssets, imageAssetsRef, fileInputRef, replaceImage, onImageInputChange, imageToast } = useImageAssets({
    stateRef,
    changeElement,
    viewMode,
  })

  // ---------- 加载 ----------
  // serverMode unknown（探测未完成）时不以 localApi 误发请求，待解析后重跑本 effect；
  // serverMode 为原始值入依赖安全——app（context 对象，每次渲染新引用）入依赖才会无限循环。
  // initedRef 单次闩锁——模式中途翻转（周期探测 server ↔ standalone）不重拉模板、不重置编辑中状态
  //（组件按 key 重挂，request 一次挂载内不变）。
  useEffect(() => {
    if (request.kind !== 'new' && serverMode === 'unknown') return
    if (initedRef.current) return
    initedRef.current = true
    const status = app.setStatus
    const init = (s: DesignState, pkg?: TemplatePackage) => {
      if (!mountedRef.current) return
      stateRef.current = s
      historyRef.current = createHistory(s, snap, parse)
      setState(s)
      setSelected([])
      selectedRef.current = []
      setZoom(1)
      setViewMode('fit')
      if (pkg) {
        setName(pkg.name)
        setGroup(pkg.group || i18next.t('designer:defaultGroup'))
        savedMetadataRef.current = { name: pkg.name, group: pkg.group || i18next.t('designer:defaultGroup') }
        initialNameRef.current = pkg.name
        uidRef.current = pkg.uid ?? request.uid
        contractNameRef.current = pkg.contract?.name ?? pkg.name
        contractVersionRef.current = pkg.contract?.version ?? '1'
        // 图片资源回填：base64 即时回填画布；像素尺寸异步解码补齐（失败保持 0，仅影响展示）。
        if (pkg.images && Object.keys(pkg.images).length > 0) {
          const assets: Record<string, ImageAsset> = {}
          for (const [key, base64] of Object.entries(pkg.images)) {
            assets[key] = {
              dataUrl: base64ToDataUrl(base64),
              width: 0,
              height: 0,
              sizeBytes: Math.floor((base64.length * 3) / 4),
            }
          }
          imageAssetsRef.current = assets
          setImageAssets(assets)
          void Promise.all(
            Object.entries(assets).map(async ([key, a]) => [key, { ...a, ...(await decodeImageSize(a.dataUrl)) }] as const),
          ).then((entries) => {
            if (!mountedRef.current) return
            const next = { ...imageAssetsRef.current }
            for (const [key, value] of entries) next[key] = value
            imageAssetsRef.current = next
            setImageAssets(next)
          })
        }
      }
    }

    if (request.kind === 'new') {
      // 深链自动开草稿时按锚点放置示例元素——并入初始基线（可正常编辑、undoCount=0 不 dirty、未编辑退出静默弃）。
      const linkAnchor = helpAnchor && Object.hasOwn(HELP_LINK_ELEMENTS, helpAnchor) ? helpAnchor : null
      const samples = linkAnchor ? buildHelpLinkElements(linkAnchor) : []
      init({ paperW: 100, paperH: 60, elements: samples })
      if (linkAnchor) {
        const ids = samples.map((e) => e.id)
        setSelected(ids)
        selectedRef.current = ids
        deepLinkDraftRef.current = true
        status(t('help:deepLink.draftOpened'))
      } else {
        status(t('status.newTemplate'))
      }
      return
    }
    void biz
      .getTemplate(request.name!)
      .then((pkg) => {
        init(
          {
            paperW: pkg.layout?.widthMm || 100,
            paperH: pkg.layout?.heightMm || 60,
            elements: applyContractDisplayNames(fromBackendElements(pkg.layout?.elements ?? []), pkg.contract?.fields ?? []),
          },
          pkg,
        )
        if (mountedRef.current) status(t('status.opened', { name: pkg.name }))
      })
      .catch((err) => {
        if (mountedRef.current) setLoadError(err instanceof ApiError ? err.message : t('status.loadFailed'))
      })
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [request, serverMode])

  useEffect(() => {
    // 客户端全局打印 DPI（设置页）；server 管理界面无本机打印语义，保持默认 203
    if (isServerUi) return
    let alive = true
    localApi
      .getPrintSettings()
      .then((s) => {
        if (alive) setPrintDpi(s.dpi ?? 203)
      })
      .catch(() => {})
    return () => {
      alive = false
    }
  }, [])

  // ---------- 预览 ----------
  const togglePreview = useCallback(() => {
    if (viewMode === 'preview') {
      setViewMode('fit')
      setZoom(1)
      setPendingType(null)
      app.setStatus(t('status.previewExited'))
    } else {
      setViewMode('preview')
      setZoom(1)
      setSelected([])
      selectedRef.current = []
      app.setStatus(t('status.previewEntered', { dpi: printDpi, dots: Math.round(printDpi / 25.4) }))
    }
  }, [app, printDpi, viewMode, t])

  // ---------- 保存 + 离开保护 ----------
  /** 保存成功统一回调（就地与离开两路）：推进保存点标记至当前 undoCount。 */
  const markSaved = useCallback(() => {
    savedMarkRef.current = historyRef.current?.undoCount ?? 0
    savedMetadataRef.current = { name, group }
  }, [name, group])

  const { save, overwriteSave, cancelOverwrite, nameError, clearNameError, saveError, clearSaveError } = useTemplateSave({
    request,
    biz,
    stateRef,
    initialNameRef,
    contractNameRef,
    contractVersionRef,
    imageAssetsRef,
    uidRef,
    group,
    name,
    onClose,
    pendingLeaveRef,
    setSaving,
    setConfirmOverwrite,
    onSaved: markSaved,
  })

  // Ctrl+S 分发用 latest 保存入口（见 saveInPlace 声明处注释）
  useEffect(() => {
    saveRef.current = save
  }, [save])

  // 空名保存拦截：名称框红边 + 内联提示 + 聚焦，补名即消
  const nameInputRef = useRef<HTMLInputElement>(null)
  useEffect(() => {
    if (nameError && !saveError) nameInputRef.current?.focus()
  }, [nameError, saveError])

  const { leaveGuardOpen, stayInEditor, discardAndLeave, saveAndLeave, requestClose } = useLeaveGuard({
    historyRef,
    savedMarkRef,
    pendingLeaveRef,
    onClose,
    save,
    registerLeaveGuard,
  })

  const fields = useMemo(() => (state ? deriveFieldInfos(state.elements) : []), [state])

  // 演示视图：override 只合并进渲染视图——图层列表与「测试默认值」页签保持原始 state；
  // 注入值不得进 previewDefaults 等派生链路（零落库保证）。
  const viewState = useMemo<DesignState | null>(() => {
    if (!state || !demoPatch) return state
    const targetId = selectedRef.current[0]
    return {
      ...state,
      elements: state.elements.map((el) => (el.id === targetId ? ({ ...el, ...demoPatch } as DesignElement) : el)),
    }
  }, [state, demoPatch])

  // 测试默认值只读预览：与后端 SaveAsync 派生语义一致（遍历元素，后出现覆盖先出现）
  const previewDefaults = useMemo(() => {
    if (!state) return null
    const m = new Map<string, string>()
    for (const e of state.elements) {
      if ('mode' in e && e.mode === 'field' && e.key && e.text) m.set(e.key, e.text)
    }
    return m
  }, [state])

  const displayNameByKey = useMemo(() => {
    const m = new Map<string, string>()
    if (!state) return m
    for (const f of deriveFieldInfos(state.elements)) {
      if (f.displayName) m.set(f.key, f.displayName)
    }
    return m
  }, [state])

  // ---------- 渲染 ----------
  return (
    <div className="page designer-page">
      {/* 图片文件选择：上传 / 更换共用，隐藏不占位 */}
      <input ref={fileInputRef} type="file" accept={IMAGE_ACCEPT} style={{ display: 'none' }} onChange={onImageInputChange} data-testid="image-file-input" />
      <div className="designer-toolbar">
        <button className="btn ghost" onClick={requestClose} title={t('toolbar.backTitle')}>
          <Icon name="back" size={12} />
        </button>
        <input
          ref={nameInputRef}
          className={`input${nameError ? ' error' : ''}`}
          style={{ width: 150 }}
          value={name}
          onChange={(ev) => {
            setName(ev.target.value)
            if (nameError) clearNameError()
          }}
          placeholder={t('toolbar.namePlaceholder')}
          title={t('toolbar.namePlaceholder')}
          data-testid="designer-name-input"
        />
        {nameError && (
          <span className="error-text" data-testid="designer-name-error">
            {t('status.nameRequired')}
          </span>
        )}
        <input className="input" style={{ width: 100 }} value={group} onChange={(ev) => setGroup(ev.target.value)} placeholder={t('toolbar.groupPlaceholder')} title={t('toolbar.groupPlaceholder')} />
        <span className="toolbar-sep" />
        <label className="toolbar-label">
          {t('toolbar.width')}
          <input
            className="input num"
            type="number"
            style={{ width: 56 }}
            value={state?.paperW ?? ''}
            min={5}
            max={300}
            onChange={(ev) => {
              const v = parseFloat(ev.target.value)
              const s = stateRef.current
              if (s && !isNaN(v) && v > 0) commit({ ...s, paperW: Math.min(300, v) })
            }}
          />
          mm
        </label>
        <label className="toolbar-label">
          {t('toolbar.height')}
          <input
            className="input num"
            type="number"
            style={{ width: 56 }}
            value={state?.paperH ?? ''}
            min={5}
            max={300}
            onChange={(ev) => {
              const v = parseFloat(ev.target.value)
              const s = stateRef.current
              if (s && !isNaN(v) && v > 0) commit({ ...s, paperH: Math.min(300, v) })
            }}
          />
          mm
        </label>
        <span className="toolbar-sep" />
        <button className={'btn' + (viewMode === 'preview' ? ' active' : '')} onClick={togglePreview} title={t('toolbar.previewTitle')}>
          <Icon name="preview" size={12} />
          {viewMode === 'preview' ? t('toolbar.exitPreview') : t('toolbar.preview')}
        </button>
        <label className="toolbar-label" title={t('toolbar.gridTitle')}>
          <input type="checkbox" checked={gridOn} onChange={(ev) => setGridOn(ev.target.checked)} />
          {t('toolbar.grid')}
        </label>
        <span className="toolbar-sep" />
        <span className="mono zoom-label" title={t('toolbar.zoomTitle')}>
          {Math.round(zoom * 100)}%
        </span>
        <span className="spacer" style={{ flex: 1 }} />
        <button className="btn ghost" onClick={() => setShortcutsOpen(true)} title={t('toolbar.shortcutsTitle')}>
          <Icon name="keyboard" size={12} />
          {t('toolbar.shortcuts')}
        </button>
        {/* 导入导出菜单 */}
        <button className="btn ghost" aria-label={t('toolbar.more')} title={t('toolbar.more')} onClick={(ev) => setMoreAnchor(moreAnchor ? null : ev.currentTarget)}>
          <Icon name="more" size={12} />
        </button>
        <button className="btn primary" onClick={() => save()} disabled={saving || !state}>
          <Icon name="save" size={12} />
          {saving ? t('toolbar.saving') : t('toolbar.save')}
        </button>
      </div>

      {moreAnchor && (
        <Popover anchor={moreAnchor} ariaLabel={t('toolbar.more')} onClose={() => setMoreAnchor(null)}>
          <MenuItem
            onClick={() => {
              setMoreAnchor(null)
              void doExportDesign()
            }}
          >
            <Icon name="clipboard" size={12} />
            {t('toolbar.exportDesign')}
          </MenuItem>
          <MenuItem
            onClick={() => {
              setMoreAnchor(null)
              void doImportDesign()
            }}
          >
            <Icon name="upload" size={12} />
            {t('toolbar.importDesign')}
          </MenuItem>
        </Popover>
      )}

      {loadError && (
        <div className="banner error">
          {loadError}
          <button className="btn sm" onClick={requestClose}>
            {t('loadError.back')}
          </button>
        </div>
      )}

      {/* 导入设计解析失败：模态弹窗（与保存失败同通道） */}
      {importError && <ErrorAlert message={importError} onClose={clearImportError} />}

      {/* 保存错误提示 */}
      {saveError && <ErrorAlert title={saveError.title} message={saveError.message} onClose={clearSaveError} />}

      {/* 上传失败提醒：页内轻量 toast，5s 自动消失，单条最新覆盖 */}
      {imageToast && (
        <div className="image-toast" role="alert" data-testid="image-toast">
          <div className="image-toast-title">{t('status.imageRejected.title')}</div>
          <div className="image-toast-body">{imageToast}</div>
        </div>
      )}

      {state && viewState && (
        <div className="designer-body">
          {/* 演示期间左栏 / 画布 / 属性面板回调整体换 no-op（写入侧结构性兜底）；
              图层列表仍显示原始 elements（见 viewState 注释） */}
          <SidePanel
            elements={state.elements}
            selected={selected}
            viewMode={viewMode}
            pendingType={pendingType}
            fields={fields}
            onPickType={demoActive ? () => {} : (t) => setPendingType(t)}
            onSelect={demoActive ? () => {} : (id, toggle) => handleSelect([id], toggle)}
            onMoveLayer={demoActive ? () => {} : moveLayer}
            onLayerTop={demoActive ? () => {} : layerToTop}
            onLayerBottom={demoActive ? () => {} : layerToBottom}
            onDelete={demoActive ? () => {} : deleteElements}
          />
          <CanvasViewport
            state={viewState}
            selected={selected}
            viewMode={viewMode}
            dpi={printDpi}
            zoom={zoom}
            gridOn={gridOn}
            pendingType={pendingType}
            locked={demoActive}
            imageAssets={imageAssets}
            onSelect={demoActive ? () => {} : handleSelect}
            onAddElement={demoActive ? () => {} : addElementAt}
            onRequestImageUpload={demoActive ? () => {} : replaceImage}
            onUpdateElements={demoActive ? () => {} : applyElements}
            onCommit={demoActive ? () => {} : commitNow}
            onZoomChange={setZoom}
          />
          <aside className="designer-right">
            <div className="right-tabs">
              <button className={'right-tab' + (rightTab === 'props' ? ' active' : '')} onClick={() => setRightTab('props')}>
                {t('right.props')}
              </button>
              <button className={'right-tab' + (rightTab === 'data' ? ' active' : '')} onClick={() => setRightTab('data')}>
                {t('right.data')}
              </button>
            </div>
            {rightTab === 'props' ? (
              <div style={{ flex: 1, overflowY: 'auto', minHeight: 0 }}>
                <PropsPanel
                  elements={viewState.elements}
                  selected={selected}
                  viewMode={viewMode}
                  onChange={demoActive ? () => {} : changeElement}
                  onAlign={demoActive ? () => {} : alignSelected}
                  onDelete={demoActive ? () => {} : deleteElements}
                  imageAssets={imageAssets}
                  onReplaceImage={demoActive ? () => {} : replaceImage}
                  demoActive={demoActive}
                  onDemoStart={setDemoAnchor}
                  onDemoExit={() => setDemoAnchor(null)}
                  autoOpenAnchor={autoOpenAnchor}
                  onAutoOpenHandled={() => setAutoOpenAnchor(null)}
                />
              </div>
            ) : (
              <div style={{ flex: 1, overflowY: 'auto', minHeight: 0, display: 'flex', flexDirection: 'column', gap: 8 }}>
                <div className="group">
                  <div className="group-title">{t('dataTab.title')}</div>
                  {!previewDefaults || previewDefaults.size === 0 ? (
                    <div className="hint">{t('dataTab.empty')}</div>
                  ) : (
                    [...previewDefaults.entries()].map(([k, v]) => (
                      <div className="field" key={k} style={{ marginTop: 8 }}>
                        <span className="mono" style={{ minWidth: 90 }} title={displayNameByKey.get(k) ? t('dataTab.fieldNameTitle', { key: k }) : undefined}>{displayNameByKey.get(k) || k}</span>
                        <span className="mono" style={{ color: 'var(--text-2)', wordBreak: 'break-all' }}>{v}</span>
                      </div>
                    ))
                  )}
                  <div className="hint" style={{ marginTop: 8 }}>
                    {t('dataTab.hint')}
                  </div>
                </div>
              </div>
            )}
          </aside>
        </div>
      )}

      {shortcutsOpen && (
        <Modal
          title={t('shortcutsModal.title')}
          onClose={() => setShortcutsOpen(false)}
          width={520}
          footer={
            <button className="btn primary" onClick={() => setShortcutsOpen(false)}>
              {t('shortcutsModal.ok')}
            </button>
          }
        >
          <div style={{ display: 'flex', flexDirection: 'column', gap: 12 }}>
            {shortcutGroups().map((g) => (
              <div key={g.title}>
                <div className="group-title" style={{ marginBottom: 4 }}>{g.title}</div>
                <table className="table">
                  <tbody>
                    {g.items.map((item) => (
                      <tr key={item.desc} style={{ cursor: 'default' }}>
                        <td style={{ width: 240, whiteSpace: 'nowrap' }}>
                          {item.keys.map((k) => (
                            <kbd key={k} className="kbd">{k}</kbd>
                          ))}
                        </td>
                        <td style={{ fontSize: 12 }}>{item.desc}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            ))}
          </div>
        </Modal>
      )}

      {confirmOverwrite && (
        <Modal
          title={t('overwrite.title')}
          onClose={cancelOverwrite}
          footer={
            <>
              <button className="btn" onClick={cancelOverwrite}>
                {t('action.cancel')}
              </button>
              <button className="btn primary" onClick={overwriteSave}>
                {t('overwrite.confirm')}
              </button>
            </>
          }
        >
          <p>
            {t('overwrite.body', { name: name.trim() })}
          </p>
        </Modal>
      )}

      {leaveGuardOpen && (
        <Modal
          title={t('leaveGuard.title')}
          onClose={stayInEditor}
          footer={
            <>
              <button className="btn" onClick={stayInEditor}>
                {t('leaveGuard.stay')}
              </button>
              <button className="btn danger" onClick={discardAndLeave}>
                {t('leaveGuard.discard')}
              </button>
              <button className="btn primary" onClick={saveAndLeave} disabled={saving}>
                {t('leaveGuard.saveAndLeave')}
              </button>
            </>
          }
        >
          <p>
            {t('leaveGuard.body')}
          </p>
        </Modal>
      )}
    </div>
  )
}
