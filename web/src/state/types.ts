// 应用级共享类型（独立文件避免 App ↔ 页面循环导入）

export type TabId = 'workbench' | 'designer' | 'data' | 'devices' | 'jobs' | 'settings' | 'packages' | 'plugin-packages' | 'help'

export interface DesignerRequest {
  kind: 'new' | 'edit'
  name?: string
}
