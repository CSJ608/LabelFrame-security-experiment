// 内联 SVG 图标（线条风格，stroke 1.6，与工具主题一致）

import type { SVGProps } from 'react'

export type IconName =
  | 'workbench' | 'designer' | 'data' | 'logs' | 'settings' | 'history'
  | 'plus' | 'edit' | 'trash' | 'download' | 'upload' | 'refresh'
  | 'retry' | 'test' | 'link' | 'printer' | 'clear' | 'layers'
  | 'check' | 'x' | 'search' | 'file' | 'grid' | 'zoom' | 'save'
  | 'back' | 'preview' | 'copy' | 'clipboard' | 'alert' | 'keyboard'
  | 'puzzle' | 'chevron' | 'more' | 'guide' | 'help' | 'toc'

const PATHS: Record<IconName, React.ReactNode> = {
  workbench: (
    <>
      <rect x="3" y="4" width="18" height="16" rx="2" />
      <path d="M3 9h18M8 4v5M16 4v5" />
    </>
  ),
  designer: (
    <>
      <path d="M13 3 5 11v3h3l8-8z" />
      <path d="m15 5 2 2" />
      <path d="M4 20h16" />
    </>
  ),
  data: (
    <>
      <rect x="3" y="4" width="18" height="16" rx="2" />
      <path d="M3 9h18M8 9v11M16 9v11" />
    </>
  ),
  logs: (
    <>
      <path d="M4 6h16M4 12h16M4 18h10" />
      <circle cx="17.5" cy="18" r="2.2" />
    </>
  ),
  settings: (
    <>
      <circle cx="12" cy="12" r="3" />
      <path d="M12 2v3M12 19v3M2 12h3M19 12h3M4.9 4.9l2.1 2.1M17 17l2.1 2.1M19.1 4.9 17 7M7 17l-2.1 2.1" />
    </>
  ),
  history: (
    <>
      <circle cx="12" cy="12" r="8" />
      <path d="M12 7v5l3.5 2" />
    </>
  ),
  plus: <path d="M12 5v14M5 12h14" />,
  edit: <path d="M17 3a2.85 2.83 0 1 1 4 4L7.5 20.5 2 22l1.5-5.5Z" />,
  trash: <path d="M4 7h16M9 7V4h6v3M6 7l1 13h10l1-13M10 11v5M14 11v5" />,
  download: <path d="M12 3v12m0 0 4-4m-4 4-4-4M4 19h16" />,
  upload: <path d="M12 15V3m0 0 4 4m-4-4-4 4M4 19h16" />,
  refresh: <path d="M20 12a8 8 0 1 1-2.3-5.6M20 4v4h-4" />,
  retry: <path d="M3 12a9 9 0 1 0 2.6-6.3M3 4v5h5" />,
  test: <path d="M12 3v6l5 8a2 2 0 0 1-1.7 3H8.7A2 2 0 0 1 7 17l5-8V3M12 3h-2M12 3h2" />,
  link: <path d="M10 14a5 5 0 0 0 7.5.5l2-2a5 5 0 0 0-7-7l-1.2 1.2M14 10a5 5 0 0 0-7.5-.5l-2 2a5 5 0 0 0 7 7l1.2-1.2" />,
  printer: (
    <>
      <path d="M6 9V3h12v6" />
      <rect x="3" y="9" width="18" height="8" rx="2" />
      <path d="M6 14h12v7H6z" />
    </>
  ),
  clear: <path d="M4 7h16M9 7V4h6v3M6 7l1 13h10l1-13" />,
  layers: <path d="m12 3 9 5-9 5-9-5 9-5zM3 13l9 5 9-5" />,
  check: <path d="m4 12.5 5 5L20 6.5" />,
  x: <path d="M6 6l12 12M18 6 6 18" />,
  search: (
    <>
      <circle cx="11" cy="11" r="6.5" />
      <path d="m16 16 5 5" />
    </>
  ),
  file: <path d="M6 2h8l4 4v16H6zM14 2v4h4" />,
  grid: (
    <>
      <rect x="3" y="3" width="7" height="7" rx="1" />
      <rect x="14" y="3" width="7" height="7" rx="1" />
      <rect x="3" y="14" width="7" height="7" rx="1" />
      <rect x="14" y="14" width="7" height="7" rx="1" />
    </>
  ),
  zoom: (
    <>
      <circle cx="11" cy="11" r="6.5" />
      <path d="m16 16 5 5" />
      <path d="M8 11h6M11 8v6" />
    </>
  ),
  save: <path d="M5 3h11l4 4v14H5zM8 3v5h7V3M8 21v-7h8v7" />,
  back: <path d="M15 5l-7 7 7 7" />,
  preview: (
    <>
      <path d="M2 12s3.5-6.5 10-6.5S22 12 22 12s-3.5 6.5-10 6.5S2 12 2 12z" />
      <circle cx="12" cy="12" r="2.6" />
    </>
  ),
  copy: (
    <>
      <rect x="8" y="8" width="12" height="12" rx="2" />
      <path d="M16 8V4H4v12h4" />
    </>
  ),
  clipboard: (
    <>
      <rect x="5" y="4" width="14" height="18" rx="2" />
      <path d="M9 4a3 3 0 0 1 6 0M9 11h6M9 15h6" />
    </>
  ),
  alert: <path d="M12 3 2.5 20h19zM12 9v5M12 17.5v.5" />,
  keyboard: (
    <>
      <rect x="2.5" y="6" width="19" height="12.5" rx="2" />
      <path d="M6.5 10h.01M9.5 10h.01M12.5 10h.01M15.5 10h.01M18.5 10h.01M6.5 14h11" />
    </>
  ),
  // 拼图块（插件管理页菜单图标）
  puzzle: (
    <>
      <rect x="4" y="4" width="16" height="16" rx="2.5" />
      <path d="M10 4V2.5a1.5 1.5 0 0 1 3 0V4" />
      <path d="M20 10h1.5a1.5 1.5 0 0 1 0 3H20" />
    </>
  ),
  // 下箭头（作业历史行展开 / 收起，展开时经 transform 旋转 180°）
  chevron: <path d="M6 9l6 6 6-6" />,
  // 三点（更多操作，工作台卡片 ⋯ 溢出菜单触发）
  more: <path d="M5 12h.01M12 12h.01M19 12h.01" strokeWidth={2.6} />,
  // 问号（状态栏「使用引导」重看入口图标）
  guide: (
    <>
      <circle cx="12" cy="12" r="9" />
      <path d="M9.4 9.3a2.7 2.7 0 0 1 5.2.9c0 1.8-2.6 2.2-2.6 3.8" />
      <path d="M12 17.2h.01" strokeWidth={2.6} />
    </>
  ),
  // 书本底问号（帮助页导航与「?」文档点；书本底与「使用引导」的圆圈问号区分，防同形混淆）
  help: (
    <>
      <path d="M4 19.5A2.5 2.5 0 0 1 6.5 17H20" />
      <path d="M6.5 2H20v20H6.5A2.5 2.5 0 0 1 4 19.5v-15A2.5 2.5 0 0 1 6.5 2z" />
      <path d="M10.1 8.6a2.4 2.4 0 0 1 4.7.8c0 1.6-2.4 2-2.4 3.4" />
      <path d="M12.4 15.6h.01" strokeWidth={2.6} />
    </>
  ),
  // 三横线（帮助页窄窗目录收起 / 展开切换——文档站目录通用形）
  toc: <path d="M4 6h16M4 12h16M4 18h16" />,
}

