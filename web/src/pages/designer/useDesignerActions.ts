// 元素操作编排 hook：选择 / 增删改 / 对齐 / 图层 / 撤销重做 / 复制粘贴 / 设计 JSON 导入导出＋快捷键分发。
// 回调体与依赖数组自 Designer.tsx 逐字搬移（纯重构零行为变化）——共享 ref 由页面壳传入。

import { useCallback, useEffect, useRef } from 'react'
import type { Dispatch, RefObject, SetStateAction } from 'react'
import { useTranslation } from 'react-i18next'
import type { DesignState } from './CanvasViewport'
import { useApp } from '../../state/AppContext'
import type { DesignElement } from '../../lib/design/types'
import { cloneElement, defaultElement } from '../../lib/design/types'
import type { DesignerHistory } from './designState'
import { r2 } from '../../lib/design/geometry'
import { exportDesign, parseDesign } from '../../lib/design/format'
import { capturePasteOnce, copyText, readClipboardText } from '../../lib/clipboard'
import i18next from '../../i18n'

export interface UseDesignerActionsOptions {
  stateRef: RefObject<DesignState | null>
  historyRef: RefObject<DesignerHistory | null>
  selectedRef: RefObject<string[]>
  setState: Dispatch<SetStateAction<DesignState | null>>
  setSelected: Dispatch<SetStateAction<string[]>>
  setPendingType: Dispatch<SetStateAction<string | null>>
  commit: (next: DesignState) => void
  viewMode: 'fit' | 'preview'
  pendingType: string | null
  /** 演示期间快捷键全停（演示锚点由 useHelpLinkDraft 持有，页面壳传入）。 */
  demoActive: boolean
}

/** 控件类型显示名（状态栏「已添加」消息用；随界面语言）。 */
function elementTypeName(type: string): string {
  switch (type) {
    case 'Barcode': return i18next.t('designer:type.barcode')
    case 'QrCode': return i18next.t('designer:type.qrcode')
    case 'Rect': return i18next.t('designer:type.rect')
    case 'Image': return i18next.t('designer:type.image')
    default: return i18next.t('designer:type.text')
  }
}

