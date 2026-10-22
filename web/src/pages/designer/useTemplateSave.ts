// 保存编排 hook：doSave（图片孤儿 GC、契约名 / 版本沿用、恒发显式 images 字典）与 save（空名校验、
// 新建 / 改名时同名覆盖确认，含 stay=true 就地保存变体）；挂起的离开动作由离开守卫（useLeaveGuard）持有并传入。
// 空名拦截置 nameError（调用方渲染名称框内联红错并聚焦；状态栏文案保留）。

import { useCallback, useRef, useState } from 'react'
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
  /** 编辑目标的模板稳定标识（打开时 pkg.uid ?? request.uid；保存请求原样回传）。 */
  uidRef: RefObject<string | undefined>
  group: string
  name: string
  onClose: () => void
  /** 离开守卫的挂起离开动作（「保存并离开」成功后由 doSave 执行；失败无条件作废，取消类拦截仅离开发起时作废——就地发起不动挂起导航）。 */
  pendingLeaveRef: RefObject<(() => void) | null>
  setSaving: Dispatch<SetStateAction<boolean>>
  setConfirmOverwrite: Dispatch<SetStateAction<boolean>>
  /** 保存成功回调（就地与离开两路共用：调用方据此刷新保存点标记）。 */
  onSaved?: () => void
}

export function useTemplateSave({ request, biz, stateRef, initialNameRef, contractNameRef, contractVersionRef, imageAssetsRef, uidRef, group, name, onClose, pendingLeaveRef, setSaving, setConfirmOverwrite, onSaved }: UseTemplateSaveOptions) {
  const app = useApp()
  const { t } = useTranslation('designer')
  const [nameError, setNameError] = useState(false)
  // 同名覆盖确认发起时的就地 / 离开语义——确认弹窗按钮按此分派 doSave 变体
  const overwriteStayRef = useRef(false)
  // 本会话已成功保存过——新建模板二次保存不再按全新处理（否则会与自己撞名弹覆盖确认）
  const savedRef = useRef(false)

  // ---------- 保存 ----------
  const doSave = useCallback(
    async (finalName: string, opts?: { stay?: boolean }) => {
      const stay = opts?.stay ?? false
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
          // 有 uid 即回传（编辑打开带入，或新建模板首次保存后认领）；缺失（旧后端）不携带，后端按名字口径兜底
          uid: uidRef.current,
          contract: toContract(contractName, version, fields),
          // 不传 testData——由后端从元素 previewValue 自动派生（读-改-写，旧值不丢）
          layout: toLayout(finalName, contractName, version, s.paperW, s.paperH, s.elements),
          images,
        }
        await biz.saveTemplate(pkg)
        app.setStatus(t('status.saved', { name: finalName }))
        onSaved?.()
        // 认领保存后的身份：模板名更新（二次保存同名不误判改名）；新建模板取后端分配的 uid
        //（此后改名保存＝同模板更新而非另建新行）；认领失败无碍——下次保存按名字口径兜底
        initialNameRef.current = finalName
        savedRef.current = true
        if (!uidRef.current) {
          try {
            uidRef.current = (await biz.getTemplate(finalName))?.uid
          } catch {
            // 取回失败不阻断保存流程
          }
        }
        // 就地保存：留在设计器，不动挂起的离开动作（三选弹窗仍按原语义工作）
        if (stay) return
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
    [app, biz, group, onClose, onSaved, request.kind, t, uidRef],
  )

  const save = useCallback((opts?: { stay?: boolean }) => {
    const stay = opts?.stay ?? false
    const trimmed = name.trim()
    if (!trimmed) {
      // 「保存并离开」无模板名时止步于此（停留设计器补名称），挂起动作作废；
      // 就地变体不动挂起的离开动作（挂起的导航与本次保存互不干扰）
      if (!stay) pendingLeaveRef.current = null
      setNameError(true)
      app.setStatus(t('status.nameRequired'))
      return
    }

    setNameError(false)
    const isNew = request.kind === 'new' && !savedRef.current
    const renamed = !isNew && trimmed !== initialNameRef.current
    if (isNew || renamed) {
      void biz
        .listTemplates()
        .then((list) => {
          if (list.some((t) => t.name === trimmed)) {
            overwriteStayRef.current = stay
            setConfirmOverwrite(true)
          }
          else void doSave(trimmed, { stay })
        })
        .catch(() => void doSave(trimmed, { stay }))
    } else {
      void doSave(trimmed, { stay })
    }
  }, [app, biz, doSave, name, request.kind, t])

  /** 覆盖确认「覆盖保存」：关闭弹窗并按发起时记录的就地 / 离开语义保存。 */
  const overwriteSave = useCallback(() => {
    setConfirmOverwrite(false)
    void doSave(name.trim(), { stay: overwriteStayRef.current })
  }, [doSave, name])

  /** 覆盖确认取消（含 Esc / 点遮罩 / 右上角关闭）：停留在设计器；离开发起的挂起动作作废，就地发起的不动挂起导航。 */
  const cancelOverwrite = useCallback(() => {
    setConfirmOverwrite(false)
    if (!overwriteStayRef.current) pendingLeaveRef.current = null
  }, [])

  /** 名称输入变更即清除空名红错（调用方 onChange 调用）。 */
  const clearNameError = useCallback(() => setNameError(false), [])

  return { doSave, save, overwriteSave, cancelOverwrite, nameError, clearNameError }
}
