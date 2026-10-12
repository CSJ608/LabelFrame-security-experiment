/// <reference types="vite/client" />

interface ImportMetaEnv {
  /* * UI 构建模式：client = 客户端界面（默认）；server = 服务端管理界面插件（vite build --outDir dist-server）。 */
  readonly VITE_UI_MODE?: string
}
