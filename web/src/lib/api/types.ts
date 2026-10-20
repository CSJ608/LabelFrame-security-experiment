// API 契约 DTO（docs/FRONTEND-SPEC.md §4，与后端实现对齐）。
// 双 base：serverApi（服务端地址）/ localApi（页面来源 = 本机 LabelFrame Client）。

import type { BackendContract, BackendElement, BackendLayout } from '../design/convert'

/* * 服务端地址默认值（127.0.0.1:53961）。 */
export const DEFAULT_BASE_URL = 'http://127.0.0.1:53961'

/** 本机客户端（WinHost）默认地址：localApi 无 window（Node 环境）时的回退。 */
export const DEFAULT_LOCAL_BASE_URL = 'http://127.0.0.1:53960'

export interface Healthz {
  service: string
  status: string
  /** 传输模式（旧单机 WinHost 字段；Server 不返回，前端仅作兼容展示，可选）。 */
  transport?: string
}

export interface TemplateSummary {
  name: string
  group: string
  updatedAt: string
}

export interface TemplatePackage {
  name: string
  group: string
  contract: BackendContract
  layout: BackendLayout
  testData?: Record<string, string>
  /* * 图片资源（键 → base64）：保存请求三态语义（缺省 / null＝保留、空字典＝清空、非空＝覆盖）——
   *  设计器恒发显式字典（被引用 key 全集）；GET 详情响应本就携带。 */
  images?: Record<string, string>
}

export type ElementJson = BackendElement

export interface JobItem {
  index: number
  status: string
  errorCode?: string
  errorMessage?: string
}

export interface JobView {
  jobId: string
  requestId: string
  status: string
  totalItems: number
  completedItems: number
  /* * Server：目标设备 ID 与在线状态（WinHost 不返回）。 */
  targetDeviceId?: string
  deviceStatus?: string
  /** Server：失败张数与错误消息（无逐张明细）。 */
  failedItems?: number
  errorMessage?: string
  /** WinHost 扩展 JobView 与 Server 对齐——创建时间（作业历史「时间」列；防御性声明为可选）。 */
  createdAt?: string
  /** WinHost：逐张明细（Server 不返回）。 */
  items?: JobItem[]
  /** Log 模拟打印：PNG 目录（仅 WinHost Log 连接时有值） */
  printImageDir?: string
  /** Log 模拟打印：PNG 张数 */
  printImageCount?: number
}

/** 设备视图（GET /api/devices；status = Online / Offline）。 */
export interface DeviceView {
  deviceId: string
  name: string
  registeredAt: string
  lastSeenAt: string
  status: string
  /* * 服务端记录的该设备来源 IP（注册 / 心跳刷新，IPv4 文本；旧设备可能为空）。 */
  lastIp?: string | null
}

export interface ExcelImportResult {
  headers: string[]
  rows: string[][]
}

export interface LogEntry {
  deviceId: string
  time: string
  line: string
}

/**
 * 提交作业请求（服务端模式优先 `templateName` 引用模板库 + `targetDeviceId` 定向投递
 * 自包含 `template` 保留兼容——单机 WinHost / 调试出图使用）。
 */
export interface SubmitJobRequest {
  requestId: string
  /** 服务端模式：引用服务端模板库（优先于 template）。 */
  templateName?: string
  /** 服务端模式：目标设备（客户端）ID。 */
  targetDeviceId?: string
  /** 自包含模板（单机 / 兼容路径；templateName 与 template 同时存在时后端优先 templateName）。 */
  template?: {
    /** 模板名（WinHost 本机提交时用于加载图片资源） */
    name?: string
    contract: BackendContract
    layout: BackendLayout
  }
  labels: { data: Record<string, string> }[]
}

// ── 连接管理（全部走 localApi）──
// 传输插件化——连接方式抽象为插件（统一接口 + 参数模型 + 注册表）
// GET /api/transport 响应扩展 availablePlugins（spec 驱动表单）/ displayText（徽标），旧字段 mode / availableModes 保留兼容。

export type TransportMode = 'Log' | 'Tcp' | 'WindowsDriver' | 'Zebra'
export type ZebraKind = 'Tcp' | 'Usb' | 'Driver'

/** 传输参数：只含当前模式所需字段，未使用字段后端返回默认 / 空，前端不展示。 */
export interface TransportParams {
  tcpHost?: string
  tcpPort?: number
  printerName?: string
  zebraKind?: ZebraKind
  zebraUsbName?: string
}

// ── 插件参数模型（后端 TransportParameterSpec / TransportPluginParameters）──

export type TransportParameterType = 'String' | 'Int' | 'Bool' | 'Select'

/** Select 枚举项：后端可能序列化为 `{ value, label? }[]` 或 `string[]`，前端兼容解析。 */
export interface TransportParameterOption {
  value: string
  label?: string
  /** 展示端翻译键（缺省 = 直接用 label 原文）。 */
  labelKey?: string | null
}

