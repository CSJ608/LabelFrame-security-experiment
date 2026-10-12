// @vitest-environment jsdom
// 上传自动压缩分支单测：jsdom 无真实图片解码 / 编码——
// 以桩全局 Image（src 赋值即 onload 并给固定像素尺寸）＋桩 HTMLCanvasElement.toBlob（记录
// 调用参数并回固定 Blob）锚定分支逻辑：阈值直通 / >1MB 触发 / 长边降采样 / JPEG 质量 / PNG
// 保持 / BMP 转 PNG / 编码失败回退原文件。真实压缩效果（字节降幅、画质）依赖陪验走查。

import { afterEach, describe, expect, it, vi } from 'vitest'
import {
  IMAGE_COMPRESS_JPEG_QUALITY,
  IMAGE_COMPRESS_MAX_EDGE_PX,
  IMAGE_COMPRESS_THRESHOLD_BYTES,
  compressImageIfNeeded,
  downscaleSize,
  encodeTargetMime,
} from './imageCompress'

// ---------- 纯函数 ----------

describe('downscaleSize（降采样目标尺寸）', () => {
  it('长边 ≤ 上限：保持原尺寸', () => {
    expect(downscaleSize(2000, 1000, IMAGE_COMPRESS_MAX_EDGE_PX)).toEqual({ width: 2000, height: 1000 })
    expect(downscaleSize(800, 600, IMAGE_COMPRESS_MAX_EDGE_PX)).toEqual({ width: 800, height: 600 })
  })

  it('长边 > 上限：等比缩到长边 = 2000px', () => {
    expect(downscaleSize(4000, 2000, IMAGE_COMPRESS_MAX_EDGE_PX)).toEqual({ width: 2000, height: 1000 })
    // 竖图（高为长边）同口径
    expect(downscaleSize(1500, 4500, IMAGE_COMPRESS_MAX_EDGE_PX)).toEqual({ width: 667, height: 2000 })
  })
})

describe('encodeTargetMime（重编码目标格式）', () => {
  it('PNG 保持 PNG；BMP 转 PNG（canvas 无 BMP 编码器）；JPEG 保持 JPEG', () => {
    expect(encodeTargetMime('image/png')).toBe('image/png')
    expect(encodeTargetMime('image/bmp')).toBe('image/png')
    expect(encodeTargetMime('image/jpeg')).toBe('image/jpeg')
  })
})

// ---------- 管道（桩 canvas / Image） ----------

interface ToBlobCall {
  mime: string
  quality?: number
  canvasWidth: number
  canvasHeight: number
}

/** 桩环境：全局 Image（固定像素尺寸异步 onload）＋ canvas getContext / toBlob / getImageData（记录参数、回可配 Blob）。
 *  opts.alpha 控制透明判定（true 含透明 / false 不透明 / 'throw' 不可判定）；opts.jpegBytes 控制 JPEG 兜底产物字节。 */
function stubCanvasPipeline(
  naturalWidth: number,
  naturalHeight: number,
  blobBytes: Uint8Array<ArrayBuffer> | ((mime: string) => Uint8Array<ArrayBuffer>) = new Uint8Array([1, 2, 3]),
  opts: { alpha?: boolean | 'throw'; jpegBytes?: Uint8Array<ArrayBuffer> } = {},
) {
  const calls: ToBlobCall[] = []
  const ctx = {
    drawImage: () => {},
    getImageData: () => {
      if (opts.alpha === 'throw') throw new Error('getImageData unavailable')
      // 单像元 alpha：含透明 128 / 不透明 255（管道只看 alpha 通道 <255）
      return { data: opts.alpha ? new Uint8ClampedArray([0, 0, 0, 128]) : new Uint8ClampedArray([0, 0, 0, 255]) }
    },
  }
  class FakeImage {
    onload: (() => void) | null = null
    onerror: (() => void) | null = null
    naturalWidth = naturalWidth
    naturalHeight = naturalHeight
    private _src = ''
    get src() {
      return this._src
    }
    set src(value: string) {
      this._src = value
      queueMicrotask(() => this.onload?.())
    }
  }
  vi.stubGlobal('Image', FakeImage as unknown as typeof Image)
  // jsdom 无 2d context（getContext 返回 null）——压缩管道依赖 drawImage / toBlob，双双打桩
  vi.spyOn(HTMLCanvasElement.prototype, 'getContext').mockReturnValue(ctx as unknown as CanvasRenderingContext2D)
  vi.spyOn(HTMLCanvasElement.prototype, 'toBlob').mockImplementation(function (
    this: HTMLCanvasElement,
    callback: BlobCallback,
    mime?: string,
    quality?: number,
  ) {
    calls.push({ mime: mime ?? '', quality, canvasWidth: this.width, canvasHeight: this.height })
    const bytes = typeof blobBytes === 'function' ? blobBytes(mime ?? '') : blobBytes
    callback(new Blob([mime === 'image/jpeg' && opts.jpegBytes ? opts.jpegBytes : bytes], { type: mime ?? 'application/octet-stream' }))
    return undefined
  })
  return calls
}

