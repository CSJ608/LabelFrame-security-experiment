// 条码 / 二维码 canvas 渲染（JsBarcode / qrcode-generator，与原型一致）

import JsBarcode from 'jsbarcode'
import qrcode from 'qrcode-generator'
import { pxv } from '../design/geometry'
import type { BarcodeElement, QrCodeElement } from '../design/types'
import { elementContent, elementContentState } from '../design/types'
import i18next from '../../i18n'

/** 未绑定占位：虚线框 + 提示文字（元素可见性提示）。 */
export function makePlaceholderCanvas(wMm: number, hMm: number, text: string): HTMLCanvasElement {
  const c = document.createElement('canvas')
  c.width = Math.max(100, pxv(wMm))
  c.height = Math.max(40, pxv(hMm))
  const ctx = c.getContext('2d')
  if (ctx) {
    ctx.strokeStyle = '#9ab3d6'
    ctx.setLineDash([6, 4])
    ctx.strokeRect(1, 1, c.width - 2, c.height - 2)
    ctx.fillStyle = '#7a8490'
    ctx.font = '14px "Microsoft YaHei"'
    ctx.textAlign = 'center'
    ctx.textBaseline = 'middle'
    ctx.fillText(text, c.width / 2, c.height / 2)
  }
  return c
}

/* * 条码 canvas（fit 到元素内盒，居中）。：占位判别走语义态、文案随界面语言。 */
export function makeBarcodeCanvas(e: BarcodeElement): HTMLCanvasElement {
  const state = elementContentState(e)
  if (state === 'literal-empty' || state === 'unbound-field') return makePlaceholderCanvas(e.w, e.h, i18next.t('designer:canvas.barcodePlaceholder'))
  const content = elementContent(e)
  const c = document.createElement('canvas')
  try {
    JsBarcode(c, content, {
      format: e.barcodeFormat || 'CODE128',
      displayValue: e.displayValue !== false,
      width: Math.max(1, e.moduleWidth || 1),
      height: Math.max(10, Math.min(80, pxv(e.h || 20) * 0.85)),
      margin: 0,
      background: 'transparent',
    })
  } catch {
    const ctx = c.getContext('2d')
    if (ctx) {
      ctx.fillStyle = '#f0f1f3'
      ctx.fillRect(0, 0, 10, 10)
    }
  }
  return c
}

/* * 二维码 canvas（同步模块绘制，稳定可见）。：占位判别走语义态、文案随界面语言。 */
export function makeQrCanvas(e: QrCodeElement, wMm: number, hMm: number): HTMLCanvasElement {
  const state = elementContentState(e)
  if (state === 'literal-empty' || state === 'unbound-field') return makePlaceholderCanvas(wMm, hMm, i18next.t('designer:canvas.qrPlaceholder'))
  const content = elementContent(e)
  const c = document.createElement('canvas')
  try {
    const qr = qrcode(0, e.qrEcc || 'M')
    qr.addData(content)
    qr.make()
    const count = qr.getModuleCount()
    const cell = 4
    const margin = (e.qrMargin == null ? 2 : e.qrMargin) * cell
    c.width = count * cell + margin * 2
    c.height = count * cell + margin * 2
    const ctx = c.getContext('2d')
    if (ctx) {
      ctx.fillStyle = '#ffffff'
      ctx.fillRect(0, 0, c.width, c.height)
      ctx.fillStyle = '#000000'
      for (let r = 0; r < count; r++) {
        for (let col = 0; col < count; col++) {
          if (qr.isDark(r, col)) ctx.fillRect(margin + col * cell, margin + r * cell, cell, cell)
        }
      }
    }
  } catch {
    c.width = Math.max(60, pxv(wMm))
    c.height = Math.max(60, pxv(hMm))
    const ctx = c.getContext('2d')
    if (ctx) {
      ctx.fillStyle = '#f0f1f3'
      ctx.fillRect(0, 0, c.width, c.height)
    }
  }
  return c
}

/** 图片节点等比适配内盒（fit 后居中）。 */
export function fitImageRect(canvas: HTMLCanvasElement, boxW: number, boxH: number): { x: number; y: number; w: number; h: number } {
  const iw = canvas.width
  const ih = canvas.height
  if (!iw || !ih) return { x: 0, y: 0, w: boxW, h: boxH }
  const fit = Math.min(boxW / iw, boxH / ih)
  return { x: (boxW - iw * fit) / 2, y: (boxH - ih * fit) / 2, w: iw * fit, h: ih * fit }
}
