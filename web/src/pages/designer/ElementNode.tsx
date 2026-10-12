// 设计器元素渲染（react-konva 声明式）：Group = 边框 + 内容。
// 图片内容三态——key 空＝未指定（虚线空占位）、key 非空缺资源 / 加载失败＝缺资源占位、
// 加载成功＝实际图片；成功与空占位分支都带整盒 fill 命中区（KImage listening={false} 不参与命中，
// 无 fill 兜底则画布点击穿透）。

import { useEffect, useMemo, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Group, Image as KImage, Line as KLine, Rect as KRect, Text as KText } from 'react-konva'
import type Konva from 'konva'
import type { KonvaEventObject } from 'konva/lib/Node'
import { pxv } from '../../lib/design/geometry'
import type { BarcodeElement, DesignElement, ImageAsset, QrCodeElement, TextElement } from '../../lib/design/types'
import { elementContent } from '../../lib/design/types'
import { fitImageRect, makeBarcodeCanvas, makeQrCanvas } from '../../lib/design/barcode'

interface ElementNodeProps {
  e: DesignElement
  editable: boolean
  ox: number
  oy: number
  /* * 图片资源表（key → 资源，Designer 持有，不进历史快照）。 */
  imageAssets?: Record<string, ImageAsset>
  onDragStart?: (ev: KonvaEventObject<DragEvent>) => void
  onDragMove?: (ev: KonvaEventObject<DragEvent>) => void
  onDragEnd?: (ev: KonvaEventObject<DragEvent>) => void
}

// 含 CJK 字符检测：Konva wrap='word' 按空格分词，中文无空格永不换行 → 长文本单行溢出被 shrink 缩小（字高失真）。
// 含 CJK 时用 'char'（逐字换行），与 Skia 打印语义（按框宽换行）一致；纯 ASCII 保持 'word'（单词不拆断）。
const hasCjk = (s: string) => /[\u3000-\u30ff\u3400-\u9fff\uf900-\ufaff\uff00-\uffef]/.test(s)

export function ElementNode({ e, editable, ox, oy, imageAssets, onDragStart, onDragMove, onDragEnd }: ElementNodeProps) {
  const { t } = useTranslation('designer')
  const x = ox + pxv(e.x)
  const y = oy + pxv(e.y)
  const w = Math.max(2, pxv(e.w))
  const h = Math.max(2, pxv(e.h))
  const borderW = Math.max(1, pxv(e.border || 0))

  const common = {
    id: e.id,
    name: 'element',
    x,
    y,
    draggable: editable,
    onDragStart,
    onDragMove,
    onDragEnd,
  }

  // 预览态（editable=false）且 key 空：不渲染任何内容（「未指定＝不存在于打印面」）
  if (!editable && e.type === 'Image' && !e.key) return null

  switch (e.type) {
    case 'Text':
      return (
        <Group {...common}>
          <KRect x={0} y={0} width={w} height={h} stroke={e.border > 0 ? '#000' : undefined} strokeWidth={borderW} strokeScaleEnabled={false} />
          <TextContent e={e} />
        </Group>
      )
    case 'Barcode':
      return (
        <Group {...common}>
          <KRect x={0} y={0} width={w} height={h} stroke={e.border > 0 ? '#000' : undefined} strokeWidth={borderW} strokeScaleEnabled={false} />
          <ImageContent e={e} wPx={w} hPx={h} />
        </Group>
      )
    case 'QrCode':
      return (
        <Group {...common}>
          <KRect x={0} y={0} width={w} height={h} stroke={e.border > 0 ? '#000' : undefined} strokeWidth={borderW} strokeScaleEnabled={false} />
          <ImageContent e={e} wPx={w} hPx={h} qr />
        </Group>
      )
    case 'Rect':
      return (
        <Group {...common}>
          <KRect x={0} y={0} width={w} height={h} stroke={e.border > 0 ? '#000' : undefined} strokeWidth={borderW} fill="transparent" strokeScaleEnabled={false} />
        </Group>
      )
    case 'Image':
      return (
        <Group {...common}>
          <ElementImageContent e={e} wPx={w} hPx={h} borderW={borderW} asset={e.key ? imageAssets?.[e.key] : undefined} />
        </Group>
      )
    case 'Line':
      return (
        <KLine id={e.id} name="element" x={x} y={y} points={[0, 0, pxv(e.w), pxv(e.h)]} stroke="#000" strokeWidth={Math.max(1, pxv(e.thickness || 0.5))} strokeScaleEnabled={false} draggable={editable} onDragStart={onDragStart} onDragMove={onDragMove} onDragEnd={onDragEnd} />
      )
    case 'Region':
      return (
        <Group {...common}>
          <KRect x={0} y={0} width={w} height={h} fill="rgba(0,128,255,0.06)" stroke={e.border > 0 ? '#000' : '#8a94a0'} strokeWidth={borderW} dash={[6, 4]} strokeScaleEnabled={false} />
          <KText x={4} y={2} text={t('canvas.regionNode', { id: e.containerId || '' })} fontSize={10} fontFamily="Microsoft YaHei" fill="#7a8490" listening={false} />
        </Group>
      )
  }
}