export function useDesignerActions({ stateRef, historyRef, selectedRef, setState, setSelected, setPendingType, commit, viewMode, pendingType, demoActive }: UseDesignerActionsOptions) {
  const app = useApp()
  const { t } = useTranslation('designer')

  // ---------- 选择 ----------
  const handleSelect = useCallback((ids: string[], toggle?: boolean) => {
    setSelected((prev) => {
      const next = toggle ? ids.filter((id) => !prev.includes(id)).concat(prev.filter((id) => !ids.includes(id))) : ids
      selectedRef.current = next
      return next
    })
  }, [])

  // ---------- 元素操作 ----------
  const addElementAt = useCallback(
    (type: string, xMm: number, yMm: number) => {
      const s = stateRef.current
      if (!s) return
      const e = defaultElement(type as 'Text' | 'Barcode' | 'QrCode' | 'Rect' | 'Image') as DesignElement
      e.x = Math.max(0, Math.min(s.paperW - 2, r2(xMm)))
      e.y = Math.max(0, Math.min(s.paperH - 2, r2(yMm)))
      const next = { ...s, elements: [...s.elements, e] }
      commit(next)
      setSelected([e.id])
      selectedRef.current = [e.id]
      setPendingType(null)
      app.setStatus(t('status.added', { type: elementTypeName(type) }))
    },
    [app, commit, t],
  )

  const changeElement = useCallback(
    (id: string, patch: Partial<DesignElement>) => {
      const s = stateRef.current
      if (!s) return
      commit({ ...s, elements: s.elements.map((e) => (e.id === id ? ({ ...e, ...patch } as DesignElement) : e)) })
    },
    [commit],
  )

  const deleteElements = useCallback(
    (ids: string[]) => {
      const s = stateRef.current
      if (!s || ids.length === 0) return
      const next = { ...s, elements: s.elements.filter((e) => !ids.includes(e.id)) }
      commit(next)
      setSelected([])
      selectedRef.current = []
      app.setStatus(t('status.deleted', { count: ids.length }))
    },
    [app, commit, t],
  )

  const alignSelected = useCallback(
    (align: 'left' | 'centerH' | 'right' | 'top' | 'centerV' | 'bottom') => {
      const s = stateRef.current
      if (!s) return
      const sel = s.elements.filter((e) => selectedRef.current.includes(e.id) && e.type !== 'Region')
      if (sel.length < 2) {
        app.setStatus(t('status.alignNeedTwo'))
        return
      }
      const left = Math.min(...sel.map((e) => e.x))
      const right = Math.max(...sel.map((e) => e.x + e.w))
      const top = Math.min(...sel.map((e) => e.y))
      const bottom = Math.max(...sel.map((e) => e.y + e.h))
      const ids = new Set(sel.map((e) => e.id))
      const next = {
        ...s,
        elements: s.elements.map((e) => {
          if (!ids.has(e.id)) return e
          const out: DesignElement = { ...e }
          delete out.regionId
          switch (align) {
            case 'left':
              out.x = left
              break
            case 'centerH':
              out.x = left + (right - left - e.w) / 2
              break
            case 'right':
              out.x = right - e.w
              break
            case 'top':
              out.y = top
              break
            case 'centerV':
              out.y = top + (bottom - top - e.h) / 2
              break
            case 'bottom':
              out.y = bottom - e.h
              break
          }
          return out
        }),
      }
      commit(next)
      app.setStatus(t('status.aligned', { count: sel.length }))
    },
    [app, commit, t],
  )

  // ---------- 图层 ----------
  const moveLayer = useCallback(
    (delta: number) => {
      const s = stateRef.current
      if (!s || selectedRef.current.length !== 1) {
        app.setStatus(t('status.layerNeedSingle'))
        return
      }
      const idx = s.elements.findIndex((e) => e.id === selectedRef.current[0])
      const ni = idx + delta
      if (idx < 0 || ni < 0 || ni >= s.elements.length) return
      const els = [...s.elements]
      ;[els[idx], els[ni]] = [els[ni], els[idx]]
      commit({ ...s, elements: els })
    },
    [app, commit, t],
  )

  const layerToTop = useCallback(() => {
    const s = stateRef.current
    if (!s || selectedRef.current.length !== 1) return
    const id = selectedRef.current[0]
    const e = s.elements.find((x) => x.id === id)
    if (!e) return
    commit({ ...s, elements: [...s.elements.filter((x) => x.id !== id), e] })
  }, [commit])

  const layerToBottom = useCallback(() => {
    const s = stateRef.current
    if (!s || selectedRef.current.length !== 1) return
    const id = selectedRef.current[0]
    const e = s.elements.find((x) => x.id === id)
    if (!e) return
    commit({ ...s, elements: [e, ...s.elements.filter((x) => x.id !== id)] })
  }, [commit])

  // ---------- 撤销 / 重做 / 复制粘贴 ----------
  const undo = useCallback(() => {
    const h = historyRef.current
    if (!h) return
    const next = h.undo()
    if (!next) {
      app.setStatus(t('status.nothingToUndo'))
      return
    }
    historyRef.current = next
    stateRef.current = next.data
    setState(next.data)
    setSelected([])
    selectedRef.current = []
    app.setStatus(t('status.undone'))
  }, [app, t])

  const redo = useCallback(() => {
    const h = historyRef.current
    if (!h) return
    const next = h.redo()
    if (!next) {
      app.setStatus(t('status.nothingToRedo'))
      return
    }
    historyRef.current = next
    stateRef.current = next.data
    setState(next.data)
    setSelected([])
    selectedRef.current = []
    app.setStatus(t('status.redone'))
  }, [app, t])

  const clipboardRef = useRef<DesignElement[]>([])

  const copySelected = useCallback(() => {
    const s = stateRef.current
    if (!s) return
    const items = s.elements.filter((e) => selectedRef.current.includes(e.id))
    if (!items.length) return
    clipboardRef.current = items.map((e) => cloneElement(e))
    app.setStatus(t('status.copied', { count: clipboardRef.current.length }))
  }, [app, t])

  const pasteClipboard = useCallback(() => {
    const s = stateRef.current
    if (!s || clipboardRef.current.length === 0) return
    const copies = clipboardRef.current.map((e) => {
      const c = cloneElement(e)
      c.x = Math.max(0, Math.min(s.paperW - c.w - 1, c.x + 5))
      c.y = Math.max(0, Math.min(s.paperH - c.h - 1, c.y + 5))
      return c
    })
    commit({ ...s, elements: [...s.elements, ...copies] })
    const ids = copies.map((c) => c.id)
    setSelected(ids)
    selectedRef.current = ids
    app.setStatus(t('status.pasted', { count: copies.length }))
  }, [app, commit, t])

  // ---------- 设计 JSON 导入 / 导出（剪贴板，多级降级） ----------
  const doExportDesign = useCallback(async () => {
    const s = stateRef.current
    if (!s) return
    const text = exportDesign(s.paperW, s.paperH, s.elements)
    if (await copyText(text)) {
      app.setStatus(t('status.exported', { count: s.elements.length }))
      return
    }
    // 终极兜底：浏览器完全禁用剪贴板时展示代码供手动复制
    const input = window.prompt(t('status.exportPrompt'), text)
    if (input !== null) app.setStatus(t('status.exportFallback'))
  }, [app, t])

  const doImportDesign = useCallback(async () => {
    let text = await readClipboardText()
    if (!text) {
      // 读取权限不可用：聚焦隐藏输入，用户按一次 Ctrl+V 即完成（Esc / 超时取消）
      app.setStatus(t('status.clipboardUnavailable'))
      text = await capturePasteOnce()
      if (!text) {
        app.setStatus(t('status.importCancelled'))
        return
      }
    }
    try {
      const d = parseDesign(text)
      commit({ paperW: d.paperW, paperH: d.paperH, elements: d.elements })
      setSelected([])
      selectedRef.current = []
      setPendingType(null)
      app.setStatus(t('status.imported', { count: d.elements.length }))
    } catch (err) {
      app.setStatus(t('status.importFailed', { reason: err instanceof Error ? err.message : i18next.t('job.unknownError') }))
    }
  }, [app, commit, t])

  // ---------- 快捷键 ----------
  useEffect(() => {
    const onKey = (ev: KeyboardEvent) => {
      const s = stateRef.current
      if (!s) return
      // 演示期间快捷键全停：撤销 / 删除 / 粘贴等都会写 stateRef，绕过锁输入
      if (demoActive) return
      const tag = ev.target instanceof HTMLElement ? ev.target.tagName : ''
      if (tag === 'INPUT' || tag === 'SELECT' || tag === 'TEXTAREA') return
      const ctrl = ev.ctrlKey || ev.metaKey
      if (viewMode === 'preview') return

      if (ctrl && ev.shiftKey && ev.key.toLowerCase() === 'c') {
        ev.preventDefault()
        void doExportDesign()
        return
      }
      if (ctrl && ev.shiftKey && ev.key.toLowerCase() === 'v') {
        ev.preventDefault()
        void doImportDesign()
        return
      }
      if (ctrl && !ev.shiftKey && ev.key.toLowerCase() === 'z') {
        ev.preventDefault()
        undo()
        return
      }
      if (ctrl && ev.key.toLowerCase() === 'y') {
        ev.preventDefault()
        redo()
        return
      }
      if (ctrl && ev.key.toLowerCase() === 'c') {
        ev.preventDefault()
        copySelected()
        return
      }
      if (ctrl && ev.key.toLowerCase() === 'v') {
        ev.preventDefault()
        pasteClipboard()
        return
      }
      if (ev.key === 'Delete' || ev.key === 'Backspace') {
        if (selectedRef.current.length) {
          ev.preventDefault()
          deleteElements(selectedRef.current)
        }
        return
      }
      if (ev.key === 'Escape' && pendingType) {
        setPendingType(null)
        app.setStatus(t('status.placementCancelled'))
      }
    }
    document.addEventListener('keydown', onKey)
    return () => document.removeEventListener('keydown', onKey)
  }, [viewMode, pendingType, demoActive, app, doExportDesign, doImportDesign, undo, redo, copySelected, pasteClipboard, deleteElements, t])

  return {
    handleSelect,
    addElementAt,
    changeElement,
    deleteElements,
    alignSelected,
    moveLayer,
    layerToTop,
    layerToBottom,
    undo,
    redo,
    copySelected,
    pasteClipboard,
    doExportDesign,
    doImportDesign,
  }
}
