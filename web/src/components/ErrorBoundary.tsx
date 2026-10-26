// 全局错误兜底：
// ErrorBoundary：渲染异常错误页（仅文案 + 重新加载按钮，不附错误摘要——避免透出内部细节；
// 开发线索由 componentDidCatch 的 console.error 承担）。
// window.onerror / unhandledrejection 监听见 lib/errorLogging.ts（main.tsx 挂载前安装）。
// 错误页文案 key 化（shell.errorBoundary.*；类组件无 hook，读 i18next 单例——错误页渲染于当前语言）。

import { Component } from 'react'
import type { ErrorInfo, ReactNode } from 'react'
import i18next from '../i18n'
import { Icon } from './Icon'

interface ErrorBoundaryProps {
  children: ReactNode
}

interface ErrorBoundaryState {
  hasError: boolean
}

export class ErrorBoundary extends Component<ErrorBoundaryProps, ErrorBoundaryState> {
  state: ErrorBoundaryState = { hasError: false }

  static getDerivedStateFromError(): ErrorBoundaryState {
    return { hasError: true }
  }

  componentDidCatch(error: unknown, errorInfo: ErrorInfo): void {
    console.error('[LabelFrame][ErrorBoundary] 界面渲染异常', error, errorInfo.componentStack)
  }

  render() {
    if (this.state.hasError) {
      return (
        <div className="page">
          <div className="empty" style={{ flex: 1 }}>
            <Icon name="alert" />
            <div className="empty-title">{i18next.t('shell:errorBoundary.title')}</div>
            <div className="hint">{i18next.t('shell:errorBoundary.hint')}</div>
            <button className="btn primary" onClick={() => window.location.reload()}>
              <Icon name="refresh" size={12} />
              {i18next.t('shell:errorBoundary.reload')}
            </button>
          </div>
        </div>
      )
    }

    return this.props.children
  }
}
