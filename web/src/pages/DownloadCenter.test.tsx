// @vitest-environment jsdom
// 下载中心页（迭代 118 改版 · #272）：页内三 tab（快速访问 / Windows 包管理 / Android 包管理，默认快速访问，
// tab 状态经路由 sub 段进 URL——迭代 127 · #312 决策 #177 由 #dc= 迁至 #/packages/<sub>，刷新 / 直链还原、
// 旧 #dc= 链接经 parseHash 兼容映射直达）；快速访问首屏 = 上排 Windows / Android「最新上传」卡（修改时间倒序
// 第一条的货架语义：文件名 / 大小 / 上传时间 / 下载二维码（origin + 下载路径）/ 下载按钮 /「全部版本 →」跳
// 管理卡、空态引导跳转上传）+ 下区块「服务端信息」卡（大二维码 = 选中裸地址 URL + 地址文本与复制 +
// 多网卡候选切换 / localhost 回退）；管理 tab（client-packages / pda-packages）上传 / 下载 / 删除（确认
// Modal）行为回归——数据与接口不动（AC-06）。

import { useState } from 'react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { useHashRoute } from '../lib/router'
import type { TabId } from '../state/types'
import { DownloadCenter } from './DownloadCenter'

const mocks = vi.hoisted(() => ({
  server: {
    listClientPackages: vi.fn(),
    uploadClientPackage: vi.fn(),
    deleteClientPackage: vi.fn(),
    listPdaPackages: vi.fn(),
    uploadPdaPackage: vi.fn(),
    deletePdaPackage: vi.fn(),
    listServerIpv4Candidates: vi.fn(),
  },
  copyText: vi.fn(),
}))

vi.mock('../lib/api/client', () => ({
  serverApi: mocks.server,
  clientPackageDownloadUrl: (fileName: string) => `/api/client-packages/${encodeURIComponent(fileName)}`,
  pdaPackageDownloadUrl: (fileName: string) => `/api/pda-packages/${encodeURIComponent(fileName)}`,
}))

vi.mock('../lib/clipboard', () => ({
  copyText: (...args: unknown[]) => mocks.copyText(...args),
}))

const CLIENT_PKGS = [
  { fileName: 'LabelFrame.Client-0.18.0.msi', sizeBytes: 42 * 1024 * 1024, modifiedAt: '2026-08-17T10:00:00Z', url: '/api/client-packages/LabelFrame.Client-0.18.0.msi' },
  { fileName: 'LabelFrame.Client-linux.zip', sizeBytes: 1024, modifiedAt: '2026-08-17T09:00:00Z' },
]

const PDA_PKGS = [
  { fileName: 'LabelFrame-AndroidHost-0.26.0.apk', sizeBytes: 22 * 1024 * 1024, modifiedAt: '2026-09-10T08:00:00Z', url: '/api/pda-packages/LabelFrame-AndroidHost-0.26.0.apk' },
  { fileName: 'LabelFrame-AndroidHost-0.25.0.apk', sizeBytes: 24 * 1024 * 1024, modifiedAt: '2026-09-01T08:00:00Z' },
]

beforeEach(() => {
  vi.clearAllMocks()
  window.location.hash = ''
  mocks.server.listClientPackages.mockResolvedValue(CLIENT_PKGS)
  mocks.server.uploadClientPackage.mockResolvedValue([])
  mocks.server.deleteClientPackage.mockResolvedValue(undefined)
  mocks.server.listPdaPackages.mockResolvedValue(PDA_PKGS)
  mocks.server.uploadPdaPackage.mockResolvedValue([])
  mocks.server.deletePdaPackage.mockResolvedValue(undefined)
  mocks.server.listServerIpv4Candidates.mockResolvedValue({ candidates: ['10.20.30.40', '192.168.1.9'] })
  mocks.copyText.mockResolvedValue(true)
})

afterEach(() => {
  cleanup()
})

/** 列表顺序断言辅助：按文本出现顺序取行号（同一文本只应出现一次）。 */
function indexOfText(text: string): number {
  const rows = Array.from(document.querySelectorAll('tbody tr'))
  return rows.findIndex((tr) => tr.textContent?.includes(text))
}

/** 切到平台管理 tab（快速访问默认 → 点击页内 tab 按钮）。 */
function gotoManageTab(label: string): void {
  fireEvent.click(screen.getByRole('button', { name: label }))
}

