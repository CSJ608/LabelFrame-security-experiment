// DataPrint 会话草稿：提升到全局的打印设置保留。
// 纯逻辑 + 可注入存储（sessionStorage 由调用方提供），便于单测与标签页隔离验证。
// 保留范围：selectedName / valuesByTemplate（按模板分键）/ dirtyKeysByTemplate / debugMode / jobId。
// 不保留：Excel 原始数据与列映射（页面局部状态，切页即丢）。

export interface PrintDraft {
  /** 选中的模板名 */
  selectedName: string
  /** 每模板的用户输入值（含主动清空），按模板名分键 */
  valuesByTemplate: Record<string, Record<string, string>>
  /** 每模板本次会话中用户输入过的字段 key（含清空），合并时按 key 存在性覆盖 testData */
  dirtyKeysByTemplate: Record<string, string[]>
  /** 调试模式开关（默认关） */
  debugMode: boolean
  /** 当前作业 ID（作业进度在切页后继续显示） */
  jobId: string | null
}

export type StorageLike = Pick<Storage, 'getItem' | 'setItem' | 'removeItem'>

export const DRAFT_KEY = 'labelframe.printDraft'

export function createEmptyDraft(): PrintDraft {
  return { selectedName: '', valuesByTemplate: {}, dirtyKeysByTemplate: {}, debugMode: false, jobId: null }
}

/**
 * 合并显示值：{ ...testData, ...用户 dirty 过的 key }。
 * 按 key 是否存在合并（非 truthy）——用户主动清空的字段（dirty 且值为空串）不被 testData 顶回。
 */
export function mergeDraftValues(
  testData: Record<string, string> | null | undefined,
  userValues: Record<string, string> | undefined,
  dirtyKeys: string[] | undefined,
): Record<string, string> {
  const out: Record<string, string> = { ...(testData ?? {}) }
  if (userValues && dirtyKeys) {
    for (const k of dirtyKeys) {
      if (k in userValues) out[k] = userValues[k]
    }
  }
  return out
}

/** 写入一个字段值并登记 dirty（不可变更新，供 AppContext 使用）。 */
export function applyDraftValue(draft: PrintDraft, template: string, key: string, value: string): PrintDraft {
  const values = { ...(draft.valuesByTemplate[template] ?? {}) }
  values[key] = value
  const dirty = draft.dirtyKeysByTemplate[template] ?? []
  const nextDirty = dirty.includes(key) ? dirty : [...dirty, key]
  return {
    ...draft,
    valuesByTemplate: { ...draft.valuesByTemplate, [template]: values },
    dirtyKeysByTemplate: { ...draft.dirtyKeysByTemplate, [template]: nextDirty },
  }
}

/** 从存储读取草稿；无存储 / 数据损坏时返回空草稿（静默容错）。 */
export function loadPrintDraft(storage: StorageLike | null | undefined): PrintDraft {
  if (!storage) return createEmptyDraft()
  try {
    const raw = storage.getItem(DRAFT_KEY)
    if (!raw) return createEmptyDraft()
    const parsed = JSON.parse(raw) as Partial<PrintDraft>
    return {
      selectedName: typeof parsed.selectedName === 'string' ? parsed.selectedName : '',
      valuesByTemplate: isRecord(parsed.valuesByTemplate) ? parsed.valuesByTemplate : {},
      dirtyKeysByTemplate: isRecord(parsed.dirtyKeysByTemplate) ? parsed.dirtyKeysByTemplate : {},
      debugMode: parsed.debugMode === true,
      jobId: typeof parsed.jobId === 'string' ? parsed.jobId : null,
    }
  } catch {
    return createEmptyDraft()
  }
}

/** 写入存储；无存储 / 序列化失败时静默忽略（草稿保留在内存，不影响使用）。 */
export function savePrintDraft(storage: StorageLike | null | undefined, draft: PrintDraft): void {
  if (!storage) return
  try {
    storage.setItem(DRAFT_KEY, JSON.stringify(draft))
  } catch {
    // 配额 / 隐私模式等场景忽略，会话内草稿仍在内存中
  }
}

function isRecord(v: unknown): v is Record<string, unknown> {
  return typeof v === 'object' && v !== null && !Array.isArray(v)
}
