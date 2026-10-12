// 设计器（核心）：顶栏（模板名/分组/纸张/DPI/预览/保存）+ 左栏（控件/字段/图层）
// + 画布（移植原型交互）+ 右栏（属性 / 测试数据 Tab）。
// 状态：stateRef 为同步真相（事件回调内先更新再 setState），历史为快照式。
// 未保存离开保护——dirty（历史栈有已提交更改）时返回按钮与 Shell 导航切 tab
// 均弹三选 Modal（保存并离开 / 放弃更改 / 继续编辑）；无编辑直接离开不弹窗。
// 帮助深链锚点 = 路由 sub 段（helpAnchor props）——页内轮询
// （useHelpDeepLink）等锚点入 DOM 自动弹文档点气泡；深链自动开草稿放示例元素入初始基线（
// 见 init 落点），守卫链零改动（三选差异由示例元素落点自然产生）。

import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { localApi, serverApi } from '../lib/api/client'
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
import type { DesignElement, ImageAsset, ImageElement } from '../lib/design/types'
import { cloneElement, defaultElement } from '../lib/design/types'
import { deriveFieldInfos } from '../lib/design/fields'
import { createHistory } from '../lib/design/history'
import { r2 } from '../lib/design/geometry'
import { exportDesign, parseDesign } from '../lib/design/format'
import { compressImageIfNeeded } from '../lib/design/imageCompress'
import { HELP_DEMO_PRESETS, HELP_LINK_ELEMENTS, helpSampleDisplayName } from '../lib/help'
import { useHelpDeepLink } from '../lib/helpLink'
import i18next from '../i18n'
import { capturePasteOnce, copyText, readClipboardText } from '../lib/clipboard'
import { applyContractDisplayNames, fromBackendElements, toContract, toLayout } from '../lib/design/convert'
import { shortcutGroups } from './designer/shortcuts'

const snap = (s: DesignState) => JSON.stringify({ paperW: s.paperW, paperH: s.paperH, elements: s.elements })
const parse = (s: string): DesignState => JSON.parse(s) as DesignState

// ---------- 图片资源：限制上限与前端预检常量 ----------
// 限制按入库（压缩后）字节判定——>1MB 先走浏览器内压缩管道（imageCompress），压缩后仍超限才拒收
/** 支持格式（accept + 预检白名单，与服务端格式探嗅对齐）。 */
const IMAGE_ACCEPT = 'image/png,image/jpeg,image/bmp'
/** 单图入库字节上限（2MB，压缩后）。 */
const IMAGE_MAX_BYTES = 2 * 1024 * 1024
/** 单模板张数上限。 */
const IMAGE_MAX_COUNT = 10
/** 单模板总量上限（10MB，入库字节）。 */
const IMAGE_MAX_TOTAL_BYTES = 10 * 1024 * 1024

/** 字节数 → MB 显示（压缩提示插值用）：≥1MB 一位小数，更小保留两位（防「0.0MB」失真）。 */
function fmtMb(bytes: number): string {
  const v = bytes / (1024 * 1024)
  return v >= 1 ? v.toFixed(1) : v.toFixed(2)
}

/** 文件名净化为资源 key（真对齐后端 zip SanitizeKey 口径：char.IsLetterOrDigit 为 Unicode 感知——中文文件名
 * 保留（复陪验反馈 2-：ASCII 白名单曾把「微信图片_….png」替成「______.png」）；\p{L}\p{N} 与 . - _ 之外替换 _）。 */
export function sanitizeImageKey(fileName: string): string {
  const safe = fileName.replace(/[^\p{L}\p{N}._-]/gu, '_')
  return safe.trim() === '' ? 'image' : safe
}

/** key 去重（同名重复上传 / 与既有元素引用撞名时追加序号，扩展名保留参与区分 a.png 与 a.jpeg）。 */
function uniqueImageKey(base: string, existing: ReadonlySet<string>): string {
  if (!existing.has(base)) return base
  const dot = base.lastIndexOf('.')
  const stem = dot > 0 ? base.slice(0, dot) : base
  const ext = dot > 0 ? base.slice(dot) : ''
  for (let i = 2; ; i++) {
    const candidate = `${stem}-${i}${ext}`
    if (!existing.has(candidate)) return candidate
  }
}

/** base64 → dataURL（按魔数 base64 前缀特征判 mime；PNG / JPEG / BMP 之外兜底 png——服务端校验只放行这三种）。 */
function base64ToDataUrl(base64: string): string {
  if (base64.startsWith('iVBORw0')) return 'data:image/png;base64,' + base64
  if (base64.startsWith('/9j/')) return 'data:image/jpeg;base64,' + base64
  if (base64.startsWith('Qk')) return 'data:image/bmp;base64,' + base64
  return 'data:image/png;base64,' + base64
}

/** 解码图片像素尺寸（异步；加载失败或环境不支持时回退 0×0——初始尺寸按正方形兜底），3s 超时防挂起。 */
function decodeImageSize(dataUrl: string): Promise<{ width: number; height: number }> {
  return new Promise((resolve) => {
    const img = new Image()
    const done = (w: number, h: number) => {
      img.onload = null
      img.onerror = null
      clearTimeout(timer)
      resolve({ width: w, height: h })
    }
    const timer = setTimeout(() => done(0, 0), 3000)
    img.onload = () => done(img.naturalWidth || 0, img.naturalHeight || 0)
    img.onerror = () => done(0, 0)
    img.src = dataUrl
  })
}