/**
 * 路由接线测试壳：等价 App 对 DownloadCenter 的接线（sub / onSubChange 经 useHashRoute 注入，
 * #/<page>/<sub> 两级形，迭代 127 · #312）；「去别页」按钮模拟主导航切走（pushState 入栈），
 * 供后退 / 前进还原 sub 用例使用。
 */
function DownloadCenterRouteHarness() {
  const [page, setPage] = useState<TabId>('packages')
  const route = useHashRoute({
    allowed: ['packages', 'jobs'],
    page,
    requestPage: (id) => {
      setPage(id)
      return true
    },
  })
  if (page !== 'packages') return <div data-testid="other-page" />
  return (
    <>
      <button onClick={() => setPage('jobs')}>go-other-page</button>
      <DownloadCenter sub={route.sub} onSubChange={route.setSub} />
    </>
  )
}

describe('下载中心页 · 三 tab 与路由 sub 段（迭代 118 · #272；迭代 127 · #312 迁 #/packages/<sub>）', () => {
  it('默认快速访问：三 tab 按钮在位，默认 tab 不写 sub 段（quick 为缺省值，hash 止于 #/packages）', async () => {
    render(<DownloadCenterRouteHarness />)
    expect(screen.getByRole('button', { name: '快速访问' })).toBeTruthy()
    expect(screen.getByRole('button', { name: 'Windows 包管理' })).toBeTruthy()
    expect(screen.getByRole('button', { name: 'Android 包管理' })).toBeTruthy()
    expect(await screen.findByText('客户端')).toBeTruthy()
    // 默认 tab 不写 sub 段（quick 为缺省值）
    await waitFor(() => expect(window.location.hash).toBe('#/packages'))
  })

  it('切换 tab 写 sub 段（replaceState 不入栈）：#/packages/windows / #/packages/android', async () => {
    render(<DownloadCenterRouteHarness />)
    await screen.findByText('LabelFrame.Client-0.18.0.msi')
    const len0 = window.history.length

    gotoManageTab('Windows 包管理')
    await waitFor(() => expect(window.location.hash).toBe('#/packages/windows'))
    // 管理视图：列表 + 上传按钮在位
    expect(await screen.findByText('LabelFrame.Client-linux.zip')).toBeTruthy()
    expect(screen.getByRole('button', { name: /上传 Windows 安装包/ })).toBeTruthy()

    gotoManageTab('Android 包管理')
    await waitFor(() => expect(window.location.hash).toBe('#/packages/android'))
    expect(await screen.findByText('LabelFrame-AndroidHost-0.25.0.apk')).toBeTruthy()
    expect(screen.getByRole('button', { name: /上传 APK/ })).toBeTruthy()
    // sub 变更 replaceState 不入栈（决策 #177）
    expect(window.history.length).toBe(len0)
  })

  it('直链还原：挂载前 hash 已是 #/packages/android → 直接呈现 Android 管理视图', async () => {
    window.location.hash = '#/packages/android'
    render(<DownloadCenterRouteHarness />)
    expect(await screen.findByText('LabelFrame-AndroidHost-0.25.0.apk')).toBeTruthy()
    // 快速访问首屏不渲染
    expect(screen.queryByText('客户端')).toBeNull()
  })

  it('旧 #dc= 分享链接兼容映射：#dc=android 直达 Android 管理，挂载期规范化为 #/packages/android', async () => {
    window.location.hash = '#dc=android'
    render(<DownloadCenterRouteHarness />)
    expect(await screen.findByText('LabelFrame-AndroidHost-0.25.0.apk')).toBeTruthy()
    await waitFor(() => expect(window.location.hash).toBe('#/packages/android'))
  })

  it('未知 sub 回退默认快速访问（挂载期规范化 URL）', async () => {
    window.location.hash = '#/packages/whatever'
    render(<DownloadCenterRouteHarness />)
    expect(await screen.findByText('客户端')).toBeTruthy()
    await waitFor(() => expect(window.location.hash).toBe('#/packages/whatever'))
  })

  it('运行中 URL 变更（hashchange）同步 tab：手动改地址栏到 #/packages/windows 也能切到 Windows 管理', async () => {
    render(<DownloadCenterRouteHarness />)
    await screen.findByText('LabelFrame.Client-0.18.0.msi')
    expect(screen.queryByText('LabelFrame.Client-linux.zip')).toBeNull()

    window.location.hash = '#/packages/windows'
    window.dispatchEvent(new HashChangeEvent('hashchange'))
    expect(await screen.findByText('LabelFrame.Client-linux.zip')).toBeTruthy()
  })

  it('后退 / 前进回到含 sub 的历史条目还原页内 tab（#/packages/windows ← #/jobs 后退）', async () => {
    render(<DownloadCenterRouteHarness />)
    await screen.findByText('客户端')
    expect(screen.queryByText('LabelFrame.Client-linux.zip')).toBeNull()
    gotoManageTab('Windows 包管理')
    await waitFor(() => expect(window.location.hash).toBe('#/packages/windows'))

    // 主导航切走（入栈）→ 后退回到含 sub 的历史条目
    fireEvent.click(screen.getByRole('button', { name: 'go-other-page' }))
    await waitFor(() => expect(window.location.hash).toBe('#/jobs'))
    window.history.back()
    await waitFor(() => expect(window.location.hash).toBe('#/packages/windows'))
    // 页内 tab 还原为 Windows 管理（非默认快速访问）
    expect(await screen.findByText('LabelFrame.Client-linux.zip')).toBeTruthy()
    expect(screen.queryByText('客户端')).toBeNull()
  })
})

