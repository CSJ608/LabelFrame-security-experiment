import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import './styles.css'
// i18n 基座（·）：模块加载即同步初始化（语言探测 + <html lang> 对齐），先于 React 挂载
import './i18n'
import App from './App.tsx'
import { ErrorBoundary } from './components/ErrorBoundary.tsx'
import { installGlobalErrorLogging } from './lib/errorLogging.ts'

// 全局错误兜底：先装监听再挂载——挂载前后 / 渲染中的异常都有 console.error 留痕
// 渲染异常由最外层 ErrorBoundary 兜底为错误页（仅文案 + 重新加载按钮）
installGlobalErrorLogging()

createRoot(document.getElementById('root')!).render(
  <ErrorBoundary>
    <StrictMode>
      <App />
    </StrictMode>
  </ErrorBoundary>,
)