/** 读文件为 dataURL（File 或压缩产物 Blob 均可）。 */
function readFileAsDataUrl(file: Blob): Promise<string> {
  return new Promise((resolve, reject) => {
    const reader = new FileReader()
    reader.onload = () => resolve(String(reader.result))
    reader.onerror = () => reject(reader.error ?? new Error('read failed'))
    reader.readAsDataURL(file)
  })
}

/* * 深链示例元素构造：预设 spec → defaultElement + patch 合并——每次调用
 *  生成新 id（跨草稿不串号）、默认文案随当前语言；元素随草稿 init 并入初始基线（见 init 落点）。
 *  spec 类型全集含控件栏外三型（Line / Region 等），defaultElement 重载面按控件栏入口收窄——
 *  数据源为受控预设表，双转型到实现签名（全 7 型均有分支）。 */
function buildHelpLinkElements(anchor: string): DesignElement[] {
  return (HELP_LINK_ELEMENTS[anchor] ?? []).map(({ type, patch }) => {
    const e = defaultElement(type as 'Text') as DesignElement
    // fill 预设（字段填充三件套）补随语言的显示名示例值（其余锚点无 displayName 语义）
    const extra = 'mode' in (patch ?? {}) ? { displayName: helpSampleDisplayName() } : {}
    return { ...e, ...patch, ...extra } as DesignElement
  })
}

interface DesignerProps {
  request: DesignerRequest
  onClose: () => void
  /* * ：向 Shell 注册离开守卫（导航 tab 切换拦截：dirty 时弹三选 Modal 挂起本次切换）；卸载自动注销。
   * 修复（取证）：守卫返回 boolean——false=已挂起（弹三选待确认）、true=无未保存更改已直接放行
   *  供路由 hashchange 分支判定是否 pre-revert（非 dirty 放行时不得回退，否则浏览器后退在设计器上失效）。 */
  registerLeaveGuard?: (fn: ((leave: () => void) => boolean) | null) => void
  /* * 帮助深链锚点（语义 = 路由 sub 段，Shell 直传不预轮询）——本组件
   *  经 useHelpDeepLink 页内轮询等锚点入 DOM 后自动弹出对应文档点气泡；锚点已登记示例元素预设
   * 且草稿为新建时，示例元素随 init 并入初始基线。 */
  helpAnchor?: string | null
}

