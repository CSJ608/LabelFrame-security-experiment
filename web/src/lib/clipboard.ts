// 剪贴板工具：navigator.clipboard 在页面无焦点 / 权限拒绝（NotAllowedError）/
// 非 secure context（局域网 HTTP）下不可用，此前 Ctrl+Shift+C/V 直接回退 prompt 弹窗要用户重复复制。
// 现提供：复制 = Clipboard API → 隐藏 textarea + execCommand('copy') 降级；读取 = Clipboard API → 一次性 paste 捕获降级。

/** 复制文本到剪贴板：优先现代 Clipboard API（secure context + 页面有焦点时），失败降级 execCommand。 */
export async function copyText(text: string): Promise<boolean> {
  if (navigator.clipboard?.writeText) {
    try {
      await navigator.clipboard.writeText(text)
      return true
    } catch {
      // NotAllowedError（无焦点 / 权限拒绝）等 → 降级传统方案
    }
  }
  return legacyCopy(text)
}

/** 隐藏 textarea + document.execCommand('copy')：非 secure context / 权限拒绝时的传统降级（需在用户手势内调用）。 */
export function legacyCopy(text: string): boolean {
  const ta = createHiddenTextarea(text)
  document.body.appendChild(ta)
  try {
    ta.focus()
    ta.select()
    ta.setSelectionRange(0, text.length)
    return document.execCommand('copy')
  } catch {
    return false
  } finally {
    document.body.removeChild(ta)
  }
}

/** 读取剪贴板文本：优先现代 Clipboard API；不可用 / 权限拒绝返回 ''（由调用方决定降级方式）。 */
export async function readClipboardText(): Promise<string> {
  if (navigator.clipboard?.readText) {
    try {
      return await navigator.clipboard.readText()
    } catch {
      return ''
    }
  }
  return ''
}

/**
 * 一次性捕获用户粘贴（读取权限不可用时的降级）：聚焦隐藏 textarea，用户按一次 Ctrl+V 即完成；
 * 超时 / 按 Esc 返回 ''。调用方应先 setStatus 提示用户操作。
 */
export function capturePasteOnce(timeoutMs = 15000): Promise<string> {
  return new Promise((resolve) => {
    const ta = createHiddenTextarea('')
    document.body.appendChild(ta)
    ta.focus()

    let settled = false
    const finish = (text: string) => {
      if (settled) return
      settled = true
      ta.removeEventListener('paste', onPaste)
      document.removeEventListener('keydown', onEscape, true)
      document.body.removeChild(ta)
      resolve(text)
    }
    const onPaste = (ev: ClipboardEvent) => {
      ev.preventDefault()
      finish(ev.clipboardData?.getData('text') ?? '')
    }
    const onEscape = (ev: KeyboardEvent) => {
      if (ev.key === 'Escape') {
        ev.preventDefault()
        finish('')
      }
    }
    ta.addEventListener('paste', onPaste)
    document.addEventListener('keydown', onEscape, true)
    window.setTimeout(() => finish(''), timeoutMs)
  })
}

function createHiddenTextarea(value: string): HTMLTextAreaElement {
  const ta = document.createElement('textarea')
  ta.value = value
  ta.setAttribute('readonly', '')
  ta.setAttribute('tabindex', '-1')
  ta.style.position = 'fixed'
  ta.style.top = '0'
  ta.style.left = '0'
  ta.style.width = '1px'
  ta.style.height = '1px'
  ta.style.padding = '0'
  ta.style.border = 'none'
  ta.style.opacity = '0'
  ta.style.pointerEvents = 'none'
  return ta
}
