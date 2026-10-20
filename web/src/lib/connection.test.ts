// 连接地址候选选择纯函数单测（下载中心「连接信息」卡地址默认选择规则）。

import { describe, expect, it } from 'vitest'
import { deriveBasePath, joinAddress, pickDefaultAddress, pickDisplayAddress } from './connection'

describe('pickDefaultAddress（默认地址选择规则，迭代 118 · #272）', () => {
  it('origin 主机在候选内：原样使用当前 origin（AC-05 匹配优先）', () => {
    expect(pickDefaultAddress('http://192.168.1.9:53961', ['10.0.0.5', '192.168.1.9'])).toBe('http://192.168.1.9:53961')
  })

  it('localhost 打开：回退首个候选（服务端私网优先排序），不产生 localhost 废码（AC-05）', () => {
    expect(pickDefaultAddress('http://localhost:53961', ['10.0.0.5', '192.168.1.9'])).toBe('http://10.0.0.5:53961')
    expect(pickDefaultAddress('http://127.0.0.1:53961', ['192.168.1.9'])).toBe('http://192.168.1.9:53961')
  })

  it('无匹配且非回环（DNS 名 / Docker 端口映射——候选是容器内网 IP）：保持 origin，绝不替换为设备不可达地址', () => {
    expect(pickDefaultAddress('http://print-server.local:53961', ['10.0.0.5'])).toBe('http://print-server.local:53961')
    // Docker compose 部署实测形态：从宿主 IP 访问，服务端枚举到容器内网 IP（172.17.0.5）
    expect(pickDefaultAddress('http://192.168.1.187:53961', ['172.17.0.5'])).toBe('http://192.168.1.187:53961')
  })

  it('候选为空 / 未加载 / 请求失败：origin 兜底（无更优信息时不猜测地址）', () => {
    expect(pickDefaultAddress('http://localhost:53961', [])).toBe('http://localhost:53961')
    expect(pickDefaultAddress('http://10.0.0.5:53961', null)).toBe('http://10.0.0.5:53961')
    expect(pickDefaultAddress('http://10.0.0.5:53961', undefined)).toBe('http://10.0.0.5:53961')
  })

  it('端口沿用 origin：localhost 无端口形态回退候选时同样省略端口段', () => {
    expect(pickDefaultAddress('https://localhost', ['10.0.0.5'])).toBe('https://10.0.0.5')
  })

  it('origin 为空串：原样返回不构造', () => {
    expect(pickDefaultAddress('', ['10.0.0.5'])).toBe('')
  })
})

describe('joinAddress（候选 IP × origin 协议端口拼装）', () => {
  it('带端口：protocol//ip:port', () => {
    expect(joinAddress('http:', '10.0.0.5', '53961')).toBe('http://10.0.0.5:53961')
  })

  it('默认端口（空串）：省略端口段', () => {
    expect(joinAddress('https:', '10.0.0.5', '')).toBe('https://10.0.0.5')
  })

  it('带部署前缀：端口段后拼接 basePath', () => {
    expect(joinAddress('http:', '10.0.0.5', '53961', '/labelframe')).toBe('http://10.0.0.5:53961/labelframe')
  })

  it('默认端口 + 部署前缀', () => {
    expect(joinAddress('https:', '10.0.0.5', '', '/labelframe')).toBe('https://10.0.0.5/labelframe')
  })
})

describe('deriveBasePath（部署子路径前缀推导）', () => {
  it('根路径 / 空串：无前缀', () => {
    expect(deriveBasePath('/')).toBe('')
    expect(deriveBasePath('')).toBe('')
  })

  it('子路径：去尾部多余斜杠', () => {
    expect(deriveBasePath('/labelframe/')).toBe('/labelframe')
    expect(deriveBasePath('/labelframe')).toBe('/labelframe')
    expect(deriveBasePath('/labelframe//')).toBe('/labelframe')
  })

  it('异常输入（非字符串 / 非「/」开头）：空串兜底', () => {
    expect(deriveBasePath('labelframe')).toBe('')
    expect(deriveBasePath('http://x/labelframe')).toBe('')
    // @ts-expect-error 运行时防御：非字符串输入
    expect(deriveBasePath(null)).toBe('')
  })
})

describe('pickDefaultAddress（携带 basePath：三个落点均拼部署前缀）', () => {
  it('origin 命中候选：origin + basePath', () => {
    expect(pickDefaultAddress('http://192.168.1.9:53961', ['10.0.0.5', '192.168.1.9'], '/labelframe')).toBe(
      'http://192.168.1.9:53961/labelframe',
    )
  })

  it('localhost 回退首个候选：候选地址 + basePath', () => {
    expect(pickDefaultAddress('http://localhost:53961', ['10.0.0.5'], '/labelframe')).toBe('http://10.0.0.5:53961/labelframe')
  })

  it('origin 兜底（不匹配 / 候选为空）：origin + basePath', () => {
    expect(pickDefaultAddress('http://print-server.local:53961', ['10.0.0.5'], '/labelframe')).toBe(
      'http://print-server.local:53961/labelframe',
    )
    expect(pickDefaultAddress('http://localhost:53961', [], '/labelframe')).toBe('http://localhost:53961/labelframe')
  })

  it('basePath 缺省（根部署）：既有语义不回归', () => {
    expect(pickDefaultAddress('http://192.168.1.9:53961', ['192.168.1.9'])).toBe('http://192.168.1.9:53961')
    expect(pickDefaultAddress('http://localhost:53961', ['10.0.0.5'])).toBe('http://10.0.0.5:53961')
    expect(pickDefaultAddress('http://localhost:53961', [])).toBe('http://localhost:53961')
  })
})

describe('pickDisplayAddress（通告优先解析）', () => {
  it('通告非空：直接采用，忽略 origin / 候选 / basePath（服务端已归一，前端不再推导）', () => {
    expect(pickDisplayAddress('https://print.example.com', 'http://localhost:53961', ['10.0.0.5'], '/labelframe')).toBe(
      'https://print.example.com',
    )
    expect(pickDisplayAddress('https://print.example.com/labelframe', 'http://192.168.1.9:53961', ['192.168.1.9'], '')).toBe(
      'https://print.example.com/labelframe',
    )
  })

  it('无通告（null / undefined / 空串）：回落 origin × 候选 × basePath 推导链', () => {
    expect(pickDisplayAddress(null, 'http://localhost:53961', ['10.0.0.5'], '/labelframe')).toBe('http://10.0.0.5:53961/labelframe')
    expect(pickDisplayAddress(undefined, 'http://192.168.1.9:53961', ['192.168.1.9'], '/labelframe')).toBe(
      'http://192.168.1.9:53961/labelframe',
    )
    expect(pickDisplayAddress('', 'http://10.0.0.5:53961', null)).toBe('http://10.0.0.5:53961')
  })
})