describe('下载中心页 · 快速访问首屏「最新上传」卡（AC-02 / AC-03）', () => {
  it('最新一条选取：两卡各展示修改时间倒序第一条（旧版本不出现在快速访问）', async () => {
    render(<DownloadCenterRouteHarness />)
    // 最新一条在位
    expect(await screen.findByText('LabelFrame.Client-0.18.0.msi')).toBeTruthy()
    expect(screen.getByText('LabelFrame-AndroidHost-0.26.0.apk')).toBeTruthy()
    expect(screen.getAllByText('最新上传')).toHaveLength(2)
    // 旧版本只在管理 tab，快速访问不出现
    expect(screen.queryByText('LabelFrame.Client-linux.zip')).toBeNull()
    expect(screen.queryByText('LabelFrame-AndroidHost-0.25.0.apk')).toBeNull()
  })

  it('乱序输入：按修改时间倒序取最新（服务端顺序不保证时页面自排）', async () => {
    mocks.server.listClientPackages.mockResolvedValue([...CLIENT_PKGS].reverse())
    mocks.server.listPdaPackages.mockResolvedValue([...PDA_PKGS].reverse())
    render(<DownloadCenterRouteHarness />)
    expect(await screen.findByText('LabelFrame.Client-0.18.0.msi')).toBeTruthy()
    expect(screen.getByText('LabelFrame-AndroidHost-0.26.0.apk')).toBeTruthy()
    expect(screen.queryByText('LabelFrame.Client-linux.zip')).toBeNull()
  })

  it('卡内容：大小 / 上传时间 / 下载二维码（origin + 下载路径完整 URL）/ 下载按钮 / 「全部版本 →」', async () => {
    render(<DownloadCenterRouteHarness />)
    await screen.findByText('LabelFrame.Client-0.18.0.msi')

    const origin = window.location.origin
    // 大小 / 上传时间（与文件名同卡展示，组合文本按包含断言）
    expect(screen.getByText(/42\.0 MB/)).toBeTruthy()
    expect(screen.getByText(/22\.0 MB/)).toBeTruthy()
    expect(screen.getAllByText(/上传于 /)).toHaveLength(2)

    // 二维码（快速访问两卡各一张；title = 局域网完整 URL）
    const clientQr = screen.getByTitle(`${origin}/api/client-packages/LabelFrame.Client-0.18.0.msi`)
    expect(clientQr.tagName).toBe('IMG')
    expect(clientQr.getAttribute('src')).toMatch(/^data:image\/gif;base64,/)
    const pdaQr = screen.getByTitle(`${origin}/api/pda-packages/LabelFrame-AndroidHost-0.26.0.apk`)
    expect(pdaQr.tagName).toBe('IMG')

    // 下载按钮（href = 下载相对路径，同源直达）
    const clientLink = screen.getByTitle('下载 LabelFrame.Client-0.18.0.msi')
    expect(clientLink.tagName).toBe('A')
    expect(clientLink.getAttribute('href')).toBe('/api/client-packages/LabelFrame.Client-0.18.0.msi')
    const pdaLink = screen.getByTitle('下载 LabelFrame-AndroidHost-0.26.0.apk')
    expect(pdaLink.getAttribute('href')).toBe('/api/pda-packages/LabelFrame-AndroidHost-0.26.0.apk')

    // 「全部版本 →」×2：分别跳对应管理 tab
    const allButtons = screen.getAllByRole('button', { name: '全部版本 →' })
    expect(allButtons).toHaveLength(2)
  })

  it('「全部版本 →」跳转：Windows 卡跳 Windows 管理、Android 卡跳 Android 管理', async () => {
    render(<DownloadCenterRouteHarness />)
    await screen.findByText('LabelFrame.Client-0.18.0.msi')

    const allButtons = screen.getAllByRole('button', { name: '全部版本 →' })
    fireEvent.click(allButtons[0]) // Windows 卡
    await waitFor(() => expect(window.location.hash).toBe('#/packages/windows'))
    expect(await screen.findByText('LabelFrame.Client-linux.zip')).toBeTruthy()

    gotoManageTab('快速访问')
    await screen.findByText('LabelFrame.Client-0.18.0.msi')
    fireEvent.click(screen.getAllByRole('button', { name: '全部版本 →' })[1]) // Android 卡
    await waitFor(() => expect(window.location.hash).toBe('#/packages/android'))
    expect(await screen.findByText('LabelFrame-AndroidHost-0.25.0.apk')).toBeTruthy()
  })

  it('空态（AC-03）：对应卡空态引导跳转管理 tab 上传，不报错', async () => {
    mocks.server.listClientPackages.mockResolvedValue([])
    render(<DownloadCenterRouteHarness />)
    expect(await screen.findByText('暂无安装包')).toBeTruthy()
    // Windows 卡空态（Android 卡正常展示最新一条）
    expect(screen.getByText('LabelFrame-AndroidHost-0.26.0.apk')).toBeTruthy()
    expect(screen.getAllByText('最新上传')).toHaveLength(1)

    fireEvent.click(screen.getByRole('button', { name: '去上传' }))
    await waitFor(() => expect(window.location.hash).toBe('#/packages/windows'))
    // 管理视图空态与上传入口
    expect(await screen.findByText('暂无 Windows 安装包')).toBeTruthy()
    expect(screen.getByRole('button', { name: /上传 Windows 安装包/ })).toBeTruthy()
  })
})

