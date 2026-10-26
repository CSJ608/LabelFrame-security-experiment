// 显示操作失败原因与确认关闭入口。

import { useTranslation } from 'react-i18next'
import { Icon } from './Icon'
import { Modal } from './Modal'

export function ErrorAlert({ title, message, onClose }: { title?: string; message: string; onClose: () => void }) {
  const { t } = useTranslation()
  return (
    <Modal
      title={title ?? t('action.actionFailed')}
      onClose={onClose}
      footer={
        <button className="btn primary" autoFocus onClick={onClose}>
          {t('action.gotIt')}
        </button>
      }
    >
      <div style={{ display: 'flex', gap: 8, alignItems: 'flex-start' }}>
        <span className="error-text" style={{ display: 'inline-flex', flexShrink: 0, marginTop: 1 }}>
          <Icon name="alert" size={14} />
        </span>
        <span>{message}</span>
      </div>
    </Modal>
  )
}