export function Designer({ request, onClose, registerLeaveGuard, helpAnchor }: DesignerProps) {
  const app = useApp()
  // 设计器文案 key 化（designer 域）；数据性默认值（分组「默认」）随界面语言（待决议-1）
  const { t } = useTranslation('designer')
  const { serverMode } = app
  /* * 业务 API 跟随模式（与 Workbench 对齐：unknown 时下方加载 effect 不发请求，待探测完成）。 */
  const biz = serverMode === 'server' ? serverApi : localApi
  const [state, setState] = useState<DesignState | null>(null)
  const [selected, setSelected] = useState<string[]>([])
  const [viewMode, setViewMode] = useState<'fit' | 'preview'>('fit')
  const [dpi, setDpi] = useState(203)
  const [zoom, setZoom] = useState(1)
  const [gridOn, setGridOn] = useState(true)
  const [pendingType, setPendingType] = useState<string | null>(null)
  const [name, setName] = useState('')
  const [group, setGroup] = useState(() => i18next.t('designer:defaultGroup'))
  const [rightTab, setRightTab] = useState<'props' | 'data'>('props')
  const [saving, setSaving] = useState(false)
  const [confirmOverwrite, setConfirmOverwrite] = useState(false)
  const [shortcutsOpen, setShortcutsOpen] = useState(false)
  const [loadError, setLoadError] = useState<string | null>(null)
  // 未保存离开保护——三选 Modal（保存并离开 / 放弃更改 / 继续编辑）
  const [leaveGuardOpen, setLeaveGuardOpen] = useState(false)
  // 「看实时效果」演示——显示层 override（记录演示文档点锚点）
  // 仅合并进 viewElements 喂画布与属性面板；不写 stateRef / historyRef → 不进历史栈、不置
  // dirty、doSave 读 stateRef 不含注入值（零落库零写请求的结构性保证）；退出 = 清锚点即还原。
  const [demoAnchor, setDemoAnchor] = useState<string | null>(null)
  // displayName 示例值随语言补入（预设不静态写死， wrapup 定夺 zh「库位」/ en "Bin"）
  const demoPatch = demoAnchor
    ? { ...HELP_DEMO_PRESETS[demoAnchor], displayName: helpSampleDisplayName() }
    : undefined
  const demoActive = demoPatch !== undefined
  // 深链自动弹气泡——页内轮询确认锚点入 DOM 后的内部信号
  //（PropsPanel 既有 autoOpenAnchor 链，处理后清信号防重复触发）。
  const [autoOpenAnchor, setAutoOpenAnchor] = useState<string | null>(null)
  // 深链草稿一次性标记：本挂载以深链自动开草稿时置位——首个 onFound 不清状态栏
  //（draftOpened 为终态提示），消费后复位（后续锚点 found 恢复清等待提示的旧口径）。
  const deepLinkDraftRef = useRef(false)

  const stateRef = useRef<DesignState | null>(null)
  const historyRef = useRef<ReturnType<typeof createHistory<DesignState>> | null>(null)
  const initialNameRef = useRef('')
  const contractNameRef = useRef('')
  const contractVersionRef = useRef('1')
  const selectedRef = useRef<string[]>([])
  // 图片资源表：key → 资源，由本组件持有——不进 DesignState / 历史快照
  //（snap 仅 paperW/paperH/elements，防 base64 撑爆撤销栈）；撤销「添加图片元素」后资源成孤儿由保存时 GC。
  const [imageAssets, setImageAssets] = useState<Record<string, ImageAsset>>({})
  const imageAssetsRef = useRef<Record<string, ImageAsset>>({})
  /** 隐藏文件选择（上传 / 更换共用同一 input；replaceTargetRef 记录更换目标元素 id）。 */
  const fileInputRef = useRef<HTMLInputElement>(null)
  const replaceTargetRef = useRef<string | null>(null)
  // （决议 a）：离开保护挂起的离开动作——「放弃更改」与「保存并离开」成功后执行
  // 各终止路径（继续编辑 / 保存失败 / 覆盖取消 / 名称缺失）就地作废，防陈旧离开动作误触发后续普通保存。
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

  // （机制迁移）：深链锚点页内轮询（旧 Shell 两段信号收口删除，等待收敛
  // 到目标页）——置于加载 effect 之前：自动草稿路径先写 pending、init 随即以 draftOpened 覆盖
  //（方案风险节「pending 一闪后被 draftOpened 覆盖属预期」）；超时走通用降级词条（未开模板 /
  // 未选中元素 / 类型不含目标分组 / 对齐组需多选 / 预览锁定等一切锚点不可见情形同此提示）。
  useHelpDeepLink(helpAnchor, {
    onFound: () => {
      if (deepLinkDraftRef.current) deepLinkDraftRef.current = false
      else app.setStatus('')
      setAutoOpenAnchor(helpAnchor ?? null)
    },
    onTimeout: () => app.setStatus(t('help:deepLink.timeout')),
    onPending: () => app.setStatus(t('help:deepLink.pending')),
  })

  // 深链到达性保障（陪验反馈 1）：锚点变更时若目标分组对当前选中不可见（未选中 / 类型不含
  // 目标组 / 对齐组少于多选），主动把界面带到锚点可见状态——否则页内轮询只会超时降级、气泡内容
  // 始终看不到。两档处置：深链原装示例草稿且未编辑（deepLinkDraftRef 在场 + undoCount=0）→ 按新
  // 锚点重建示例元素集（等价「换一个小节看」，不丢任何用户内容）；其余（用户草稿已编辑 / 已开模板）
  // → 自动选中既有匹配型元素（对齐组选任意两个），没有匹配元素则不动作（不往用户内容里加元素，
  // 维持超时降级口径）。
  useEffect(() => {
    if (!helpAnchor) return
    const specs = Object.hasOwn(HELP_LINK_ELEMENTS, helpAnchor) ? HELP_LINK_ELEMENTS[helpAnchor] : null
    if (!specs) return // 非设计器文档点锚（settings 卡片等）——锚点常在，走既有轮询
    const s = stateRef.current
    if (!s) return
    const selEls = s.elements.filter((e) => selectedRef.current.includes(e.id))
    const types = new Set(specs.map((sp) => sp.type))
    const reachable = helpAnchor === 'designer.align' ? selEls.length >= 2 : selEls.some((e) => types.has(e.type))
    if (reachable) return
    if (request.kind === 'new' && (historyRef.current?.undoCount ?? 0) === 0) {
      // 重建示例集：undoCount=0 = 无任何用户已提交更改（含手动新建未动过的草稿），重建不丢工作；
      // 沿用当前纸张尺寸，元素与历史基线整体换为目标锚点预设（undoCount 归零不 dirty）
      const samples = buildHelpLinkElements(helpAnchor)
      const next = { ...s, elements: samples }
      stateRef.current = next
      historyRef.current = createHistory(next, snap, parse)
      setState(next)
      const ids = samples.map((e) => e.id)
      setSelected(ids)
      selectedRef.current = ids
      // 与挂载路径同款一次性标记：本次 onFound 不清状态栏（draftOpened 为终态提示）
      deepLinkDraftRef.current = true
      app.setStatus(t('help:deepLink.draftOpened'))
      return
    }
    // 用户内容：只改选中不改元素（对齐组取任意两个，其余取首个匹配型）
    const pick =
      helpAnchor === 'designer.align'
        ? s.elements.filter((e) => e.type !== 'Region').slice(0, 2).map((e) => e.id)
        : [s.elements.find((e) => types.has(e.type))?.id].filter((id): id is string => id !== undefined)
    if (pick.length === 0) return
    setSelected(pick)
    selectedRef.current = pick
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [helpAnchor])

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

  // ---------- 加载 ----------
  // 与 Workbench 的 serverMode 守卫对齐——unknown（模式探测未完成）时不以 localApi 误发请求
  // 待 AppContext 解析出 server / standalone 后重跑本 effect 按正确 base 加载。serverMode 为原始值入依赖，
  // 不会触发既有注释所述的无限循环（app 为 context 对象、每次渲染新引用，才是不能入依赖的雷区）；
  // initedRef 单次闩锁——模式中途翻转（10s 周期探测 server ↔ standalone）不重拉模板、不重置编辑中状态
  // （组件在 App 按 key 重挂，request 在一次挂载内不变，故无需按 request 变化作废在途结果）。
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
        initialNameRef.current = pkg.name
        contractNameRef.current = pkg.contract?.name ?? pkg.name
        contractVersionRef.current = pkg.contract?.version ?? '1'
        // 图片资源回填：GET 响应本就携带 images base64——dataURL 即时回填画布可渲染
        // 像素尺寸异步解码补齐（属性面板信息行用；失败保持 0，仅影响展示不影响渲染）。
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
      // （初始基线）：深链自动开草稿时按锚点小节类型放置示例
      // 元素——随 init 并入 stateRef 且作 createHistory 基线（可正常编辑、undoCount=0 不 dirty、
      // 未编辑退出静默弃、用户编辑后三选守卫照常）；「不落库零业务数据」重解释＝自动机制零写请求
      // ＋用户显式命名保存属正常草稿行为（记录，偏离 Issue 约束句「不写 stateRef 保存链」的
      // 理由：纯显示层注入 ⇒ undoCount=0 ⇒ 「退出即弃带未保存守卫」永不触发的内在矛盾）。
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
            // 显示名存于契约 fields（版式元素 JSON 无此字段），加载时按字段名回填到元素
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

  // ---------- 选择 ----------
  const handleSelect = useCallback((ids: string[], toggle?: boolean) => {
    setSelected((prev) => {
      const next = toggle ? ids.filter((id) => !prev.includes(id)).concat(prev.filter((id) => !ids.includes(id))) : ids
      selectedRef.current = next
      return next
    })
  }, [])

  // ---------- 元素操作 ----------
  const addElementAt = useCallback(
    (type: string, xMm: number, yMm: number) => {
      const s = stateRef.current
      if (!s) return
      // 五类控件栏入口（图片两步式：先落 key 空 20×20mm 空占位，再上传填充——）
      const e = defaultElement(type as 'Text' | 'Barcode' | 'QrCode' | 'Rect' | 'Image') as DesignElement
      e.x = Math.max(0, Math.min(s.paperW - 2, r2(xMm)))
      e.y = Math.max(0, Math.min(s.paperH - 2, r2(yMm)))
      const next = { ...s, elements: [...s.elements, e] }
      commit(next)
      setSelected([e.id])
      selectedRef.current = [e.id]
      setPendingType(null)
      app.setStatus(t('status.added', { type: elementTypeName(type) }))
    },
    [app, commit, t],
  )

  const changeElement = useCallback(
    (id: string, patch: Partial<DesignElement>) => {
      const s = stateRef.current
      if (!s) return
      commit({ ...s, elements: s.elements.map((e) => (e.id === id ? ({ ...e, ...patch } as DesignElement) : e)) })
    },
    [commit],
  )

  // ---------- 图片上传（； 两步式＋压缩） ----------
  /* * 上传失败提醒：页内轻量 toast 浮层自动消失（5s）——单条最新覆盖（连续失败不堆积），
   *  Designer 局部实现不引全局组件；成功 / 压缩提示仍走状态栏（常驻语义，最新覆盖）。 */
  const [imageToast, setImageToast] = useState<string | null>(null)
  const imageToastTimerRef = useRef<number | null>(null)
  const showImageToast = useCallback((message: string) => {
    setImageToast(message)
    if (imageToastTimerRef.current !== null) window.clearTimeout(imageToastTimerRef.current)
    imageToastTimerRef.current = window.setTimeout(() => setImageToast(null), 5000)
  }, [])
  useEffect(
    () => () => {
      if (imageToastTimerRef.current !== null) window.clearTimeout(imageToastTimerRef.current)
    },
    [],
  )

  /** 文件落地（两步式，必带目标元素——空占位填充或更换）：格式预检 → 压缩管道（>1MB 自动压缩，
   *  压缩后仍 >2MB 才拒收；张数 / 总量按入库（压缩后）字节）→ dataURL → 解码尺寸 → key 净化去重 →
   *  写目标元素 key（换新 key 防共享旧 key 的多元素同变；旧 key 孤儿随保存 GC）。填充后保持用户
   * 所画盒尺寸、图片拉伸填满（不做 30mm 自动尺寸）。资源表不进历史快照。 */
  const addImageFile = useCallback(
    (file: File, targetId: string) => {
      const reject = (reasonKey: string) => showImageToast(t('status.imageRejected.body', { reason: t(reasonKey) }))
      if (file.type && !['image/png', 'image/jpeg', 'image/bmp'].includes(file.type)) {
        reject('imageReject.format')
        return
      }
      const s0 = stateRef.current
      // 目标必须是图片元素（空占位或已填）——画布 / 属性面板入口都以元素 id 为准
      if (!s0 || !s0.elements.some((el) => el.id === targetId && el.type === 'Image')) return

      void (async () => {
        const { blob, compressed, pngAlphaKept } = await compressImageIfNeeded(file, IMAGE_MAX_BYTES)
        if (blob.size > IMAGE_MAX_BYTES) {
          reject(pngAlphaKept ? 'imageReject.pngAlphaTooLarge' : 'imageReject.tooLarge')
          return
        }
        const assets = imageAssetsRef.current
        // 张数按「将被引用的新 key」计：目标已持有 key 的更换不占新增名额（旧 key 成孤儿随保存 GC）
        const targetEl = stateRef.current?.elements.find((el) => el.id === targetId && el.type === 'Image') as ImageElement | undefined
        if (!targetEl) return
        if (!targetEl.key && Object.keys(assets).length >= IMAGE_MAX_COUNT) {
          reject('imageReject.tooMany')
          return
        }
        const totalBytes = Object.values(assets).reduce((sum, a) => sum + a.sizeBytes, 0)
        if (totalBytes + blob.size > IMAGE_MAX_TOTAL_BYTES) {
          reject('imageReject.totalTooLarge')
          return
        }

        const s = stateRef.current
        if (!s) return
        const dataUrl = await readFileAsDataUrl(blob)
        const size = await decodeImageSize(dataUrl)
        const existing = new Set<string>([
          ...Object.keys(imageAssetsRef.current),
          ...s.elements.filter((e) => e.type === 'Image' && e.key).map((e) => (e as ImageElement).key),
        ])
        const key = uniqueImageKey(sanitizeImageKey(file.name), existing)
        const nextAssets: Record<string, ImageAsset> = {
          ...imageAssetsRef.current,
          [key]: { dataUrl, width: size.width, height: size.height, sizeBytes: blob.size },
        }
        imageAssetsRef.current = nextAssets
        setImageAssets(nextAssets)
        changeElement(targetId, { key })
        // 成功提示（复陪验反馈 2-，推翻）：压缩发生时以 toast 呈现（状态栏轻提示用户实证看不见）
        // 原始→入库体积知情权；未压缩的常规更新仍走状态栏（画布变化本身可见）。
        if (compressed) showImageToast(t('status.imageCompressed', { from: fmtMb(file.size), to: fmtMb(blob.size) }))
        else app.setStatus(t('status.imageReplaced'))
      })()
    },
    [app, changeElement, showImageToast, t],
  )

  /* * 属性面板「上传 / 更换图片」与画布空占位单击：打开文件选择，目标为该元素。 */
  const replaceImage = useCallback(
    (id: string) => {
      if (viewMode === 'preview') return
      replaceTargetRef.current = id
      fileInputRef.current?.click()
    },
    [viewMode],
  )

  const onImageInputChange = useCallback(
    (ev: React.ChangeEvent<HTMLInputElement>) => {
      const file = ev.target.files?.[0]
      const targetId = replaceTargetRef.current
      replaceTargetRef.current = null
      ev.target.value = '' // 同名文件可再次选择
      // 入口只经元素目标（属性面板按钮 / 画布空占位单击）——无目标即对话框已过期的防御性丢弃
      if (file && targetId) addImageFile(file, targetId)
    },
    [addImageFile],
  )

  const deleteElements = useCallback(
    (ids: string[]) => {
      const s = stateRef.current
      if (!s || ids.length === 0) return
      const next = { ...s, elements: s.elements.filter((e) => !ids.includes(e.id)) }
      commit(next)
      setSelected([])
      selectedRef.current = []
      app.setStatus(t('status.deleted', { count: ids.length }))
    },
    [app, commit, t],
  )

  const alignSelected = useCallback(
    (align: 'left' | 'centerH' | 'right' | 'top' | 'centerV' | 'bottom') => {
      const s = stateRef.current
      if (!s) return
      const sel = s.elements.filter((e) => selectedRef.current.includes(e.id) && e.type !== 'Region')
      if (sel.length < 2) {
        app.setStatus(t('status.alignNeedTwo'))
        return
      }
      const left = Math.min(...sel.map((e) => e.x))
      const right = Math.max(...sel.map((e) => e.x + e.w))
      const top = Math.min(...sel.map((e) => e.y))
      const bottom = Math.max(...sel.map((e) => e.y + e.h))
      const ids = new Set(sel.map((e) => e.id))
      const next = {
        ...s,
        elements: s.elements.map((e) => {
          if (!ids.has(e.id)) return e
          const out: DesignElement = { ...e }
          delete out.regionId
          switch (align) {
            case 'left':
              out.x = left
              break
            case 'centerH':
              out.x = left + (right - left - e.w) / 2
              break
            case 'right':
              out.x = right - e.w
              break
            case 'top':
              out.y = top
              break
            case 'centerV':
              out.y = top + (bottom - top - e.h) / 2
              break
            case 'bottom':
              out.y = bottom - e.h
              break
          }
          return out
        }),
      }
      commit(next)
      app.setStatus(t('status.aligned', { count: sel.length }))
    },
    [app, commit, t],
  )

  // ---------- 图层 ----------
  const moveLayer = useCallback(
    (delta: number) => {
      const s = stateRef.current
      if (!s || selectedRef.current.length !== 1) {
        app.setStatus(t('status.layerNeedSingle'))
        return
      }
      const idx = s.elements.findIndex((e) => e.id === selectedRef.current[0])
      const ni = idx + delta
      if (idx < 0 || ni < 0 || ni >= s.elements.length) return
      const els = [...s.elements]
      ;[els[idx], els[ni]] = [els[ni], els[idx]]
      commit({ ...s, elements: els })
    },
    [app, commit, t],
  )

  const layerToTop = useCallback(() => {
    const s = stateRef.current
    if (!s || selectedRef.current.length !== 1) return
    const id = selectedRef.current[0]
    const e = s.elements.find((x) => x.id === id)
    if (!e) return
    commit({ ...s, elements: [...s.elements.filter((x) => x.id !== id), e] })
  }, [commit])

  const layerToBottom = useCallback(() => {
    const s = stateRef.current
    if (!s || selectedRef.current.length !== 1) return
    const id = selectedRef.current[0]
    const e = s.elements.find((x) => x.id === id)
    if (!e) return
    commit({ ...s, elements: [e, ...s.elements.filter((x) => x.id !== id)] })
  }, [commit])

  // ---------- 撤销 / 重做 / 复制粘贴 ----------
  const undo = useCallback(() => {
    const h = historyRef.current
    if (!h) return
    const next = h.undo()
    if (!next) {
      app.setStatus(t('status.nothingToUndo'))
      return
    }
    historyRef.current = next
    stateRef.current = next.data
    setState(next.data)
    setSelected([])
    selectedRef.current = []
    app.setStatus(t('status.undone'))
  }, [app, t])

  const redo = useCallback(() => {
    const h = historyRef.current
    if (!h) return
    const next = h.redo()
    if (!next) {
      app.setStatus(t('status.nothingToRedo'))
      return
    }
    historyRef.current = next
    stateRef.current = next.data
    setState(next.data)
    setSelected([])
    selectedRef.current = []
    app.setStatus(t('status.redone'))
  }, [app, t])

  const clipboardRef = useRef<DesignElement[]>([])

  const copySelected = useCallback(() => {
    const s = stateRef.current
    if (!s) return
    const items = s.elements.filter((e) => selectedRef.current.includes(e.id))
    if (!items.length) return
    clipboardRef.current = items.map((e) => cloneElement(e))
    app.setStatus(t('status.copied', { count: clipboardRef.current.length }))
  }, [app, t])

  const pasteClipboard = useCallback(() => {
    const s = stateRef.current
    if (!s || clipboardRef.current.length === 0) return
    const copies = clipboardRef.current.map((e) => {
      const c = cloneElement(e)
      c.x = Math.max(0, Math.min(s.paperW - c.w - 1, c.x + 5))
      c.y = Math.max(0, Math.min(s.paperH - c.h - 1, c.y + 5))
      return c
    })
    commit({ ...s, elements: [...s.elements, ...copies] })
    const ids = copies.map((c) => c.id)
    setSelected(ids)
    selectedRef.current = ids
    app.setStatus(t('status.pasted', { count: copies.length }))
  }, [app, commit, t])

  // ---------- 设计 JSON 导入 / 导出（剪贴板，多级降级；修复：不再弹 prompt 重复复制） ----------
  const doExportDesign = useCallback(async () => {
    const s = stateRef.current
    if (!s) return
    const text = exportDesign(s.paperW, s.paperH, s.elements)
    if (await copyText(text)) {
      app.setStatus(t('status.exported', { count: s.elements.length }))
      return
    }
    // 终极兜底：浏览器完全禁用剪贴板时展示代码供手动复制
    const input = window.prompt(t('status.exportPrompt'), text)
    if (input !== null) app.setStatus(t('status.exportFallback'))
  }, [app, t])

  const doImportDesign = useCallback(async () => {
    let text = await readClipboardText()
    if (!text) {
      // 读取权限不可用：聚焦隐藏输入，用户按一次 Ctrl+V 即完成（Esc / 超时取消）
      app.setStatus(t('status.clipboardUnavailable'))
      text = await capturePasteOnce()
      if (!text) {
        app.setStatus(t('status.importCancelled'))
        return
      }
    }
    try {
      const d = parseDesign(text)
      commit({ paperW: d.paperW, paperH: d.paperH, elements: d.elements })
      setSelected([])
      selectedRef.current = []
      setPendingType(null)
      app.setStatus(t('status.imported', { count: d.elements.length }))
    } catch (err) {
      app.setStatus(t('status.importFailed', { reason: err instanceof Error ? err.message : i18next.t('job.unknownError') }))
    }
  }, [app, commit, t])

  // ---------- 快捷键 ----------
  useEffect(() => {
    const onKey = (ev: KeyboardEvent) => {
      const s = stateRef.current
      if (!s) return
      // 演示期间快捷键全停：撤销 / 删除 / 粘贴等都会写 stateRef，绕过锁输入
      if (demoActive) return
      const tag = ev.target instanceof HTMLElement ? ev.target.tagName : ''
      if (tag === 'INPUT' || tag === 'SELECT' || tag === 'TEXTAREA') return
      const ctrl = ev.ctrlKey || ev.metaKey
      if (viewMode === 'preview') return

      if (ctrl && ev.shiftKey && ev.key.toLowerCase() === 'c') {
        ev.preventDefault()
        void doExportDesign()
        return
      }
      if (ctrl && ev.shiftKey && ev.key.toLowerCase() === 'v') {
        ev.preventDefault()
        void doImportDesign()
        return
      }
      if (ctrl && !ev.shiftKey && ev.key.toLowerCase() === 'z') {
        ev.preventDefault()
        undo()
        return
      }
      if (ctrl && ev.key.toLowerCase() === 'y') {
        ev.preventDefault()
        redo()
        return
      }
      if (ctrl && ev.key.toLowerCase() === 'c') {
        ev.preventDefault()
        copySelected()
        return
      }
      if (ctrl && ev.key.toLowerCase() === 'v') {
        ev.preventDefault()
        pasteClipboard()
        return
      }
      if (ev.key === 'Delete' || ev.key === 'Backspace') {
        if (selectedRef.current.length) {
          ev.preventDefault()
          deleteElements(selectedRef.current)
        }
        return
      }
      if (ev.key === 'Escape' && pendingType) {
        setPendingType(null)
        app.setStatus(t('status.placementCancelled'))
      }
    }
    document.addEventListener('keydown', onKey)
    return () => document.removeEventListener('keydown', onKey)
  }, [viewMode, pendingType, demoActive, app, doExportDesign, doImportDesign, undo, redo, copySelected, pasteClipboard, deleteElements, t])

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
      app.setStatus(t('status.previewEntered', { dpi, dots: Math.round(dpi / 25.4) }))
    }
  }, [app, dpi, viewMode, t])

  // ---------- 保存 ----------
  const doSave = useCallback(
    async (finalName: string) => {
      const s = stateRef.current
      if (!s) {
        pendingLeaveRef.current = null
        return
      }
      setSaving(true)
      try {
        const fields = deriveFieldInfos(s.elements)
        const version = request.kind === 'new' ? '1' : contractVersionRef.current
        const contractName = request.kind === 'new' ? finalName : contractNameRef.current
        // 图片资源：仅提交被元素引用的 key（孤儿 GC），剥 dataURL 前缀取纯 base64
        // 恒发显式字典（有图非空 / 无图空字典），不依赖契约缺省态（缺省 / null＝保留仅为旧调用方兜底）
        const usedKeys = new Set(s.elements.filter((e) => e.type === 'Image' && e.key).map((e) => (e as ImageElement).key))
        const images: Record<string, string> = {}
        for (const [key, asset] of Object.entries(imageAssetsRef.current)) {
          if (usedKeys.has(key)) {
            const comma = asset.dataUrl.indexOf(',')
            images[key] = comma >= 0 ? asset.dataUrl.slice(comma + 1) : asset.dataUrl
          }
        }
        const pkg: TemplatePackage = {
          name: finalName,
          group: group.trim() || i18next.t('designer:defaultGroup'),
          contract: toContract(contractName, version, fields),
          // 不传 testData——由后端从元素 previewValue 自动派生（读-改-写，旧值不丢）
          layout: toLayout(finalName, contractName, version, s.paperW, s.paperH, s.elements),
          images,
        }
        await biz.saveTemplate(pkg)
        app.setStatus(t('status.saved', { name: finalName }))
        // 「保存并离开」成功后执行挂起的离开动作（保存成功自然复位 dirty——组件随离开卸载）
        // 普通保存无挂起动作，维持既有 onClose 回工作台
        const leave = pendingLeaveRef.current
        pendingLeaveRef.current = null
        if (leave) leave()
        else onClose()
      } catch (err) {
        app.setStatus(err instanceof ApiError ? err.message : t('status.saveFailed'))
        // 保存失败停留设计器、不丢编辑——挂起的离开动作就地作废
        pendingLeaveRef.current = null
      } finally {
        setSaving(false)
      }
    },
    [app, biz, group, onClose, request.kind, t],
  )

  const save = useCallback(() => {
    const trimmed = name.trim()
    if (!trimmed) {
      // 「保存并离开」无模板名时止步于此（停留设计器补名称），挂起动作作废
      pendingLeaveRef.current = null
      app.setStatus(t('status.nameRequired'))
      return
    }
    const isNew = request.kind === 'new'
    const renamed = !isNew && trimmed !== initialNameRef.current
    if (isNew || renamed) {
      void biz
        .listTemplates()
        .then((list) => {
          if (list.some((t) => t.name === trimmed)) setConfirmOverwrite(true)
          else void doSave(trimmed)
        })
        .catch(() => void doSave(trimmed))
    } else {
      void doSave(trimmed)
    }
  }, [app, biz, doSave, name, request.kind, t])

  // ---------- 离开保护（决议 a：dirty = 历史栈有任一已提交更改） ----------
  /* * 请求离开：无已提交更改（undoCount = 0）直接离开不弹窗；有则弹三选 Modal 挂起本次离开动作。
   * 返回 false=已挂起（三选待确认）、true=已直接放行——Shell switchTab 以此决定路由是否 pre-revert（取证修复）。 */
  const requestLeave = useCallback((leave: () => void): boolean => {
    if ((historyRef.current?.undoCount ?? 0) > 0) {
      pendingLeaveRef.current = leave
      setLeaveGuardOpen(true)
      return false
    }
    leave()
    return true
  }, [])

  /** 三选之「继续编辑」（含 Esc / 点遮罩 / 右上角关闭）：作废挂起的离开动作，留在设计器。 */
  const stayInEditor = useCallback(() => {
    pendingLeaveRef.current = null
    setLeaveGuardOpen(false)
  }, [])

  /** 三选之「放弃更改」：不保存，直接执行挂起的离开动作（回工作台 / 完成挂起的导航切换）。 */
  const discardAndLeave = useCallback(() => {
    const leave = pendingLeaveRef.current
    pendingLeaveRef.current = null
    setLeaveGuardOpen(false)
    leave?.()
  }, [])

  /** 三选之「保存并离开」：走既有保存链路（空名校验 / 同名覆盖确认），成功后由 doSave 执行挂起离开；
   * 失败停留设计器显示错误、不丢编辑。 */
  const saveAndLeave = useCallback(() => {
    setLeaveGuardOpen(false)
    save()
  }, [save])

  /* * 设计器返回按钮 / 加载失败返回工作台：dirty 时经三选确认，否则直接返回。 */
  const requestClose = useCallback(() => requestLeave(onClose), [requestLeave, onClose])

  /** 覆盖确认取消（含 Esc / 点遮罩 / 右上角关闭）：停留在设计器，挂起的离开动作作废。 */
  const cancelOverwrite = useCallback(() => {
    setConfirmOverwrite(false)
    pendingLeaveRef.current = null
  }, [])

  // Shell 导航 tab 切换拦截：挂载注册 / 卸载注销（设计器随切 tab 卸载后 Shell 侧不再拦截）
  useEffect(() => {
    if (!registerLeaveGuard) return
    registerLeaveGuard(requestLeave)
    return () => registerLeaveGuard(null)
  }, [registerLeaveGuard, requestLeave])

  const fields = useMemo(() => (state ? deriveFieldInfos(state.elements) : []), [state])

  // 演示视图：override 只合并进「渲染视图」state——画布（预览值本就画布渲染）与属性面板
  // 实时反映示例值；SidePanel 图层列表与「测试默认值」页签保持原始 state（有意边界：演示不影响
  // 派生链路，防注入值写入 previewDefaults 相关链路破坏零落库论断—— 方案评审明示）。
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

  // 字段显示名：与推导一致取首个非空，显示处 `displayName || key` 回退
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
          <Icon name="back" size={14} />
        </button>
        <input className="input" style={{ width: 150 }} value={name} onChange={(ev) => setName(ev.target.value)} placeholder={t('toolbar.namePlaceholder')} title={t('toolbar.namePlaceholder')} />
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
        <select className="input" value={dpi} onChange={(ev) => setDpi(parseInt(ev.target.value, 10))} title={t('toolbar.dpiTitle')}>
          <option value={203}>203 dpi</option>
          <option value={300}>300 dpi</option>
        </select>
        <button className={'btn' + (viewMode === 'preview' ? ' active' : '')} onClick={togglePreview} title={t('toolbar.previewTitle')}>
          <Icon name="preview" size={13} />
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
          <Icon name="keyboard" size={13} />
          {t('toolbar.shortcuts')}
        </button>
        <button className="btn" onClick={() => void doExportDesign()} title="Ctrl+Shift+C">
          <Icon name="clipboard" size={13} />
          {t('toolbar.exportDesign')}
        </button>
        <button className="btn" onClick={() => void doImportDesign()} title="Ctrl+Shift+V">
          <Icon name="upload" size={13} />
          {t('toolbar.importDesign')}
        </button>
        <button className="btn primary" onClick={save} disabled={saving || !state}>
          <Icon name="save" size={13} />
          {saving ? t('toolbar.saving') : t('toolbar.save')}
        </button>
      </div>

      {loadError && (
        <div className="banner error">
          {loadError}
          <button className="btn sm" onClick={requestClose}>
            {t('loadError.back')}
          </button>
        </div>
      )}

      {/* 上传失败提醒：页内轻量 toast，5s 自动消失，单条最新覆盖 */}
      {imageToast && (
        <div className="image-toast" role="alert" data-testid="image-toast">
          <div className="image-toast-title">{t('status.imageRejected.title')}</div>
          <div className="image-toast-body">{imageToast}</div>
        </div>
      )}

      {state && viewState && (
        <div className="designer-body">
          {/* 演示期间（demoActive）左栏与画布编辑全禁用：回调整体换 no-op（写入侧结构性兜底，
              拍板 4 方案 A）；图层列表仍显示原始 elements（演示有意边界，见 viewState 注释） */}
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
            dpi={dpi}
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
                      <div className="field" key={k} style={{ marginTop: 6 }}>
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
              <button className="btn primary" onClick={() => { setConfirmOverwrite(false); void doSave(name.trim()) }}>
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

/** 控件类型显示名（状态栏「已添加」消息用；随界面语言）。 */
function elementTypeName(type: string): string {
  switch (type) {
    case 'Barcode': return i18next.t('designer:type.barcode')
    case 'QrCode': return i18next.t('designer:type.qrcode')
    case 'Rect': return i18next.t('designer:type.rect')
    case 'Image': return i18next.t('designer:type.image')
    default: return i18next.t('designer:type.text')
  }
}