describe('下载中心页 · 连接信息卡（AC-04 / AC-05，迭代 118 · #272）', () => {
  it('localhost 打开自动回退：默认选中首个候选（不产生 localhost 废码），二维码内容 = 同一裸地址 URL', async () => {
    render(<DownloadCenterRouteHarness />)
    await screen.findByText('服务端信息')

    const expected = `http://10.20.30.40:${window.location.port}`
    // 等宽地址文本
    await waitFor(() => expect(screen.getByText(expected)).toBeTruthy())
    // 大二维码：title = 选中裸地址（无包装协议 / deep link）——候选 chip 同 title，按 IMG 元素过滤
    const qr = screen.getAllByTitle(expected).find((el) => el.tagName === 'IMG')
    expect(qr).toBeTruthy()
    expect(qr!.getAttribute('src')).toMatch(/^data:image\/gif;base64,/)
    // localhost 废码不出现
    expect(screen.queryByTitle(window.location.origin)).toBeNull()
  })

  it('复制行为（AC-04）：点击复制 → copyText 收到当前选中地址，按钮变「已复制」', async () => {
    render(<DownloadCenterRouteHarness />)
    await screen.findByText('服务端信息')
    const expected = `http://10.20.30.40:${window.location.port}`
    await waitFor(() => expect(screen.getByText(expected)).toBeTruthy())

    fireEvent.click(screen.getByRole('button', { name: '复制' }))
    await waitFor(() => expect(mocks.copyText).toHaveBeenCalledWith(expected))
    expect(await screen.findByRole('button', { name: '已复制' })).toBeTruthy()
  })

  it('候选切换（AC-05）：多候选可切换，地址文本 / 二维码 / 复制内容同步更新', async () => {
    render(<DownloadCenterRouteHarness />)
    await screen.findByText('服务端信息')
    const first = `http://10.20.30.40:${window.location.port}`
    await waitFor(() => expect(screen.getByText(first)).toBeTruthy())

    // 切到第二个候选
    fireEvent.click(screen.getByRole('button', { name: '192.168.1.9' }))
    const second = `http://192.168.1.9:${window.location.port}`
    expect(await screen.findByText(second)).toBeTruthy()
    const qr = screen.getAllByTitle(second).find((el) => el.tagName === 'IMG')
    expect(qr).toBeTruthy()

    fireEvent.click(screen.getByRole('button', { name: '复制' }))
    await waitFor(() => expect(mocks.copyText).toHaveBeenCalledWith(second))
  })

  it('origin 匹配候选：默认原样使用当前 origin', async () => {
    // jsdom origin 主机固定 localhost，无法直接构造「origin = 候选 IP」形态——
    // 该规则由 lib/connection pickDefaultAddress 单测覆盖（connection.test.ts），此处覆盖接口形态断言。
    mocks.server.listServerIpv4Candidates.mockResolvedValue({ candidates: [] })
    render(<DownloadCenterRouteHarness />)
    await screen.findByText('服务端信息')
    // 无候选（旧版服务端 / 枚举失败）：origin 兜底展示，不报错、不渲染候选行
    await waitFor(() => expect(screen.getByText(window.location.origin)).toBeTruthy())
    expect(screen.queryByText('地址候选')).toBeNull()
  })

  it('候选接口失败：静默回退 origin，不阻塞首屏', async () => {
    mocks.server.listServerIpv4Candidates.mockRejectedValue(new Error('old server'))
    render(<DownloadCenterRouteHarness />)
    expect(await screen.findByText('LabelFrame.Client-0.18.0.msi')).toBeTruthy()
    await waitFor(() => expect(screen.getByText(window.location.origin)).toBeTruthy())
  })
})

