// 设计器左侧栏：控件栏（点击放置 / 拖入画布）+ 打印字段（只读推导）+ 图层
// 文案 key 化（designer 域 side.*）；控件栏标签随界面语言。
// 控件栏增第五入口「图片」。
// 图片改两步式——与其他控件同模式（点击武装 pendingType /
// 可拖入画布），先落空占位元素再上传填充（key 空＝未指定），不再走一步式文件上传入口。

import { useTranslation } from 'react-i18next'
import type { DesignElement } from '../../lib/design/types'
import { layerLabel } from '../../lib/design/types'
import { elementsByIds } from '../../lib/design/model'
import type { FieldInfo } from '../../lib/design/fields'
import { Icon } from '../../components/Icon'

const PALETTE: { type: string; labelKey: string; icon: PaletteIconName }[] = [
  { type: 'Text', labelKey: 'type.text', icon: 'text' },
  { type: 'Barcode', labelKey: 'type.barcode', icon: 'barcode' },
  { type: 'QrCode', labelKey: 'type.qrcode', icon: 'qrcode' },
  { type: 'Rect', labelKey: 'type.rect', icon: 'rect' },
  { type: 'Image', labelKey: 'type.image', icon: 'image' },
]

type PaletteIconName = 'text' | 'barcode' | 'qrcode' | 'rect' | 'image'

export interface SidePanelProps {
  elements: DesignElement[]
  selected: string[]
  viewMode: 'fit' | 'preview'
  pendingType: string | null
  fields: FieldInfo[]
  onPickType: (type: string) => void
  onSelect: (id: string, toggle?: boolean) => void
  onMoveLayer: (delta: number) => void
  onLayerTop: () => void
  onLayerBottom: () => void
  onDelete: (ids: string[]) => void
}

export function SidePanel(p: SidePanelProps) {
  const { t } = useTranslation('designer')
  const locked = p.viewMode === 'preview'
  const sel = elementsByIds(p.elements, p.selected)

  return (
    <aside className="designer-side">
      <section>
        <h3>
          {t('side.paletteTitle')}
          <small>{t('side.paletteHint')}</small>
        </h3>
        <div className="palette">
          {PALETTE.map((it) => (
            <button
              key={it.type}
              className={'palette-btn' + (p.pendingType === it.type ? ' armed' : '')}
              draggable={!locked}
              onClick={() => {
                if (locked) {
                  return
                }
                p.onPickType(p.pendingType === it.type ? '' : it.type)
              }}
              onDragStart={(ev) => {
                ev.dataTransfer.setData('text/plain', it.type)
                ev.dataTransfer.effectAllowed = 'copy'
              }}
            >
              <PaletteIcon name={it.icon} />
              {t(it.labelKey)}
            </button>
          ))}
        </div>
        {p.pendingType && (
          <div className="pending-hint">
            {locked ? t('side.previewLocked') : t('side.pendingHint', { label: t(PALETTE.find((x) => x.type === p.pendingType)?.labelKey ?? 'type.text') })}
          </div>
        )}
      </section>

      <section>
        <h3>
          {t('side.fieldsTitle')}
          <small>{t('side.fieldsHint')}</small>
        </h3>
        {locked ? (
          <div className="side-empty">{t('side.previewing')}</div>
        ) : p.fields.length === 0 ? (
          <div className="side-empty">{t('side.noFields')}</div>
        ) : (
          <ul className="field-list">
            {p.fields.map((f) => (
              <li key={f.key} className="mono" title={f.displayName && f.displayName !== f.key ? t('side.fieldNameTitle', { key: f.key }) : undefined}>
                {f.displayName || f.key}
              </li>
            ))}
          </ul>
        )}
      </section>

      <section style={{ flex: 1, minHeight: 0, display: 'flex', flexDirection: 'column' }}>
        <h3>
          {t('side.layersTitle')}
          <small>{t('side.layersHint')}</small>
        </h3>
        {locked ? (
          <div className="side-empty">{t('side.previewLockedEditable')}</div>
        ) : (
          <ul className="layer-list">
            {p.elements.map((e, i) => (
              <li
                key={e.id}
                className={'layer-item' + (p.selected.includes(e.id) ? ' active' : '')}
                title={layerLabel(e)}
                onClick={(ev) => p.onSelect(e.id, ev.shiftKey || ev.ctrlKey)}
              >
                <span className="layer-idx mono">{i + 1}</span>
                <span className="layer-label">{layerLabel(e)}</span>
              </li>
            ))}
          </ul>
        )}
        <div className="layer-actions">
          <button className="btn sm" onClick={p.onLayerTop} disabled={locked || sel.length !== 1} title={t('side.toTop')}>
            <Icon name="layers" size={12} />
            {t('side.toTop')}
          </button>
          <button className="btn sm" onClick={() => p.onMoveLayer(-1)} disabled={locked || sel.length !== 1} title={t('side.up')}>
            {t('side.up')}
          </button>
          <button className="btn sm" onClick={() => p.onMoveLayer(1)} disabled={locked || sel.length !== 1} title={t('side.down')}>
            {t('side.down')}
          </button>
          <button className="btn sm" onClick={p.onLayerBottom} disabled={locked || sel.length !== 1} title={t('side.toBottom')}>
            {t('side.toBottom')}
          </button>
        </div>
      </section>
    </aside>
  )
}

function PaletteIcon({ name }: { name: PaletteIconName }) {
  switch (name) {
    case 'text':
      return (
        <svg viewBox="0 0 24 24" width="17" height="17" fill="none" stroke="currentColor" strokeWidth="1.7" strokeLinecap="round" strokeLinejoin="round">
          <path d="M5 7V5h14v2M12 5v14M9 19h6" />
        </svg>
      )
    case 'barcode':
      return (
        <svg viewBox="0 0 24 24" width="17" height="17" fill="none" stroke="currentColor" strokeWidth="1.7" strokeLinecap="round">
          <path d="M4 7v10M7.5 7v10M11 7v10M14 7v10M17 7v10M20 7v10" />
        </svg>
      )
    case 'qrcode':
      return (
        <svg viewBox="0 0 24 24" width="17" height="17" fill="none" stroke="currentColor" strokeWidth="1.7" strokeLinecap="round">
          <path d="M4 4h7v7H4zM13 4h7v4h-7zM4 13h4v7H4zM13 13h3v3h-3zM17 17h4v4h-4zM13 20h4M20 13v4" />
        </svg>
      )
    case 'rect':
      return <svg viewBox="0 0 24 24" width="17" height="17" fill="none" stroke="currentColor" strokeWidth="1.7"><rect x="4" y="7" width="16" height="10" rx="1" /></svg>
    case 'image':
      // 山日图标（与原型图片占位形态呼应）
      return (
        <svg viewBox="0 0 24 24" width="17" height="17" fill="none" stroke="currentColor" strokeWidth="1.7" strokeLinecap="round" strokeLinejoin="round">
          <rect x="3.5" y="5" width="17" height="14" rx="1.5" />
          <circle cx="9" cy="10" r="1.6" />
          <path d="M4.5 17.5l4.8-4.8 3.2 3.2 3-3 4 4" />
        </svg>
      )
  }
}
