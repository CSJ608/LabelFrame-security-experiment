// 插件包 64MB 上限预检纯函数（与后端 PluginPackageLimits.MaxBytes=64MB 一致）。
// 组件测试不便构造 64MB+ 的 File，阈值判定在此单测覆盖；组件内 mock 返回控制。

import { describe, expect, it } from 'vitest'
import { PLUGIN_PACKAGE_MAX_BYTES, pluginPackageTooLarge } from './pluginLimits'

describe('pluginPackageTooLarge（迭代 23）', () => {
  it('恰好 64MB：不拦截（后端业务检查同为 > 上限才拒绝；multipart 边界 413 由 Kestrel 兜底）', () => {
    expect(pluginPackageTooLarge(PLUGIN_PACKAGE_MAX_BYTES)).toBeNull()
  })

  it('超过 64MB：返回中文提示', () => {
    expect(pluginPackageTooLarge(PLUGIN_PACKAGE_MAX_BYTES + 1)).toContain('64MB')
  })

  it('远小于上限 / 0：不拦截', () => {
    expect(pluginPackageTooLarge(2048)).toBeNull()
    expect(pluginPackageTooLarge(0)).toBeNull()
  })

  it('非法值（NaN / Infinity）：拦截提示；负数视为未知不拦截', () => {
    expect(pluginPackageTooLarge(Number.NaN)).not.toBeNull()
    expect(pluginPackageTooLarge(Number.POSITIVE_INFINITY)).not.toBeNull()
    expect(pluginPackageTooLarge(-1)).toBeNull()
  })
})
