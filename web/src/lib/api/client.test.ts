// @vitest-environment jsdom
// （K1）：getServerBaseUrl 双构建分支——client 构建保持现状（默认 53961 / localStorage 兜底 / setServerBaseUrl 内存更新）
// server 构建恒同源相对路径 ''（不读 localStorage / 机器级配置，避免局域网访问时回环错连 / 残留地址错连）。
// 两分支均用 vi.doMock('../lib/uiMode') 显式注入（不依赖进程 env），任何 VITE_UI_MODE 环境下结果稳定。

import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { getServerBaseUrl as GetServerBaseUrlFn, probeHealthz as ProbeHealthzFn, setServerBaseUrl as SetServerBaseUrlFn } from './client'
import { ApiError } from './types'
import type { TemplatePackage } from './types'
import { setBaseUrl } from '../settings'

type ClientModule = {
  getServerBaseUrl: typeof GetServerBaseUrlFn
  setServerBaseUrl: typeof SetServerBaseUrlFn
  probeHealthz: typeof ProbeHealthzFn
  pluginPackageDownloadUrl: typeof import('./client')['pluginPackageDownloadUrl']
  localApi: typeof import('./client')['localApi']
  serverApi: typeof import('./client')['serverApi']
  /* * ：超时分档可变配置——测试注入秒级短超时验证超时分支。 */
  requestTimeouts: typeof import('./client')['requestTimeouts']
}

const KEY = 'labelframe.baseUrl'

beforeEach(() => {
  localStorage.removeItem(KEY)
})

afterEach(() => {
  vi.doUnmock('../uiMode')
  vi.resetModules()
  localStorage.removeItem(KEY)
})

async function loadClient(mode: 'client' | 'server'): Promise<ClientModule> {
  vi.resetModules()
  vi.doMock('../uiMode', () => ({
    UI_MODE: mode,
    isServerUi: mode === 'server',
  }))
  return import('./client')
}

describe('client 构建（VITE_UI_MODE=client）：getServerBaseUrl 保持现状', () => {
  it('无存储值 → 默认 127.0.0.1:53961', async () => {
    const mod = await loadClient('client')
    expect(mod.getServerBaseUrl()).toBe('http://127.0.0.1:53961')
  })

  it('localStorage 兜底生效（模块加载时读取一次；机器级配置不可用时的回退）', async () => {
    setBaseUrl('http://192.168.1.9:53961')
    const mod = await loadClient('client')
    expect(mod.getServerBaseUrl()).toBe('http://192.168.1.9:53961')
  })

  it('setServerBaseUrl 内存更新即时生效（机器级配置保存路径）', async () => {
    const mod = await loadClient('client')
    mod.setServerBaseUrl('http://192.168.1.9:53961')
    expect(mod.getServerBaseUrl()).toBe('http://192.168.1.9:53961')
  })
})

describe('server 构建（K1）：getServerBaseUrl 恒同源相对路径', () => {
  it('返回 ""（同源相对路径）', async () => {
    const mod = await loadClient('server')
    expect(mod.getServerBaseUrl()).toBe('')
  })

  it('不读 localStorage：残留旧值（如客户端 53960 / 服务端地址）也不生效', async () => {
    setBaseUrl('http://127.0.0.1:53960')
    const mod = await loadClient('server')
    expect(mod.getServerBaseUrl()).toBe('')
  })

  it('setServerBaseUrl 不生效（server 构建下地址固定同源，AppContext 也不会调用）', async () => {
    const mod = await loadClient('server')
    mod.setServerBaseUrl('http://192.168.1.9:53961')
    expect(mod.getServerBaseUrl()).toBe('')
  })
})

describe('pluginPackageDownloadUrl（迭代 23 §2.1：与 clientPackageDownloadUrl 同模式）', () => {
  it('client 构建：绝对 URL（默认 127.0.0.1:53961 + 路径编码）', async () => {
    const mod = await loadClient('client')
    expect(mod.pluginPackageDownloadUrl('sample-1.0.0.lfplugin')).toBe('http://127.0.0.1:53961/api/plugin-packages/sample-1.0.0.lfplugin')
  })

  it('server 构建：同源相对路径', async () => {
    const mod = await loadClient('server')
    expect(mod.pluginPackageDownloadUrl('sample-1.0.0.lfplugin')).toBe('/api/plugin-packages/sample-1.0.0.lfplugin')
  })
})

