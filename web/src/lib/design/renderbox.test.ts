// 渲染盒口径机制锚定与旧口径证伪（迭代 130 · #323）：
// Konva getClientRect 默认把居中描边半线宽计入盒外——线 0.5mm 每侧 +0.25mm、
// 矩形/容器边框 0.3mm 每侧 +0.15mm、图片占位描边每侧 +0.125mm；
// 修复前吸附候选 / 拖动落点提交 / 8 手柄缩放提交全取渲染盒，外扩被传染给 mm 坐标
// （用户报告「自动对齐后位置偏差约 0.2」）。本文件用真实 Konva 节点锚定机制并在
// 测试内复刻旧提交算式证伪修复前行为；修复后口径（elementModelRect / commitDragPosition /
// skipStroke）残差为 0。
//
// 环境说明：headless Konva（node 无 2d context）可构造 Group / Rect / Line 并计算
// getClientRect；Konva.Text 量宽与 Layer 绘制需要 canvas，node 环境不可构造——
// 「无描边文本组盒 = 模型盒」由源码论证（Text.getWidth 返回 attrs.width 固定盒 +
// Group 并集必含 KRect 外框）与浏览器走查覆盖，不在本文件单测锚定范围。

import { describe, expect, it } from 'vitest'
import Konva from 'konva'
import { PX, RULER, commitDragPosition } from './geometry'
import { computeSnap, elementModelRect } from './snapping'
import { defaultElement } from './types'

// 与 CanvasViewport 一致的画布常量：纸 100x60（1mm = 4px），内容区偏移 = 标尺 + 留白
const OX = RULER + 10 * PX
const OY = RULER + 10 * PX
const CANVAS_W = RULER + (100 + 20) * PX // 500
const CANVAS_H = RULER + (60 + 20) * PX // 340

/** 修复前 handleTransformEnd 的提交算式（CanvasViewport 旧代码原样复刻）。 */
const oldTransformCommit = (r: { x: number; y: number; width: number; height: number }) => ({
  x: Math.round(((r.x - OX) / PX) * 100) / 100,
  y: Math.round(((r.y - OY) / PX) * 100) / 100,
  w: Math.round((r.width / PX) * 100) / 100,
  h: Math.round((r.height / PX) * 100) / 100,
})

describe('Konva 渲染盒机制锚定（防上游升级悄悄变更默认语义）', () => {
  it('Line（默认 thickness 0.5mm → strokeWidth 2px）：渲染盒每侧外扩半线宽，skipStroke 恰为模型盒', () => {
    const line = new Konva.Line({ x: 80, y: 60, points: [0, 0, 240, 0], stroke: '#000', strokeWidth: 2 })
    expect(line.getClientRect()).toEqual({ x: 79, y: 59, width: 242, height: 2 })
    expect(line.getClientRect({ skipStroke: true })).toEqual({ x: 80, y: 60, width: 240, height: 0 })
  })

  it('Rect/Group（默认 border 0.3mm → strokeWidth 1.2px）：每侧外扩 0.6px = 0.15mm，Group 并集透传 skipStroke', () => {
    const group = new Konva.Group({ x: 100, y: 80 })
    group.add(new Konva.Rect({ width: 120, height: 80, stroke: '#000', strokeWidth: 1.2 }))
    expect(group.getClientRect().x).toBeCloseTo(99.4, 10)
    expect(group.getClientRect().width).toBeCloseTo(121.2, 10)
    expect(group.getClientRect({ skipStroke: true })).toEqual({ x: 100, y: 80, width: 120, height: 80 })
  })

  it('Image 占位描边（border 0 仍画 1px 描边）：每侧外扩 0.5px = 0.125mm', () => {
    const img = new Konva.Rect({ x: 60, y: 60, width: 80, height: 80, stroke: '#aab4c0', strokeWidth: 1, dash: [4, 3] })
    expect(img.getClientRect()).toEqual({ x: 59.5, y: 59.5, width: 81, height: 81 })
    expect(img.getClientRect({ skipStroke: true })).toEqual({ x: 60, y: 60, width: 80, height: 80 })
  })
})

