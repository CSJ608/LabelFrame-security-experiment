// 离开保护编排 hook：dirty（undoCount 与最近成功保存点的标记不一致）时离开走三选 Modal
//（保存并离开 / 放弃更改 / 继续编辑），一致（未编辑或已撤销回保存点）直接放行；
// Shell 导航 tab 切换拦截的挂载注册 / 卸载注销。

import { useCallback, useEffect, useState } from 'react'
import type { RefObject } from 'react'
import type { DesignerHistory } from './designState'

export interface UseLeaveGuardOptions {
  historyRef: RefObject<DesignerHistory | null>
  /* * 最近一次成功保存时的 undoCount（0＝尚未保存过）——dirty 判定基准。 */
  savedMarkRef: RefObject<number>
  /** 离开保护挂起的离开动作（与保存链共用：离开保存成功后执行；终止路径就地作废——就地保存变体成功与取消不动，失败仍作废）。 */
  pendingLeaveRef: RefObject<(() => void) | null>
  onClose: () => void
  save: () => void
  /* * 向 Shell 注册离开守卫（导航 tab 切换拦截）；卸载自动注销。返回 false=已挂起三选待确认、
   * true=已直接放行——路由据此判定是否 pre-revert（非 dirty 放行时不得回退，否则浏览器后退在设计器上失效）。 */
  registerLeaveGuard?: (fn: ((leave: () => void) => boolean) | null) => void
}

export function useLeaveGuard({ historyRef, savedMarkRef, pendingLeaveRef, onClose, save, registerLeaveGuard }: UseLeaveGuardOptions) {
  // 未保存离开保护——三选 Modal（保存并离开 / 放弃更改 / 继续编辑）
  const [leaveGuardOpen, setLeaveGuardOpen] = useState(false)

  // ---------- 离开保护（dirty = undoCount 与保存点标记不一致） ----------
  /* * 请求离开：与最近保存点一致（未编辑 / 已就地保存 / 撤销回保存点）直接离开不弹窗；
   * 不一致则弹三选 Modal 挂起离开动作。返回 false=已挂起、true=已放行——Shell 以此决定路由是否
   * pre-revert（非 dirty 放行不得回退，否则浏览器后退失效）。 */
  const requestLeave = useCallback((leave: () => void): boolean => {
    if ((historyRef.current?.undoCount ?? 0) !== savedMarkRef.current) {
      pendingLeaveRef.current = leave
      setLeaveGuardOpen(true)
      return false
    }
    leave()
    return true
  }, [])

  /** 三选之「继续编辑」（含 Esc / 点遮罩 / 右上角关闭）：作废挂起的离开动作，留在设计器。 */
  const stayInEditor = useCallback(() => {
    pendingLeaveRef.current = null
    setLeaveGuardOpen(false)
  }, [])

  /** 三选之「放弃更改」：不保存，直接执行挂起的离开动作（回工作台 / 完成挂起的导航切换）。 */
  const discardAndLeave = useCallback(() => {
    const leave = pendingLeaveRef.current
    pendingLeaveRef.current = null
    setLeaveGuardOpen(false)
    leave?.()
  }, [])

  /** 三选之「保存并离开」：走既有保存链路（空名校验 / 同名覆盖确认），成功后由 doSave 执行挂起离开；
   * 失败停留设计器显示错误、不丢编辑。 */
  const saveAndLeave = useCallback(() => {
    setLeaveGuardOpen(false)
    save()
  }, [save])

  /* * 设计器返回按钮 / 加载失败返回工作台：dirty 时经三选确认，否则直接返回。 */
  const requestClose = useCallback(() => requestLeave(onClose), [requestLeave, onClose])

  // Shell 导航 tab 切换拦截：挂载注册 / 卸载注销
  useEffect(() => {
    if (!registerLeaveGuard) return
    registerLeaveGuard(requestLeave)
    return () => registerLeaveGuard(null)
  }, [registerLeaveGuard, requestLeave])

  return { leaveGuardOpen, requestLeave, stayInEditor, discardAndLeave, saveAndLeave, requestClose }
}