describe('probeHealthz 空地址语义（迭代 86，#142 a 案）', () => {
  it('空地址（空串 / 空白 / 纯斜杠）不发起网络探测直接判失败——空串 base 落同源会打到本机 WinHost /healthz 200 谎报可连接', async () => {
    const mod = await loadClient('client')
    const fetchStub = vi.fn()
    vi.stubGlobal('fetch', fetchStub)
    try {
      await expect(mod.probeHealthz('')).resolves.toBe(false)
      await expect(mod.probeHealthz('   ')).resolves.toBe(false)
      await expect(mod.probeHealthz('/')).resolves.toBe(false)
      expect(fetchStub).not.toHaveBeenCalled()
    } finally {
      vi.unstubAllGlobals()
    }
  })

  it('非空地址照常探测 /healthz 且去尾斜杠（既有行为不变）', async () => {
    const mod = await loadClient('client')
    const fetchStub = vi.fn().mockResolvedValue(new Response(JSON.stringify({ status: 'ok' }), { status: 200 }))
    vi.stubGlobal('fetch', fetchStub)
    try {
      await expect(mod.probeHealthz('http://192.168.1.9:53961/')).resolves.toBe(true)
      expect(fetchStub).toHaveBeenCalledWith('http://192.168.1.9:53961/healthz', expect.objectContaining({ mode: 'cors' }))
    } finally {
      vi.unstubAllGlobals()
    }
  })
})

describe('previewTemplate 请求形态（迭代 46 真机验收回归：旧版 Server 兼容）', () => {
  it('POST 携带 Content-Type: application/json 与空 JSON 体；返回 blob 与文件名', async () => {
    const mod = await loadClient('client')
    const fetchStub = vi.fn().mockResolvedValue(
      new Response(new Blob(['png-bytes'], { type: 'image/png' }), {
        status: 200,
        headers: { 'Content-Disposition': 'attachment; filename="70x50.png"' },
      }),
    )
    vi.stubGlobal('fetch', fetchStub)
    try {
      const result = await mod.localApi.previewTemplate('70*50 容器码')
      expect(fetchStub).toHaveBeenCalledTimes(1)
      const [url, init] = fetchStub.mock.calls[0] as [string, RequestInit]
      expect(url).toBe(`${window.location.origin}/api/templates/${encodeURIComponent('70*50 容器码')}/preview`)
      expect(init.method).toBe('POST')
      expect((init.headers as Record<string, string>)['Content-Type']).toBe('application/json')
      expect(init.body).toBe('{}')
      expect(result.filename).toBe('70x50.png')
      expect(result.blob.size).toBeGreaterThan(0)
    } finally {
      vi.unstubAllGlobals()
    }
  })
})

// ── （/ · a 案决议）：业务请求超时分档 + 导出错误通道同构 ──

/** 挂起端点：不 resolve，仅在收到中止信号时拒绝（模拟后端「接了不回」——超时分支的唯一触发路径）。 */
function hangingFetch() {
  return vi.fn((_url: string, init?: RequestInit) =>
    new Promise<Response>((_resolve, reject) => {
      init?.signal?.addEventListener('abort', () => reject(new DOMException('The operation was aborted.', 'AbortError')))
    }),
  )
}

const TIMEOUT_PKG: TemplatePackage = {
  name: '70x50',
  group: '默认',
  contract: { name: 'contract-t', version: '1', fields: [] },
  layout: { name: 'layout-t', contractName: 'contract-t', contractVersion: '1', widthMm: 70, heightMm: 50, elements: [] },
}

