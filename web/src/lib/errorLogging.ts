// 全局错误监听：window.onerror / unhandledrejection 统一 console.error 留痕
// （开发线索，不做后端上报）；main.tsx 在挂载应用前安装。
// 处理函数单独导出供单元测试直接断言（jsdom 中手工派发 error 事件不触发 window.onerror，只走真实未捕获路径）。

/** window.onerror 处理：未捕获脚本异常留痕（优先错误对象，退化时仅消息文本）。 */
export function logUnhandledScriptError(
  message: string | Event,
  _source?: string,
  _lineno?: number,
  _colno?: number,
  error?: Error,
): void {
  console.error('[LabelFrame][window.onerror] 未捕获异常', error ?? message)
}

/** unhandledrejection 处理：未处理的 Promise 拒绝留痕。 */
export function logUnhandledRejection(event: PromiseRejectionEvent): void {
  console.error('[LabelFrame][unhandledrejection] 未处理的 Promise 拒绝', event.reason)
}

/** 安装全局错误监听：未捕获异常与未处理的 Promise 拒绝统一 console.error 留痕。 */
export function installGlobalErrorLogging(): void {
  if (typeof window === 'undefined') return
  window.onerror = logUnhandledScriptError
  window.addEventListener('unhandledrejection', logUnhandledRejection)
}