/** 插件参数规格（spec）：前端按此动态渲染参数表单。 */
export interface TransportParameterSpec {
  key: string
  /** 中文标签（源语言兜底，展示端优先走 labelKey 词条） */
  label: string
  type: TransportParameterType
  required?: boolean
  /** 默认值（Bool / Int 可能以字符串序列化，前端按 type 防御解析；后端缺省为 null）。 */
  defaultValue?: string | number | boolean | null
  /** Select 枚举：`{ value, label? }[]` 或 `string[]` 均可。 */
  options?: TransportParameterOption[] | string[]
  /** 输入提示 / 占位说明 */
  hint?: string
  /** 条件可见性：依据哪个参数键（如 kind / connectionType）；缺省 = 恒可见。 */
  visibleWhenKey?: string | null
  /** 条件可见性：条件键取值集合（忽略大小写）；键缺失 / 空值 = 不可见。 */
  visibleWhenValues?: string[] | null
  /** 标签展示端翻译键（缺省 = 直接用 label 原文）。 */
  labelKey?: string | null
  /** 提示展示端翻译键（缺省 = 直接用 hint 原文）。 */
  hintKey?: string | null
  /** 同行分组键：同值渲染于同一行（行序 = 首个成员的声明序）；缺省 = 各自成行。 */
  row?: number | null
}

/** 插件目录项（GET /api/transport.availablePlugins）。 */
export interface TransportPluginInfo {
  id: string
  displayName: string
  description?: string
  /** 展示名展示端翻译键（插件接口 DIM 声明；缺省 = 直接用 displayName 原文）。 */
  displayNameKey?: string | null
  /** 描述展示端翻译键（缺省 = 直接用 description 原文）。 */
  descriptionKey?: string | null
  parameters: TransportParameterSpec[]
  /* * 文档编译能力位：true 时插件声明 printMode 参数，native 模式可编译品牌原生指令；旧后端缺省 undefined。 */
  supportsDocumentCompile?: boolean
}

/** 插件参数值（后端 TransportPluginParameters 弱类型字典：String→string、Int→number、Bool→boolean）。 */
export type PluginParamValue = string | number | boolean
export type PluginParams = Record<string, PluginParamValue>

export interface TransportConfig {
  /* * 当前生效插件 ID（log / tcp9100 / winspool / zebra / 外部插件）；旧后端为 undefined。 */
  pluginId?: string
  /* * 当前插件展示名（后端提供）。 */
  displayName?: string
  /* * 连接徽标文本（后端 displayText，如「TCP 192.168.1.50:9100」）；前端优先展示，旧后端回退本地格式化。 */
  displayText?: string
  /* * 已装配插件目录（spec 驱动参数表单）；旧后端无此字段 → 前端回退内置 4 模式。 */
  availablePlugins?: TransportPluginInfo[]
  /* * 插件参数（弱类型字典）；旧后端为平铺 TransportParams。 */
  params: TransportParams | PluginParams
  /** 旧字段（0.14 兼容，后端仍返回）。 */
  mode: TransportMode
  availableModes?: TransportMode[]
}

/* * POST /api/transport 请求体（pluginId + params 字典，testOnly 由测试连接填充；旧字段保留兼容）。 */
export interface TransportApplyRequest {
  /* * 目标插件 ID（如 tcp9100）。 */
  pluginId?: string
  /* * 插件参数字典。 */
  params?: PluginParams
  testOnly?: boolean
  // ── 旧字段（0.14 兼容，后端仍接受）──
  mode?: TransportMode
  tcpHost?: string
  tcpPort?: number
  printerName?: string
  zebraKind?: ZebraKind
  zebraUsbName?: string
}

/** POST /api/transport 响应：成功与失败（200）统一返回 config = 当前生效连接。 */
export interface TransportResult {
  ok: boolean
  message: string
  config: TransportConfig
}

// ── 客户端下载分发（服务端 client-packages 目录 + API）──

/** 服务端可用客户端安装包（GET /api/client-packages）。 */
export interface ClientPackageInfo {
  fileName: string
  sizeBytes: number
  modifiedAt: string
  /** 下载 URL（后端提供；前端也可按 {serverBaseUrl}/api/client-packages/{fileName} 自行构造）。 */
  url?: string
}

// ── PDA（Android 宿主）安装包分发（服务端 pda-packages 目录 + API，与 client-packages 对称）──

/** 服务端可用 PDA 安装包（GET /api/pda-packages；下载 MIME 为 application/vnd.android.package-archive）。 */
export interface PdaPackageInfo {
  fileName: string
  sizeBytes: number
  modifiedAt: string
  /** 下载 URL（后端提供；前端也可按 {serverBaseUrl}/api/pda-packages/{fileName} 自行构造）。 */
  url?: string
}

// ── 本机 IPv4 候选（下载中心「连接信息」卡地址候选）──

/** 服务端枚举的本机 IPv4 候选（GET /api/server/ipv4-candidates；私网（局域网）地址优先排序）。 */
export interface Ipv4Candidates {
  candidates: string[]
}

// ── 服务端自描述（下载中心「连接信息」卡地址解析）──

