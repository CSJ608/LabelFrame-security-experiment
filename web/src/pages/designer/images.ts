// 图片资源纯函数层：格式 / 大小上限常量与编解码、净化、去重（无 React 依赖——机制层）。

/** 支持格式（accept + 预检白名单，与服务端格式探嗅对齐）。 */
export const IMAGE_ACCEPT = 'image/png,image/jpeg,image/bmp'
/** 单图入库字节上限（2MB，压缩后）。 */
export const IMAGE_MAX_BYTES = 2 * 1024 * 1024
/** 单模板张数上限。 */
export const IMAGE_MAX_COUNT = 10
/** 单模板总量上限（10MB，入库字节）。 */
export const IMAGE_MAX_TOTAL_BYTES = 10 * 1024 * 1024

/** 字节数 → MB 显示（压缩提示插值用）：≥1MB 一位小数，更小保留两位（防「0.0MB」失真）。 */
export function fmtMb(bytes: number): string {
  const v = bytes / (1024 * 1024)
  return v >= 1 ? v.toFixed(1) : v.toFixed(2)
}

/** 文件名净化为资源 key（口径对齐后端 SanitizeKey：Unicode 字母数字保留，中文文件名不得被 ASCII 白名单毁成下划线；\p{L}\p{N} 与 . - _ 之外替换 _）。 */
export function sanitizeImageKey(fileName: string): string {
  const safe = fileName.replace(/[^\p{L}\p{N}._-]/gu, '_')
  return safe.trim() === '' ? 'image' : safe
}

/** key 去重（同名重复上传 / 与既有元素引用撞名时追加序号，扩展名保留参与区分 a.png 与 a.jpeg）。 */
export function uniqueImageKey(base: string, existing: ReadonlySet<string>): string {
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
export function base64ToDataUrl(base64: string): string {
  if (base64.startsWith('iVBORw0')) return 'data:image/png;base64,' + base64
  if (base64.startsWith('/9j/')) return 'data:image/jpeg;base64,' + base64
  if (base64.startsWith('Qk')) return 'data:image/bmp;base64,' + base64
  return 'data:image/png;base64,' + base64
}

/** 解码图片像素尺寸（异步；加载失败或环境不支持时回退 0×0——初始尺寸按正方形兜底），3s 超时防挂起。 */
export function decodeImageSize(dataUrl: string): Promise<{ width: number; height: number }> {
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
export function readFileAsDataUrl(file: Blob): Promise<string> {
  return new Promise((resolve, reject) => {
    const reader = new FileReader()
    reader.onload = () => resolve(String(reader.result))
    reader.onerror = () => reject(reader.error ?? new Error('read failed'))
    reader.readAsDataURL(file)
  })
}
