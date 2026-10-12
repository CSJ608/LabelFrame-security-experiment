// @vitest-environment jsdom
// 上传自动压缩分支单测（迭代 132 返修 · #329 方案 v2）：jsdom 无真实图片解码 / 编码——
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

/** 桩环境：全局 Image（固定像素尺寸异步 onload）＋ canvas getContext / toBlob（记录参数、回固定 Blob）。 */
function stubCanvasPipeline(naturalWidth: number, naturalHeight: number, blobBytes = new Uint8Array([1, 2, 3])) {
  const calls: ToBlobCall[] = []
  const ctx = { drawImage: () => {} }
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
    callback(new Blob([blobBytes], { type: mime ?? 'application/octet-stream' }))
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
})
