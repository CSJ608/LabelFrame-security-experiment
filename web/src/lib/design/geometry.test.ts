import { describe, expect, it } from 'vitest'
import { clamp, designCanvasSize, dotsPerMm, fitScale, logicToContentMm, mm, pointerToLogic, previewScale, pxv, r2, viewportToLogic } from './geometry'

describe('geometry 毫米↔像素换算', () => {
  it('mm/px 互换算', () => {
    expect(mm(40)).toBe(10)
    expect(pxv(10)).toBe(40)
    expect(mm(pxv(7.5))).toBe(7.5)
  })

  it('r2 保留两位小数', () => {
    expect(r2(3.14159)).toBe(3.14)
    expect(r2(0.001)).toBe(0)
  })

  it('clamp 夹取', () => {
    expect(clamp(5, 0, 10)).toBe(5)
    expect(clamp(-2, 0, 10)).toBe(0)
    expect(clamp(12, 0, 10)).toBe(10)
  })

  it('DPI → 每毫米点数（203 → 8，300 → 12）', () => {
    expect(dotsPerMm(203)).toBe(8)
    expect(dotsPerMm(300)).toBe(12)
    expect(previewScale(203)).toBe(2)  // 8 点 / 4px每 mm
    expect(previewScale(300)).toBe(3)
  })

  it('画布尺寸：设计态含标尺 + 留白，预览态仅标签', () => {
    const d = designCanvasSize(100, 60)
    expect(d.w).toBe(20 + (100 + 20) * 4)
    expect(d.h).toBe(20 + (60 + 20) * 4)
    const p = designCanvasSize(100, 60) // 占位防未用
    expect(p.w).toBe(500)
  })

  it('逻辑坐标 → 内容 mm（设计态扣除标尺与留白）', () => {
    const pos = logicToContentMm(20 + 10 * 4 + 20, 20 + 10 * 4 + 10, false)
    expect(pos.x).toBe(5)
    expect(pos.y).toBe(2.5)
  })

  it('预览态偏移为 0', () => {
    const pos = logicToContentMm(20, 10, true)
    expect(pos.x).toBe(5)
    expect(pos.y).toBe(2.5)
  })

  it('fitScale 适应窗口（略小于视口，取宽高较小约束）', () => {
    expect(fitScale(500, 400, 500, 400)).toBeCloseTo(0.92) // min((500-32)/500, (400-32)/400)
    expect(fitScale(100, 100, 200, 200)).toBeCloseTo(0.34)
  })

  it('viewportToLogic：无页面缩放（zoom=1）等价原换算', () => {
    // 与 CanvasViewport handleDrop 原换算等价：rect.width === clientWidth → 因子 1
    // canvas 750×600（逻辑 500×400 × total 1.5），stage 未平移，鼠标在画布逻辑 (200,150) 处
    const logic = viewportToLogic({
      clientX: 120 + 300, // rect.left + canvas CSS 偏移（300 = 200 × 1.5）
      clientY: 90 + 225,
      rect: { left: 120, top: 90, width: 750, height: 600 },
      clientWidth: 750,
      clientHeight: 600,
      stageX: 0,
      stageY: 0,
      scaleX: 1.5,
      scaleY: 1.5,
    })
    expect(logic.x).toBeCloseTo(200, 5)
    expect(logic.y).toBeCloseTo(150, 5)
    // 再转内容 mm：逻辑 (200,150) → (200-60)/4=35, (150-60)/4=22.5（RULER 20 + PAD 10mm×4px）
    const mmPos = logicToContentMm(logic.x, logic.y, false)
    expect(mmPos.x).toBeCloseTo(35, 5)
    expect(mmPos.y).toBeCloseTo(22.5, 5)
  })

  it('viewportToLogic：页面缩放 125% 时修正因子生效（旧换算偏差的回归守门）', () => {
    // Windows 显示缩放 125%：getBoundingClientRect 同比放大（750→937.5），clientWidth 不变（750）
    // 鼠标视觉位置 = 逻辑位置 × total × zoom
    const zoom = 1.25
    const logic = viewportToLogic({
      clientX: 120 * zoom + 300 * zoom, // rect.left 与偏移均随 zoom 放大
      clientY: 90 * zoom + 225 * zoom,
      rect: { left: 120 * zoom, top: 90 * zoom, width: 750 * zoom, height: 600 * zoom },
      clientWidth: 750,
      clientHeight: 600,
      stageX: 0,
      stageY: 0,
      scaleX: 1.5,
      scaleY: 1.5,
    })
    // 修正后仍应得逻辑 (200,150)，而非 (250,187.5)（旧换算直接 (offset - 0)/1.5 的结果）
    expect(logic.x).toBeCloseTo(200, 5)
    expect(logic.y).toBeCloseTo(150, 5)
    const mmPos = logicToContentMm(logic.x, logic.y, false)
    expect(mmPos.x).toBeCloseTo(35, 5)
    expect(mmPos.y).toBeCloseTo(22.5, 5)
  })

  it('viewportToLogic：stage 平移参与换算', () => {
    // 平移 (40, 20) 后，同一逻辑点对应的 canvas CSS 偏移应扣除平移再除 scale
    const logic = viewportToLogic({
      clientX: 0 + 40 + 200 * 1.5,
      clientY: 0 + 20 + 150 * 1.5,
      rect: { left: 0, top: 0, width: 750, height: 600 },
      clientWidth: 750,
      clientHeight: 600,
      stageX: 40,
      stageY: 20,
      scaleX: 1.5,
      scaleY: 1.5,
    })
    expect(logic.x).toBeCloseTo(200, 5)
    expect(logic.y).toBeCloseTo(150, 5)
  })

  it('viewportToLogic：clientWidth 为 0 时因子兜底为 1（防除零）', () => {
    const logic = viewportToLogic({
      clientX: 0 + 300,
      clientY: 0 + 225,
      rect: { left: 0, top: 0, width: 750, height: 600 },
      clientWidth: 0,
      clientHeight: 0,
      stageX: 0,
      stageY: 0,
      scaleX: 1.5,
      scaleY: 1.5,
    })
    expect(logic.x).toBeCloseTo(200, 5)
    expect(logic.y).toBeCloseTo(150, 5)
  })

  it('pointerToLogic：Konva 指针（canvas CSS 像素）→ 逻辑坐标', () => {
    // 场景 A 实测：stage 750×600 + scale 1.5，getPointerPosition 返回 (375,300)（未除 scale）
    // 旧 handleClick 直接 (ptr - stage.pos) → 逻辑 375 → mm 78.75（错）；须除 scale → 250 → mm 47.5
    const logic = pointerToLogic({ x: 375, y: 300 }, 0, 0, 1.5, 1.5)
    expect(logic.x).toBeCloseTo(250, 5)
    expect(logic.y).toBeCloseTo(200, 5)
    const mmPos = logicToContentMm(logic.x, logic.y, false)
    expect(mmPos.x).toBeCloseTo(47.5, 5)
    expect(mmPos.y).toBeCloseTo(35, 5)
  })

  it('pointerToLogic：stage 平移参与换算', () => {
    const logic = pointerToLogic({ x: 375 + 40, y: 300 + 20 }, 40, 20, 1.5, 1.5)
    expect(logic.x).toBeCloseTo(250, 5)
    expect(logic.y).toBeCloseTo(200, 5)
  })
})
