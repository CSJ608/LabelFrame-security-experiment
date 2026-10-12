// UI 构建模式（双构建）：client = 客户端界面（WinHost 托管，默认）；server = 服务端管理界面插件。
// 构建时由 VITE_UI_MODE 决定（Windows：PowerShell $env:VITE_UI_MODE='server' 后 pnpm build:server，或 cross-env）；
// 测试中通过 vi.mock 本模块注入分支（vitest.config.ts 已 define 显式注入默认值 'client'）。

export type UiMode = 'client' | 'server'

export const UI_MODE: UiMode = import.meta.env.VITE_UI_MODE === 'server' ? 'server' : 'client'

/** Server UI（管理界面插件）：同源 API、无打印机相关内容、无单机降级。 */
export const isServerUi = UI_MODE === 'server'
