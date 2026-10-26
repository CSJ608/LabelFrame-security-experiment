// 主导航元数据单源：SERVER_TABS / CLIENT_TABS 供三处共享派生（App 导航渲染 / 路由白名单
// ROUTE_ALLOWED / 帮助页集 helpPagesFor），防同款页集在多处复制漂移；label 经 shell 域 key
// 在渲染期求值（t() 绑定当前语言，切换即时生效）。

import type { TabId } from '../state/types'
import type { IconName } from '../components/Icon'

export interface TabMeta {
  id: TabId
  labelKey: string
  icon: IconName
  /* * 导航分组：work = 日常作业页；system = 配置 / 分发 / 文档页。组间渲染分隔线。 */
  group: 'work' | 'system'
}

export const SERVER_TABS: TabMeta[] = [
  { id: 'workbench', labelKey: 'nav.workbench', icon: 'workbench', group: 'work' },
  { id: 'designer', labelKey: 'nav.designer', icon: 'designer', group: 'work' },
  { id: 'data', labelKey: 'nav.data', icon: 'data', group: 'work' },
  { id: 'devices', labelKey: 'nav.devices', icon: 'grid', group: 'work' },
  { id: 'jobs', labelKey: 'nav.jobs', icon: 'history', group: 'work' },
  // 「下载中心」：客户端安装包 + PDA APK 同页分区、扫码下载
  { id: 'packages', labelKey: 'nav.packages', icon: 'download', group: 'system' },
  // 「插件管理」：插件包列表 / 上传 / 下载 / 删除
  { id: 'plugin-packages', labelKey: 'nav.pluginPackages', icon: 'puzzle', group: 'system' },
  // 「帮助」末位（与 client 同位）；/api/logs 端点与 logs.db 保留（「设备日志」页下线后未来回传地基）
  { id: 'help', labelKey: 'nav.help', icon: 'help', group: 'system' },
]

export const CLIENT_TABS: TabMeta[] = [
  { id: 'workbench', labelKey: 'nav.workbench', icon: 'workbench', group: 'work' },
  { id: 'designer', labelKey: 'nav.designer', icon: 'designer', group: 'work' },
  { id: 'data', labelKey: 'nav.data', icon: 'data', group: 'work' },
  { id: 'jobs', labelKey: 'nav.jobs', icon: 'history', group: 'work' },
  { id: 'settings', labelKey: 'nav.settings', icon: 'settings', group: 'system' },
  // 主导航末位「帮助」（双构建均挂载）
  { id: 'help', labelKey: 'nav.help', icon: 'help', group: 'system' },
]
