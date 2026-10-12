// LF_* 错误码码表测试：
// 覆盖断言：解析后端错误码注册表源码
//    提取码字面量，断言 zh-CN + en 两份码表全覆盖且无多余死码——新增码漏表即红（删一条码表本测试同样变红）；
// 展示端翻译行为：en 已知码按码表插值渲染、未知码 / 参数不全回退后端中文、zh-CN 直用后端 message。
// 取证说明：本断言的失败形态 = 逐条列出缺失 / 多余码后 expect 失败。

import { readFileSync } from 'node:fs'
import { afterEach, describe, expect, it } from 'vitest'
import zhCNErrorCodes from './locales/zh-CN/errorCodes.json'
import enErrorCodes from './locales/en/errorCodes.json'
import { changeLocale, currentLocale } from './index'
import { resolveApiErrorMessage } from './errorMessages'

/* * 错误码注册表（后端源码即权威；新增码须同步两份码表）。 */
const REGISTRIES: { name: string; file: string }[] = [
  { name: 'ApiErrorCodes（LabelFrame.Api）', file: '../../../src/LabelFrame.Api/ApiErrorCodes.cs' },
  { name: 'ServerErrorCodes（LabelFrame.Server）', file: '../../../src/LabelFrame.Server/ServerErrorCodes.cs' },
  { name: 'JobErrorCodes（LabelFrame.Core，WinHost 直连模式经 ErrorView 同表暴露）', file: '../../../src/LabelFrame.Core/Jobs/JobErrorCodes.cs' },
]

/** 解析注册表源码中的码字面量（`"LF_XXX"`；注释中的约定说明不在引号内，不会误提取）。 */
function extractRegistryCodes(file: string): Set<string> {
  const source = readFileSync(new URL(file, import.meta.url), 'utf8')
  return new Set([...source.matchAll(/"(LF_[A-Z0-9_]+)"/g)].map((m) => m[1]))
}

describe('码表覆盖注册表全部错误码（AC-05）', () => {
  const registryCodes = new Set<string>(REGISTRIES.flatMap((r) => [...extractRegistryCodes(r.file)]))

  it('注册表源码解析非空（防文件路径失效后空集合假绿）', () => {
    expect(registryCodes.size).toBeGreaterThanOrEqual(20)
    for (const registry of REGISTRIES) {
      expect(extractRegistryCodes(registry.file).size, `${registry.name} 解析为空`).toBeGreaterThan(0)
    }
  })

  it.each([
    ['zh-CN', zhCNErrorCodes],
    ['en', enErrorCodes],
  ] as const)('%s 码表覆盖全部注册码且无死码（删一条即红）', (_locale, table) => {
    const tableKeys = new Set(Object.keys(table))
    expect(tableKeys.size).toBeGreaterThan(0) // 码表不为空（防误删整包后空集合假绿）
    const missing = [...registryCodes].filter((code) => !tableKeys.has(code)).sort()
    const extra = [...tableKeys].filter((code) => !registryCodes.has(code)).sort()
    expect(
      { missingCodes: missing, extraCodes: extra },
      `注册表 ↔ 码表不一致：缺 ${JSON.stringify(missing)}，多 ${JSON.stringify(extra)}`,
    ).toEqual({ missingCodes: [], extraCodes: [] })
  })

  it('码表插值槽统一为双花括号 {{key}}（与后端 params 键名同名对接，无单花括号残缺）', () => {
    for (const [code, template] of Object.entries(enErrorCodes)) {
      expect(
        (template as string).match(/(^|[^{])\{[A-Za-z0-9_]+\}(?!\})/),
        `${code} 存在单花括号占位符：${template}`,
      ).toBeNull()
    }
  })
})

describe('错误码展示端翻译（AC-04）', () => {
  const originalLocale = currentLocale()

  afterEach(() => {
    changeLocale(originalLocale)
  })

  it('en + 已知码 + params 齐全：按 en 码表模板插值渲染', () => {
    changeLocale('en')
    expect(
      resolveApiErrorMessage('LF_SRV_012', { ip: '10.0.0.9' }, '按 IP 未找到设备：10.0.0.9。'),
    ).toBe('No device found for IP: 10.0.0.9.')
    expect(
      resolveApiErrorMessage('LF_SRV_001', { deviceId: 'dev-1' }, '设备未注册：dev-1。'),
    ).toBe('Device not registered: dev-1.')
    expect(
      resolveApiErrorMessage('LF_SRV_003', { jobId: 'job-7' }, '作业不存在：job-7。'),
    ).toBe('Job not found: job-7.')
  })

  it('en + 已知码 + 无参模板：渲染码表文案', () => {
    changeLocale('en')
    expect(resolveApiErrorMessage('LF_INTERNAL_001', undefined, '服务器内部错误，请查看服务端日志。')).toBe(
      'Internal server error. Check the server logs.',
    )
  })

  it('en + 已知码但 params 缺失（无参消息变体同码）：回退后端中文 message，不出现裸 {{key}}', () => {
    changeLocale('en')
    // LF_PLUGIN_INVALID 的码表模板带 {{detail}}；WinHost 端点变体不带 params → 回退后端原文
    const resolved = resolveApiErrorMessage('LF_PLUGIN_INVALID', undefined, '插件包无效，无法完成安装。')
    expect(resolved).toBe('插件包无效，无法完成安装。')
    expect(resolved).not.toContain('{{')
  })

  it('en + 未知码（后端新码漏表 / 临时假码）：回退后端中文 message', () => {
    changeLocale('en')
    expect(resolveApiErrorMessage('LF_FAKE_999', undefined, '未知错误原文。')).toBe('未知错误原文。')
    // 前端自造码（超时 / 网络不可达）不在码表内，同样维持前端文案
    expect(resolveApiErrorMessage('TIMEOUT', undefined, '请求超时（服务端超过 30 秒未响应）……')).toBe(
      '请求超时（服务端超过 30 秒未响应）……',
    )
  })

  it('zh-CN：已知码也直用后端 message（中文权威在后端）', () => {
    changeLocale('zh-CN')
    expect(resolveApiErrorMessage('LF_SRV_012', { ip: '10.0.0.9' }, '按 IP 未找到设备：10.0.0.9。')).toBe(
      '按 IP 未找到设备：10.0.0.9。',
    )
  })

  it('code 缺省：直接回退传入文案', () => {
    changeLocale('en')
    expect(resolveApiErrorMessage(undefined, undefined, '回退原文。')).toBe('回退原文。')
  })
})
