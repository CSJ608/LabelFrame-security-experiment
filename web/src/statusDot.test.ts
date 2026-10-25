import { readFileSync } from 'node:fs'
import { describe, expect, it } from 'vitest'

const css = readFileSync(new URL('./styles.css', import.meta.url), 'utf8')

describe('连接状态灯样式', () => {
  it('保留在线颜色与尺寸，不运行持续动画', () => {
    const base = css.match(/\.status-dot\s*\{([^}]+)\}/)?.[1]
    const online = css.match(/\.status-dot\.on\s*\{([^}]+)\}/)?.[1]
    expect(base).toContain('width: 9px; height: 9px;')
    expect(base).toContain('background: var(--danger)')
    expect(online).toContain('background: var(--ok)')
    expect(base).not.toMatch(/animation\s*:/)
    expect(online).not.toMatch(/animation\s*:/)
    expect(css).not.toMatch(/@keyframes\s+breathe\b/)
  })

  it('保留缩略图加载提示动画', () => {
    expect(css).toMatch(/\.preview-thumb-skel\s*\{[^}]*animation:\s*previewSkel/)
    expect(css).toMatch(/@keyframes\s+previewSkel\b/)
  })
})
