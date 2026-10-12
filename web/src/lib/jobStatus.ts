// 作业状态展示文案：JobHistory 与 DataPrint 共用（common.jobStatus.* 词条）
// 未知状态回退后端原始英文值。以 hook 形式提供：文案在渲染期经 useTranslation 订阅当前语言。

import { useCallback } from 'react'
import { useTranslation } from 'react-i18next'

/** 状态枚举 → 本地化文案（Pending / Printing / Completed / Failed / Suspended / Cancelled / Claimed / Expired）。 */
export function useJobStatusLabel(): (status: string) => string {
  const { t } = useTranslation()
  return useCallback(
    (status: string): string => {
      switch (status) {
        case 'Pending':
          return t('jobStatus.pending')
        case 'Printing':
          return t('jobStatus.printing')
        case 'Completed':
          return t('jobStatus.completed')
        case 'Failed':
          return t('jobStatus.failed')
        case 'Suspended':
          return t('jobStatus.suspended')
        case 'Cancelled':
          return t('jobStatus.cancelled')
        case 'Claimed':
          return t('jobStatus.claimed')
        case 'Expired':
          return t('jobStatus.expired')
        default:
          return status
      }
    },
    [t],
  )
}
