// draft 保留逻辑（合并语义 / 按模板分键 / 刷新持久化 / 标签页隔离）

import { describe, expect, it } from 'vitest'
import type { StorageLike } from './draft'
import {
  DRAFT_KEY,
  applyDraftValue,
  createEmptyDraft,
  loadPrintDraft,
  mergeDraftValues,
  savePrintDraft,
} from './draft'

function fakeStorage(): StorageLike {
  const m = new Map<string, string>()
  return {
    getItem: (k) => m.get(k) ?? null,
    setItem: (k, v) => void m.set(k, v),
    removeItem: (k) => void m.delete(k),
  }
}

describe('mergeDraftValues：按 key 存在性合并（非 truthy）', () => {
  const testData = { location: 'A-01', sku: 'ABC', remark: '默认' }

  it('无用户输入时全部用 testData', () => {
    expect(mergeDraftValues(testData, {}, [])).toEqual(testData)
  })

  it('用户改过的 key 覆盖 testData', () => {
    expect(mergeDraftValues(testData, { location: 'B-02' }, ['location'])).toEqual({
      location: 'B-02',
      sku: 'ABC',
      remark: '默认',
    })
  })

  it('用户主动清空的字段不被 testData 顶回（空串也是有效输入）', () => {
    expect(mergeDraftValues(testData, { remark: '' }, ['remark'])).toEqual({
      location: 'A-01',
      sku: 'ABC',
      remark: '',
    })
  })

  it('dirty 但用户值里不存在的 key 跳过（保持 testData）', () => {
    expect(mergeDraftValues(testData, { location: 'B-02' }, ['location', 'ghost'])).toEqual({
      location: 'B-02',
      sku: 'ABC',
      remark: '默认',
    })
  })

  it('无 testData / 无用户值时安全返回', () => {
    expect(mergeDraftValues(undefined, undefined, undefined)).toEqual({})
    expect(mergeDraftValues(null, {}, [])).toEqual({})
  })
})

describe('applyDraftValue：按模板分键 + dirty 登记', () => {
  it('写入值并登记 dirty，不影响其它模板', () => {
    const d = applyDraftValue(createEmptyDraft(), 'T-A', 'location', 'B-02')
    expect(d.valuesByTemplate['T-A']).toEqual({ location: 'B-02' })
    expect(d.dirtyKeysByTemplate['T-A']).toEqual(['location'])
    expect(d.valuesByTemplate['T-B']).toBeUndefined()

    const d2 = applyDraftValue(d, 'T-B', 'sku', 'X')
    expect(d2.valuesByTemplate['T-A']).toEqual({ location: 'B-02' })
    expect(d2.valuesByTemplate['T-B']).toEqual({ sku: 'X' })
    expect(d2.dirtyKeysByTemplate['T-B']).toEqual(['sku'])
  })

  it('重复写入同一 key 不重复登记 dirty；清空（空串）同样登记', () => {
    let d = applyDraftValue(createEmptyDraft(), 'T', 'a', '1')
    d = applyDraftValue(d, 'T', 'a', '2')
    expect(d.dirtyKeysByTemplate['T']).toEqual(['a'])

    let d2 = applyDraftValue(createEmptyDraft(), 'T', 'b', '')
    d2 = applyDraftValue(d2, 'T', 'b', '')
    expect(d2.dirtyKeysByTemplate['T']).toEqual(['b'])
    expect(d2.valuesByTemplate['T']).toEqual({ b: '' })
  })

  it('不可变更新：原 draft 不被修改', () => {
    const d = createEmptyDraft()
    applyDraftValue(d, 'T', 'a', '1')
    expect(d.valuesByTemplate['T']).toBeUndefined()
  })
})

describe('持久化（sessionStorage 语义）：刷新保留 / 标签页隔离', () => {
  it('保存后可从同一存储读回（刷新保留）', () => {
    const storage = fakeStorage()
    const d = applyDraftValue(createEmptyDraft(), 'T', 'a', '1')
    const draft: ReturnType<typeof createEmptyDraft> = { ...d, selectedName: 'T', debugMode: true, jobId: 'job-1' }
    savePrintDraft(storage, draft)
    expect(loadPrintDraft(storage)).toEqual(draft)
    expect(storage.getItem(DRAFT_KEY)).toBeTruthy()
  })

  it('两个标签页（不同存储实例）互不互通', () => {
    const tabA = fakeStorage()
    const tabB = fakeStorage()
    const d = { ...createEmptyDraft(), selectedName: 'T', debugMode: true }
    savePrintDraft(tabA, d)
    // B 页读不到 A 页的草稿
    expect(loadPrintDraft(tabB).selectedName).toBe('')
    expect(loadPrintDraft(tabB).debugMode).toBe(false)
    // A 页仍在
    expect(loadPrintDraft(tabA).selectedName).toBe('T')
  })

  it('损坏数据回退空草稿，不抛错', () => {
    const storage = fakeStorage()
    storage.setItem(DRAFT_KEY, '{not-json')
    expect(loadPrintDraft(storage)).toEqual(createEmptyDraft())
    storage.setItem(DRAFT_KEY, JSON.stringify({ selectedName: 123, debugMode: 'yes', jobId: null }))
    expect(loadPrintDraft(storage)).toEqual(createEmptyDraft())
  })

  it('无存储 / 存储不可用时静默容错', () => {
    expect(loadPrintDraft(undefined)).toEqual(createEmptyDraft())
    expect(loadPrintDraft(null)).toEqual(createEmptyDraft())
    expect(() => savePrintDraft(undefined, createEmptyDraft())).not.toThrow()
  })
})
