import { createServer } from 'node:http'
import { readFile, mkdir } from 'node:fs/promises'
import { resolve, extname } from 'node:path'
import assert from 'node:assert/strict'
import { chromium } from 'playwright-core'

const root = resolve('dist-server')
const output = resolve('../artifacts/publishing-ui')
await mkdir(output, { recursive: true })
const server = createServer(async (request, response) => {
  const path = resolve(root, '.' + new URL(request.url, 'http://localhost').pathname.replace(/\/$/, '/index.html'))
  if (!path.startsWith(root + '/'.replace('/', process.platform === 'win32' ? '\\' : '/'))) { response.writeHead(403).end(); return }
  try {
    response.setHeader('Content-Type', ({ '.html': 'text/html', '.js': 'text/javascript', '.css': 'text/css' })[extname(path)] ?? 'application/octet-stream')
    response.end(await readFile(path))
  } catch { response.writeHead(404).end() }
})
await new Promise(resolve => server.listen(0, '127.0.0.1', resolve))
const browser = await chromium.launch({ executablePath: 'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe', headless: true })
try {
  for (const locale of ['zh-CN', 'en']) {
    const page = await browser.newPage({ viewport: { width: 1440, height: 1000 }, locale })
    await page.addInitScript(locale => { localStorage.setItem('labelframe.locale', locale); localStorage.setItem('labelframe.guide.seen', 'true') }, locale)
    let releases = []
    let published = 0
    const pkg = { version: '1.6.0', versionName: '1.6.0', versionCode: 10600, architecture: 'x64', abis: ['arm64-v8a'], sha256: 'A'.repeat(64) }
    const rows = extension => [{ fileName: `LabelFrame-Client-1.6.0.${extension}`, sizeBytes: 24000000, modifiedAt: '2026-10-29T00:00:00Z' }]
    await page.route('**/api/**', async route => {
      const url = new URL(route.request().url())
      const path = url.pathname
      let body = []
      if (path.endsWith('/client-packages')) body = rows('msi')
      else if (path.endsWith('/pda-packages')) body = rows('apk')
      else if (path.endsWith('/server/info')) body = { version: '1.5.1', publicBaseUrl: 'http://127.0.0.1' }
      else if (path.endsWith('/ipv4-candidates')) body = { candidates: [] }
      else if (path.includes('-updates')) {
        assert.equal(route.request().headers().authorization, undefined)
        if (path.endsWith('/preview')) body = pkg
        else if (path.endsWith('/publish')) {
          const request = route.request().postDataJSON()
          assert.deepEqual(request.devices, [])
          assert.equal(request.expectedSha256, pkg.sha256)
          published++
          releases = [{ id: 'release', package: pkg, devices: [], paused: false }]
          body = releases[0]
        } else if (path.endsWith('/pause')) {
          releases[0].paused = route.request().postDataJSON().paused
          body = releases[0]
        } else if (path.endsWith('/drafts')) body = rows(path.includes('android') ? 'apk' : 'msi').map(row => ({ fileName: row.fileName, sha256: pkg.sha256 }))
        else if (!path.endsWith('/status')) body = releases
      }
      await route.fulfill({ json: body })
    })
    for (const platform of ['windows', 'android']) {
      releases = []
      const before = published
      await page.goto(`http://127.0.0.1:${server.address().port}/#/packages/${platform}`)
      const publish = page.getByRole('button', { name: locale === 'en' ? 'Publish' : '发布', exact: true })
      await publish.click()
      const dialog = page.getByRole('dialog')
      await dialog.waitFor()
      assert.equal(published, before)
      assert.equal(await page.locator('input[type=password]').count(), 0)
      await page.screenshot({ path: resolve(output, `${platform}-${locale}-confirm.png`), fullPage: true, animations: 'disabled' })
      await dialog.getByRole('button', { name: locale === 'en' ? 'Confirm publication' : '确认发布', exact: true }).click()
      const pause = page.getByRole('button', { name: locale === 'en' ? 'Pause' : '暂停', exact: true }).first()
      await pause.waitFor()
      await page.screenshot({ path: resolve(output, `${platform}-${locale}-published.png`), fullPage: true, animations: 'disabled' })
      await pause.click()
      await page.getByRole('button', { name: locale === 'en' ? 'Resume publication' : '恢复发布', exact: true }).first().waitFor()
      assert.equal(published, before + 1)
    }
    await page.goto(`http://127.0.0.1:${server.address().port}/#/packages`)
    await page.getByRole('link', { name: locale === 'en' ? 'Download' : '下载', exact: true }).first().waitFor()
    await page.screenshot({ path: resolve(output, `downloads-${locale}.png`), fullPage: true })
    await page.close()
  }
  console.log('PASS: two platforms × two locales; verified confirmation, publication, pause, anonymous downloads; mocked API, no device access')
} finally { await browser.close(); server.close() }