describe('旧口径复算证伪——把渲染盒喂给修复前的提交算式（缺陷可执行化）', () => {
  it('拖动链（形态 c）：以描边 Rect（border 0.3）为锚贴边，吸附方落点偏 −0.15mm（9.85 而非 10）', () => {
    // 锚元素：模型 x=10mm、w=40mm、border 0.3——ElementNode 结构（Group + KRect 居中描边）
    const anchorModel = defaultElement('Rect')
    anchorModel.x = 10
    const anchorNode = new Konva.Group({ x: OX + anchorModel.x * PX, y: OY + anchorModel.y * PX })
    anchorNode.add(new Konva.Rect({ width: anchorModel.w * PX, height: anchorModel.h * PX, stroke: '#000', strokeWidth: 1.2 }))
    // 旧口径候选 = 锚渲染盒（左缘外扩 0.6px）
    const ar = anchorNode.getClientRect()
    const others = [{ x: ar.x, y: ar.y, w: ar.width, h: ar.height }]
    // 被拖元素：无边框（渲染盒 = 模型盒）、w 10mm，左缘拖到锚附近
    const nodeX = OX + 10 * PX + 3
    const oldDragged = { x: nodeX, y: OY + 5 * PX, w: 10 * PX, h: 10 * PX }
    const snap = computeSnap(oldDragged, CANVAS_W, CANVAS_H, OX, OY, 100 * PX, 60 * PX, others)
    expect(snap.guideX).toBe(ar.x) // 吸到锚的外扩盒左缘
    // 旧提交算式：logicToContentMm(渲染盒左上) + 两位舍入（handleDragEnd 旧代码复刻）
    const oldCommitX = Math.round((((nodeX + snap.dx) - OX) / PX) * 100) / 100
    expect(oldCommitX).toBe(9.85) // 期望 10，偏 −0.15mm（= 半线宽 0.6px）

    // 修复后口径：候选 = 锚模型盒，落点从节点原点提交——残差 0
    const modelOthers = [elementModelRect(anchorModel, OX, OY)]
    const newSnap = computeSnap({ ...oldDragged, x: nodeX }, CANVAS_W, CANVAS_H, OX, OY, 100 * PX, 60 * PX, modelOthers)
    expect(newSnap.guideX).toBe(OX + 10 * PX)
    const landed = commitDragPosition(nodeX + newSnap.dx, OY + 5 * PX, false)
    expect(landed.x).toBe(10)
  })

  it('拖动链（形态 c）：以 Line（thickness 0.5）为锚贴边，吸附方落点偏 −0.25mm（4.75 而非 5）', () => {
    const anchorModel = defaultElement('Line')
    anchorModel.x = 5
    const anchorNode = new Konva.Line({
      x: OX + anchorModel.x * PX,
      y: OY + anchorModel.y * PX,
      points: [0, 0, anchorModel.w * PX, anchorModel.h * PX],
      stroke: '#000',
      strokeWidth: 2,
    })
    const ar = anchorNode.getClientRect()
    const nodeX = OX + 5 * PX + 3
    const dragged = { x: nodeX, y: OY + 15 * PX, w: 10 * PX, h: 10 * PX }
    const snap = computeSnap(dragged, CANVAS_W, CANVAS_H, OX, OY, 100 * PX, 60 * PX, [{ x: ar.x, y: ar.y, w: ar.width, h: ar.height }])
    const oldCommitX = Math.round((((nodeX + snap.dx) - OX) / PX) * 100) / 100
    expect(oldCommitX).toBe(4.75) // 期望 5，偏 −0.25mm（= 半线宽 1px）

    const newSnap = computeSnap(dragged, CANVAS_W, CANVAS_H, OX, OY, 100 * PX, 60 * PX, [elementModelRect(anchorModel, OX, OY)])
    expect(commitDragPosition(nodeX + newSnap.dx, OY + 15 * PX, false).x).toBe(5)
  })

  it('缩放链（形态 d）：Line 每轮 transformend 提交 x −0.25mm / w +0.50mm，逐次累计（5→4.75→4.5→4.25）', () => {
    let model = { x: 5, w: 60 }
    const xs: number[] = []
    const ws: number[] = []
    for (let i = 0; i < 3; i++) {
      // 按模型重渲染（ElementNode 同构的 Konva.Line）
      const node = new Konva.Line({
        x: OX + model.x * PX,
        y: OY + 5 * PX,
        points: [0, 0, model.w * PX, 0],
        stroke: '#000',
        strokeWidth: 2,
      })
      model = oldTransformCommit(node.getClientRect())
      xs.push(model.x)
      ws.push(model.w)
    }
    expect(xs).toEqual([4.75, 4.5, 4.25])
    expect(ws).toEqual([60.5, 61, 61.5])
  })

  it('缩放链（形态 d）：Rect（border 0.3）每轮 x −0.15mm / w +0.30mm（w：30→30.3→30.6→30.9）', () => {
    let model = { x: 10, w: 30 }
    for (let i = 0; i < 3; i++) {
      const node = new Konva.Group({ x: OX + model.x * PX, y: OY + 5 * PX })
      node.add(new Konva.Rect({ width: model.w * PX, height: 20 * PX, stroke: '#000', strokeWidth: 1.2 }))
      model = oldTransformCommit(node.getClientRect())
    }
    expect(model.x).toBe(9.55) // 10 → 9.85 → 9.7 → 9.55
    expect(model.w).toBe(30.9) // 30 → 30.3 → 30.6 → 30.9
  })

  it('缩放链修复口径：skipStroke 盒多轮提交值不变（累计漂移消除）', () => {
    const cases: readonly { strokeW: number; init: { x: number; w: number } }[] = [
      { strokeW: 2, init: { x: 5, w: 60 } },
      { strokeW: 1.2, init: { x: 10, w: 30 } },
    ]
    for (const { strokeW, init } of cases) {
      let model = { ...init }
      for (let i = 0; i < 3; i++) {
        const node = new Konva.Line({
          x: OX + model.x * PX,
          y: OY + 5 * PX,
          points: [0, 0, model.w * PX, 0],
          stroke: '#000',
          strokeWidth: strokeW,
        })
        model = oldTransformCommit(node.getClientRect({ skipStroke: true }))
        expect(model.x).toBe(init.x)
        expect(model.w).toBe(init.w)
      }
    }
  })
})
