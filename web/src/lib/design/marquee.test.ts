import { describe, expect, it } from 'vitest'
import { elementsIntersecting } from './marquee'
import { defaultElement } from './types'

// 内容区偏移 (60,60)（与 snapping.test 同布局）；1mm = 4px
const OX = 60
const OY = 60

describe('marquee 框选命中（elementsIntersecting）', () => {
  it('部分相交命中：选区扫过元素一角（正面积重叠）', () => {
    const text = defaultElement('Text', 'a') // 模型盒 (80,80,160,40)：x5 y5 w40 h10
    expect(elementsIntersecting({ x: 100, y: 90, w: 100, h: 100 }, [text], OX, OY)).toEqual(['a'])
  })

  it('边缘相切不算：共享边界（右缘贴左缘 / 底缘贴顶缘 / 角接触）；越过边界即命中', () => {
    const text = defaultElement('Text', 'a') // (80,80)-(240,120)
    expect(elementsIntersecting({ x: 0, y: 0, w: 80, h: 200 }, [text], OX, OY)).toEqual([]) // 右缘 80 贴左缘 80
    expect(elementsIntersecting({ x: 0, y: 0, w: 200, h: 80 }, [text], OX, OY)).toEqual([]) // 底缘 80 贴顶缘 80
    expect(elementsIntersecting({ x: 240, y: 120, w: 50, h: 50 }, [text], OX, OY)).toEqual([]) // 角接触 (240,120)
    expect(elementsIntersecting({ x: 239.5, y: 80.5, w: 10, h: 10 }, [text], OX, OY)).toEqual(['a']) // 越界 0.5px 即命中
  })

  it('完全包含命中：元素整体在选区内；选区整体在元素盒内同样命中', () => {
    const rect = defaultElement('Rect', 'r') // (80,80)-(240,160)：x5 y5 w40 h20
    expect(elementsIntersecting({ x: 0, y: 0, w: 500, h: 340 }, [rect], OX, OY)).toEqual(['r'])
    expect(elementsIntersecting({ x: 100, y: 90, w: 20, h: 20 }, [rect], OX, OY)).toEqual(['r'])
  })

  it('Line 与 Region 盒：Line 零高按模型盒 ≥2px 下限判交，Region 按 w/h 整盒', () => {
    const line = defaultElement('Line', 'l') // 模型盒 (80,80,240,2)——h0 钳 2px
    expect(elementsIntersecting({ x: 90, y: 79, w: 50, h: 2 }, [line], OX, OY)).toEqual(['l']) // 纵向交叠 (80,81)
    expect(elementsIntersecting({ x: 90, y: 78, w: 50, h: 2 }, [line], OX, OY)).toEqual([]) // 底缘 80 相切不命中
    const region = defaultElement('Region', 'g') // (80,80)-(320,200)：x5 y5 w60 h30
    expect(elementsIntersecting({ x: 82, y: 82, w: 4, h: 4 }, [region], OX, OY)).toEqual(['g'])
  })

  it('多元素命中返回全部 id（按 elements 顺序）；无交集返回空', () => {
    const a = defaultElement('Text', 'a') // (80,80)-(240,120)
    const b = defaultElement('Line', 'b') // (80,80)-(320,82)
    const c = defaultElement('Region', 'c') // (80,80)-(320,200)
    expect(elementsIntersecting({ x: 80, y: 80, w: 10, h: 10 }, [a, b, c], OX, OY)).toEqual(['a', 'b', 'c'])
    expect(elementsIntersecting({ x: 0, y: 0, w: 10, h: 10 }, [a, b, c], OX, OY)).toEqual([])
  })

  it('反向拖拽（w/h 负值）与正向等价', () => {
    const text = defaultElement('Text', 'a')
    expect(elementsIntersecting({ x: 200, y: 200, w: -150, h: -150 }, [text], OX, OY)).toEqual(['a'])
    expect(elementsIntersecting({ x: 50, y: 200, w: 150, h: -150 }, [text], OX, OY)).toEqual(['a'])
  })
})
