// 工作台模板预览放大灯箱：半透明暗背景 + 居中卡片，图片按真实宽高比约束在视口 75% 且 ≤900px
//（203 DPI 原生约 560px，再往上是放大插值、接受轻微发虚）；Esc / 点背景 / 点 × 关闭，点卡片本体不关闭。

import { useTranslation } from 'react-i18next'
import { Icon } from '../components/Icon'
import type { TemplatePreviewEntry } from './useTemplatePreview'

/** 居中灯箱：背景点击关闭；卡片内点击（图片 / 标题）不冒泡关闭。 */
export function TemplatePreviewModal({ name, entry, onClose }: { name: string; entry: TemplatePreviewEntry; onClose: () => void }) {
  const { t } = useTranslation('workbench')
  return (
    <div className="preview-modal" onClick={onClose} aria-hidden="true">
      <div
        className="preview-modal-card"
        role="dialog"
        aria-label={t('preview.modalAria', { name })}
        onClick={(ev) => ev.stopPropagation()}
      >
        <div className="preview-modal-title">
          {name}
          <span className="spacer" style={{ flex: 1 }} />
          <button className="preview-modal-close" onClick={onClose} title={t('preview.closeTitle')}>
            <Icon name="x" size={14} />
          </button>
        </div>
        {entry.status === 'loading' ? (
          <div className="preview-modal-state">
            <Icon name="refresh" size={14} />
            {t('preview.generating')}
          </div>
        ) : entry.status === 'error' ? (
          <div className="preview-modal-state err">
            <Icon name="alert" size={14} />
            {t('preview.unavailableTitle')}
            <small>{entry.message}</small>
          </div>
        ) : (
          <>
            <img className="preview-modal-img" src={entry.url} alt={t('preview.modalAria', { name })} />
            <div className="preview-modal-foot">{t('preview.sampleFoot')}</div>
          </>
        )}
      </div>
    </div>
  )
}
