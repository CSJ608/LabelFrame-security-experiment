// i18n 基座：react-i18next 接入 + 语言状态（localStorage 持久化
// 首启跟随浏览器语言：`zh*` → zh-CN，其他 → en）+ `index.html` lang 动态化 + Intl 日期 / 数字格式化。
//
// 语言包组织（待决议-2 结论，供 111/112 沿用）：按域单文件 JSON（`locales/<locale>/<域>.json`），
// 域 = common（跨页复用）/ shell（应用壳层）/ settings / …（页面迁移迭代按页新增域文件）；
// zh-CN 为源语言，en 覆盖 zh-CN 全部 key（vitest 断言，缺失即红）。
// key 惯例：语义 camelCase，按 UI 区域分组前缀（`nav.*` / `statusbar.*` / `clearLogsConfirm.*`）；
// 插值用 i18next 默认双花括号 `{{name}}`；组件内 `useTranslation('<域>')` 绑定域命名空间，
// 跨域复用 common 词条经 `fallbackNS` 兜底（无需 `common:` 前缀）。
//
// 注意：本模块在 import 时即完成 i18next 初始化（main.tsx 首个 import，先于 React 挂载），
// 语言探测只发生一次；运行期切换统一走 `changeLocale`（持久化 + changeLanguage + lang 属性同步）。

import i18next from 'i18next'
import { initReactI18next } from 'react-i18next'
import zhCNCommon from './locales/zh-CN/common.json'
import zhCNShell from './locales/zh-CN/shell.json'
import zhCNSettings from './locales/zh-CN/settings.json'
import zhCNErrorCodes from './locales/zh-CN/errorCodes.json'
import zhCNWorkbench from './locales/zh-CN/workbench.json'
import zhCNJobHistory from './locales/zh-CN/jobHistory.json'
import zhCNDataPrint from './locales/zh-CN/dataPrint.json'
import zhCNDesigner from './locales/zh-CN/designer.json'
import zhCNDownloadCenter from './locales/zh-CN/downloadCenter.json'
import zhCNDevices from './locales/zh-CN/devices.json'
import zhCNPluginPackages from './locales/zh-CN/pluginPackages.json'
import zhCNGuide from './locales/zh-CN/guide.json'
import zhCNHelp from './locales/zh-CN/help.json'
import enCommon from './locales/en/common.json'
import enShell from './locales/en/shell.json'
import enSettings from './locales/en/settings.json'
import enErrorCodes from './locales/en/errorCodes.json'
import enWorkbench from './locales/en/workbench.json'
import enJobHistory from './locales/en/jobHistory.json'
import enDataPrint from './locales/en/dataPrint.json'
import enDesigner from './locales/en/designer.json'
import enDownloadCenter from './locales/en/downloadCenter.json'
import enDevices from './locales/en/devices.json'
import enPluginPackages from './locales/en/pluginPackages.json'
import enGuide from './locales/en/guide.json'
import enHelp from './locales/en/help.json'

/* * 界面语言全集（zh-CN 为源语言；en 为第一翻译目标）。 */
export type AppLocale = 'zh-CN' | 'en'

export const APP_LOCALES: readonly AppLocale[] = ['zh-CN', 'en']

/** 语言偏好 localStorage 键（显式切换才写入；首启未写入时跟随浏览器语言）。 */
export const LOCALE_STORAGE_KEY = 'labelframe.locale'

/** 把 i18next 内部语言标识归一为 AppLocale（未知值回退源语言 zh-CN）。 */
export function toAppLocale(lng: string | undefined | null): AppLocale {
  return lng === 'en' ? 'en' : 'zh-CN'
}

/**
 * 首启语言探测（依赖注入形式便于组件测试模拟 localStorage 与 navigator.language）：
 * localStorage 已保存的偏好优先； 否则浏览器语言列表任一 `zh*` 开头 → zh-CN，其他 → en。
 */
export function detectInitialLocale(storage: Pick<Storage, 'getItem'> | null | undefined, languages: readonly string[]): AppLocale {
  const saved = storage?.getItem(LOCALE_STORAGE_KEY)
  if (saved === 'zh-CN' || saved === 'en') return saved
  return languages.some((l) => typeof l === 'string' && l.toLowerCase().startsWith('zh')) ? 'zh-CN' : 'en'
}

/** 浏览器语言列表（jsdom / Node 容错：无 navigator 时返回空 → 探测结果为 en）。 */
function navigatorLanguages(): string[] {
  if (typeof navigator === 'undefined') return []
  const langs = typeof navigator.languages !== 'undefined' ? Array.from(navigator.languages) : []
  if (langs.length > 0) return langs
  return typeof navigator.language === 'string' ? [navigator.language] : []
}

