# LabelFrame Web 前端

React + TypeScript + Vite + Konva；同一工程产出客户端界面与服务端管理界面两种构建。

## 命令

```bash
pnpm install
pnpm dev            # 开发（:5173）
pnpm lint           # oxlint
pnpm test           # vitest（client 模式；server 模式：VITE_UI_MODE=server pnpm test）
pnpm build          # 客户端产物 → web/dist（WinHost 托管）
pnpm build:server   # 服务端管理界面插件产物 → web/dist-server
```

## 双构建模式

由构建时环境变量 `VITE_UI_MODE` 区分（默认 `client`）：

- **client（`pnpm build`）**：完整界面——设计器 / 数据与打印 / 设置（连接 / 插件 / 更新）/ 作业历史 / 日志；API 指向本机 WinHost（127.0.0.1:53960，地址可配置）。
- **server（`pnpm build:server`）**：服务端管理界面——工作台 / 设计器 / 在线设备 / 客户端下载 / 插件管理 / 作业历史 / 设备日志；API 走同源相对路径；无打印机相关内容与单机降级分支。

产物自检：CI 构建后校验两产物特征（防止 `VITE_UI_MODE` 未生效把 client 产物打进 server 包）。

## i18n

- 基座：react-i18next（`src/i18n/index.ts` 模块加载即初始化；语言包 `src/i18n/locales/<locale>/<域>.json` 按域单文件，zh-CN 源 + en 全量覆盖）。
- 惯例：组件内 `useTranslation('<域>')` 绑定域命名空间，key 语义 camelCase 按 UI 区域分组（`nav.*` / `statusbar.*`），插值 `{{name}}`，跨域复用 common 词条经 fallbackNS 免前缀；语言切换走设置页「语言」卡片（localStorage `labelframe.locale` 持久化，首启跟随浏览器语言）。
- 防线：① `src/i18n/locales.test.ts` 断言 en 覆盖 zh-CN 全部 key（缺失即红）；② oxlint 自定义规则 `labelframe/no-bare-cjk-jsx`（插件 `oxlint-plugin-labelframe.cjs`）拦截**圈禁文件**内裸中文 JSX——圈禁清单在 `.oxlintrc.json` 的 `overrides.files` 显式维护，页面迁移一个文件就加一个。

## 开发联调

`vite dev`（:5173）的 proxy 按模式分支：server 模式指向 `http://127.0.0.1:53961`（服务端），client 模式指向 `http://127.0.0.1:53960`（本机客户端），覆盖 `/api` 与 `/healthz`。
