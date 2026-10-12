// 工作台模板预览缓存：会话内「模板名 → blob URL」缓存 + 在途去重 + 周期失效；
// 复用既有 POST /api/templates/{name}/preview（按模板 TestData 渲染 PNG），零契约变更。

import { useCallback, useEffect, useMemo, useReducer, useRef } from 'react'
import i18next from '../i18n'

export type TemplatePreviewEntry =
  | { status: 'loading' }
  | { status: 'ready'; url: string }
  | { status: 'error'; message: string }

function releaseAll(cache: Map<string, TemplatePreviewEntry>): void {
  for (const entry of cache.values()) {
    if (entry.status === 'ready') URL.revokeObjectURL(entry.url)
  }
}

/**
 * 会话内预览缓存：列表周期内每个模板只请求一次（含在途去重与失败缓存）；
 * 刷新周期（invalidate，随列表重新 load 调用）后整体失效并释放全部 blob URL；卸载时兜底释放。
 */
export function useTemplatePreviewCache(
  fetchPreview: (name: string) => Promise<{ blob: Blob }>,
): {
  get: (name: string) => TemplatePreviewEntry | undefined
  ensure: (name: string) => void
  invalidate: () => void
} {
  const cacheRef = useRef(new Map<string, TemplatePreviewEntry>())
  /** 缓存周期代数：invalidate 递增；在途请求回来时若已换代则丢弃（防旧结果写入新周期）。 */
  const genRef = useRef(0)
  const fetchRef = useRef(fetchPreview)
  fetchRef.current = fetchPreview
  const [, bump] = useReducer((n: number) => n + 1, 0)

  const ensure = useCallback((name: string) => {
    const cache = cacheRef.current
    if (cache.has(name)) return
    const gen = genRef.current
    cache.set(name, { status: 'loading' })
    bump()
    void fetchRef
      .current(name)
      .then(({ blob }) => {
        if (gen !== genRef.current) return // 列表已刷新：本次结果属于旧周期，直接丢弃
        cache.set(name, { status: 'ready', url: URL.createObjectURL(blob) })
      })
      .catch((err: unknown) => {
        if (gen !== genRef.current) return
        cache.set(name, { status: 'error', message: err instanceof Error ? err.message : i18next.t('api.previewFailed') })
      })
      .finally(() => {
        if (gen === genRef.current) bump()
      })
  }, [])

  const invalidate = useCallback(() => {
    releaseAll(cacheRef.current)
    cacheRef.current = new Map()
    genRef.current += 1
    bump()
  }, [])

  // 卸载兜底：释放本周期全部 blob URL（防泄漏）；cacheRef 为稳定引用
  useEffect(() => {
    const cache = cacheRef
    return () => releaseAll(cache.current)
  }, [])

  const get = useCallback((name: string) => cacheRef.current.get(name), [])

  return useMemo(() => ({ get, ensure, invalidate }), [get, ensure, invalidate])
}