function readSavedLocale(): AppLocale | null {
  try {
    if (typeof window === 'undefined') return null
    return toAppLocaleOrNull(window.localStorage.getItem(LOCALE_STORAGE_KEY))
  } catch {
    // 隐私模式等容错：视为未保存
    return null
  }
}

function toAppLocaleOrNull(v: string | null): AppLocale | null {
  return v === 'zh-CN' || v === 'en' ? v : null
}

function currentLocaleFromEnv(): AppLocale {
  const saved = readSavedLocale()
  if (saved) return saved
  return detectInitialLocale(null, navigatorLanguages())
}

/** 同步 `index.html` 的 `lang` 属性（无 document 环境容错跳过）。 */
function syncDocumentLang(lng: string): void {
  if (typeof document === 'undefined') return
  document.documentElement.lang = lng
}

// `index.html` 静态 <title>（LabelFrame 标签打印）随语言切换同步——
// 108 结项时暂留的壳层残余，用 shell 域 appTitle 词条（zh 值与原静态标题一致，中文态无变化）。
function syncDocumentTitle(lng: string): void {
  if (typeof document === 'undefined') return
  document.title = i18next.t('appTitle', { lng, ns: 'shell' })
}

// 单例初始化：同步资源（静态 import 打包，无网络请求，client / server 双构建离线可用）；
// 挂 React 绑定（react-i18next）；fallbackLng = 源语言 zh-CN（空缺词条回退中文不崩， 前提）
// useSuspense: false —— 组件树无 Suspense 边界，同步资源下禁用 Suspense 语义即可即时返回。
void i18next.use(initReactI18next).init({
  resources: {
    'zh-CN': {
      common: zhCNCommon,
      shell: zhCNShell,
      settings: zhCNSettings,
      errorCodes: zhCNErrorCodes,
      workbench: zhCNWorkbench,
      jobHistory: zhCNJobHistory,
      dataPrint: zhCNDataPrint,
      designer: zhCNDesigner,
      downloadCenter: zhCNDownloadCenter,
      devices: zhCNDevices,
      pluginPackages: zhCNPluginPackages,
      guide: zhCNGuide,
      help: zhCNHelp,
    },
    en: {
      common: enCommon,
      shell: enShell,
      settings: enSettings,
      errorCodes: enErrorCodes,
      workbench: enWorkbench,
      jobHistory: enJobHistory,
      dataPrint: enDataPrint,
      designer: enDesigner,
      downloadCenter: enDownloadCenter,
      devices: enDevices,
      pluginPackages: enPluginPackages,
      guide: enGuide,
      help: enHelp,
    },
  },
  lng: currentLocaleFromEnv(),
  fallbackLng: 'zh-CN',
  defaultNS: 'common',
  fallbackNS: 'common',
  react: { useSuspense: false },
  interpolation: { escapeValue: false }, // React 已做 XSS 转义，插值不再 escape
})

// 语言切换（含首启后的每一次切换）同步 <html lang> 与 <title>；初始化后立即对齐一次
i18next.on('languageChanged', syncDocumentLang)
i18next.on('languageChanged', syncDocumentTitle)
syncDocumentLang(i18next.language)
syncDocumentTitle(i18next.language)

/** 当前界面语言（读取 i18next 单例；React 组件内需要语言变化触发重渲染时用 useTranslation 订阅）。 */
export function currentLocale(): AppLocale {
  return toAppLocale(i18next.language)
}

/** 切换界面语言：localStorage 持久化 + changeLanguage（订阅组件随之重渲染，`<html lang>` 由事件同步）。 */
export function changeLocale(locale: AppLocale): void {
  try {
    if (typeof window !== 'undefined') window.localStorage.setItem(LOCALE_STORAGE_KEY, locale)
  } catch {
    // 隐私模式等容错：跳过持久化，仅本次会话生效
  }
  void i18next.changeLanguage(locale)
}

// Intl 格式化（替换硬编码 toLocaleString（'zh-CN'），跟随当前界面语言）

/** 日期时间（含时分秒，24 小时制）——工作台模板更新时间等列表展示用。 */
export function formatDateTime(locale: AppLocale, date: Date): string {
  return date.toLocaleString(locale, { hour12: false })
}

/** 日期（年月日）——列表纯日期列。 */
export function formatDate(locale: AppLocale, date: Date): string {
  return date.toLocaleDateString(locale)
}

/** 时间（时分秒，24 小时制）——运行日志行时间戳（AppContext.log）。 */
export function formatLogTime(locale: AppLocale, date: Date): string {
  return date.toLocaleTimeString(locale, { hour12: false })
}

export default i18next
