// 上传图片浏览器内自动压缩（迭代 132 返修 · #329 方案 v2，用户拍板 A）：
// >1MB 的图片上传前先压缩——长边降采样 ≤2000px＋同格式重编码（JPEG q0.85 / PNG 保持 / BMP 转 PNG，
// canvas 无 BMP 编码器），压缩后仍 >2MB 才拒收（拒收闸在 Designer 上传链）；用户原文件不动。
// jsdom 无真实解码 / 编码——单测以桩 canvas 锚定分支逻辑（阈值 / 降采样 / 格式 / 质量参数），字节量
// 降幅与画质依赖陪验走查（方案 v2 风险节）。

/** 触发压缩的体积阈值（1MB，含）——不超过则原样直通。 */
export const IMAGE_COMPRESS_THRESHOLD_BYTES = 1024 * 1024
/** 降采样目标：长边上限（px）。 */
export const IMAGE_COMPRESS_MAX_EDGE_PX = 2000
/** JPEG 重编码质量。 */
export const IMAGE_COMPRESS_JPEG_QUALITY = 0.85

/** 压缩结果：blob 为入库字节（未压缩时即原文件）；compressed 标记是否经过压缩管道。 */
export interface ImageCompressResult {
  blob: Blob
  compressed: boolean
}

/** 降采样目标尺寸（纯函数）：长边 >maxEdge 时等比缩到长边 = maxEdge，否则保持原尺寸。 */
export function downscaleSize(width: number, height: number, maxEdge: number): { width: number; height: number } {
  if (width <= 0 || height <= 0) return { width, height }
  const long = Math.max(width, height)
  if (long <= maxEdge) return { width, height }
  const scale = maxEdge / long
  return { width: Math.max(1, Math.round(width * scale)), height: Math.max(1, Math.round(height * scale)) }
}

/** 重编码目标格式（纯函数）：PNG 保持 PNG；BMP 转 PNG（canvas 无 BMP 编码器，拍板 4-A）；其余（JPEG）按原格式。 */
export function encodeTargetMime(fileType: string): string {
  if (fileType === 'image/png') return 'image/png'
  if (fileType === 'image/bmp') return 'image/png'
  return 'image/jpeg'
}

/** 读文件为 dataURL（jsdom 无 URL.createObjectURL，统一走 FileReader 通道，测试可桩 Image）。 */
function readFileAsDataUrl(file: Blob): Promise<string> {
  return new Promise((resolve, reject) => {
    const reader = new FileReader()
    reader.onload = () => resolve(String(reader.result))
    reader.onerror = () => reject(reader.error ?? new Error('read failed'))
    reader.readAsDataURL(file)
  })
}

/** 解码为 HTMLImageElement（onload / onerror 双出口）。 */
function loadImage(dataUrl: string): Promise<HTMLImageElement> {
  return new Promise((resolve, reject) => {
    const img = new window.Image()
    img.onload = () => resolve(img)
    img.onerror = () => reject(new Error('decode failed'))
    img.src = dataUrl
  })
}

/** canvas.toBlob Promise 包装（toBlob 缺失或返回 null 均拒绝——由调用方回退原文件）。 */
function canvasToBlob(canvas: HTMLCanvasElement, mime: string, quality?: number): Promise<Blob> {
  return new Promise((resolve, reject) => {
    try {
      canvas.toBlob(
        (blob) => (blob && blob.size > 0 ? resolve(blob) : reject(new Error('encode failed'))),
        mime,
        quality,
      )
    } catch (err) {
      reject(err instanceof Error ? err : new Error('encode failed'))
    }
  })
}

/**
 * 压缩入口：≤1MB 直通原文件；>1MB 走「解码 → 长边降采样 → 重编码」管道。
 * 压缩管道任一环不可用（解码 / canvas / toBlob）时回退原文件（compressed=false）——
 * 上传链的 2MB 拒收闸仍以最终 blob 字节判定，不会因压缩失败放行超限图。
 */
export async function compressImageIfNeeded(file: File): Promise<ImageCompressResult> {
  if (file.size <= IMAGE_COMPRESS_THRESHOLD_BYTES) return { blob: file, compressed: false }
  try {
    const img = await loadImage(await readFileAsDataUrl(file))
    const target = downscaleSize(img.naturalWidth, img.naturalHeight, IMAGE_COMPRESS_MAX_EDGE_PX)
    const canvas = document.createElement('canvas')
    canvas.width = target.width
    canvas.height = target.height
    const ctx = canvas.getContext('2d')
    if (!ctx) throw new Error('canvas 2d unavailable')
    ctx.drawImage(img, 0, 0, target.width, target.height)
    const mime = encodeTargetMime(file.type)
    const blob = await canvasToBlob(canvas, mime, mime === 'image/jpeg' ? IMAGE_COMPRESS_JPEG_QUALITY : undefined)
    return { blob, compressed: true }
  } catch {
    return { blob: file, compressed: false }
  }
}
