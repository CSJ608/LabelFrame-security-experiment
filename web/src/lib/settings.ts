// 设置持久化（sessionStorage 之外的浏览器存储）：服务端地址 localStorage 兜底。
// （F2）：serverBase 优先级 = 机器级配置（GET /api/host/config）> localStorage 兜底 > 默认 127.0.0.1:53961
// 移除方案 B 残留检测（比较基准已随 DEFAULT_BASE_URL 变更失效）。
// 显式 window.localStorage + typeof 守卫：Node 26 实验性全局 localStorage 会遮蔽 jsdom 注入版。

import { DEFAULT_BASE_URL } from './api/types'

const KEY = 'labelframe.baseUrl'

function getLocalStorage(): Storage | null {
  try {
    return typeof window !== 'undefined' ? window.localStorage : null
  } catch {
    return null
  }
}

/** localStorage 兜底的服务端地址（无存储值返回默认 127.0.0.1:53961）。 */
export function getBaseUrl(): string {
  const storage = getLocalStorage()
  const v = storage ? storage.getItem(KEY) : null
  if (v && v.trim()) {
    return v.trim().replace(/\/+$/, '')
  }
  return DEFAULT_BASE_URL
}

export function setBaseUrl(url: string): void {
  const storage = getLocalStorage()
  if (storage) storage.setItem(KEY, url.trim().replace(/\/+$/, ''))
}
