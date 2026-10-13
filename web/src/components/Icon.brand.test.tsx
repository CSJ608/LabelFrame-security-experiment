// @vitest-environment jsdom
// 品牌承形色重设计：导航 LabelLogo「剥离 / Peel & Feed」微型几何换承形色——
// 24px 方形 viewBox、珊瑚 #C9553B 标签体 + 深青 #123B3C 剥离角（母版 assets/brand/symbol-color-24.svg，
// 固定填充不走 currentColor/--accent，--accent 测量蓝是界面语言非品牌标识；珊瑚在深浅背景
// 对比度均 ≥3.5:1，无需主题切换）；index.html favicon 外链最小集不变化。

import { describe, expect, it } from 'vitest'
import { render } from '@testing-library/react'
import { LabelLogo } from './Icon'
// jsdom 环境 import.meta.url 非 file 协议（node:fs readFileSync 不可用），?raw 编译期内联源码
import indexHtml from '../../index.html?raw'

describe('LabelLogo 品牌微型几何（承形色重设计）', () => {
  it('24px 方形 viewBox，珊瑚标签体 + 深青剥离角固定填充（不走 currentColor / --accent）', () => {
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
    expect(paths[0]?.getAttribute('fill')).toBe('#C9553B')
    expect(paths[1]?.getAttribute('fill')).toBe('#123B3C')
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

describe('index.html favicon 外链最小集', () => {
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
