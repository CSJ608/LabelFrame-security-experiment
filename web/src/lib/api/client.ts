// fetch 封装（双 base）：serverApi = 服务端地址（机器级配置 > localStorage 兜底 > 默认 127.0.0.1:53961），
// localApi = 页面来源（托管本页的 LabelFrame Client）。业务 API 双端共有（单机降级：Server 不可达时
// 模板 / 作业 / 日志走本机 WinHost 全套 API）；错误消息区分「服务端」与「本机客户端」。

import type {
  ApiErrorBody,
  ClientPackageInfo,
  DeviceView,
  ExcelImportResult,
  Healthz,
  HostConfig,
  InstalledPluginInfo,
  Ipv4Candidates,
  JobView,
  LogEntry,
  PdaPackageInfo,
  PluginInstallResult,
  PluginPackageInfo,
  PluginUninstallResult,
  PrinterStatus,
  PrinterTestResult,
  PrintSettings,
  SubmitJobRequest,
  TemplatePackage,
  TemplateSummary,
  TransportApplyRequest,
  TransportConfig,
  TransportResult,
} from './types'
import { ApiError, DEFAULT_LOCAL_BASE_URL } from './types'
import { getBaseUrl } from '../settings'
import { UI_MODE } from '../uiMode'
import { resolveApiErrorMessage } from '../../i18n/errorMessages'
import i18next from '../../i18n'

// ── base 解析 ──

// server 构建下 serverApi base 固定同源相对路径（''，不读 localStorage / 机器级配置）——
// Server UI 由服务端托管，同源即可访问 API；局域网其他机器访问时避免残留值错连到访问者本机
// 或客户端 WinHost。client 构建读配置值。
let serverBaseUrl = UI_MODE === 'server' ? '' : getBaseUrl()

/** 更新服务端地址（AppContext 机器级配置加载 / 保存后调用，模块级即时生效；server 构建下不调用）。 */
export function setServerBaseUrl(url: string): void {
  serverBaseUrl = url.trim().replace(/\/+$/, '')
}

export function getServerBaseUrl(): string {
  if (UI_MODE === 'server') return ''
  return serverBaseUrl
}

/** 本机客户端地址 = 页面来源（页面由 LabelFrame Client 托管）；无 window（Node 测试）回退默认本机地址。 */
export function getLocalBaseUrl(): string {
  if (typeof window !== 'undefined' && window.location && window.location.origin) {
    return window.location.origin
  }
  return DEFAULT_LOCAL_BASE_URL
}

// ── 请求超时 ──

/** 超时分档：normal = 普通请求（列表 / 保存 / 提交等）；heavy = 出图 / 导入导出 / 安装包等大负载端点放宽档。
 *  导出为可变对象：单测注入秒级短超时验证超时分支（不真实等待 30s / 120s）。 */
export const requestTimeouts: { normal: number; heavy: number } = { normal: 30_000, heavy: 120_000 }

/** 业务请求超时信号：到时中止在途 fetch；didTimeout 供错误归一化区分「超时」与「网络不可达」。
 *  不用 AbortSignal.timeout：手动计时器可被测试以注入短超时秒级触发，且结束时 dispose 清理（挂起的
 *  setTimeout 不清理会驻留进程）。init.signal 由本原语统一接管（当前无调用方自带 signal）。 */
function createRequestTimeout(timeoutMs: number) {
  const controller = new AbortController()
  let timedOut = false
  const timer = setTimeout(() => {
    timedOut = true
    controller.abort()
  }, timeoutMs)
  return {
    signal: controller.signal,
    dispose: () => clearTimeout(timer),
    didTimeout: () => timedOut,
  }
}

/** fetch 传输层失败归一化：超时 → ApiError('TIMEOUT')；其余（不可达 / 连接被拒）→ 既有 NETWORK_ERROR（语义不变）。
 * 兜底文案 key 化（common.api.* 词条，读 i18next 单例当前语言——错误在请求失败期构造
 *  语言即当时界面语言；zh-CN 值与原硬编码逐字一致）。 */
function transportError(t: ReturnType<typeof createRequestTimeout>, timeoutMs: number, base: () => string, label: '服务端' | '本机客户端'): ApiError {
  const targetKey = label === '服务端' ? 'api.targetServer' : 'api.targetLocalClient'
  if (t.didTimeout()) {
    // 秒数下限 1：注入短超时的测试与极小值场景不出现「超过 0 秒」的病态文案
    return new ApiError('TIMEOUT', i18next.t('api.timeout', { target: i18next.t(targetKey), seconds: Math.max(1, Math.round(timeoutMs / 1000)) }))
  }
  return new ApiError('NETWORK_ERROR', i18next.t('api.networkError', { target: i18next.t(targetKey), base: base() }))
}

