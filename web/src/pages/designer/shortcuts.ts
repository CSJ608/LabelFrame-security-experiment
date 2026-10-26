// 设计器快捷操作清单（工具栏「快捷键」弹窗的唯一清单来源）。
// 取词为函数形态（调用期取当前界面语言）——改为模块级常量则语言切换后不刷新。

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
        { keys: ['Ctrl+S'], desc: i18next.t('designer:shortcuts.save') },
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
