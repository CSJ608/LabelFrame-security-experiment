import { chromium } from 'playwright-core'
import { createServer } from 'node:http'
import { readFile, mkdir, writeFile } from 'node:fs/promises'
import { resolve, extname, sep } from 'node:path'
import assert from 'node:assert/strict'

const evidence = resolve('../artifacts/release-151-20261027/browser')
await mkdir(evidence, { recursive: true })
const results = []
for (const mode of ['client', 'server']) {
  const dist = resolve(mode === 'client' ? 'dist' : 'dist-server')
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
    for (const locale of ['zh-CN', 'en']) {
      const page = await browser.newPage({ viewport: { width: 1280, height: 900 }, locale })
      page.setDefaultTimeout(10000)
      console.log(`Checking ${mode}/${locale}`)
      const errors = []
      let saves = 0
      page.on('pageerror', error => errors.push(error.message))
      await page.addInitScript(locale => {
        localStorage.setItem('labelframe.guide.client.v1', 'done')
        localStorage.setItem('labelframe.locale', locale)
      }, locale)
      await page.route('**/*', route => {
        const request = route.request()
        const url = new URL(request.url())
        if (url.origin !== base) return route.abort('connectionrefused')
        const path = url.pathname
        const json = (body, status = 200) => route.fulfill({ status, contentType: 'application/json', body: JSON.stringify(body) })
        if (path === '/healthz') return json({ service: mode === 'client' ? 'LabelFrame.WinHost' : 'LabelFrame.Server', status: 'ok', transport: 'Log' })
        if (path === '/api/host/config') return json({ serverUrl: '', deviceId: 'fixture', version: '1.5.1' })
        if (path === '/api/host/print-settings') return json({ batchEnabled: false, batchSize: 10, batchIntervalMs: 500, dpi: 203 })
        if (path === '/api/transport') return json({ mode: 'Log', params: {}, availableModes: ['Log'] })
        if (path === '/api/host/lifecycle') return json({ instanceId: 'fixture', restartRequired: false, restarting: false })
        if (path === '/api/templates' && request.method() === 'POST') { saves++; return json({ code: 'LF_TPL_SAVE_FAILED', message: 'Fixture save failed' }, 500) }
        if (path.startsWith('/api/')) return json([])
        return route.continue()
      })
      const zh = locale === 'zh-CN'
      await page.goto(base)
      await page.getByRole('button', { name: zh ? '新建模板' : 'New Template', exact: true }).click()
      const name = page.getByPlaceholder(zh ? '模板名称' : 'Template name', { exact: true })
      await name.waitFor()
      await page.getByRole('button', { name: zh ? '更多操作' : 'More actions', exact: true }).click()
      await page.screenshot({ path: resolve(evidence, `${mode}-${locale}-menu.png`) })
      await page.keyboard.press('Escape')
      await page.getByRole('button', { name: zh ? '保存并退出' : 'Save & Exit', exact: true }).click()
      const dialog = page.getByRole('dialog')
      await dialog.waitFor()
      await page.screenshot({ path: resolve(evidence, `${mode}-${locale}-empty-name.png`), animations: 'disabled' })
      await dialog.locator('.modal-foot button').click()
      assert.equal(await name.evaluate(el => el === document.activeElement), true)
      await name.fill('UI fixture')
      await page.keyboard.press('Control+s')
      await dialog.waitFor()
      assert.equal(saves, 1)
      await page.keyboard.press('Control+s')
      assert.equal(saves, 1)
      await page.screenshot({ path: resolve(evidence, `${mode}-${locale}-save-error.png`), animations: 'disabled' })
      await dialog.locator('.modal-foot button').click()
      assert.equal(await name.inputValue(), 'UI fixture')
      assert.deepEqual(errors, [])
      results.push({ mode, locale, saves, errors, checks: ['navigation', 'more-menu', 'empty-name', 'focus', 'failed-save', 'shortcut-blocked', 'draft-preserved'] })
      await page.close()
    }
  } finally { await browser?.close(); server.closeAllConnections(); await new Promise(resolve => server.close(resolve)) }
}
await writeFile(resolve(evidence, 'results.json'), JSON.stringify(results, null, 2))
console.log(JSON.stringify(results))