// ── 请求原语 ──

function makeRequest(base: () => string, label: '服务端' | '本机客户端') {
  return async function request<T>(path: string, init?: RequestInit, timeoutMs = requestTimeouts.normal): Promise<T> {
    // 默认 30s 超时（后端挂起不再无限「处理中…」）；出图 / 上传类大负载端点在调用处传 heavy 档（120s）
    const timeout = createRequestTimeout(timeoutMs)
    try {
      let res: Response
      try {
        res = await fetch(base() + path, { ...init, mode: 'cors', signal: timeout.signal })
      } catch {
        throw transportError(timeout, timeoutMs, base, label)
      }
      if (!res.ok) {
        let body: ApiErrorBody | null = null
        try {
          body = (await res.json()) as ApiErrorBody
        } catch {
          body = null
        }
        throw new ApiError(
          body?.code ?? 'HTTP_' + res.status,
          resolveApiErrorMessage(body?.code, body?.params, body?.message ?? i18next.t('api.httpError', { status: res.status })),
          body?.fieldKey,
          body?.params)
      }
      if (res.status === 204) return undefined as T
      try {
        return (await res.json()) as T
      } catch {
        // 部分端点返回纯文本（如模板导入返回模板名）
        try {
          return (await res.text()) as T
        } catch {
          // 响应体读取中断 / 超时中止：与传输层同一归一化
          throw transportError(timeout, timeoutMs, base, label)
        }
      }
    } finally {
      timeout.dispose()
    }
  }
}

/* * 解析 Content-Disposition 下载文件名。
 *  后端 ASP.NET Core 对非 ASCII 文件名按 RFC 6266/5987 同时发两个参数——
 *  `filename=____.lfpkg`（ASCII 回退值，非 ASCII 字符替换为 _）与 `filename*=UTF-8''%E4%B8%AD...`（百分号编码，无损）。
 *  优先解析 filename* 并 decodeURIComponent 还原中文；无 / 畸形 / 非 UTF-8 的 filename* 回退现行为 filename=，
 *  均无则返回 null（调用方用 fallbackName）——全程容错不抛异常。纯 ASCII 名后端仅发 filename=，行为不变。 */