/** 服务端自描述（GET /api/server/info）；publicBaseUrl 为管理员配置的对设备可达基址通告。 */
export interface ServerInfo {
  listenUrl: string
  uiEnabled: boolean
  version: string
  /** 未配置时为 null；旧版服务端无该字段（前端按无通告处理）。 */
  publicBaseUrl?: string | null
}

// ── 客户端插件分发（服务端 plugin-packages + 客户端 /api/plugins 安装 / 卸载）──

/** 服务端插件包列表项（GET /api/plugin-packages；invalid 条目元数据字段缺失，仅文件信息有效）。 */
export interface PluginPackageInfo {
  fileName: string
  /** 元数据可空——invalid 条目解析失败时缺失，前端显示「—」。 */
  pluginId?: string
  name?: string
  version?: string
  description?: string
  sizeBytes: number
  modifiedAt: string
  /** 下载 URL（后端恒提供；前端也可按 {serverBaseUrl}/api/plugin-packages/{fileName} 自行构造）。 */
  url?: string
  /** zip / manifest 解析是否成功；false = invalid 条目（仍可删除、不可安装）。 */
  valid: boolean
  invalidReason?: string
}

/** 已安装插件（GET /api/plugins/installed，WinHost；source 决定可卸载性）。 */
export interface InstalledPluginInfo {
  pluginId: string
  name: string
  version: string
  description?: string
  /** 注册表已装配（与 /api/transport/plugins 交集）；false = 待重启生效 / 加载失败 / 未装配。 */
  loaded: boolean
  /** 非空 = 加载失败原因（坏 DLL / 缺依赖 / manifest 解析失败）；loaded=false 且为空 = 待重启生效 / 未装配。 */
  loadError?: string
  /** 安装包所属子目录名（source=package 时有值）。 */
  packageDir?: string
  /** "package" = 安装包（可卸载）；"manual" = 平铺手动 DLL（只读）。 */
  source: 'package' | 'manual'
  installedAt?: string
}

/** POST /api/plugins/install 成功响应。 */
export interface PluginInstallResult {
  ok: boolean
  message: string
  plugin: InstalledPluginInfo
}

/** POST /api/plugins/uninstall 成功响应。 */
export interface PluginUninstallResult {
  ok: boolean
  message: string
}

// ── 本机客户端（GET/POST /api/host/config，机器级持久化）──

/** 机器级配置：settings.json 缺失 / 损坏时 GET 返回 200 + 默认 serverUrl。
 *  GET 响应含 deviceId / deviceName；POST 只更新 serverUrl（deviceId / deviceName 由客户端自身提供，后端忽略请求体中的值）。 */
export interface HostConfig {
  serverUrl: string
  deviceId?: string
  deviceName?: string
  /* * 本机 IPv4 列表（枚举网卡、过滤回环；多 IP 状态栏逗号分隔显示全部）。 */
  ips?: string[]
  /* * 客户端产品版本（只读；旧客户端无此字段——「检查更新」比较口径）。 */
  version?: string
}

/* * 客户端批次作业设置（GET/POST /api/host/print-settings，用户级持久化 print-settings.json）。
 *  后端读取 Normalize：缺失 / 损坏 / 越界统一回默认值（batchSize<1→10、batchIntervalMs<0→500、batchEnabled 非 bool→false），
 *  GET 永不返回非法值；POST 校验 batchSize ≥ 1、batchIntervalMs ≥ 0，非法 400 + 中文原因；保存即生效（无需重启）。
 *  batchEnabled=false 时节流逻辑忽略 batchSize / batchIntervalMs（读取时两者仍参与 Normalize）。 */
export interface PrintSettings {
  /** 是否开启批次作业（默认 false）。 */
  batchEnabled: boolean
  /** 每批次打印数量（默认 10，≥ 1）。 */
  batchSize: number
  /** 批次打印间隔毫秒（默认 500，≥ 0；0 = 无间隔）。 */
  batchIntervalMs: number
  /** 打印 DPI（203/300 白名单；GET 一律返回生效值——未设置时后端已按宿主配置兜底）。 */
  dpi: number
}

/* * GET /api/printer/status。 */
export interface PrinterStatus {
  isOnline: boolean
  isPaperOut: boolean
  isPaused: boolean
  message: string
}

/** POST /api/printer/test 响应。 */
export interface PrinterTestResult {
  sent: boolean
  bytes: number
}

/* * 后端错误响应（ErrorView；params 为错误码模板插值参数，可选）。 */
export interface ApiErrorBody {
  code: string
  message: string
  fieldKey?: string
  /** 错误码模板参数：扁平字符串键值对象，键名与后端消息模板占位符一致（已知码按码表插值渲染用）。 */
  params?: Record<string, string>
}

/** 前端统一错误：code 用于判断，message 为可直接展示文案（已知码按当前语言码表渲染，未知码回退后端中文）。 */
export class ApiError extends Error {
  readonly code: string
  readonly fieldKey?: string
  readonly params?: Record<string, string>
  constructor(code: string, message: string, fieldKey?: string, params?: Record<string, string>) {
    super(message)
    this.name = 'ApiError'
    this.code = code
    this.fieldKey = fieldKey
    this.params = params
  }
}
