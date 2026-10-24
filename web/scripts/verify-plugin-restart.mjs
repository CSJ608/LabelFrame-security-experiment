import { chromium } from 'playwright-core'
import { createServer } from 'node:http'
import { readFile, mkdir, writeFile } from 'node:fs/promises'
import { resolve, extname, sep } from 'node:path'
import assert from 'node:assert/strict'

const dist = resolve('dist')
const evidence = resolve('../docs/evidence/165/plugin-restart-fix')
await mkdir(evidence, { recursive: true })
const server = createServer(async (request, response) => {
  const path = resolve(dist, `.${new URL(request.url, 'http://localhost').pathname}`)
  if (path !== dist && !path.startsWith(dist + sep)) { response.writeHead(400).end(); return }
  const file = extname(path) ? path : resolve(dist, 'index.html')
  try {
    response.setHeader('Content-Type', ({ '.html': 'text/html', '.js': 'application/javascript', '.css': 'text/css', '.svg': 'image/svg+xml' })[extname(file)] ?? 'application/octet-stream')
    response.end(await readFile(file))
  } catch { response.writeHead(404).end() }
})
await new Promise(resolve => server.listen(0, '127.0.0.1', resolve))
const base = `http://127.0.0.1:${server.address().port}`
let browser
try {
  browser = await chromium.launch({ executablePath: 'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe', headless: true })
  const page = await browser.newPage({ viewport: { width: 1280, height: 900 }, locale: 'zh-CN' })
  const errors = []
  page.on('pageerror', error => errors.push(error.message))
  await page.addInitScript(() => localStorage.setItem('labelframe.guide.client.v1', 'done'))
  let address = 'http://192.0.2.10:53961'
  let activity = { dirty: false, busy: false }
  let pending = false
  let plugins = [{ pluginId: 'labelframe-transport-tspl', name: 'TSPL 指令集传输（官方）', version: '1.4.5', loaded: true, source: 'package' }]
  const restartResults = []
  const saves = []
  await page.route('**/*', async route => {
    const request = route.request()
    const url = new URL(request.url())
    if (url.origin !== base) { await route.abort('connectionrefused'); return }
    const path = url.pathname
    const json = (body, status = 200) => route.fulfill({ status, contentType: 'application/json', body: JSON.stringify(body) })
    if (path === '/healthz') return json({ service: 'LabelFrame.WinHost', status: 'ok', transport: 'Log' })
    if (path === '/api/host/config') {
      if (request.method() === 'POST') { address = request.postDataJSON().serverUrl; saves.push(address); return json({ ok: true }) }
      await new Promise(resolve => setTimeout(resolve, 350))
      return json({ serverUrl: address, deviceId: 'fixture', version: '1.4.5' })
    }
    if (path === '/api/host/print-settings') return json({ batchEnabled: false, batchSize: 10, batchIntervalMs: 500, dpi: 203 })
    if (path === '/api/transport') return json({ mode: 'Log', params: {}, availableModes: ['Log'] })
    if (path === '/api/printer/status') return json({ isOnline: false, isPaperOut: false, isPaused: false, message: '未连接打印机' })
    if (path === '/api/plugins/installed') return json(plugins)
    if (path === '/api/plugins/uninstall') { plugins = []; pending = true; return json({ ok: true, message: '插件已卸载，重启客户端后生效。' }) }
    if (path === '/api/host/lifecycle') return json({ instanceId: 'fixture', restartRequired: pending, restarting: false })
    if (path === '/api/host/ui-activity') { activity = request.postDataJSON(); return json({ preparationId: null }) }
    if (path === '/api/host/restart') {
      const blocked = activity.dirty || activity.busy
      restartResults.push({ blocked, dirty: activity.dirty, busy: activity.busy })
      return blocked ? json({ message: '界面有未保存的更改，请先保存或放弃更改后再重启。' }, 409) : json({ restarting: true })
    }
    if (path === '/api/host/update') return json({ settings: { mode: 'download', windowStart: '02:00', windowEnd: '04:00' }, status: { phase: 'idle', currentVersion: '1.4.5' } })
    if (path.startsWith('/api/')) return json([])
    return route.continue()
  })
  const section = name => page.getByRole('navigation', { name: '设置菜单' }).getByRole('button', { name })
  await page.goto(`${base}/#/settings/settings.plugins`)
  await page.getByRole('button', { name: '卸载', exact: true }).click()
  await page.getByRole('button', { name: '确认卸载', exact: true }).click()
  await page.getByRole('button', { name: '立即重启', exact: true }).click()
  await page.getByRole('button', { name: '正在重启客户端…' }).waitFor()
  assert.equal(restartResults.at(-1).blocked, false)
  await page.screenshot({ path: resolve(evidence, 'offline-uninstall-restart.png') })

  await page.reload()
  await section('常规设置').click()
  const input = page.getByPlaceholder('http://127.0.0.1:53961')
  await page.waitForFunction(expected => document.querySelector('input[placeholder="http://127.0.0.1:53961"]')?.value === expected, address)
  await input.fill('http://192.0.2.11:53961')
  await section('插件管理').click()
  await page.getByRole('button', { name: '立即重启', exact: true }).click()
  await page.getByRole('alert').filter({ hasText: '未保存' }).waitFor()
  assert.equal(restartResults.at(-1).blocked, true)
  await page.screenshot({ path: resolve(evidence, 'edited-address-blocked.png') })
  await section('常规设置').click()
  await page.getByRole('button', { name: '放弃地址更改' }).click()
  assert.equal(await input.inputValue(), address)
  assert.equal(saves.length, 0)
  await section('插件管理').click()
  await page.getByRole('button', { name: '立即重启', exact: true }).click()
  await page.getByRole('button', { name: '正在重启客户端…' }).waitFor()
  assert.equal(restartResults.at(-1).blocked, false)

  await page.reload()
  await section('常规设置').click()
  await input.fill(' http://192.0.2.12:53961/ ')
  await page.getByRole('button', { name: '保存并生效' }).click()
  await page.getByText('已保存到本机配置并立即生效。', { exact: true }).waitFor()
  assert.equal(await input.inputValue(), 'http://192.0.2.12:53961')
  await page.getByRole('button', { name: '重启客户端', exact: true }).click()
  await page.getByRole('button', { name: '正在重启客户端…' }).waitFor()
  assert.equal(restartResults.at(-1).blocked, false)
  assert.deepEqual(errors, [])
  await writeFile(resolve(evidence, 'browser.json'), JSON.stringify({ mode: '真实 Edge + 隔离 API 夹具；不代替完整宿主进程', restartResults, saves, pageErrors: errors }, null, 2))
  console.log(JSON.stringify({ restartResults, saves, pageErrors: errors }))
} finally { await browser?.close(); await new Promise(resolve => server.close(resolve)) }
