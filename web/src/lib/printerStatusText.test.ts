// 打印机状态消息展示端翻译单元测试——静态精确映射、参数化模板插值、未命中回退原文、
// zh 词条与后端原文逐字一致（防漂移锚点：后端改文案此文件先红，须同步映射表与词条）。

import { describe, expect, it, afterEach } from 'vitest'
import i18next from '../i18n'
import { printerStatusMessage } from './printerStatusText'

describe('printerStatusMessage：静态消息映射', () => {
  afterEach(() => {
    i18next.changeLanguage('zh-CN')
  })

  it('zh：词条与后端原文一致（防漂移——后端改文案此断言先红）', () => {
    i18next.changeLanguage('zh-CN')
    expect(printerStatusMessage('日志模拟在线。')).toBe('日志模拟在线。')
    expect(printerStatusMessage('缺纸，请装纸。')).toBe('缺纸，请装纸。')
    expect(printerStatusMessage('已暂停，按打印机的暂停键恢复。')).toBe('已暂停，按打印机的暂停键恢复。')
    expect(printerStatusMessage('驱动模式无法读回打印机状态（默认在线）。')).toBe('驱动模式无法读回打印机状态（默认在线）。')
    expect(printerStatusMessage('当前传输不支持状态查询。')).toBe('当前传输不支持状态查询。')
  })

  it('en：静态消息映射为英文词条', () => {
    i18next.changeLanguage('en')
    expect(printerStatusMessage('日志模拟在线。')).toBe('Simulated printing is online.')
    expect(printerStatusMessage('缺纸，请装纸。')).toBe('Paper out; load label stock.')
    expect(printerStatusMessage('打印头未闭合，请合上打印头。')).toBe('Printhead is open; close the printhead.')
    expect(printerStatusMessage('卡纸，请开盖清理后重试。')).toBe('Paper jam; open the cover, clear it and retry.')
    expect(printerStatusMessage('色带已用尽，请更换色带。')).toBe('Ribbon is used up; replace the ribbon.')
    expect(printerStatusMessage('驱动模式无法读回打印机状态（默认在线）。')).toBe('Driver mode cannot read printer status (treated as online).')
    expect(printerStatusMessage('当前传输不支持状态查询。')).toBe('The current transport does not support status queries.')
  })
})

describe('printerStatusMessage：参数化模板', () => {
  afterEach(() => {
    i18next.changeLanguage('zh-CN')
  })

  it('zh：模板消息插值回填与原文一致', () => {
    i18next.changeLanguage('zh-CN')
    expect(printerStatusMessage('状态查询超时（10 秒无响应，192.168.1.50:9100）——打印机可能离线、缺纸或打印头打开。'))
      .toBe('状态查询超时（10 秒无响应，192.168.1.50:9100）——打印机可能离线、缺纸或打印头打开。')
    expect(printerStatusMessage('连接失败：连接被拒绝')).toBe('连接失败：连接被拒绝')
  })

  it('en：超时 / 读取失败 / 响应不完整模板按捕获组插值', () => {
    i18next.changeLanguage('en')
    expect(printerStatusMessage('状态查询超时（10 秒无响应，192.168.1.50:9100）——打印机可能离线、缺纸或打印头打开。'))
      .toBe('Status query timed out (no response for 10 s from 192.168.1.50:9100) — the printer may be offline, out of paper, or the printhead may be open.')
    expect(printerStatusMessage('读取 Zebra 打印机状态失败（USB（自动发现））：boom'))
      .toBe('Failed to read Zebra printer status (USB（自动发现）): boom')
    expect(printerStatusMessage('TSPL 打印机状态无应答（USB:VID_1203）。'))
      .toBe('TSPL printer did not respond to the status query (USB:VID_1203).')
    expect(printerStatusMessage('状态响应不完整，无法解析（首行仅 2 个字段，期望至少 3 个）。'))
      .toBe('Status response is incomplete and cannot be parsed (first line has only 2 fields, expected at least 3).')
  })

  it('未命中消息回退原文；空值透传由调用方走「无补充信息」词条', () => {
    i18next.changeLanguage('en')
    expect(printerStatusMessage('厂商私有错误 xyz')).toBe('厂商私有错误 xyz')
    expect(printerStatusMessage('  缺纸，请装纸。  ')).toBe('Paper out; load label stock.')
    expect(printerStatusMessage(null)).toBe(null)
    expect(printerStatusMessage(undefined)).toBe(null)
    expect(printerStatusMessage('')).toBe('')
  })
})
