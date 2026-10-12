// @vitest-environment jsdom
// 品牌 v02（client 分支）：导航 LabelLogo 换「剥离 / Peel & Feed」微型几何——
// 24px 方形 viewBox、深青 #123B3C 标签体 + 珊瑚 #D96C4F 剥离角（母版 assets/brand/symbol-color-24.svg，
// 固定填充不走 currentColor/--accent，--accent 测量蓝是界面语言非品牌标识）；
// index.html favicon 外链最小集（拍板：favicon.svg + favicon.ico + apple-touch-icon.png，去 data URI）。

import { describe, expect, it } from 'vitest'
import { render } from '@testing-library/react'
import { LabelLogo } from './Icon'
// jsdom 环境 import.meta.url 非 file 协议（node:fs readFileSync 不可用），?raw 编译期内联源码
import indexHtml from '../../index.html?raw'

describe('LabelLogo 品牌 v02 微型几何（AC-05）', () => {
  it('24px 方形 viewBox，深青标签体 + 珊瑚剥离角固定填充（不再走 currentColor / --accent）', () => {
    const { container } = render(<LabelLogo />)
    const svg = container.querySelector('svg')
    expect(svg).not.toBeNull()
    expect(svg?.getAttribute('viewBox')).toBe('0 0 24 24')
    expect(svg?.getAttribute('width')).toBe('24')
    expect(svg?.getAttribute('height')).toBe('24')

    // 旧标（虚线标签纸 + 条码线，stroke currentColor）不应残存
    expect(svg?.querySelector('rect')).toBeNull()
    const paths = Array.from(svg?.querySelectorAll('path') ?? [])
    expect(paths).toHaveLength(2)
    expect(paths[0]?.getAttribute('fill')).toBe('#123B3C')
    expect(paths[1]?.getAttribute('fill')).toBe('#D96C4F')
    for (const path of paths) {
      expect(path.getAttribute('stroke')).toBeNull()
      expect(path.getAttribute('fill')).not.toBe('none')
    }
  })

  it('size 按方形几何等比缩放（width = height = size）', () => {
    const { container } = render(<LabelLogo size={32} />)
    const svg = container.querySelector('svg')
    expect(svg?.getAttribute('width')).toBe('32')
    expect(svg?.getAttribute('height')).toBe('32')
  })
})

describe('index.html favicon 外链最小集（AC-05，拍板①）', () => {
  const html = indexHtml

  it('三件外链齐全：favicon.svg 主标 + favicon.ico alternate + apple-touch-icon', () => {
    expect(html).toContain('<link rel="icon" href="favicon.svg" type="image/svg+xml" />')
    expect(html).toContain('<link rel="alternate icon" href="favicon.ico" />')
    expect(html).toContain('<link rel="apple-touch-icon" href="apple-touch-icon.png" />')
  })

  it('旧 data URI 内嵌 favicon 已移除（外链形态规避浏览器缓存旧标）', () => {
    expect(html).not.toContain('data:image/svg+xml')
  })
})