function parseDispositionFilename(disposition: string): string | null {
  // RFC 5987 ext-value 形态：charset 'language' 百分号编码值（language 可空；本仓后端固定 UTF-8''）
  const star = /filename\*\s*=\s*([^']*)'[^']*'([^;]*)/i.exec(disposition)
  // decodeURIComponent 只按 UTF-8 语义解码，非 UTF-8 charset 不误解（回退 filename=）
  if (star && /^utf-?8$/i.test(star[1].trim())) {
    try {
      const decoded = decodeURIComponent(star[2].trim())
      if (decoded) return decoded
    } catch {
      // 畸形百分号编码（如 %E4 截断）：弃用 filename*，回退 filename= 回退值
    }
  }
  return /filename="?([^";]+)"?/.exec(disposition)?.[1] ?? null
}

/** 下载型端点（render-image / render-images / 模板导出）：返回 blob + Content-Disposition 文件名，错误解析 ErrorView。
 * 超时覆盖整个下载过程（含 res.blob 响应体读取）——大负载端点调用处传 heavy 档。 */
function makeFetchBlob(base: () => string, label: '服务端' | '本机客户端') {
  return async function fetchBlob(
    path: string,
    init: RequestInit,
    fallbackName: string,
    failMessage: string,
    timeoutMs = requestTimeouts.normal,
  ): Promise<{ blob: Blob; filename: string }> {
    const timeout = createRequestTimeout(timeoutMs)
    try {
      let res: Response
      try {
        res = await fetch(base() + path, { ...init, mode: 'cors', signal: timeout.signal })
      } catch {
        throw transportError(timeout, timeoutMs, base, label)
      }
      if (!res.ok) {
        let body: ApiErrorBody | null = null
        try {
          body = (await res.json()) as ApiErrorBody
        } catch {
          body = null
        }
        throw new ApiError(
          body?.code ?? 'HTTP_' + res.status,
          resolveApiErrorMessage(body?.code, body?.params, body?.message ?? i18next.t('api.blobHttpError', { message: failMessage, status: res.status })),
          body?.fieldKey,
          body?.params)
      }
      let blob: Blob
      try {
        blob = await res.blob()
      } catch {
        // 响应体下载中断 / 超时中止：与传输层同一归一化（慢速大文件下载超时同样呈现 TIMEOUT）
        throw transportError(timeout, timeoutMs, base, label)
      }
      const disposition = res.headers.get('Content-Disposition') ?? ''
      const filename = parseDispositionFilename(disposition) ?? fallbackName
      return { blob, filename }
    } finally {
      timeout.dispose()
    }
  }
}

/** 业务 API（双端共有：Server 与 WinHost 均实现；调用方按模式选择）。 */
function makeBusinessApi(base: () => string, label: '服务端' | '本机客户端') {
  const request = makeRequest(base, label)
  const fetchBlob = makeFetchBlob(base, label)
  return {
    /** 健康探测（5s 超时：远程服务端不可达时快速判为离线，不挂起连接状态）。 */
    healthz: async () => {
      let res: Response
      try {
        res = await fetch(base() + '/healthz', { mode: 'cors', signal: AbortSignal.timeout(5000) })
      } catch {
        throw new ApiError('NETWORK_ERROR', i18next.t('api.networkError', { target: i18next.t(label === '服务端' ? 'api.targetServer' : 'api.targetLocalClient'), base: base() }))
      }
      if (!res.ok) throw new ApiError('HTTP_' + res.status, i18next.t('api.httpError', { status: res.status }))
      return (await res.json()) as Healthz
    },

    listTemplates: (group?: string) => request<TemplateSummary[]>(group ? `/api/templates?group=${encodeURIComponent(group)}` : '/api/templates'),
    getTemplate: (name: string) => request<TemplatePackage>(`/api/templates/${encodeURIComponent(name)}`),
    saveTemplate: (pkg: TemplatePackage) =>
      request<{ name: string; group: string }>('/api/templates', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(pkg),
      }),
    deleteTemplate: (name: string) => request<void>(`/api/templates/${encodeURIComponent(name)}`, { method: 'DELETE' }),
    /* * 模板导出改走 makeFetchBlob 同构——失败解析后端 ErrorView 呈现真实原因（不再固定 EXPORT_FAILED
     *  吞掉后端信息），Content-Disposition 解析收敛到 fetchBlob 一处；模板导出为大负载端点，挂 heavy 档超时。 */
    exportTemplate: (name: string): Promise<{ blob: Blob; filename: string }> =>
      fetchBlob(
        `/api/templates/${encodeURIComponent(name)}/export`,
        { method: 'GET' },
        `${name}.lfpkg`,
        i18next.t('api.exportFailed'),
        requestTimeouts.heavy,
      ),
    importTemplate: async (file: File): Promise<string> => {
      const form = new FormData()
      form.append('file', file)
      return request<string>('/api/templates/import', { method: 'POST', body: form }, requestTimeouts.heavy)
    },

    importExcel: (file: File) => {
      const form = new FormData()
      form.append('file', file)
      return request<ExcelImportResult>('/api/import/excel', { method: 'POST', body: form }, requestTimeouts.heavy)
    },

    /* * 下载 Excel 模板（Server 与 WinHost 都实现）——按契约字段 + testData 生成 xlsx 供直接套用导入。 */
    excelTemplate: (columns: { key: string; displayName: string }[], sampleRow: Record<string, string>) =>
      fetchBlob(
        '/api/import/excel-template',
        {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ columns, sampleRow }),
        },
        'excel-template.xlsx',
        i18next.t('api.excelTemplateFailed'),
        requestTimeouts.heavy,
      ),

    submitJob: (req: SubmitJobRequest) =>
      request<JobView>('/api/jobs', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(req),
      }),
    getJob: (jobId: string) => request<JobView>(`/api/jobs/${encodeURIComponent(jobId)}`),
    retryJobItem: (jobId: string, index: number) =>
      request<JobView>(`/api/jobs/${encodeURIComponent(jobId)}/items/${index}/retry`, { method: 'POST' }),

    /* * 作业历史：默认 100、上限 500，倒序；可选 deviceId 过滤（客户端 UI 传本机 ID 只看自己）。 */
    getJobs: (limit = 100, deviceId?: string) => {
      const params = new URLSearchParams({ limit: String(limit) })
      if (deviceId) params.set('deviceId', deviceId)
      return request<JobView[]>(`/api/jobs?${params.toString()}`)
    },

    /* * 设备 / 客户端目录（Server）；404 / 失败 = 单机 WinHost，前端降级为单机模式。 */
    listDevices: () => request<DeviceView[]>('/api/devices'),

    renderImage: (req: SubmitJobRequest) =>
      fetchBlob('/api/print/render-image', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(req),
      }, 'label-print.png', i18next.t('api.renderFailed'), requestTimeouts.heavy),

    /* * 模板预览 PNG（按模板 TestData 渲染，工作台预览列用；Server 与 WinHost 双宿主已实现
     *  模板不存在等业务失败由调用方按失败态处理）。与 renderImage 同构的 blob 封装。
     *  空 JSON 体 + 显式 Content-Type 必须携带：旧版 Server 对无 Content-Type 的裸 POST 按路由不匹配
     *  返回空体 404（2026-09-10 真机验收发现——新版宽容、旧版不宽容，显式携带两端通吃）。 */
    previewTemplate: (name: string): Promise<{ blob: Blob; filename: string }> =>
      fetchBlob(
        `/api/templates/${encodeURIComponent(name)}/preview`,
        { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: '{}' },
        `${name}.png`,
        i18next.t('api.previewFailed'),
        requestTimeouts.heavy,
      ),

    /* * 调试批量出图：后端渲染全部标签为 PNG 打包 zip 下载（不建作业）。 */
    renderImages: (req: SubmitJobRequest) =>
      fetchBlob('/api/print/render-images', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(req),
      }, 'labels-debug.zip', i18next.t('api.debugImagesFailed'), requestTimeouts.heavy),

    getLogs: (deviceId?: string, since?: string) => {
      const params = new URLSearchParams()
      if (deviceId) params.set('deviceId', deviceId)
      if (since) params.set('since', since)
      const qs = params.toString()
      return request<LogEntry[]>(qs ? `/api/logs?${qs}` : '/api/logs')
    },
  }
}

