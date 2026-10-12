// 语言包 key 覆盖断言（「CI 防线」）：
// en 语言包必须覆盖 zh-CN（源语言）的全部 key——缺失即红，防翻译欠账静默回落中文。
// 反向（en 多出的 key）同样报错——死词条应随 key 下线一并清理。
// 取证说明：本断言的失败形态 = 逐条列出缺失 / 多余 key 路径后 expect 失败；
// PR 内证据为本地演示（删一条 en 词条 → 本测试变红 → 恢复）的输出摘录。

import { describe, expect, it } from 'vitest'
import zhCNCommon from './locales/zh-CN/common.json'
import zhCNShell from './locales/zh-CN/shell.json'
import zhCNSettings from './locales/zh-CN/settings.json'
import zhCNErrorCodes from './locales/zh-CN/errorCodes.json'
import zhCNWorkbench from './locales/zh-CN/workbench.json'
import zhCNJobHistory from './locales/zh-CN/jobHistory.json'
import zhCNDataPrint from './locales/zh-CN/dataPrint.json'
import zhCNDesigner from './locales/zh-CN/designer.json'
import zhCNDownloadCenter from './locales/zh-CN/downloadCenter.json'
import zhCNDevices from './locales/zh-CN/devices.json'
import zhCNPluginPackages from './locales/zh-CN/pluginPackages.json'
import zhCNGuide from './locales/zh-CN/guide.json'
import zhCNHelp from './locales/zh-CN/help.json'
import enCommon from './locales/en/common.json'
import enShell from './locales/en/shell.json'
import enSettings from './locales/en/settings.json'
import enErrorCodes from './locales/en/errorCodes.json'
import enWorkbench from './locales/en/workbench.json'
import enJobHistory from './locales/en/jobHistory.json'
import enDataPrint from './locales/en/dataPrint.json'
import enDesigner from './locales/en/designer.json'
import enDownloadCenter from './locales/en/downloadCenter.json'
import enDevices from './locales/en/devices.json'
import enPluginPackages from './locales/en/pluginPackages.json'
import enGuide from './locales/en/guide.json'
import enHelp from './locales/en/help.json'

/** 展平嵌套 JSON 为 `a.b.c` 全量 key 路径集合。 */
function flattenKeys(obj: unknown, prefix = ''): Set<string> {
  const keys = new Set<string>()
  if (obj === null || typeof obj !== 'object' || Array.isArray(obj)) {
    // 语言包约定为纯嵌套对象（叶子字符串）；数组 / 标量不是合法层级，按不可展开处理
    if (prefix) keys.add(prefix)
    return keys
  }
  for (const [k, v] of Object.entries(obj as Record<string, unknown>)) {
    const path = prefix ? `${prefix}.${k}` : k
    if (v !== null && typeof v === 'object') {
      for (const sub of flattenKeys(v, path)) keys.add(sub)
    } else {
      keys.add(path)
    }
  }
  return keys
}

const NAMESPACES: Record<string, { zh: unknown; en: unknown }> = {
  common: { zh: zhCNCommon, en: enCommon },
  shell: { zh: zhCNShell, en: enShell },
  settings: { zh: zhCNSettings, en: enSettings },
  errorCodes: { zh: zhCNErrorCodes, en: enErrorCodes },
  workbench: { zh: zhCNWorkbench, en: enWorkbench },
  jobHistory: { zh: zhCNJobHistory, en: enJobHistory },
  dataPrint: { zh: zhCNDataPrint, en: enDataPrint },
  designer: { zh: zhCNDesigner, en: enDesigner },
  downloadCenter: { zh: zhCNDownloadCenter, en: enDownloadCenter },
  devices: { zh: zhCNDevices, en: enDevices },
  pluginPackages: { zh: zhCNPluginPackages, en: enPluginPackages },
  guide: { zh: zhCNGuide, en: enGuide },
  help: { zh: zhCNHelp, en: enHelp },
}

describe('语言包 en 覆盖 zh-CN 全部 key（AC-04）', () => {
  for (const [ns, { zh, en }] of Object.entries(NAMESPACES)) {
    it(`${ns}：en 与 zh-CN 的 key 集合完全一致（缺失即红）`, () => {
      const zhKeys = flattenKeys(zh)
      const enKeys = flattenKeys(en)
      expect(zhKeys.size).toBeGreaterThan(0) // 域文件不为空（防误删整包后空集合假绿）
      const missing = [...zhKeys].filter((k) => !enKeys.has(k))
      const extra = [...enKeys].filter((k) => !zhKeys.has(k))
      expect(
        {
          missingEnKeys: missing,
          extraEnKeys: extra,
        },
        `${ns} 域语言包 key 不一致：en 缺 ${JSON.stringify(missing)}，en 多 ${JSON.stringify(extra)}`,
      ).toEqual({ missingEnKeys: [], extraEnKeys: [] })
    })
  }
})