describe('业务请求超时（迭代 91 F-01：普通 30s / 出图与上传类 120s 分档，注入短超时秒级验证）', () => {
  it('普通请求挂起：超时归一化为 ApiError("TIMEOUT", 中文文案)，请求携带中止信号（不真实等待 30s）', async () => {
    const mod = await loadClient('client')
    mod.requestTimeouts.normal = 30 // 注入秒级短超时验证超时分支
    const fetchStub = hangingFetch()
    vi.stubGlobal('fetch', fetchStub)
    try {
      const err = await mod.localApi.saveTemplate(TIMEOUT_PKG).then(
        () => null,
        (e: unknown) => e,
      )
      expect(err).toBeInstanceOf(Error)
      expect((err as ApiError).name).toBe('ApiError')
      expect((err as ApiError).code).toBe('TIMEOUT')
      expect((err as ApiError).message).toContain('请求超时')
      expect(fetchStub).toHaveBeenCalledTimes(1)
      expect((fetchStub.mock.calls[0] as [string, RequestInit])[1].signal).toBeTruthy()
    } finally {
      vi.unstubAllGlobals()
    }
  })

  it('大负载端点（renderImage 出图）挂起：走 heavy 档——normal 档保持 30s 不变，仅 heavy 注入短超时（分档落点反向断言）', async () => {
    const mod = await loadClient('client')
    mod.requestTimeouts.heavy = 30 // 仅 heavy 缩短；若 renderImage 误挂 normal（30s），本用例将挂起至测试超时失败
    const fetchStub = hangingFetch()
    vi.stubGlobal('fetch', fetchStub)
    try {
      const err = await mod.localApi.renderImage({ requestId: 'r-1', labels: [] }).then(
        () => null,
        (e: unknown) => e,
      )
      expect(err).toBeInstanceOf(Error)
      expect((err as ApiError).name).toBe('ApiError')
      expect((err as ApiError).code).toBe('TIMEOUT')
      expect((err as ApiError).message).toContain('请求超时')
    } finally {
      vi.unstubAllGlobals()
    }
  })

  it('普通端点不吃 heavy 档：listTemplates 仅随 normal 注入超时（normal 档分档落点反向断言）', async () => {
    const mod = await loadClient('client')
    mod.requestTimeouts.normal = 30
    mod.requestTimeouts.heavy = 60_000 // heavy 放宽不影响普通请求
    const fetchStub = hangingFetch()
    vi.stubGlobal('fetch', fetchStub)
    try {
      const err = await mod.localApi.listTemplates().then(
        () => null,
        (e: unknown) => e,
      )
      expect((err as ApiError).code).toBe('TIMEOUT')
    } finally {
      vi.unstubAllGlobals()
    }
  })

  it('网络不可达（fetch 立即拒绝，非超时）：保持既有 NETWORK_ERROR 语义与中文文案，不误判为 TIMEOUT', async () => {
    const mod = await loadClient('client')
    const fetchStub = vi.fn().mockRejectedValue(new TypeError('fetch failed'))
    vi.stubGlobal('fetch', fetchStub)
    try {
      const err = await mod.localApi.listTemplates().then(
        () => null,
        (e: unknown) => e,
      )
      expect(err).toBeInstanceOf(Error)
      expect((err as ApiError).name).toBe('ApiError')
      expect((err as ApiError).code).toBe('NETWORK_ERROR')
      expect((err as ApiError).message).toContain('无法连接')
    } finally {
      vi.unstubAllGlobals()
    }
  })

  it('成功请求不受超时改造影响：正常返回 JSON（计时器即清理，不驻留）', async () => {
    const mod = await loadClient('client')
    const fetchStub = vi.fn().mockResolvedValue(new Response(JSON.stringify([{ name: '70x50', group: '默认', updatedAt: '2026-09-18' }]), { status: 200 }))
    vi.stubGlobal('fetch', fetchStub)
    try {
      const list = await mod.localApi.listTemplates()
      expect(list).toHaveLength(1)
      expect(list[0].name).toBe('70x50')
      expect((fetchStub.mock.calls[0] as [string, RequestInit])[1].signal).toBeTruthy()
    } finally {
      vi.unstubAllGlobals()
    }
  })
})

describe('exportTemplate 错误通道与 fetchBlob 同构（迭代 91 F-09）', () => {
  it('AC-02：HTTP 500 + 后端 ErrorView → 呈现后端真实 code 与 message，不再固定「导出失败（HTTP 500）」', async () => {
    const mod = await loadClient('client')
    const fetchStub = vi.fn().mockResolvedValue(
      new Response(JSON.stringify({ code: 'TEMPLATE_NOT_FOUND', message: '模板「70x50」不存在。' }), {
        status: 500,
        headers: { 'Content-Type': 'application/json' },
      }),
    )
    vi.stubGlobal('fetch', fetchStub)
    try {
      const err = await mod.localApi.exportTemplate('70x50').then(
        () => null,
        (e: unknown) => e,
      )
      expect(err).toBeInstanceOf(Error)
      expect((err as ApiError).name).toBe('ApiError')
      expect((err as ApiError).code).toBe('TEMPLATE_NOT_FOUND')
      expect((err as ApiError).message).toBe('模板「70x50」不存在。')
      // GET 请求形态不变
      expect((fetchStub.mock.calls[0] as [string, RequestInit])[1].method).toBe('GET')
    } finally {
      vi.unstubAllGlobals()
    }
  })

  it('HTTP 500 无 JSON 体（网关 / 中间层错误）：回退既有中文文案「导出失败（HTTP 500）。」', async () => {
    const mod = await loadClient('client')
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response('bad gateway', { status: 500 })))
    try {
      const err = await mod.localApi.exportTemplate('70x50').then(
        () => null,
        (e: unknown) => e,
      )
      expect(err).toBeInstanceOf(Error)
      expect((err as ApiError).name).toBe('ApiError')
      expect((err as ApiError).code).toBe('HTTP_500')
      expect((err as ApiError).message).toBe('导出失败（HTTP 500）。')
    } finally {
      vi.unstubAllGlobals()
    }
  })

  it('成功：返回 blob + Content-Disposition 文件名；响应头缺省时回退 {模板名}.lfpkg（解析收敛到 fetchBlob 一处）', async () => {
    const mod = await loadClient('client')
    const withName = vi.fn().mockResolvedValue(
      new Response(new Blob(['pkg-bytes']), { status: 200, headers: { 'Content-Disposition': 'attachment; filename="custom-name.lfpkg"' } }),
    )
    vi.stubGlobal('fetch', withName)
    try {
      const result = await mod.localApi.exportTemplate('70x50')
      expect(result.filename).toBe('custom-name.lfpkg')
      expect(result.blob.size).toBeGreaterThan(0)
    } finally {
      vi.unstubAllGlobals()
    }
    const withoutName = vi.fn().mockResolvedValue(new Response(new Blob(['pkg-bytes']), { status: 200 }))
    vi.stubGlobal('fetch', withoutName)
    try {
      const result = await mod.localApi.exportTemplate('70x50')
      expect(result.filename).toBe('70x50.lfpkg')
    } finally {
      vi.unstubAllGlobals()
    }
  })
})