/** 文本内容：文本框 = 遮罩；单行溢出可缩小适应（shrink）或隐藏（overflow）。 */
function TextContent({ e }: { e: TextElement }) {
  const content = elementContent(e)
  const padH = pxv(e.paddingH || 0)
  const padV = pxv(e.paddingV || 0)
  const wPx = Math.max(2, pxv(e.w) - padH * 2)
  const hPx = Math.max(2, pxv(e.h) - padV * 2)
  const [fs, setFs] = useState(Math.max(1, pxv(e.fontH)))
  const textRef = useRef<Konva.Text>(null)

  // 缩小适应：measureSize 循环降字号直到放得下（wrap=true 换行后超高也整体缩小，与 Skia 打印语义一致）。
  // Konva measureSize 返回单行尺寸（与 wrap 设置无关）——wrap 模式须按「单行宽 ÷ 内容宽 = 行数」估算总高。
  useEffect(() => {
    const t = textRef.current
    const base = Math.max(1, pxv(e.fontH))
    if (!t || e.fitMode !== 'shrink') {
      setFs(base)
      return
    }
    const minFs = Math.max(1, pxv(1.5))
    const lineH = e.lineHeight || 1.2
    let f = base
    t.fontSize(f)
    let m = t.measureSize(content)
    const fits = () => {
      if (e.wrap) {
        const lines = Math.max(1, Math.ceil(m.width / Math.max(1, wPx)))
        return lines * lineH * f <= hPx
      }
      return m.width <= wPx && m.height <= hPx
    }
    while (!fits() && f > minFs) {
      f = Math.max(minFs, f - 0.5)
      t.fontSize(f)
      m = t.measureSize(content)
    }
    setFs(f)
  }, [content, wPx, hPx, e.fontH, e.wrap, e.fitMode, e.lineHeight])

  const align = e.align === 'Center' ? 'center' : e.align === 'Right' ? 'right' : 'left'
  const fontSize = e.fitMode === 'overflow' ? Math.max(1, pxv(e.fontH)) : fs

  return (
    <Group x={padH} y={padV} clipX={0} clipY={0} clipWidth={wPx} clipHeight={hPx}>
      <KText
        ref={textRef}
        text={content}
        fontSize={fontSize}
        fontStyle={e.bold ? 'bold' : 'normal'}
        fontFamily={e.fontFamily || 'Microsoft YaHei'}
        fill={e.mode === 'field' && !e.key ? '#999' : '#000'}
        width={wPx}
        height={hPx}
        align={align}
        verticalAlign={e.valign || 'middle'}
        lineHeight={e.lineHeight || 1.2}
        wrap={e.wrap ? (hasCjk(content) ? 'char' : 'word') : 'none'}
        ellipsis={false}
        listening={false}
        strokeScaleEnabled={false}
      />
    </Group>
  )
}

/** 条码 / 二维码图片内容（fit 到内盒居中）。 */
function ImageContent({ e, wPx, hPx, qr }: { e: BarcodeElement | QrCodeElement; wPx: number; hPx: number; qr?: boolean }) {
  const { i18n } = useTranslation('designer')
  const padH = pxv(e.paddingH || 0)
  const padV = pxv(e.paddingV || 0)
  const innerW = Math.max(2, wPx - padH * 2)
  const innerH = Math.max(2, hPx - padV * 2)
  // 依赖含界面语言——占位文案需随语言重建画布（其余情形重建无副作用）
  const canvas = useMemo(
    () => (qr ? makeQrCanvas(e as QrCodeElement, innerW, innerH) : makeBarcodeCanvas(e as BarcodeElement)),
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [e, qr, innerW, innerH, i18n.language],
  )
  const fit = useMemo(() => fitImageRect(canvas, innerW, innerH), [canvas, innerW, innerH])
  return <KImage image={canvas} x={fit.x + padH} y={fit.y + padV} width={fit.w} height={fit.h} listening={false} />
}

/* * 图片元素内容：dataURL 异步解码为 HTMLImageElement 喂 Konva Image，拉伸填满元素盒（与后端渲染同口径）。
 *  三态中缺资源占位仅编辑器内呈现（可辨认、可重新上传修复），预览 / 打印侧才显式报错；
 *  成功分支与空占位分支都渲染整盒 fill 的 Rect——KImage listening={false} 不参与事件命中，命中全靠这个 Rect（「盒即命中」）。 */
function ElementImageContent({ e, wPx, hPx, borderW, asset }: { e: DesignElement; wPx: number; hPx: number; borderW: number; asset?: ImageAsset }) {
  const { t } = useTranslation('designer')
  const [img, setImg] = useState<HTMLImageElement | null>(null)
  const dataUrl = asset?.dataUrl
  useEffect(() => {
    if (!dataUrl) {
      setImg(null)
      return
    }
    let cancelled = false
    const image = new window.Image()
    image.onload = () => {
      if (!cancelled) setImg(image)
    }
    image.onerror = () => {
      if (!cancelled) setImg(null)
    }
    image.src = dataUrl
    return () => {
      cancelled = true
      image.onload = null
      image.onerror = null
    }
  }, [dataUrl])

  const key = (e as { key?: string }).key || ''

  if (img) {
    return (
      <>
        <KImage image={img} x={0} y={0} width={wPx} height={hPx} listening={false} />
        {/* 兜底命中区：fill=transparent 整盒常在（stroke 仅 border>0）——一个 Rect 同时承担边框与命中 */}
        <KRect x={0} y={0} width={wPx} height={hPx} fill="transparent" stroke={e.border > 0 ? '#000' : undefined} strokeWidth={borderW} strokeScaleEnabled={false} />
      </>
    )
  }

  // 空占位与缺资源占位形态一致（虚线灰框），文案分流；整盒 fill 命中保留
  return (
    <>
      <KRect x={0} y={0} width={wPx} height={hPx} fill="#f5f6f8" stroke={e.border > 0 ? '#000' : '#aab4c0'} strokeWidth={borderW} dash={[4, 3]} strokeScaleEnabled={false} />
      <KText x={4} y={4} text={key ? t('canvas.imageNode', { key }) : t('canvas.imageEmpty')} fontSize={11} fontFamily="Microsoft YaHei" fill="#6b7684" listening={false} />
    </>
  )
}
