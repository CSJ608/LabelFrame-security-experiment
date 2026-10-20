# Web i18n 机制说明

> 机制决策见 `docs/DESIGN.md`。本文件是执行层速查手册：新增文案照此流程走。

## 基座

- **库**：react-i18next 17 + i18next 26（`src/i18n/index.ts` 模块加载即完成初始化，`main.tsx` 于 React 挂载前 import）。
- **语言集**：`zh-CN`（源语言）与 `en`；`fallbackLng: 'zh-CN'`——en 空缺词条回退中文不崩。
- **首启语言**：localStorage `labelframe.locale`（仅显式切换写入）→ 浏览器语言探测（`zh*` → zh-CN，其他 → en）。切换统一走 `changeLocale(locale)`（持久化 + `<html lang>` / `<title>` 同步）。
- **格式化**：日期 / 时间统一走本模块导出的 `formatDate / formatDateTime / formatLogTime(locale, date)`（显式 locale 入参，24 小时制）。

## 语言包组织（按域单文件）

- 路径：`src/i18n/locales/<locale>/<域>.json`，zh-CN 与 en 目录同构、文件同名、key 集合一致。
- **域 = 一个 UI 面**：`common`（跨页复用词条）/ `shell`（应用壳层）/ `settings` / `workbench` / `jobHistory` / `dataPrint` / `designer` / `downloadCenter` / `devices` / `pluginPackages` / `errorCodes`（后端 LF_* 码表）。
- 新增域须三处同步：zh-CN / en 两个 JSON + `src/i18n/index.ts` 注册 + `src/i18n/locales.test.ts` 的 `NAMESPACES`（漏注册则 `locales.test.ts` 红）。

## key 惯例

1. **key = 语义 camelCase，按 UI 区域分组前缀**：`nav.workbench`、`statusbar.localIps`、`props.font`——前缀 = UI 区域（不是数据实体）。
2. **组件内 `useTranslation('<域>')` 绑定域命名空间**，域内 key 不带前缀书写：`t('serverAddress.test')`；**跨域复用 common 词条免前缀**（全局 `fallbackNS: 'common'`）：`t('action.cancel')` 直接解析。
3. **插值用双花括号**：`"已删除 {{count}} 个元素。"` ↔ `t('status.deleted', { count })`——插值值是运行时数据，不进词条。
4. **en 词条风格**：按钮 / 导航标题式（Title Case）；句子 sentence case；冒号提示语保留；语言选项名固定各自语言原文（「中文」/「English」不随当前语言翻译）。
5. **富文案（词条内嵌标签）用 Trans 位置语法**：词条 `<0>…</0>` + `components={[<b key={0} />]}` 数组形式——命名组件形式（`components={{ b: <b/> }}`）在 react-i18next 17 下不把文本注入子元素（实测锚定的坑）。
6. **异步闭包内的文案**（轮询 catch、请求失败分支等）：不捕组件 `t`（语言切换后闭包过期），改读 i18next 单例 `i18next.t('<域>:<key>')`——文案构造期即当时界面语言（先例：`errorMessages.ts`、`client.ts` 兜底、`AppContext` 状态消息）。
7. **非 React 模块的显示文案**（lib 层标签 / 默认值，如 `lib/design/types.ts` 的 `typeLabel`、`lib/transport.ts` 的 `modeLabel`）：改为函数在调用期读 i18next 单例；消费组件持 `useTranslation` 订阅，语言切换后随重渲染刷新。

## 新增文案流程

1. 选域：页面专属 → 该页域文件；≥2 页复用 → `common`。
2. zh-CN 与 en 两份 JSON 同步加 key（值一一对应）。
3. 组件内 `t('key')`（域内）/ `t('commonKey')`（common 兜底）替换硬编码。
4. `pnpm lint`（圈禁拦截裸中文 JSX，见下）+ `pnpm test`（`locales.test.ts` 校验 key 覆盖）本地过再提 PR。

## en 词条维护 + 覆盖断言

- `src/i18n/locales.test.ts` 断言 **en 覆盖 zh-CN 全部 key**（缺失即红、多出的死词条同样红），按域逐文件报错定位。
- 防误删：断言同时要求每个域文件 key 集合非空。
- 运行期 en 空缺回退中文（`fallbackLng`），断言是防线不是兜底——翻译欠账必须在 CI 挡住。

## 圈禁防线（全站）

- `web/.oxlintrc.json` 顶层规则 `labelframe/no-bare-cjk-jsx: error`——**全站生效**，新增页面自动受保护：源码内新增裸中文 JSX（含 JSX 属性字符串）被 `pnpm lint` 拦截。
- 白名单（`overrides` 关闭该规则的文件）：仅测试 fixtures 中以中文 JSX 为测试对象的用例（`Popover.test.tsx`、`ErrorBoundary.test.tsx`）；语言包 JSON 不在 oxlint 范围。
- oxlint jsPlugins 为 alpha 能力（`oxlint-plugin-labelframe.cjs` 文件头说明）——oxlint 升级后如防线失效须第一时间修复。
- 允许保留的中文（不在拦截范围）：代码注释、console / 日志前缀等诊断字符串、开发者向报错（如 `useApp 必须在 AppProvider 内使用`）、`client.ts` 内部模式判别字符串（仅用于分支，不渲染）。

## 数据性默认值跟随语言

- **UI 层默认值随 locale、模板存储值不受语言影响**：新建文本元素默认占位「文本」/「Text」（`designer:elementDefaultText`，`defaultElement` 调用期取词）；设计器分组默认「默认」/「Default」同理。
- 存量模板 Literal 不动：已保存的元素文本 / 分组名是存储值，语言切换不回写、不回译（`pagesBilingualBatch2.test.tsx` 锚定）。