interface IconProps extends SVGProps<SVGSVGElement> {
  name: IconName
  size?: number
}

export function Icon({ name, size = 16, ...rest }: IconProps) {
  return (
    <svg
      viewBox="0 0 24 24"
      width={size}
      height={size}
      fill="none"
      stroke="currentColor"
      strokeWidth={1.6}
      strokeLinecap="round"
      strokeLinejoin="round"
      aria-hidden="true"
      {...rest}
    >
      {PATHS[name]}
    </svg>
  )
}

/* * 品牌图形 v02「剥离 / Peel & Feed」微型几何——深青标签纸 + 珊瑚剥离角。
 *  几何逐字取自品牌母版 symbol-color-24.svg（assets/brand/），固定填充不随 --accent
 * （--accent 测量蓝是界面语言非品牌标识，Issue 不在范围）。 */
export function LabelLogo({ size = 24 }: { size?: number }) {
  return (
    <svg viewBox="0 0 24 24" width={size} height={size} aria-hidden="true">
      <path fill="#123B3C" d="M6 3 H18 Q21 3 21 6 V14 C17.134 14 14 17.134 14 21 H6 Q3 21 3 18 V6 Q3 3 6 3 Z" />
      <path fill="#D96C4F" d="M21 15.75 C18.1005 15.75 15.75 18.1005 15.75 21 H21 Z" />
    </svg>
  )
}