// ── ：Content-Disposition 文件名解析优先 filename*（RFC 5987）──
// 后端 ASP.NET Core 对非 ASCII 文件名按 RFC 6266/5987 同时发两个参数（实测形态见 Issue 「目标与依据」）：
// filename=____.lfpkg（ASCII 回退值，非 ASCII 替换为 _）在前、filename*=UTF-8''%E4%B8%AD...（百分号编码，无损）在后。

describe('Content-Disposition 文件名解析：filename*（RFC 5987）优先还原中文（迭代 102 · #220）', () => {
  it('AC-01：双参数实测头 → 优先取 filename* 解码还原中文（exportTemplate 链），不再用下划线回退值', async () => {
    const mod = await loadClient('client')
    const fetchStub = vi.fn().mockResolvedValue(
      new Response(new Blob(['pkg-bytes']), {
        status: 200,
        headers: { 'Content-Disposition': "attachment; filename=____.lfpkg; filename*=UTF-8''%E4%B8%AD%E6%96%87%E6%A8%A1%E6%9D%BF.lfpkg" },
      }),
    )
    vi.stubGlobal('fetch', fetchStub)
    try {
      const result = await mod.localApi.exportTemplate('中文模板')
      expect(result.filename).toBe('中文模板.lfpkg')
      expect(result.blob.size).toBeGreaterThan(0)
    } finally {
      vi.unstubAllGlobals()
    }
  })

  it('AC-01：renderImage 链同覆盖——{name}-print.png 中文名还原（货架标签 → 货架标签-print.png）', async () => {
    const mod = await loadClient('client')
    const fetchStub = vi.fn().mockResolvedValue(
      new Response(new Blob(['png-bytes'], { type: 'image/png' }), {
        status: 200,
        headers: { 'Content-Disposition': "attachment; filename=____-print.png; filename*=UTF-8''%E8%B4%A7%E6%9E%B6%E6%A0%87%E7%AD%BE-print.png" },
      }),
    )
    vi.stubGlobal('fetch', fetchStub)
    try {
      const result = await mod.localApi.renderImage({ requestId: 'r-1', labels: [] })
      expect(result.filename).toBe('货架标签-print.png')
      expect(result.blob.size).toBeGreaterThan(0)
    } finally {
      vi.unstubAllGlobals()
    }
  })

  it('AC-02：纯 ASCII 名（仅 filename=，实测未加引号形态）行为不变（带引号形态既有用例已覆盖）', async () => {
    const mod = await loadClient('client')
    const fetchStub = vi.fn().mockResolvedValue(
      new Response(new Blob(['xlsx-bytes']), { status: 200, headers: { 'Content-Disposition': 'attachment; filename=excel-template.xlsx' } }),
    )
    vi.stubGlobal('fetch', fetchStub)
    try {
      const result = await mod.localApi.excelTemplate([{ key: 'sku', displayName: 'SKU' }], { sku: 'A-1' })
      expect(result.filename).toBe('excel-template.xlsx')
    } finally {
      vi.unstubAllGlobals()
    }
  })

  it('AC-02：仅 filename*（无 filename=）——直接解码取用，不再落到 fallbackName', async () => {
    const mod = await loadClient('client')
    const fetchStub = vi.fn().mockResolvedValue(
      new Response(new Blob(['pkg-bytes']), {
        status: 200,
        headers: { 'Content-Disposition': "attachment; filename*=UTF-8''%E8%B4%A7%E6%9E%B6%E6%A0%87%E7%AD%BE.lfpkg" },
      }),
    )
    vi.stubGlobal('fetch', fetchStub)
    try {
      const result = await mod.localApi.exportTemplate('货架标签')
      expect(result.filename).toBe('货架标签.lfpkg')
    } finally {
      vi.unstubAllGlobals()
    }
  })

  it('AC-02：畸形 filename* 值（截断百分号编码）容错——回退 filename= 回退值，不抛未捕获异常', async () => {
    const mod = await loadClient('client')
    const fetchStub = vi.fn().mockResolvedValue(
      new Response(new Blob(['pkg-bytes']), {
        status: 200,
        headers: { 'Content-Disposition': "attachment; filename=____.lfpkg; filename*=UTF-8''%E4%B8" },
      }),
    )
    vi.stubGlobal('fetch', fetchStub)
    try {
      const result = await mod.localApi.exportTemplate('中文模板')
      expect(result.filename).toBe('____.lfpkg')
    } finally {
      vi.unstubAllGlobals()
    }
  })

  it('AC-02：畸形 filename* 且无 filename= ——回退调用方 fallbackName（renderImage → label-print.png）', async () => {
    const mod = await loadClient('client')
    const fetchStub = vi.fn().mockResolvedValue(
      new Response(new Blob(['png-bytes'], { type: 'image/png' }), {
        status: 200,
        headers: { 'Content-Disposition': "attachment; filename*=UTF-8''%E4%B8" },
      }),
    )
    vi.stubGlobal('fetch', fetchStub)
    try {
      const result = await mod.localApi.renderImage({ requestId: 'r-1', labels: [] })
      expect(result.filename).toBe('label-print.png')
    } finally {
      vi.unstubAllGlobals()
    }
  })
})

