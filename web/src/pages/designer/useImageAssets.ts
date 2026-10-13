// 图片资源编排 hook：资源表持有（不进历史快照）、上传两步式（格式预检 → 压缩 → dataURL → key 净化去重 →
// 写目标元素 key）、上传 / 更换共用的隐藏文件选择、页内轻量 toast。回调体自 Designer.tsx 逐字搬移。

import { useCallback, useEffect, useRef, useState } from 'react'
import type { RefObject } from 'react'
import { useTranslation } from 'react-i18next'
import type { DesignState } from './CanvasViewport'
import { useApp } from '../../state/AppContext'
import type { DesignElement, ImageAsset, ImageElement } from '../../lib/design/types'
import { compressImageIfNeeded } from '../../lib/design/imageCompress'
import { IMAGE_MAX_BYTES, IMAGE_MAX_COUNT, IMAGE_MAX_TOTAL_BYTES, fmtMb, sanitizeImageKey, uniqueImageKey, decodeImageSize, readFileAsDataUrl } from './images'

export interface UseImageAssetsOptions {
  stateRef: RefObject<DesignState | null>
  changeElement: (id: string, patch: Partial<DesignElement>) => void
  viewMode: 'fit' | 'preview'
}

export function useImageAssets({ stateRef, changeElement, viewMode }: UseImageAssetsOptions) {
  const app = useApp()
  const { t } = useTranslation('designer')

  // 图片资源表：key → 资源，由设计器持有——不进历史快照（snap 仅 paperW/paperH/elements，防 base64 撑爆撤销栈）；孤儿 key 由保存时 GC。
  const [imageAssets, setImageAssets] = useState<Record<string, ImageAsset>>({})
  const imageAssetsRef = useRef<Record<string, ImageAsset>>({})
  /** 隐藏文件选择（上传 / 更换共用同一 input；replaceTargetRef 记录更换目标元素 id）。 */
  const fileInputRef = useRef<HTMLInputElement>(null)
  const replaceTargetRef = useRef<string | null>(null)

  // ---------- 图片上传（两步式＋压缩） ----------
  /* * 上传失败提醒：页内轻量 toast（5s 自动消失，单条最新覆盖）；成功 / 压缩提示仍走状态栏。 */
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

  /** 文件落地（两步式，必带目标元素）：格式预检 → 压缩（拒收按压缩后字节）→ dataURL → key 净化去重 →
   *  写目标元素 key（换新 key 防共享旧 key 的多元素同变；旧 key 孤儿随保存 GC）。 */
  const addImageFile = useCallback(
    (file: File, targetId: string) => {
      const reject = (reasonKey: string) => showImageToast(t('status.imageRejected.body', { reason: t(reasonKey) }))
      if (file.type && !['image/png', 'image/jpeg', 'image/bmp'].includes(file.type)) {
        reject('imageReject.format')
        return
      }
      const s0 = stateRef.current
      if (!s0 || !s0.elements.some((el) => el.id === targetId && el.type === 'Image')) return

      void (async () => {
        const { blob, compressed, pngAlphaKept } = await compressImageIfNeeded(file, IMAGE_MAX_BYTES)
        if (blob.size > IMAGE_MAX_BYTES) {
          reject(pngAlphaKept ? 'imageReject.pngAlphaTooLarge' : 'imageReject.tooLarge')
          return
        }
        const assets = imageAssetsRef.current
        // 张数按「将被引用的新 key」计：更换不占新增名额（旧 key 成孤儿随保存 GC）
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
        // 压缩发生时以 toast 呈现（状态栏轻提示用户看不见——踩过）；未压缩仍走状态栏。
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
      if (file && targetId) addImageFile(file, targetId)
    },
    [addImageFile],
  )

  return { imageAssets, setImageAssets, imageAssetsRef, fileInputRef, replaceImage, onImageInputChange, imageToast }
}
