// 应用级共享类型（独立文件避免 App ↔ 页面循环导入）
// 迭代 22：TabId 增加 'packages'（Server UI 安装包分发页，迭代 59 起为统一「下载中心」）；迭代 23：增加 'plugin-packages'（Server UI「插件管理」页）。
// 迭代 75（#112）：移除 'logs'（「PDA 日志 / 设备日志」页下线，未来回传立项时恢复）。
// 迭代 126（#308）：增加 'help'（帮助页）；迭代 129（#320）起双构建均有入口（SERVER_TABS /
// CLIENT_TABS 均居末位，元数据单源 lib/tabs.ts）。

export type TabId = 'workbench' | 'designer' | 'data' | 'devices' | 'jobs' | 'settings' | 'packages' | 'plugin-packages' | 'help'

export interface DesignerRequest {
  kind: 'new' | 'edit'
  name?: string
}
