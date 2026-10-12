// 客户端「检查更新」辅助：
// 从服务端 client-packages 文件名解析客户端 MSI 版本（发版命名事实 LabelFrame-Client-<版本>.msi），
// 与本机版本（/api/host/config.version）比较——有新版提示获取入口、无新版明确「已是最新」；
// 不做应用内自动下载安装（/ 维持）。

/** 客户端 MSI 命名（release.yml 产物 / 发版事实）：LabelFrame-Client-<版本>.msi（大小写不敏感）。 */
const CLIENT_PACKAGE_PATTERN = /^LabelFrame-Client-(\d+(?:\.\d+)*)\.msi$/i

/** 解析客户端安装包文件名中的版本号（非客户端 MSI 命名返回 null——不参与比较，无误导）。 */
export function parseClientPackageVersion(fileName: string): string | null {
  const match = CLIENT_PACKAGE_PATTERN.exec(fileName.trim())
  return match ? match[1] : null
}

/** 取列表中可解析的最高客户端包版本（无可解析包返回 null）。 */
export function pickLatestPackageVersion(fileNames: string[]): string | null {
  let best: string | null = null
  for (const name of fileNames) {
    const version = parseClientPackageVersion(name)
    if (version && (best === null || compareVersions(version, best) > 0)) {
      best = version
    }
  }
  return best
}

/* * 数值段版本比较（VersionSemantics 的前端等价实现：段缺失视为 0；>0 = 左新，<0 = 左旧）。 */
export function compareVersions(left: string, right: string): number {
  const leftParts = left.split('.').map((part) => Number.parseInt(part, 10))
  const rightParts = right.split('.').map((part) => Number.parseInt(part, 10))
  const length = Math.max(leftParts.length, rightParts.length)
  for (let index = 0; index < length; index++) {
    const delta = (leftParts[index] ?? 0) - (rightParts[index] ?? 0)
    if (delta !== 0) {
      return delta
    }
  }
  return 0
}

/* * 「检查更新」结论（三种形态——有新版 / 已是最新 / 无法比较不显示）。 */
export type UpdateCheck =
  | { kind: 'update-available'; latestVersion: string; localVersion: string }
  | { kind: 'up-to-date'; localVersion: string }
  | { kind: 'unknown' }

/** 评估「检查更新」：本机版本与服务端最高客户端包版本比较（任一侧未知 → unknown，不显示结论）。 */
export function checkForUpdate(localVersion: string | null | undefined, fileNames: string[]): UpdateCheck {
  if (!localVersion) {
    return { kind: 'unknown' }
  }

  const latest = pickLatestPackageVersion(fileNames)
  if (!latest) {
    return { kind: 'unknown' }
  }

  return compareVersions(latest, localVersion) > 0
    ? { kind: 'update-available', latestVersion: latest, localVersion }
    : { kind: 'up-to-date', localVersion }
}
