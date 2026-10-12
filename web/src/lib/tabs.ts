// 主导航元数据单源（·）：SERVER_TABS / CLIENT_TABS 自 App.tsx 迁入——
// 「某构建有哪些页」这一事实只在纯数据层维护，供三处共享派生（App 导航渲染 / 路由白名单
// ROUTE_ALLOWED / 帮助页集 helpPagesFor），防同款页集在多处复制漂移；label 经 shell 域 key
// 在渲染期求值（t() 绑定当前语言，切换即时生效）。

import type { TabId } from '../state/types'
import type { IconName } from '../components/Icon'

export interface TabMeta {
  id: TabId
  labelKey: string
  icon: IconName
}

export const SERVER_TABS: TabMeta[] = [
  { id: 'workbench', labelKey: 'nav.workbench', icon: 'workbench' },
  { id: 'designer', labelKey: 'nav.designer', icon: 'designer' },
  { id: 'data', labelKey: 'nav.data', icon: 'data' },
  { id: 'devices', labelKey: 'nav.devices', icon: 'grid' },
  { id: 'jobs', labelKey: 'nav.jobs', icon: 'history' },
  // Server UI「客户端下载」页升级为统一「下载中心」——客户端安装包 + PDA APK 同页分区、扫码下载
  { id: 'packages', labelKey: 'nav.packages', icon: 'download' },
  // Server UI「插件管理」页（插件包列表 / 上传 / 下载 / 删除，与「客户端下载」并列）
  { id: 'plugin-packages', labelKey: 'nav.pluginPackages', icon: 'puzzle' },
  // 「设备日志」页下线——/api/logs 端点与 logs.db 保留（未来回传地基）
  // server 管理界面纳入帮助体系——「帮助」页末位追加（与 client 同位）
  // #/help 随路由白名单派生自动从越权回退反转为可达
  { id: 'help', labelKey: 'nav.help', icon: 'help' },
]

export const CLIENT_TABS: TabMeta[] = [
  { id: 'workbench', labelKey: 'nav.workbench', icon: 'workbench' },
  { id: 'designer', labelKey: 'nav.designer', icon: 'designer' },
  { id: 'data', labelKey: 'nav.data', icon: 'data' },
  { id: 'jobs', labelKey: 'nav.jobs', icon: 'history' },
  { id: 'settings', labelKey: 'nav.settings', icon: 'settings' },
  // 主导航第 6 项「帮助」（帮助页文档底座）；起双构建均挂载
  { id: 'help', labelKey: 'nav.help', icon: 'help' },
]
