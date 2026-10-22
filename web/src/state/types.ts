// 应用级共享类型（独立文件避免 App ↔ 页面循环导入）

export type TabId = 'workbench' | 'designer' | 'data' | 'devices' | 'jobs' | 'settings' | 'packages' | 'plugin-packages' | 'help'

export interface DesignerRequest {
  kind: 'new' | 'edit'
  name?: string
  /** 编辑目标的模板稳定标识（工作台列表项透传；旧后端缺失时为 undefined）。 */
  uid?: string
}