const serverRequest = makeRequest(getServerBaseUrl, '服务端')
const serverFetchBlob = makeFetchBlob(getServerBaseUrl, '服务端')

/* * 服务端 API（模板 / 作业 / 设备 / 日志 / Excel / 调试出图 / healthz → 服务端地址；+ 客户端 / PDA 安装包与插件包分发）。 */
export const serverApi = {
  ...makeBusinessApi(getServerBaseUrl, '服务端'),

  // ── 客户端下载分发（仅 Server 实现；路径穿越防护在后端）──
  listClientPackages: () => serverRequest<ClientPackageInfo[]>('/api/client-packages'),
  uploadClientPackage: (file: File) => {
    const form = new FormData()
    form.append('file', file)
    return serverRequest<ClientPackageInfo[]>('/api/client-packages', { method: 'POST', body: form }, requestTimeouts.heavy)
  },
  deleteClientPackage: (fileName: string) => serverRequest<void>(`/api/client-packages/${encodeURIComponent(fileName)}`, { method: 'DELETE' }),

  // ── PDA（Android 宿主）安装包分发（仅 Server 实现；与客户端安装包同模式）──
  listPdaPackages: () => serverRequest<PdaPackageInfo[]>('/api/pda-packages'),
  uploadPdaPackage: (file: File) => {
    const form = new FormData()
    form.append('file', file)
    return serverRequest<PdaPackageInfo[]>('/api/pda-packages', { method: 'POST', body: form }, requestTimeouts.heavy)
  },
  deletePdaPackage: (fileName: string) => serverRequest<void>(`/api/pda-packages/${encodeURIComponent(fileName)}`, { method: 'DELETE' }),

  // ── 本机 IPv4 候选（下载中心「连接信息」卡；只读枚举，协议与端口由前端按 origin 拼装）──
  listServerIpv4Candidates: () => serverRequest<Ipv4Candidates>('/api/server/ipv4-candidates'),

  // ── 插件包分发（仅 Server 实现；64MB 上限与校验在后端，前端按 sizeBytes 预检）──
  listPluginPackages: () => serverRequest<PluginPackageInfo[]>('/api/plugin-packages'),
  uploadPluginPackage: (file: File) => {
    const form = new FormData()
    form.append('file', file)
    return serverRequest<PluginPackageInfo[]>('/api/plugin-packages', { method: 'POST', body: form }, requestTimeouts.heavy)
  },
  deletePluginPackage: (fileName: string) => serverRequest<void>(`/api/plugin-packages/${encodeURIComponent(fileName)}`, { method: 'DELETE' }),
  /** 下载插件包（blob；客户端安装中转用——下载后 `new File([blob], fileName)` 交给 localApi.installPlugin）。 */
  downloadPluginPackage: (fileName: string): Promise<{ blob: Blob; filename: string }> =>
    serverFetchBlob(
      `/api/plugin-packages/${encodeURIComponent(fileName)}`,
      { method: 'GET' },
      fileName,
      '下载插件包失败',
      requestTimeouts.heavy,
    ),
}

