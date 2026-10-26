import { describe, expect, it } from 'vitest'
// 清单为函数式（shortcutGroups，调用期取当前界面语言）——zh-CN 断言值不变（vitest.setup 钉住首启语言 zh-CN）。
import { shortcutGroups } from './shortcuts'

describe('设计器快捷键清单', () => {
  it('分组建表且每项有键位与说明', () => {
    const groups = shortcutGroups()
    expect(groups.length).toBeGreaterThanOrEqual(3)
    for (const g of groups) {
      expect(g.title.length).toBeGreaterThan(0)
      expect(g.items.length).toBeGreaterThan(0)
      for (const item of g.items) {
        expect(item.keys.length).toBeGreaterThan(0)
        expect(item.desc.length).toBeGreaterThan(0)
      }
    }
  })

  it('键位条目不重复', () => {
    const all = shortcutGroups().flatMap((g) => g.items.flatMap((i) => i.keys))
    expect(new Set(all).size).toBe(all.length)
  })

  it('核心键位（Ctrl+Z / Ctrl+C / Ctrl+S / Delete）都在完整清单中', () => {
    const all = shortcutGroups().flatMap((g) => g.items.flatMap((i) => i.keys))
    expect(all).toContain('Ctrl+Z')
    expect(all).toContain('Ctrl+C')
    expect(all).toContain('Ctrl+S')
    expect(all).toContain('Delete')
  })
})
