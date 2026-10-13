// 保存编排 hook：doSave（图片孤儿 GC、契约名 / 版本沿用、恒发显式 images 字典）与 save（空名校验、
// 新建 / 改名时同名覆盖确认）；挂起的离开动作由离开守卫（useLeaveGuard）持有并传入。
// 回调体自 Designer.tsx 逐字搬移（纯重构零行为变化）。

import { useCallback } from 'react'
import type { Dispatch, RefObject, SetStateAction } from 'react'
import { useTranslation } from 'react-i18next'
import type { DesignState } from './CanvasViewport'
import { useApp } from '../../state/AppContext'
import type { DesignerRequest } from '../../state/types'
import type { ImageAsset, ImageElement } from '../../lib/design/types'
import { deriveFieldInfos } from '../../lib/design/fields'
import { toContract, toLayout } from '../../lib/design/convert'
import { localApi, serverApi } from '../../lib/api/client'
import { ApiError } from '../../lib/api/types'
import type { TemplatePackage } from '../../lib/api/types'
import i18next from '../../i18n'

export interface UseTemplateSaveOptions {
  request: DesignerRequest
  biz: typeof serverApi | typeof localApi
  stateRef: RefObject<DesignState | null>
  initialNameRef: RefObject<string>
  contractNameRef: RefObject<string>
  contractVersionRef: RefObject<string>
  imageAssetsRef: RefObject<Record<string, ImageAsset>>
  group: string
  name: string
  onClose: () => void
  /** 离开守卫的挂起离开动作（「保存并离开」成功后由 doSave 执行；失败 / 取消路径就地作废）。 */
  pendingLeaveRef: RefObject<(() => void) | null>
  setSaving: Dispatch<SetStateAction<boolean>>
  setConfirmOverwrite: Dispatch<SetStateAction<boolean>>
}

export function useTemplateSave({ request, biz, stateRef, initialNameRef, contractNameRef, contractVersionRef, imageAssetsRef, group, name, onClose, pendingLeaveRef, setSaving, setConfirmOverwrite }: UseTemplateSaveOptions) {
  const app = useApp()
  const { t } = useTranslation('designer')

  // ---------- 保存 ----------
  const doSave = useCallback(
    async (finalName: string) => {
      const s = stateRef.current
      if (!s) {
        pendingLeaveRef.current = null
        return
      }
      setSaving(true)
      try {
        const fields = deriveFieldInfos(s.elements)
        const version = request.kind === 'new' ? '1' : contractVersionRef.current
        const contractName = request.kind === 'new' ? finalName : contractNameRef.current
        // 图片资源：仅提交被引用的 key（孤儿 GC），剥 dataURL 前缀；恒发显式字典——
        // 契约缺省 / null＝保留旧图，空字典才是清空，不得依赖缺省态。
        const usedKeys = new Set(s.elements.filter((e) => e.type === 'Image' && e.key).map((e) => (e as ImageElement).key))
        const images: Record<string, string> = {}
        for (const [key, asset] of Object.entries(imageAssetsRef.current)) {
          if (usedKeys.has(key)) {
            const comma = asset.dataUrl.indexOf(',')
            images[key] = comma >= 0 ? asset.dataUrl.slice(comma + 1) : asset.dataUrl
          }
        }
        const pkg: TemplatePackage = {
          name: finalName,
          group: group.trim() || i18next.t('designer:defaultGroup'),
          contract: toContract(contractName, version, fields),
          // 不传 testData——由后端从元素 previewValue 自动派生（读-改-写，旧值不丢）
          layout: toLayout(finalName, contractName, version, s.paperW, s.paperH, s.elements),
          images,
        }
        await biz.saveTemplate(pkg)
        app.setStatus(t('status.saved', { name: finalName }))
        // 「保存并离开」成功后执行挂起的离开动作；普通保存维持 onClose 回工作台
        const leave = pendingLeaveRef.current
        pendingLeaveRef.current = null
        if (leave) leave()
        else onClose()
      } catch (err) {
        app.setStatus(err instanceof ApiError ? err.message : t('status.saveFailed'))
        // 保存失败停留设计器、不丢编辑——挂起的离开动作就地作废
        pendingLeaveRef.current = null
      } finally {
        setSaving(false)
      }
    },
    [app, biz, group, onClose, request.kind, t],
  )

  const save = useCallback(() => {
    const trimmed = name.trim()
    if (!trimmed) {
      // 「保存并离开」无模板名时止步于此（停留设计器补名称），挂起动作作废
      pendingLeaveRef.current = null
      app.setStatus(t('status.nameRequired'))
      return
    }
    const isNew = request.kind === 'new'
    const renamed = !isNew && trimmed !== initialNameRef.current
    if (isNew || renamed) {
      void biz
        .listTemplates()
        .then((list) => {
          if (list.some((t) => t.name === trimmed)) setConfirmOverwrite(true)
          else void doSave(trimmed)
        })
        .catch(() => void doSave(trimmed))
    } else {
      void doSave(trimmed)
    }
  }, [app, biz, doSave, name, request.kind, t])

  /** 覆盖确认取消（含 Esc / 点遮罩 / 右上角关闭）：停留在设计器，挂起的离开动作作废。 */
  const cancelOverwrite = useCallback(() => {
    setConfirmOverwrite(false)
    pendingLeaveRef.current = null
  }, [])

  return { doSave, save, cancelOverwrite }
}