/** 客户端安装包下载 URL（Server UI 与客户端设置「更新与安装包」共用；server 构建下 base 为空 = 同源相对路径）。 */
export function clientPackageDownloadUrl(fileName: string): string {
  return `${getServerBaseUrl()}/api/client-packages/${encodeURIComponent(fileName)}`
}

/* * PDA（Android 宿主）安装包下载 URL（Server UI「下载中心」页与二维码共用；server 构建下 base 为空 = 同源相对路径）。 */
export function pdaPackageDownloadUrl(fileName: string): string {
  return `${getServerBaseUrl()}/api/pda-packages/${encodeURIComponent(fileName)}`
}

/* * 插件包下载 URL（Server UI「插件管理」页下载链接；与 clientPackageDownloadUrl 同模式）。 */
export function pluginPackageDownloadUrl(fileName: string): string {
  return `${getServerBaseUrl()}/api/plugin-packages/${encodeURIComponent(fileName)}`
}

const localRequest = makeRequest(getLocalBaseUrl, '本机客户端')

/** 本机客户端 API（transport / printer / host/config 本机接口 + 业务 API 供单机降级）。 */
export const localApi = {
  ...makeBusinessApi(getLocalBaseUrl, '本机客户端'),

  // ── 连接管理 ──
  getTransport: () => localRequest<TransportConfig>('/api/transport'),
  setTransport: (req: TransportApplyRequest) =>
    localRequest<TransportResult>('/api/transport', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(req),
    }),
  testTransport: (req: TransportApplyRequest) =>
    localRequest<TransportResult>('/api/transport', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ ...req, testOnly: true }),
    }),

  // ── 打印机 ──
  getPrinterStatus: () => localRequest<PrinterStatus>('/api/printer/status'),
  testPrinter: () => localRequest<PrinterTestResult>('/api/printer/test', { method: 'POST' }),

  // ── 机器级配置 ──
  getHostConfig: () => localRequest<HostConfig>('/api/host/config'),
  setHostConfig: (cfg: HostConfig) =>
    localRequest<void>('/api/host/config', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(cfg),
    }),

  // ── 批次作业设置（仅 WinHost 实现；GET Normalize 兜底、POST 校验 400、保存即生效；旧 WinHost 404）──
  getPrintSettings: () => localRequest<PrintSettings>('/api/host/print-settings'),
  setPrintSettings: (s: PrintSettings) =>
    localRequest<void>('/api/host/print-settings', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(s),
    }),

  // ── 客户端插件安装 / 卸载（仅 WinHost 实现；失败 400 ErrorView；旧 WinHost 404）──
  listInstalledPlugins: () => localRequest<InstalledPluginInfo[]>('/api/plugins/installed'),
  /** 安装插件包（multipart；file 保留原始文件名——服务端下载后 `new File([blob], fileName)` 传入）。 */
  installPlugin: (file: File) => {
    const form = new FormData()
    form.append('file', file)
    return localRequest<PluginInstallResult>('/api/plugins/install', { method: 'POST', body: form }, requestTimeouts.heavy)
  },
  uninstallPlugin: (pluginId: string) =>
    localRequest<PluginUninstallResult>('/api/plugins/uninstall', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ pluginId }),
    }),
}

/** 探测任意地址的 /healthz（设置页「测试连接」用输入值探测，不保存；5s 超时防挂起）。
 * 空地址不发起网络探测直接判失败——空串 base 落同源会打到本机 WinHost /healthz 200
 *  谎报「该地址可访问服务端」；未配置服务端地址的终态判定在 AppContext（空地址 = 未连接单机模式，不探测）。 */
export async function probeHealthz(url: string): Promise<boolean> {
  const cleaned = url.trim().replace(/\/+$/, '')
  if (!cleaned) return false
  try {
    const res = await fetch(cleaned + '/healthz', { mode: 'cors', signal: AbortSignal.timeout(5000) })
    return res.ok
  } catch {
    return false
  }
}
