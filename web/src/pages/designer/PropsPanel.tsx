// 属性面板：选中元素才显示；多选显示对齐操作；文本 / 条码 / 二维码 / 矩形等分组属性。
// 字体下拉显示名随界面语言，内部名 / 模板存储值不变（白名单 6 项不动）。
// 分组标题挂「?」文档点（双构建直渲染；设置页挂点为 client-only 防御门，见 Settings.tsx）。
// 演示（「看实时效果」）期间输入与操作禁用（界面信号，写入侧由 Designer 传 no-op 回调结构性兜底）；
// 深链经 autoOpenAnchor 信号自动弹出对应文档点气泡。

import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import type { DesignElement, ImageAsset } from '../../lib/design/types'
import { typeLabel } from '../../lib/design/types'
import { elementsByIds } from '../../lib/design/model'
import { Icon } from '../../components/Icon'
import { HelpDot } from '../../components/HelpDot'

export interface PropsPanelProps {
  elements: DesignElement[]
  selected: string[]
  viewMode: 'fit' | 'preview'
  onChange: (id: string, patch: Partial<DesignElement>) => void
  onAlign: (align: 'left' | 'centerH' | 'right' | 'top' | 'centerV' | 'bottom') => void
  onDelete: (ids: string[]) => void
  /* * 图片资源表（key → 资源）：图片组信息行与缺资源判定用。 */
  imageAssets?: Record<string, ImageAsset>
  /* * 「上传 / 更换图片」按钮：打开文件选择为目标元素填 key（空占位填充或换新 key，旧 key 孤儿随保存 GC）。 */
  onReplaceImage?: (id: string) => void
  /* * 演示进行中（填充组「看实时效果」）：输入与操作禁用。 */
  demoActive?: boolean
  onDemoStart?: (anchor: string) => void
  onDemoExit?: () => void
  /** 帮助文章深链信号（Shell 已确认锚点在 DOM）：自动弹出对应文档点气泡后回调清信号。 */
  autoOpenAnchor?: string | null
  onAutoOpenHandled?: () => void
}

