// 检查更新辅助：版本解析 / 比较 / 结论三态。

import { describe, expect, it } from 'vitest'
import { checkForUpdate, compareVersions, parseClientPackageVersion, pickLatestPackageVersion } from './update'

describe('parseClientPackageVersion（发版命名事实 LabelFrame-Client-<版本>.msi）', () => {
  it.each([
    ['LabelFrame-Client-0.27.0.msi', '0.27.0'],
    ['labelframe-client-0.27.0.msi', '0.27.0'], // 大小写不敏感
    ['LabelFrame-Client-0.27.msi', '0.27'],
    ['LabelFrame-Client-1.2.3.4.msi', '1.2.3.4'],
  ])('%s → %s', (fileName, expected) => {
    expect(parseClientPackageVersion(fileName)).toBe(expected)
  })

  it.each([
    ['LabelFrame.Client-0.27.0.msi'], // 命名不符（点号）
    ['LabelFrame-Client-linux.zip'], // 非客户端 MSI
    ['客户端安装包.zip'],
    ['LabelFrame-Client-dev.msi'], // 非数值版本段
  ])('%s → null（不参与比较，无误导）', (fileName) => {
    expect(parseClientPackageVersion(fileName)).toBeNull()
  })
})

describe('compareVersions（§6.11 VersionSemantics 前端等价：段缺失视为 0）', () => {
  it.each([
    ['0.26.0', '0.27.0', -1],
    ['0.27.0', '0.26.0', 1],
    ['0.27', '0.27.0', 0],
    ['2.0.0', '10.0.0', -1], // 数值比较（非字符串序）
    ['1.0', '1.0', 0],
  ])('%s vs %s → %d', (left, right, expected) => {
    expect(Math.sign(compareVersions(left, right))).toBe(expected)
  })
})

describe('pickLatestPackageVersion（列表取最高客户端包版本）', () => {
  it('多版本取最高', () => {
    expect(
      pickLatestPackageVersion(['LabelFrame-Client-0.26.0.msi', 'LabelFrame-Client-0.28.0.msi', 'LabelFrame-Client-0.27.0.msi']),
    ).toBe('0.28.0')
  })

  it('混合不可解析文件只比较可解析项', () => {
    expect(pickLatestPackageVersion(['客户端安装包.zip', 'LabelFrame-Client-0.26.0.msi'])).toBe('0.26.0')
  })

  it('无可解析包返回 null', () => {
    expect(pickLatestPackageVersion(['a.zip', 'b.msi'])).toBeNull()
  })
})

describe('checkForUpdate（结论三态）', () => {
  it('有新版', () => {
    expect(checkForUpdate('0.26.0', ['LabelFrame-Client-0.27.0.msi'])).toEqual({
      kind: 'update-available',
      latestVersion: '0.27.0',
      localVersion: '0.26.0',
    })
  })

  it('已是最新（同版与更高本机版均不算更新）', () => {
    expect(checkForUpdate('0.27.0', ['LabelFrame-Client-0.27.0.msi'])).toEqual({
      kind: 'up-to-date',
      localVersion: '0.27.0',
    })
    expect(checkForUpdate('0.28.0', ['LabelFrame-Client-0.27.0.msi'])).toEqual({
      kind: 'up-to-date',
      localVersion: '0.28.0',
    })
  })

  it('本机版本未知 / 无可解析包 → unknown', () => {
    expect(checkForUpdate(null, ['LabelFrame-Client-0.27.0.msi']).kind).toBe('unknown')
    expect(checkForUpdate(undefined, ['x.zip']).kind).toBe('unknown')
    expect(checkForUpdate('0.26.0', ['x.zip']).kind).toBe('unknown')
  })
})