describe('下载中心页 · 管理 tab 回归（AC-06：client-packages / pda-packages 行为不动）', () => {
  it('Windows 管理列表：条目 / 大小 / 下载链接 / 二维码（时间倒序最新在上）', async () => {
    mocks.server.listClientPackages.mockResolvedValue([...CLIENT_PKGS].reverse())
    render(<DownloadCenterRouteHarness />)
    gotoManageTab('Windows 包管理')
    expect(await screen.findByText('LabelFrame.Client-0.18.0.msi')).toBeTruthy()
    expect(screen.getByText('LabelFrame.Client-linux.zip')).toBeTruthy()
    expect(screen.getByText('42.0 MB')).toBeTruthy()

    const origin = window.location.origin
    const clientQr = screen.getByTitle(`${origin}/api/client-packages/LabelFrame.Client-0.18.0.msi`)
    expect(clientQr.tagName).toBe('IMG')
    expect(indexOfText('LabelFrame.Client-0.18.0.msi')).toBeLessThan(indexOfText('LabelFrame.Client-linux.zip'))

    const clientLink = screen.getByTitle('下载 LabelFrame.Client-0.18.0.msi')
    expect(clientLink.getAttribute('href')).toBe('/api/client-packages/LabelFrame.Client-0.18.0.msi')
  })

  it('Android 管理：列表 + 「未知来源 / 安装未知应用」授权提示常驻', async () => {
    render(<DownloadCenterRouteHarness />)
    gotoManageTab('Android 包管理')
    expect(await screen.findByText('LabelFrame-AndroidHost-0.26.0.apk')).toBeTruthy()
    expect(indexOfText('LabelFrame-AndroidHost-0.26.0.apk')).toBeLessThan(indexOfText('LabelFrame-AndroidHost-0.25.0.apk'))
    expect(screen.getByText(/未知来源/)).toBeTruthy()
    expect(screen.getByText(/安装未知应用/)).toBeTruthy()
    expect(screen.getByText(/同一局域网/)).toBeTruthy()
  })

  it('管理空态：两 tab 各自空态提示与上传按钮', async () => {
    mocks.server.listClientPackages.mockResolvedValue([])
    mocks.server.listPdaPackages.mockResolvedValue([])
    render(<DownloadCenterRouteHarness />)
    gotoManageTab('Windows 包管理')
    expect(await screen.findByText('暂无 Windows 安装包')).toBeTruthy()
    expect(screen.getByRole('button', { name: /上传 Windows 安装包/ })).toBeTruthy()

    gotoManageTab('Android 包管理')
    expect(await screen.findByText('暂无 Android 安装包')).toBeTruthy()
    expect(screen.getByRole('button', { name: /上传 APK/ })).toBeTruthy()
  })

  it('加载失败：显示错误信息', async () => {
    mocks.server.listClientPackages.mockRejectedValue(new Error('network down'))
    render(<DownloadCenterRouteHarness />)
    expect(await screen.findByText(/获取安装包列表失败/)).toBeTruthy()
  })

  it('上传：Windows 选 MSI → uploadClientPackage；Android 选 APK → uploadPdaPackage；均刷新列表', async () => {
    render(<DownloadCenterRouteHarness />)
    gotoManageTab('Windows 包管理')
    await screen.findByText('LabelFrame.Client-0.18.0.msi')

    const msi = new File(['x'], 'LabelFrame.Client-0.19.0.msi')
    fireEvent.change(document.getElementById('clientPkgFile')!, { target: { files: [msi] } })
    await waitFor(() => expect(mocks.server.uploadClientPackage).toHaveBeenCalledWith(msi))

    gotoManageTab('Android 包管理')
    await screen.findByText('LabelFrame-AndroidHost-0.26.0.apk')
    const apk = new File(['y'], 'LabelFrame-AndroidHost-0.26.0.apk')
    fireEvent.change(document.getElementById('pdaPkgFile')!, { target: { files: [apk] } })
    await waitFor(() => expect(mocks.server.uploadPdaPackage).toHaveBeenCalledWith(apk))

    // 挂载 1 次 + 两次上传各刷新 1 次 = 3 次
    await waitFor(() => expect(mocks.server.listClientPackages).toHaveBeenCalledTimes(3))
    await waitFor(() => expect(mocks.server.listPdaPackages).toHaveBeenCalledTimes(3))
    expect(await screen.findByText(/APK「LabelFrame-AndroidHost-0\.26\.0\.apk」已上传/)).toBeTruthy()
  })

  it('删除：Android 条目确认 Modal 后调 deletePdaPackage + 刷新；取消不调用', async () => {
    render(<DownloadCenterRouteHarness />)
    gotoManageTab('Android 包管理')
    await screen.findByText('LabelFrame-AndroidHost-0.26.0.apk')

    const delButtons = screen.getAllByRole('button', { name: /删除/ })
    const pdaDelete = delButtons.find((b) => b.closest('tr')?.textContent?.includes('AndroidHost-0.26.0'))
    fireEvent.click(pdaDelete!)
    // 迭代 93（#151 F-04）：自研 Modal 确认（替代原生 confirm 弹窗）——标题区分分区，确认后删除 + 刷新
    expect(await screen.findByText('删除 APK')).toBeTruthy()
    fireEvent.click(screen.getByRole('button', { name: '确认删除' }))
    await waitFor(() => expect(mocks.server.deletePdaPackage).toHaveBeenCalledWith('LabelFrame-AndroidHost-0.26.0.apk'))
    await waitFor(() => expect(mocks.server.listPdaPackages).toHaveBeenCalledTimes(2))

    // 取消：关闭确认框，不再调用删除
    const pdaDeleteAgain = screen.getAllByRole('button', { name: /删除/ }).find((b) =>
      b.closest('tr')?.textContent?.includes('AndroidHost-0.26.0'),
    )
    fireEvent.click(pdaDeleteAgain!)
    fireEvent.click(await screen.findByRole('button', { name: '取消' }))
    expect(mocks.server.deletePdaPackage).toHaveBeenCalledTimes(1)
  })

  it('删除：Windows 条目确认 Modal 后调 deleteClientPackage', async () => {
    render(<DownloadCenterRouteHarness />)
    gotoManageTab('Windows 包管理')
    await screen.findByText('LabelFrame.Client-0.18.0.msi')

    const delButtons = screen.getAllByRole('button', { name: /删除/ })
    const clientDelete = delButtons.find((b) => b.closest('tr')?.textContent?.includes('Client-linux'))
    fireEvent.click(clientDelete!)
    expect(await screen.findByText('删除 Windows 安装包')).toBeTruthy()
    fireEvent.click(screen.getByRole('button', { name: '确认删除' }))
    await waitFor(() => expect(mocks.server.deleteClientPackage).toHaveBeenCalledWith('LabelFrame.Client-linux.zip'))
  })
})