export function PropsPanel({ elements, selected, viewMode, onChange, onAlign, onDelete, imageAssets, onReplaceImage, demoActive, onDemoStart, onDemoExit, autoOpenAnchor, onAutoOpenHandled }: PropsPanelProps) {
  const { t } = useTranslation('designer')
  // 开启的文档点锚点（单开；hook 必须先于下方条件返回声明）
  const [openDot, setOpenDot] = useState<string | null>(null)

  // 深链：Shell 轮询确认锚点在 DOM 后下发信号——开启即见效，处理后清信号防重复触发
  useEffect(() => {
    if (!autoOpenAnchor) return
    setOpenDot(autoOpenAnchor)
    onAutoOpenHandled?.()
  }, [autoOpenAnchor, onAutoOpenHandled])

  // 演示与气泡同生命周期：开启点变化（切换 / 关闭）即退出演示——显示层 override 自动还原
  const changeDot = (next: string | null) => {
    if (demoActive && next !== openDot) onDemoExit?.()
    setOpenDot(next)
  }
  const toggleDot = (anchor: string) => changeDot(openDot === anchor ? null : anchor)

  /* * 分组标题「?」文档点（双构建直渲染）。 */
  const dot = (anchor: string) => (
    <HelpDot
      anchor={anchor}
      topicKey={`topic.${anchor}`}
      open={openDot === anchor}
      onToggle={() => toggleDot(anchor)}
      onClose={() => changeDot(null)}
      hasDemo={anchor === 'designer.fill'}
      demoActive={demoActive}
      onDemoStart={anchor === 'designer.fill' ? () => onDemoStart?.(anchor) : undefined}
      onDemoExit={anchor === 'designer.fill' ? () => onDemoExit?.() : undefined}
    />
  )

  if (viewMode === 'preview') {
    return <div className="props-empty">{t('props.previewLocked')}</div>
  }
  if (selected.length === 0) {
    return <div className="props-empty">{t('props.empty')}</div>
  }
  const sel = elementsByIds(elements, selected)
  if (sel.length > 1) {
    return (
      <div style={{ display: 'flex', flexDirection: 'column', gap: 12 }}>
        <div style={{ fontWeight: 600 }}>{t('props.selectedCount', { count: sel.length })}</div>
        <div className="group">
          <div className="group-title">
            {t('props.alignTitle')}
            {dot('designer.align')}
          </div>
          <div className="align-grid">
            {(
              [
                [t('props.alignLeft'), 'left'],
                [t('props.alignCenterH'), 'centerH'],
                [t('props.alignRight'), 'right'],
                [t('props.alignTop'), 'top'],
                [t('props.alignCenterV'), 'centerV'],
                [t('props.alignBottom'), 'bottom'],
              ] as const
            ).map(([label, key]) => (
              <button key={key} className="btn sm" disabled={demoActive} onClick={() => onAlign(key)}>
                {label}
              </button>
            ))}
          </div>
        </div>
        <button className="btn danger" disabled={demoActive} onClick={() => onDelete(selected)}>
          <Icon name="trash" size={12} />
          {t('props.deleteSelected')}
        </button>
      </div>
    )
  }

  const e = sel[0]
  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 8 }}>
      <div style={{ fontWeight: 600, fontSize: 13 }}>
        {typeLabel(e)}
        {'key' in e && e.key ? <span className="mono" style={{ color: 'var(--accent)', marginLeft: 4 }}>{e.key}</span> : null}
      </div>

      <div className="group">
        <div className="group-title">
          {t('props.positionTitle')}
          {dot('designer.position')}
        </div>
        <NumField label="X" value={e.x} disabled={demoActive} onSet={(v) => onChange(e.id, { x: v })} />
        <NumField label="Y" value={e.y} disabled={demoActive} onSet={(v) => onChange(e.id, { y: v })} />
        {e.type !== 'Line' && (
          <>
            <NumField
              label={t('props.width')}
              value={e.w}
              disabled={demoActive}
              onSet={(v) => onChange(e.id, e.type === 'QrCode' ? { w: Math.max(1, v), h: Math.max(1, v) } : { w: Math.max(1, v) })}
            />
            <NumField
              label={t('props.height')}
              value={e.h}
              disabled={demoActive}
              onSet={(v) => onChange(e.id, e.type === 'QrCode' ? { h: Math.max(1, v), w: Math.max(1, v) } : { h: Math.max(1, v) })}
            />
          </>
        )}
      </div>

      {(e.type === 'Text' || e.type === 'Barcode' || e.type === 'QrCode') && (
        <>
          <ContentGroup
            e={e}
            onChange={onChange}
            disabled={demoActive}
            helpOpen={openDot === 'designer.fill'}
            onHelpToggle={() => toggleDot('designer.fill')}
            onHelpClose={() => changeDot(null)}
            onDemoStart={() => onDemoStart?.('designer.fill')}
            onDemoExit={() => onDemoExit?.()}
            demoActive={demoActive}
          />
          <div className="group">
            <div className="group-title">
              {t('props.boxTitle')}
              {dot('designer.box')}
            </div>
            <NumField label={t('props.paddingH')} value={e.paddingH ?? 0} disabled={demoActive} onSet={(v) => onChange(e.id, { paddingH: Math.max(0, v) })} />
            <NumField label={t('props.paddingV')} value={e.paddingV ?? 0} disabled={demoActive} onSet={(v) => onChange(e.id, { paddingV: Math.max(0, v) })} />
            <NumField label={t('props.border')} value={e.border ?? 0} disabled={demoActive} onSet={(v) => onChange(e.id, { border: Math.max(0, v) })} />
          </div>
        </>
      )}

      {e.type === 'Text' && (
        <div className="group">
          <div className="group-title">
            {t('props.textTitle')}
            {dot('designer.text')}
          </div>
          <SelectField
            label={t('props.font')}
            value={e.fontFamily || 'Microsoft YaHei'}
            disabled={demoActive}
            options={[
              [t('props.fontYaHei'), 'Microsoft YaHei'],
              [t('props.fontSimSun'), 'SimSun'],
              [t('props.fontSimHei'), 'SimHei'],
              [t('props.fontKaiTi'), 'KaiTi'],
              ['Arial', 'Arial'],
              ['Consolas', 'Consolas'],
            ]}
            onSet={(v) => onChange(e.id, { fontFamily: v })}
          />
          <NumField
            label={t('props.fontHeight')}
            value={e.fontH}
            disabled={demoActive}
            onSet={(v) => {
              const fontH = Math.max(1, v)
              onChange(e.id, fontH > e.h ? { fontH, h: fontH } : { fontH })
            }}
          />
          <CheckField label={t('props.wrap')} value={e.wrap === true} disabled={demoActive} onSet={(v) => onChange(e.id, { wrap: v })} />
          <CheckField label={t('props.bold')} value={e.bold === true} disabled={demoActive} onSet={(v) => onChange(e.id, { bold: v })} />
          <NumField label={t('props.lineHeight')} value={e.lineHeight ?? 1.2} disabled={demoActive} onSet={(v) => onChange(e.id, { lineHeight: Math.max(1, v) })} />
          <SelectField
            label={t('props.alignH')}
            value={e.align}
            disabled={demoActive}
            options={[
              [t('props.alignOptionLeft'), 'Left'],
              [t('props.alignOptionCenter'), 'Center'],
              [t('props.alignOptionRight'), 'Right'],
            ]}
            onSet={(v) => onChange(e.id, { align: v as 'Left' | 'Center' | 'Right' })}
          />
          <SelectField
            label={t('props.alignV')}
            value={e.valign ?? 'middle'}
            disabled={demoActive}
            options={[
              [t('props.valignOptionTop'), 'top'],
              [t('props.valignOptionMiddle'), 'middle'],
              [t('props.valignOptionBottom'), 'bottom'],
            ]}
            onSet={(v) => onChange(e.id, { valign: v as 'top' | 'middle' | 'bottom' })}
          />
          <SelectField
            label={t('props.fitMode')}
            value={e.fitMode ?? 'shrink'}
            disabled={demoActive}
            options={[
              [t('props.fitOptionShrink'), 'shrink'],
              [t('props.fitOptionOverflow'), 'overflow'],
            ]}
            onSet={(v) => onChange(e.id, { fitMode: v as 'shrink' | 'overflow' })}
          />
        </div>
      )}

      {e.type === 'Barcode' && (
        <div className="group">
          <div className="group-title">
            {t('props.barcodeTitle')}
            {dot('designer.barcode')}
          </div>
          <SelectField
            label={t('props.barcodeFormat')}
            value={e.barcodeFormat || 'CODE128'}
            disabled={demoActive}
            options={[
              ['Code128', 'CODE128'],
              ['EAN13', 'EAN13'],
              ['CODE39', 'CODE39'],
              ['UPC', 'UPC'],
            ]}
            onSet={(v) => onChange(e.id, { barcodeFormat: v })}
          />
          <CheckField label={t('props.displayValue')} value={e.displayValue !== false} disabled={demoActive} onSet={(v) => onChange(e.id, { displayValue: v })} />
          <NumField label={t('props.moduleWidth')} value={e.moduleWidth ?? 1} disabled={demoActive} onSet={(v) => onChange(e.id, { moduleWidth: Math.max(0.5, v) })} />
        </div>
      )}

      {e.type === 'QrCode' && (
        <div className="group">
          <div className="group-title">
            {t('props.qrTitle')}
            {dot('designer.qrcode')}
          </div>
          <SelectField
            label={t('props.qrEcc')}
            value={e.qrEcc ?? 'M'}
            disabled={demoActive}
            options={[
              [t('props.qrEccL'), 'L'],
              [t('props.qrEccM'), 'M'],
              [t('props.qrEccQ'), 'Q'],
              [t('props.qrEccH'), 'H'],
            ]}
            onSet={(v) => onChange(e.id, { qrEcc: v as 'L' | 'M' | 'Q' | 'H' })}
          />
          <NumField label={t('props.qrMargin')} value={e.qrMargin ?? 2} disabled={demoActive} onSet={(v) => onChange(e.id, { qrMargin: Math.max(0, v) })} />
        </div>
      )}

      {e.type === 'Rect' && (
        <div className="group">
          <div className="group-title">
            {t('props.rectTitle')}
            {dot('designer.rect')}
          </div>
          <NumField label={t('props.border')} value={e.border ?? 0} disabled={demoActive} onSet={(v) => onChange(e.id, { border: Math.max(0, v) })} />
        </div>
      )}

      {e.type === 'Line' && (
        <div className="group">
          <div className="group-title">
            {t('props.lineTitle')}
            {dot('designer.line')}
          </div>
          <NumField label={t('props.lengthX')} value={e.w} disabled={demoActive} onSet={(v) => onChange(e.id, { w: v })} />
          <NumField label={t('props.lengthY')} value={e.h} disabled={demoActive} onSet={(v) => onChange(e.id, { h: v })} />
          <NumField label={t('props.thickness')} value={e.thickness ?? 0.5} disabled={demoActive} onSet={(v) => onChange(e.id, { thickness: Math.max(0.1, v) })} />
        </div>
      )}

      {e.type === 'Region' && (
        <div className="group">
          <div className="group-title">
            {/* 兼容组收敛为仅容器——图片元素有专属「图片」组（其深链锚点 designer.image） */}
            {t('type.region')}
            {dot('designer.compat')}
          </div>
          <div className="hint">{t('props.regionId', { id: e.containerId })}</div>
          <NumField label={t('props.border')} value={e.border ?? 0} disabled={demoActive} onSet={(v) => onChange(e.id, { border: Math.max(0, v) })} />
        </div>
      )}

      {e.type === 'Image' && (
        <div className="group">
          <div className="group-title">
            {t('props.imageTitle')}
            {dot('designer.image')}
          </div>
          {/* 三态：key 空＝未指定（提示＋「上传图片」）；key 非空有资源＝元信息；
              key 非空缺资源＝资源缺失提示（可重新上传修复）。key 非空两态按钮组合不变（更换＋移除）。 */}
          {!e.key ? (
            <div className="hint">{t('props.imageEmpty')}</div>
          ) : imageAssets?.[e.key] ? (
            <div className="hint mono">{t('props.imageMeta', { key: e.key, w: imageAssets[e.key].width, h: imageAssets[e.key].height })}</div>
          ) : (
            <div className="hint">{t('props.imageMissing')}</div>
          )}
          <div style={{ display: 'flex', gap: 8, marginTop: 4 }}>
            <button className="btn sm" disabled={demoActive} onClick={() => onReplaceImage?.(e.id)}>
              <Icon name="upload" size={12} />
              {e.key ? t('props.imageReplace') : t('props.imageUpload')}
            </button>
            <button className="btn sm" disabled={demoActive} onClick={() => onDelete([e.id])}>
              <Icon name="trash" size={12} />
              {t('props.imageRemove')}
            </button>
          </div>
          <NumField label={t('props.border')} value={e.border ?? 0} disabled={demoActive} onSet={(v) => onChange(e.id, { border: Math.max(0, v) })} />
        </div>
      )}

      <button className="btn danger" disabled={demoActive} onClick={() => onDelete([e.id])}>
        <Icon name="trash" size={12} />
        {t('props.deleteElement')}
      </button>
    </div>
  )
}

