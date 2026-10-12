// @vitest-environment jsdom
// settings 单测：serverBase 兜底 = localStorage > 默认 127.0.0.1:53961。
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { DEFAULT_BASE_URL } from './api/types'
import { getBaseUrl, setBaseUrl } from './settings'

const KEY = 'labelframe.baseUrl'

beforeEach(() => {
  localStorage.removeItem(KEY)
})

afterEach(() => {
  vi.unstubAllGlobals()
  localStorage.removeItem(KEY)
})

describe('getBaseUrl（localStorage 兜底，默认服务端地址）', () => {
  it('无存储值 → 默认 127.0.0.1:53961（与页面来源无关）', () => {
    expect(getBaseUrl()).toBe(DEFAULT_BASE_URL)
    expect(DEFAULT_BASE_URL).toBe('http://127.0.0.1:53961')
  })

  it('有存储值 → 返回存储值（机器级配置不可用时的兜底）', () => {
    setBaseUrl('http://192.168.1.3:53961')
    expect(getBaseUrl()).toBe('http://192.168.1.3:53961')
  })

  it('存储值等于默认值（旧版残留 53960 已不是默认）→ 原样返回存储值', () => {
    // 方案 B 已移除：非本机来源不再忽略旧默认值
    setBaseUrl('http://127.0.0.1:53960')
    expect(getBaseUrl()).toBe('http://127.0.0.1:53960')
  })

  it('无 window（Node）→ 回退 DEFAULT_BASE_URL', () => {
    vi.stubGlobal('window', undefined)
    expect(getBaseUrl()).toBe(DEFAULT_BASE_URL)
  })

  it('尾部 / 归一化', () => {
    setBaseUrl('http://192.168.1.3:53961/')
    expect(getBaseUrl()).toBe('http://192.168.1.3:53961')
  })
})
