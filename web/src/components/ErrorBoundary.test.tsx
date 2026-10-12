// @vitest-environment jsdom
// 全局错误兜底：渲染异常 → 错误页（仅文案 + 重新加载按钮）+ console.error 留痕，非白屏
// window.onerror / unhandledrejection 处理函数 → console.error 留痕（jsdom 手工派发 error 事件不触发
// window.onerror，故处理函数直接断言 + 安装接线单独验证）。

import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import type { ReactNode } from 'react'
import { ErrorBoundary } from './ErrorBoundary'
import {
  installGlobalErrorLogging,
  logUnhandledRejection,
  logUnhandledScriptError,
} from '../lib/errorLogging'

function Bomb({ message }: { message: string }): ReactNode {
  throw new Error(message)
}

afterEach(() => {
  vi.restoreAllMocks()
  cleanup()
})

describe('ErrorBoundary：渲染异常兜底', () => {
  it('子组件渲染异常 → 错误页文案 + 重新加载按钮（非白屏），console.error 留痕', () => {
    const errorSpy = vi.spyOn(console, 'error').mockImplementation(() => {})
    render(
      <ErrorBoundary>
        <Bomb message="渲染炸了" />
      </ErrorBoundary>,
    )

    // 错误页：仅文案 + 重新加载按钮，不透出异常细节
    expect(screen.getByText('界面出现异常')).toBeTruthy()
    expect(screen.queryByText(/渲染炸了/)).toBeNull()
    expect(screen.getByRole('button', { name: /重新加载/ })).toBeTruthy()

    // 开发线索：错误对象经 console.error 留痕
    expect(
      errorSpy.mock.calls.some((call) =>
        call.some((arg) => arg instanceof Error && arg.message === '渲染炸了'),
      ),
    ).toBe(true)
  })

  it('点击「重新加载」→ window.location.reload', () => {
    vi.spyOn(console, 'error').mockImplementation(() => {})
    // jsdom 的 Location#reload 不可重定义，替换 window.location 本体（descriptor 可配置）
    const originalLocation = window.location
    const reloadMock = vi.fn()
    Object.defineProperty(window, 'location', { value: { reload: reloadMock }, configurable: true })
    try {
      render(
        <ErrorBoundary>
          <Bomb message="再次渲染炸了" />
        </ErrorBoundary>,
      )
      fireEvent.click(screen.getByRole('button', { name: /重新加载/ }))
      expect(reloadMock).toHaveBeenCalledTimes(1)
    } finally {
      Object.defineProperty(window, 'location', { value: originalLocation, configurable: true })
    }
  })

  it('子组件正常 → 原样渲染（不干预）', () => {
    render(
      <ErrorBoundary>
        <div>正常内容</div>
      </ErrorBoundary>,
    )
    expect(screen.getByText('正常内容')).toBeTruthy()
    expect(screen.queryByText('界面出现异常')).toBeNull()
  })
})

describe('全局错误监听：console.error 留痕', () => {
  it('window.onerror 处理：错误对象优先留痕', () => {
    const errorSpy = vi.spyOn(console, 'error').mockImplementation(() => {})
    const boom = new Error('boom')

    logUnhandledScriptError('boom', 'app.ts', 1, 2, boom)

    expect(errorSpy).toHaveBeenCalledTimes(1)
    expect(errorSpy.mock.calls[0][0]).toContain('[LabelFrame][window.onerror]')
    expect(errorSpy.mock.calls[0][1]).toBe(boom)
  })

  it('window.onerror 处理：无错误对象时留消息文本', () => {
    const errorSpy = vi.spyOn(console, 'error').mockImplementation(() => {})

    logUnhandledScriptError('raw message')

    expect(errorSpy.mock.calls[0][0]).toContain('[LabelFrame][window.onerror]')
    expect(errorSpy.mock.calls[0][1]).toBe('raw message')
  })

  it('unhandledrejection 处理：reason 留痕', () => {
    const errorSpy = vi.spyOn(console, 'error').mockImplementation(() => {})
    const reason = new Error('rejected')

    logUnhandledRejection({ reason } as PromiseRejectionEvent)

    expect(errorSpy.mock.calls[0][0]).toContain('[LabelFrame][unhandledrejection]')
    expect(errorSpy.mock.calls[0][1]).toBe(reason)
  })

  it('installGlobalErrorLogging：挂上 window.onerror 与 unhandledrejection 监听', () => {
    const errorSpy = vi.spyOn(console, 'error').mockImplementation(() => {})
    installGlobalErrorLogging()
    expect(window.onerror).toBe(logUnhandledScriptError)

    // unhandledrejection 经 addEventListener 挂载：派发事件（手工可触发 addEventListener 通道）
    const event = new Event('unhandledrejection') as PromiseRejectionEvent
    Object.defineProperty(event, 'reason', { value: new Error('late rejection') })
    window.dispatchEvent(event)

    expect(
      errorSpy.mock.calls.some(
        (call) => typeof call[0] === 'string' && call[0].includes('[LabelFrame][unhandledrejection]'),
      ),
    ).toBe(true)
  })
})