/** 填充：固定值 / 字段填充（字段名 + 显示名 + 预览值）；标题挂「?」文档点（内嵌实时演示）。 */
function ContentGroup({
  e,
  onChange,
  disabled,
  helpOpen,
  onHelpToggle,
  onHelpClose,
  onDemoStart,
  onDemoExit,
  demoActive,
}: {
  e: DesignElement
  onChange: (id: string, patch: Partial<DesignElement>) => void
  disabled?: boolean
  helpOpen: boolean
  onHelpToggle: () => void
  onHelpClose: () => void
  onDemoStart: () => void
  onDemoExit: () => void
  demoActive?: boolean
}) {
  const { t } = useTranslation('designer')
  if (e.type !== 'Text' && e.type !== 'Barcode' && e.type !== 'QrCode') return null
  const set = (patch: Partial<typeof e>) => onChange(e.id, patch)
  return (
    <div className="group">
      <div className="group-title">
        {t('props.fillTitle')}
        <HelpDot
          anchor="designer.fill"
          topicKey="topic.designer.fill"
          open={helpOpen}
          onToggle={onHelpToggle}
          onClose={onHelpClose}
          hasDemo
          demoActive={demoActive}
          onDemoStart={onDemoStart}
          onDemoExit={onDemoExit}
        />
      </div>
      <SelectField
        label={t('props.source')}
        value={e.mode}
        disabled={disabled}
        options={[
          [t('props.sourceLiteral'), 'literal'],
          [t('props.sourceField'), 'field'],
        ]}
        onSet={(v) => set(v === 'literal' ? { mode: 'literal', key: '', displayName: undefined } : { mode: 'field' })}
      />
      {e.mode === 'literal' ? (
        <label className="field">
          {t('props.literalLabel')}
          <input className="input" value={e.text} disabled={disabled} onChange={(ev) => set({ text: ev.target.value })} placeholder={t('props.literalPlaceholder')} />
        </label>
      ) : (
        <>
          <label className="field">
            {t('props.fieldKeyLabel')}
            <input className="input mono" value={e.key} disabled={disabled} onChange={(ev) => set({ key: ev.target.value })} placeholder={t('props.fieldKeyPlaceholder')} />
          </label>
          <label className="field">
            {t('props.displayNameLabel')}
            <input className="input" value={e.displayName ?? ''} disabled={disabled} onChange={(ev) => set({ displayName: ev.target.value })} placeholder={t('props.displayNamePlaceholder')} />
          </label>
          <label className="field">
            {t('props.previewValueLabel')}
            <input className="input" value={e.text} disabled={disabled} onChange={(ev) => set({ text: ev.target.value })} placeholder={t('props.previewValuePlaceholder')} />
          </label>
        </>
      )}
    </div>
  )
}

