// #324 多选拖动复现 / 回归脚本（本地手动运行，不进 CI——CI 无浏览器且 workflows 属治理红线）。
// 起本地 dev server → playwright-core 驱动系统 Edge/Chrome（executablePath，真实 CDP 输入事件，
// 与 Konva 的 window mousemove 监听对接——jsdom 复刻两轮未覆盖的链路）→ 构造用户场景
// （A 右下角与 B 左上角对齐 → shift 多选 → 拖动 → 松手）。
// 用法：
//   node scripts/repro-324.mjs            —— 回归模式（默认）：三场景断言（贴线松手 / 空旷松手 / 连拖两段）
// 依赖：web devDependencies 的 playwright-core（无浏览器下载），系统已装 Edge 或 Chrome。

import { chromium } from 'playwright-core'
import { spawn, execSync } from 'node:child_process'
import { existsSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { dirname } from 'node:path'

const WEB_ROOT = dirname(dirname(fileURLToPath(import.meta.url)))
const PORT = Number(process.env.LF_REPRO_PORT || 5199)
const BASE = `http://127.0.0.1:${PORT}`

// 纸张 100x60、内容偏移 ox=60 逻辑 px、画布总宽 500 逻辑 px（RULER 20 + 120mm*4）——与 CanvasViewport 常量一致
const CANVAS_W_LOGIC = 500
const OX_LOGIC = 60
const PX_PER_MM = 4

// ---------- dev server ----------

let server = null

function startServer() {
  const cmd = process.platform === 'win32' ? 'pnpm.cmd' : 'pnpm'
  // --host 127.0.0.1：vite 默认绑 IPv6 localhost，轮询与页面统一走 IPv4
  server = spawn(cmd, ['dev', '--host', '127.0.0.1', '--port', String(PORT), '--strictPort'], { cwd: WEB_ROOT, shell: true, stdio: 'ignore' })
  return new Promise((resolve, reject) => {
    const deadline = Date.now() + 60_000
    const poll = async () => {
      try {
        const res = await fetch(BASE, { signal: AbortSignal.timeout(1500) })
        if (res.ok) return resolve()
      } catch {
        /* 尚未就绪 */
      }
      if (Date.now() > deadline) return reject(new Error('dev server 启动超时'))
      setTimeout(poll, 500)
    }
    poll()
  })
}

function stopServer() {
  if (!server) return
  if (process.platform === 'win32') {
    try {
      execSync(`taskkill /pid ${server.pid} /T /F`, { stdio: 'ignore' })
    } catch {
      /* 尽力而为 */
    }
  } else {
    server.kill('SIGTERM')
  }
}

// ---------- 浏览器 ----------

function findBrowser() {
  const candidates = [
    process.env.LF_BROWSER,
    'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe',
    'C:/Program Files/Google/Chrome/Application/chrome.exe',
    'C:/Program Files/Microsoft/Edge/Application/msedge.exe',
    '/Applications/Google Chrome.app/Contents/MacOS/Google Chrome',
    '/usr/bin/google-chrome',
    '/usr/bin/chromium',
  ].filter(Boolean)
  for (const p of candidates) if (existsSync(p)) return p
  throw new Error('未找到系统 Edge/Chrome（可用 LF_BROWSER 环境变量指定可执行文件路径）')
}

async function launchBrowser() {
  const executablePath = findBrowser()
  const base = { executablePath, viewport: { width: 1600, height: 1000 } }
  // 优先有头（与用户真实事件管线一致）；环境不支持再退 headless（playwright chromium 默认 new headless）
  for (const headless of [false, true]) {
    try {
      return await chromium.launch({ ...base, headless })
    } catch (ev) {
      if (headless) throw ev
    }
  }
}

// ---------- 场景基建 ----------

/** 属性面板位置组前四行依次 X / Y / 宽 / 高（PropsPanel NumField 渲染顺序）——fill 后 Enter 即 blur 提交。 */
async function setNumField(page, rowIdx, value) {
  const input = page.locator('.num-row').nth(rowIdx).locator('input')
  await input.fill(String(value))
  await input.press('Enter') // NumField onKeyDown：Enter 即 blur 提交
  await page.waitForTimeout(60)
}

/** 画布逻辑换算：模型 mm → 屏幕 CSS px（stage 平移为 0、画布自适应窗口）。 */
async function canvasMapper(page) {
  const box = await page.locator('.canvas-viewport .konvajs-content').first().boundingBox()
  if (!box) throw new Error('画布未就绪')
  const scale = box.width / CANVAS_W_LOGIC
  const mm = (mx, my) => ({ x: box.x + (OX_LOGIC + mx * PX_PER_MM) * scale, y: box.y + (OX_LOGIC + my * PX_PER_MM) * scale, scale })
  return { mm, box }
}

/** 新建草稿并放置 A（10,10 20x15）与 B（30,25 20x15）——A 右下角与 B 左上角精确重合（用户复现场景）。 */
async function setupScenario(page) {
  await page.goto(`${BASE}/#/designer`)
  await page.waitForTimeout(400) // 等首帧与模式探测稳定（首见引导已由 addInitScript 跳过）
  await page.locator('[data-guide="designer-empty-new"]').click({ timeout: 10_000 })
  await page.waitForTimeout(150)
  const map = await canvasMapper(page)
  // 放置 A、B（每次放置前重新武装控件栏；粗放后经属性面板精确定值）
  for (const [mx, my] of [[12, 9], [35, 30]]) {
    await page.locator('.palette-btn').nth(3).click()
    await page.waitForTimeout(100)
    const pt = map.mm(mx, my)
    await page.mouse.click(pt.x, pt.y)
    await page.waitForTimeout(80)
  }
  const layerCount = await page.locator('.layer-list .layer-item').count()
  if (layerCount !== 2) throw new Error(`元素放置失败（图层 ${layerCount} 个，预期 2）`)
  // 精确定值：先单选 A
  const a1 = map.mm(15, 12)
  await page.mouse.click(a1.x, a1.y)
  await page.waitForTimeout(80)
  await setNumField(page, 0, 10)
  await setNumField(page, 1, 10)
  await setNumField(page, 2, 20)
  await setNumField(page, 3, 15)
  // 单选 B
  const b1 = map.mm(38, 33)
  await page.mouse.click(b1.x, b1.y)
  await page.waitForTimeout(80)
  await setNumField(page, 0, 30)
  await setNumField(page, 1, 25)
  await setNumField(page, 2, 20)
  await setNumField(page, 3, 15)
  return map
}

/** shift 多选 A、B（图层列表点选，避开画布点击的命中歧义）。 */
async function multiSelect(page) {
  await page.locator('.layer-list .layer-item').nth(0).click()
  await page.locator('.layer-list .layer-item').nth(1).click({ modifiers: ['Shift'] })
  await page.waitForTimeout(60)
}

/** 从指定 mm 坐标（默认 A 初始中心）拖动 (dxMm, dyMm)：步进真实鼠标序列，末步停留 60ms 后松手。 */
async function dragLeader(page, map, dxMm, dyMm, grabMx = 20, grabMy = 17.5) {
  const grab = map.mm(grabMx, grabMy)
  const scale = grab.scale
  const end = { x: grab.x + dxMm * PX_PER_MM * scale, y: grab.y + dyMm * PX_PER_MM * scale }
  await page.mouse.move(grab.x, grab.y)
  await page.mouse.down()
  const steps = 24
  for (let i = 1; i <= steps; i++) {
    await page.mouse.move(grab.x + ((end.x - grab.x) * i) / steps, grab.y + ((end.y - grab.y) * i) / steps)
    await page.waitForTimeout(16)
  }
  await page.waitForTimeout(60)
  await page.mouse.up()
  await page.waitForTimeout(250)
}

/** 点击模型坐标（选单元素 / 清选）。 */
async function clickAt(page, map, mx, my) {
  const pt = map.mm(mx, my)
  await page.mouse.click(pt.x, pt.y)
  await page.waitForTimeout(80)
}

/** 经图层列表单选读属性面板 X/Y（评审建议「松手后单点选再读值」的可靠落地——多选态下画布点击
 *  会被 Transformer 手柄 / 时序干扰，图层列表是 DOM 层确定性通道）。idx：0 = A（先放置）、1 = B。 */
async function readElementXY(page, idx) {
  await page.locator('.layer-list .layer-item').nth(idx).click()
  await page.waitForTimeout(80)
  return page.evaluate(() => {
    if (document.querySelector('.props-empty')) return null
    const rows = [...document.querySelectorAll('.num-row')]
    const read = (i) => rows[i]?.querySelector('input')?.value ?? null
    return { x: Number(read(0)), y: Number(read(1)) }
  })
}

/** 清选（点击空白画布角）。 */
async function clearSelection(page, map) {
  await clickAt(page, map, 2, 56)
}

const approxEq = (a, b, tol = 0.06) => Math.abs(a - b) <= tol

// ---------- 回归（三场景） ----------

function assert(cond, msg, failures) {
  if (!cond) failures.push(msg)
  console.log(`${cond ? '  ✅' : '  ❌'} ${msg}`)
}

async function runScenarioSnapLine(page, failures) {
  console.log('场景 1：贴线松手（拖 +21.3mm，联合盒中心线吸附到内容区中心 50mm，预期同 Δ=+20）')
  const map = await setupScenario(page)
  await multiSelect(page)
  await dragLeader(page, map, 21.3, 0)
  const a = await readElementXY(page, 0)
  const b = await readElementXY(page, 1)
  assert(a !== null && approxEq(a.x, 30) && approxEq(a.y, 10), `A 落点 = 贴线终态 30/10（实际 ${a && `${a.x}/${a.y}`}）`, failures)
  assert(b !== null && approxEq(b.x, 50) && approxEq(b.y, 25), `B 落点 = 同 Δ 终态 50/25（实际 ${b && `${b.x}/${b.y}`}）`, failures)
  if (a && b) assert(approxEq(b.x - a.x, 20, 0.02) && approxEq(b.y - a.y, 15, 0.02), '相对位置恒定（B−A = 20/15）', failures)
  // 松手不瞬移（模型侧稳定）：触发一次重渲染（改属性提交）后再读
  await setNumField(page, 2, 20.5)
  await setNumField(page, 2, 20)
  const b2 = await readElementXY(page, 1)
  assert(b2 !== null && approxEq(b2.x, 50) && approxEq(b2.y, 25), `重渲染后 B 不跳回（实际 ${b2 && `${b2.x}/${b2.y}`}）`, failures)
  await clearSelection(page, map)
}

async function runScenarioOpenGround(page, failures) {
  console.log('场景 2：空旷松手（拖 +13.37/+8.63mm，无参考线命中，网格兜底，预期 A=23/19、B=43/34）')
  const map = await setupScenario(page)
  await multiSelect(page)
  await dragLeader(page, map, 13.37, 8.63)
  const a = await readElementXY(page, 0)
  const b = await readElementXY(page, 1)
  assert(a !== null && approxEq(a.x, 23) && approxEq(a.y, 19), `A 落点 = 网格兜底 23/19（实际 ${a && `${a.x}/${a.y}`}）`, failures)
  assert(b !== null && approxEq(b.x, 43) && approxEq(b.y, 34), `B 落点 = 同 Δ 终态 43/34（实际 ${b && `${b.x}/${b.y}`}）`, failures)
  if (a && b) assert(approxEq(b.x - a.x, 20, 0.02) && approxEq(b.y - a.y, 15, 0.02), '相对位置恒定（B−A = 20/15）', failures)
  await clearSelection(page, map)
}

async function runScenarioTwoSegments(page, failures) {
  console.log('场景 3：连拖两段（第一段 +21.3 贴线；第二段 +5.6 网格，预期 A=36/10、B=56/25）')
  const map = await setupScenario(page)
  await multiSelect(page)
  await dragLeader(page, map, 21.3, 0)
  await page.waitForTimeout(120)
  await multiSelect(page) // 松手后重选（第二段拖动前置）
  await dragLeader(page, map, 5.6, 0, 40, 17.5) // 第二段抓 A 新中心（第一段后 A 移到 30,10 → 中心 40,17.5）
  const a = await readElementXY(page, 0)
  const b = await readElementXY(page, 1)
  assert(a !== null && approxEq(a.x, 36) && approxEq(a.y, 10), `两段后 A = 36/10（实际 ${a && `${a.x}/${a.y}`}）`, failures)
  assert(b !== null && approxEq(b.x, 56) && approxEq(b.y, 25), `两段后 B = 56/25（实际 ${b && `${b.x}/${b.y}`}）`, failures)
  if (a && b) assert(approxEq(b.x - a.x, 20, 0.02) && approxEq(b.y - a.y, 15, 0.02), '相对位置恒定（B−A = 20/15）', failures)
  await clearSelection(page, map)
}

async function runRegress(browser) {
  const failures = []
  // 每场景独立 context：上一场景的设计器草稿仍挂在路由状态上，复用页面会跳过空态新建按钮
  for (const run of [runScenarioSnapLine, runScenarioOpenGround, runScenarioTwoSegments]) {
    const context = await browser.newContext({ viewport: { width: 1600, height: 1000 } })
    await context.addInitScript(() => localStorage.setItem('labelframe.guide.client.v1', 'done')) // 跳过首见引导遮罩
    const page = await context.newPage()
    try {
      await run(page, failures)
    } finally {
      await context.close()
    }
  }
  console.log(failures.length === 0 ? '\n全部断言通过 ✅' : `\n失败 ${failures.length} 项 ❌\n- ${failures.join('\n- ')}`)
  return failures.length
}

// ---------- 入口 ----------

await startServer()
console.log(`dev server 就绪：${BASE}`)
let browser
let code = 0
try {
  browser = await launchBrowser()
  code = await runRegress(browser)
} finally {
  await browser?.close().catch(() => {})
  stopServer()
}
process.exit(code)
