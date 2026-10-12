// 连接地址候选选择（· ：下载中心「连接信息」卡）：
// 服务端 GET /api/server/ipv4-candidates 只枚举本机 IPv4 候选（私网优先排序），
// 协议与端口由前端按当前 origin 拼装——本模块收敛「默认选谁 / 候选怎么拼完整地址」两条纯规则，便于单测。

/**
 * 默认地址选择规则：
 * - origin 主机名在候选列表内 → 原样使用当前 origin（管理员正在访问的地址即最优）；
 * - origin 为 localhost / 127.0.0.1 / [::1]（设备不可达回环地址）且候选非空 → 回退首个候选
 *   （服务端已按「私网（局域网）优先」排序，即「回退首个局域网 IPv4」，不产生 localhost 废码）；
 * - 其余不匹配形态（如 Docker 端口映射部署：服务端枚举到的是容器内网 IP，与访问 origin 必不匹配）
 *   → 保持 origin：用户正在访问的地址就是设备可达的地址，绝不替换为连不上的容器内网 IP；
 * - 候选为空（旧版服务端 / 枚举失败 / null 未加载）→ origin 兜底（无更优信息时不猜测地址）。
 */
export function pickDefaultAddress(origin: string, candidates: string[] | null | undefined): string {
  if (!origin) return origin
  try {
    const u = new URL(origin)
    if (candidates && candidates.includes(u.hostname)) return origin
    const isLoopback = u.hostname === 'localhost' || u.hostname === '127.0.0.1' || u.hostname === '[::1]'
    if (isLoopback && candidates && candidates.length > 0) return joinAddress(u.protocol, candidates[0], u.port)
  } catch {
    // origin 非法（理论不发生：来自 window.location.origin）→ origin 兜底
  }
  return origin
}

/** 按当前 origin 的协议与端口拼装候选 IP 的完整地址（默认端口为空时省略端口段）。 */
export function joinAddress(protocol: string, ip: string, port: string): string {
  return port ? `${protocol}//${ip}:${port}` : `${protocol}//${ip}`
}