afterEach(() => {
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
})

function fileOf(size: number, type = 'image/png', name = 'a.png'): File {
  return new File([new Uint8Array(size)], name, { type })
}

describe('compressImageIfNeeded（压缩管道分支）', () => {
  it('≤1MB 直通：不经 canvas（无 toBlob 调用），返回原文件', async () => {
    const calls = stubCanvasPipeline(4000, 3000)
    const file = fileOf(IMAGE_COMPRESS_THRESHOLD_BYTES)
    const result = await compressImageIfNeeded(file)
    expect(result.blob).toBe(file)
    expect(result.compressed).toBe(false)
    expect(calls).toHaveLength(0)
  })

  it('>1MB 触发压缩：JPEG 以 q0.85 重编码，返回压缩后 blob', async () => {
    const calls = stubCanvasPipeline(1600, 1200)
    const result = await compressImageIfNeeded(fileOf(IMAGE_COMPRESS_THRESHOLD_BYTES + 1, 'image/jpeg', 'a.jpg'))
    expect(result.compressed).toBe(true)
    expect(result.blob.size).toBe(3)
    expect(calls).toHaveLength(1)
    expect(calls[0].mime).toBe('image/jpeg')
    expect(calls[0].quality).toBe(IMAGE_COMPRESS_JPEG_QUALITY)
  })

  it('>1MB 的 PNG：保持 PNG 且不传质量参数', async () => {
    const calls = stubCanvasPipeline(1600, 1200)
    const result = await compressImageIfNeeded(fileOf(2 * 1024 * 1024, 'image/png'))
    expect(result.compressed).toBe(true)
    expect(calls[0].mime).toBe('image/png')
    expect(calls[0].quality).toBeUndefined()
  })

  it('>1MB 的 BMP：转 PNG（拍板 4-A）', async () => {
    const calls = stubCanvasPipeline(1600, 1200)
    await compressImageIfNeeded(fileOf(2 * 1024 * 1024, 'image/bmp', 'a.bmp'))
    expect(calls[0].mime).toBe('image/png')
  })

  it('长边 >2000px 的 JPEG：canvas 尺寸降采样到长边 2000（等比）', async () => {
    const calls = stubCanvasPipeline(8000, 6000)
    await compressImageIfNeeded(fileOf(2 * 1024 * 1024, 'image/jpeg', 'a.jpg'))
    expect(calls[0].canvasWidth).toBe(2000)
    expect(calls[0].canvasHeight).toBe(1500)
  })

  it('长边 ≤2000px 的 >1MB 图：原尺寸重编码（只压质量不降采样）', async () => {
    const calls = stubCanvasPipeline(1600, 1200)
    await compressImageIfNeeded(fileOf(2 * 1024 * 1024, 'image/jpeg', 'a.jpg'))
    expect(calls[0].canvasWidth).toBe(1600)
    expect(calls[0].canvasHeight).toBe(1200)
  })

  it('编码失败（toBlob 回 null）：回退原文件直通（拒收闸仍按最终 blob 字节判定）', async () => {
    class FakeImage {
      onload: (() => void) | null = null
      onerror: (() => void) | null = null
      naturalWidth = 1600
      naturalHeight = 1200
      private _src = ''
      get src() {
        return this._src
      }
      set src(value: string) {
        this._src = value
        queueMicrotask(() => this.onload?.())
      }
    }
    vi.stubGlobal('Image', FakeImage as unknown as typeof Image)
    vi.spyOn(HTMLCanvasElement.prototype, 'getContext').mockReturnValue({ drawImage: () => {} } as unknown as CanvasRenderingContext2D)
    vi.spyOn(HTMLCanvasElement.prototype, 'toBlob').mockImplementation((callback) => {
      callback(null)
      return undefined
    })
    const file = fileOf(2 * 1024 * 1024, 'image/jpeg', 'a.jpg')
    const result = await compressImageIfNeeded(file)
    expect(result.blob).toBe(file)
    expect(result.compressed).toBe(false)
  })

  // PNG 超 maxBytes 的 alpha 感知兜底（LOGO_2 类大 PNG 压不进 2MB——踩过）

  it('PNG 重编码后仍超 maxBytes 且无透明：转 JPEG q0.85 兜底（采用更小的 JPEG 产物）', async () => {
    const pngBytes = new Uint8Array(2.2 * 1024 * 1024) // 2.2MB：>2MB 上限
    const jpegBytes = new Uint8Array(300 * 1024) // 兜底 JPEG 300KB
    const calls = stubCanvasPipeline(2752, 1536, pngBytes, { alpha: false, jpegBytes })
    const result = await compressImageIfNeeded(fileOf(2.86 * 1024 * 1024, 'image/png', 'logo.png'), 2 * 1024 * 1024)
    expect(result.compressed).toBe(true)
    expect(result.pngAlphaKept).toBeUndefined()
    expect(result.blob.size).toBe(300 * 1024)
    expect(result.blob.type).toBe('image/jpeg')
    expect(calls).toHaveLength(2)
    expect(calls[0].mime).toBe('image/png')
    expect(calls[1].mime).toBe('image/jpeg')
    expect(calls[1].quality).toBe(IMAGE_COMPRESS_JPEG_QUALITY)
  })

  it('PNG 重编码后仍超 maxBytes 且含透明：保持 PNG 并置 pngAlphaKept（调用方给针对性指引）', async () => {
    const pngBytes = new Uint8Array(2.2 * 1024 * 1024)
    const calls = stubCanvasPipeline(2752, 1536, pngBytes, { alpha: true, jpegBytes: new Uint8Array(1024) })
    const result = await compressImageIfNeeded(fileOf(2.86 * 1024 * 1024, 'image/png', 'logo.png'), 2 * 1024 * 1024)
    expect(result.compressed).toBe(true)
    expect(result.pngAlphaKept).toBe(true)
    expect(result.blob.type).toBe('image/png')
    expect(calls).toHaveLength(1) // 含透明不转格式——仅 PNG 一次编码
  })

  it('PNG 超 maxBytes 但透明判定不可用（getImageData 异常）：按含透明保守处理（不转格式）', async () => {
    const pngBytes = new Uint8Array(2.2 * 1024 * 1024)
    const calls = stubCanvasPipeline(2752, 1536, pngBytes, { alpha: 'throw', jpegBytes: new Uint8Array(1024) })
    const result = await compressImageIfNeeded(fileOf(2.86 * 1024 * 1024, 'image/png', 'logo.png'), 2 * 1024 * 1024)
    expect(result.pngAlphaKept).toBe(true)
    expect(calls).toHaveLength(1)
  })

  it('无透明但 JPEG 兜底产物不小于 PNG：仍用 PNG、不置 pngAlphaKept（走通用超限口径）', async () => {
    const pngBytes = new Uint8Array(2.2 * 1024 * 1024)
    stubCanvasPipeline(2752, 1536, pngBytes, { alpha: false, jpegBytes: new Uint8Array(2.3 * 1024 * 1024) })
    const result = await compressImageIfNeeded(fileOf(2.86 * 1024 * 1024, 'image/png', 'logo.png'), 2 * 1024 * 1024)
    expect(result.blob.type).toBe('image/png')
    expect(result.pngAlphaKept).toBeUndefined()
  })

  it('PNG 重编码后 ≤ maxBytes：不走兜底（无第二次编码）', async () => {
    const pngBytes = new Uint8Array(1.5 * 1024 * 1024)
    const calls = stubCanvasPipeline(2752, 1536, pngBytes, { alpha: false, jpegBytes: new Uint8Array(1024) })
    const result = await compressImageIfNeeded(fileOf(2.86 * 1024 * 1024, 'image/png', 'logo.png'), 2 * 1024 * 1024)
    expect(result.blob.type).toBe('image/png')
    expect(result.pngAlphaKept).toBeUndefined()
    expect(calls).toHaveLength(1)
  })

  it('未传 maxBytes（默认不限）：PNG 保持单次编码且不置 pngAlphaKept——既有调用方行为不变', async () => {
    const pngBytes = new Uint8Array(2.2 * 1024 * 1024)
    const calls = stubCanvasPipeline(2752, 1536, pngBytes, { alpha: false, jpegBytes: new Uint8Array(1024) })
    const result = await compressImageIfNeeded(fileOf(2.86 * 1024 * 1024, 'image/png', 'logo.png'))
    expect(result.blob.type).toBe('image/png')
    expect(result.pngAlphaKept).toBeUndefined()
    expect(calls).toHaveLength(1)
  })
})
