// 设计器快捷操作清单（增强：画布常驻提示条 + 工具栏「快捷键」弹窗完整清单）
// 文案 key 化（designer 域 shortcuts.*）——改为函数在调用期取当前界面语言
// 消费组件（Designer 弹窗 / CanvasViewport 提示条）经 useTranslation 订阅语言切换后重渲染刷新。

import i18next from '../../i18n'

export interface ShortcutItem {
  keys: string[]
  desc: string
}

export interface ShortcutGroup {
  title: string
  items: ShortcutItem[]
}

/** 完整清单（title / desc 随当前界面语言；键位字符串不翻译）。 */
export function shortcutGroups(): ShortcutGroup[] {
  return [
    {
      title: i18next.t('designer:shortcuts.groupEdit'),
      items: [
        { keys: ['Ctrl+Z', 'Ctrl+Y'], desc: i18next.t('designer:shortcuts.undoRedo') },
        { keys: ['Delete', 'Backspace'], desc: i18next.t('designer:shortcuts.deleteSelected') },
        { keys: ['Esc'], desc: i18next.t('designer:shortcuts.cancelPlacement') },
      ],
    },
    {
      title: i18next.t('designer:shortcuts.groupClipboard'),
      items: [
        { keys: ['Ctrl+C', 'Ctrl+V'], desc: i18next.t('designer:shortcuts.copyPaste') },
        { keys: ['Ctrl+Shift+C', 'Ctrl+Shift+V'], desc: i18next.t('designer:shortcuts.exportImport') },
      ],
    },
    {
      title: i18next.t('designer:shortcuts.groupCanvas'),
      items: [
        { keys: [i18next.t('designer:shortcuts.keyMiddleDrag')], desc: i18next.t('designer:shortcuts.pan') },
        { keys: [i18next.t('designer:shortcuts.keyCtrlWheel')], desc: i18next.t('designer:shortcuts.zoom') },
        { keys: [i18next.t('designer:shortcuts.keyShiftCtrlClick')], desc: i18next.t('designer:shortcuts.multiSelect') },
        { keys: [i18next.t('designer:shortcuts.keyDrag')], desc: i18next.t('designer:shortcuts.dragMove') },
        { keys: [i18next.t('designer:shortcuts.keyDragHandle')], desc: i18next.t('designer:shortcuts.dragResize') },
      ],
    },
  ]
}

/** 画布顶部常驻提示条（编辑模式，与预览模式提示同款视觉；随当前界面语言）。 */
export function coreShortcuts(): string {
  return i18next.t('designer:shortcuts.core')
}
