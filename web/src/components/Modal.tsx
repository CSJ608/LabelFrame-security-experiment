// 通用模态框（确认 / 表单）。关闭按钮 aria-label key 化（common 域）；
// 本文件已圈入 oxlint 裸中文 JSX 防线（新增文案必须走 t() 词条）。

import { useEffect } from 'react'
import { useTranslation } from 'react-i18next'
import type { ReactNode } from 'react'
import { Icon } from './Icon'

interface ModalProps {
  title: string
  onClose: () => void
  children: ReactNode
  footer?: ReactNode
  width?: number
}

export function Modal({ title, onClose, children, footer, width = 440 }: ModalProps) {
  const { t } = useTranslation()

  useEffect(() => {
    const onKey = (ev: KeyboardEvent) => {
      if (ev.key === 'Escape') onClose()
    }
    document.addEventListener('keydown', onKey)
    return () => document.removeEventListener('keydown', onKey)
  }, [onClose])

  return (
    <div className="modal-mask" onMouseDown={(ev) => ev.target === ev.currentTarget && onClose()}>
      <div className="modal" style={{ width }} role="dialog" aria-label={title}>
        <div className="modal-head">
          <span>{title}</span>
          <button className="modal-close" onClick={onClose} aria-label={t('action.close')}>
            <Icon name="x" size={15} />
          </button>
        </div>
        <div className="modal-body">{children}</div>
        {footer && <div className="modal-foot">{footer}</div>}
      </div>
    </div>
  )
}