// ── ：ErrorView.params 展示链路——已知码按码表渲染、未知码回退后端中文 ──

describe('错误响应文案解析（迭代 109：LF_* 码表 + params 插值）', () => {
  /** 加载 client（resetModules 后的新模块图）→ 切语言（与 client.ts 绑定同一 i18n 实例）→ 打一个 404 ErrorView，返回抛出的 ApiError。 */
  async function fetchError(body: unknown, locale?: 'en' | 'zh-CN'): Promise<ApiError> {
    const mod = await loadClient('client')
    const i18n = await import('../../i18n/index')
    if (locale) i18n.changeLocale(locale)
    const fetchStub = vi.fn().mockResolvedValue(
      new Response(JSON.stringify(body), { status: 404, headers: { 'Content-Type': 'application/json' } }),
    )
    vi.stubGlobal('fetch', fetchStub)
    try {
      const err = await mod.serverApi.getJob('job-x').then(
        () => null,
        (e: unknown) => e,
      )
      // resetModules 后 client 模块图内的 ApiError 类对象与测试文件静态导入的不是同一引用，按 name 判型
      expect((err as ApiError).name).toBe('ApiError')
      return err as ApiError
    } finally {
      if (locale) i18n.changeLocale('zh-CN')
      vi.unstubAllGlobals()
    }
  }

  it('en + 已知码 + params：message 为码表英文插值（ApiError 携带 params）', async () => {
    // 按 IP 未找到设备拆码 LF_SRV_012（LF_SRV_001 保留设备未注册 {deviceId}）
    const err = await fetchError(
      {
        code: 'LF_SRV_012',
        message: '按 IP 未找到设备：10.0.0.9。',
        fieldKey: null,
        params: { ip: '10.0.0.9' },
      },
      'en',
    )
    expect(err.code).toBe('LF_SRV_012')
    expect(err.message).toBe('No device found for IP: 10.0.0.9.')
    expect(err.params).toEqual({ ip: '10.0.0.9' })
  })

  it('en + 未知码：回退后端中文 message（后端新码漏表 / 旧版兼容路径）', async () => {
    const err = await fetchError({ code: 'LF_FAKE_999', message: '未知场景的中文原文。', fieldKey: null }, 'en')
    expect(err.message).toBe('未知场景的中文原文。')
  })

  it('zh-CN + 已知码：直用后端中文 message（现状不变）', async () => {
    const err = await fetchError({
      code: 'LF_SRV_012',
      message: '按 IP 未找到设备：10.0.0.9。',
      fieldKey: null,
      params: { ip: '10.0.0.9' },
    })
    expect(err.message).toBe('按 IP 未找到设备：10.0.0.9。')
  })
})