function NumField({ label, value, disabled, onSet }: { label: string; value: number; disabled?: boolean; onSet: (v: number) => void }) {
  // 受控 + 同步：value 变化（切换选中元素）时输入框跟随刷新——非受控会在切换后残留旧元素的值（踩过）
  const [text, setText] = useState(Number(value || 0).toFixed(1))
  useEffect(() => setText(Number(value || 0).toFixed(1)), [value])
  return (
    <label className="num-row">
      <span>{label}</span>
      <input
        className="input"
        type="number"
        step="0.5"
        value={text}
        disabled={disabled}
        onChange={(ev) => setText(ev.target.value)}
        onBlur={(ev) => {
          const v = parseFloat(ev.target.value)
          if (!isNaN(v) && v !== value) onSet(v)
          else setText(Number(value || 0).toFixed(1))
        }}
        onKeyDown={(ev) => {
          if (ev.key === 'Enter') (ev.target as HTMLInputElement).blur()
        }}
      />
    </label>
  )
}

function SelectField({ label, value, disabled, options, onSet }: { label: string; value: string; disabled?: boolean; options: [string, string][]; onSet: (v: string) => void }) {
  return (
    <label className="num-row">
      <span>{label}</span>
      <select className="input" value={value} disabled={disabled} onChange={(ev) => onSet(ev.target.value)}>
        {options.map(([text, val]) => (
          <option key={val} value={val}>
            {text}
          </option>
        ))}
      </select>
    </label>
  )
}

function CheckField({ label, value, disabled, onSet }: { label: string; value: boolean; disabled?: boolean; onSet: (v: boolean) => void }) {
  return (
    <label className="num-row">
      <span>{label}</span>
      <input type="checkbox" checked={value} disabled={disabled} onChange={(ev) => onSet(ev.target.checked)} style={{ width: 'auto' }} />
    </label>
  )
}
