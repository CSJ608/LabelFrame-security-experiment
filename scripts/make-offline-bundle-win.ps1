# 组装 LabelFrame Windows 离线部署包（全量 / 升级）并打 zip。
# 契约：docs/DESIGN.md §6.2（安装清单与离线布局）；用法文档见 docs/DEPLOY.md「Windows 离线部署包」。
#
# 用途：发版流水线（release.yml windows-offline-bundle job）把当版 Release 产物与运行时安装器组装成两个
#       离线分发单元，随 Release 附件发布；本地构建调试亦可直接调用。
#   - labelframe-offline-<版本>-win-x64.zip   全量包：新机首装——含三个运行时安装器，目标机免预装
#     .NET 10 / WebView2，解压后双击包内引导 EXE 全程零外网；
#   - labelframe-upgrade-<版本>-win-x64.zip   升级包：已装机离线升级——内容 = 全量减三运行时安装器
#     （升级机运行时已装，引导链 DetectCondition 跳过、不再重复携带百 MB 级安装器；运行时中途缺失或
#     大版本要求抬升时升级包会离线失败，属预期，兜底改用全量包——见包内 README）。
# 包内布局 = 离线布局契约：组件平铺于包根、文件名按 urls[0] 末段推导（查询型直链固定名兜底）、
#   install-manifest.json / latest.json 原样字节落盘、引导 EXE 同目录——目标机双击引导 EXE 即以本地
#   清单与组件完成安装 / 升级（远程源仅在本地文件缺席时使用）。
# 校验语义与布局生成脚本一致：常规组件 sha256 与清单逐字节一致；evergreen 组件（WebView2 恒变直链无
#   摘要可钉）按微软 Authenticode 发布者验签——任一组件不符即整包失败，不产出残包。
# 整包不进 install-manifest（自足分发单元而非可安装产物，与 Linux 离线包同口径）。
#
# 用法（Windows PowerShell 5.1+ / PowerShell 7）：
#   powershell -ExecutionPolicy Bypass -File scripts\make-offline-bundle-win.ps1 -Version 1.3.0 `
#     -AssetsDir release -ManifestPath inputs\install-manifest.json -LatestPath inputs\latest.json `
#     -RuntimesDir inputs\runtimes -BootstrapperPath release\LabelFrame-Bootstrapper-1.3.0.exe `
#     -OutputDir bundle            # -Kind full / upgrade / both（默认 both）
param(
    [Parameter(Mandatory = $true)][string]$Version,
    [Parameter(Mandatory = $true)][string]$AssetsDir,
    [Parameter(Mandatory = $true)][string]$ManifestPath,
    # 当版本地产物目录：双 MSI / 管理界面 zip / .lfplugin / PDA APK / Linux 归档（按清单文件名匹配）
    [string]$LatestPath = '',
    # latest.json 指针（缺省取清单同目录，仍不存在则不收录）
    [Parameter(Mandatory = $true)][string]$BootstrapperPath,
    [string]$RuntimesDir = '',
    # 三个运行时安装器目录（清单生成期下载缓存；组全量包必填）
    [string]$OutputDir = '',
    [ValidateSet('full', 'upgrade', 'both')][string]$Kind = 'both'
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$root = Split-Path -Parent $PSScriptRoot
if (-not $OutputDir) { $OutputDir = Join-Path $root 'artifacts\offline-bundle-win' }

function Write-Failure([string]$Message) {
    # GitHub Actions 注解要求单行；本地自验时退出码即判据
    Write-Host "::error::$Message"
    Write-Host "失败：$Message" -ForegroundColor Red
    exit 1
}
function Write-Info([string]$Message) { Write-Host "[Windows 离线包] $Message" }
function Get-Sha256([string]$Path) {
    (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

# 组件布局文件名推导（与核心库 OfflineLayoutNaming 同源约定：urls[0] 末段含扩展名 → 固定名兜底）
$fixedNames = @{ 'runtime-webview2' = 'MicrosoftEdgeWebView2RuntimeInstallerSimpleX64.exe' }
function Get-LayoutFileName([object]$Component) {
    $url = @($Component.urls)[0]
    if ($url) {
        try {
            $uri = [Uri]$url
            if ($uri.Scheme -in @('http', 'https') -and $uri.Segments.Length -gt 0) {
                $segment = $uri.Segments[$uri.Segments.Length - 1].TrimEnd('/')
                if ($segment -and $segment.IndexOf('.') -ge 1 -and $segment -notmatch '[\\/:*?"<>|]') {
                    return [Uri]::UnescapeDataString($segment)
                }
            }
        } catch {
        }
    }
    if ($fixedNames.ContainsKey($Component.id)) { return $fixedNames[$Component.id] }
    return $null
}

function Test-EvergreenComponent([object]$Component) {
    # 恒变直链无摘要可钉（version = evergreen）——校验走发布者验签而非哈希
    return ($Component.version -is [string]) -and ($Component.version -eq 'evergreen')
}

function Verify-ComponentFile([string]$Path, [object]$Component) {
    # 返回 $null = 校验通过；否则返回中文失败原因
    if (Test-EvergreenComponent $Component) {
        $signature = Get-AuthenticodeSignature -LiteralPath $Path
        if ($signature.Status -ne 'Valid') {
            return "验签拒绝：Authenticode 签名无效（$($signature.Status)）"
        }
        $subject = $signature.SignerCertificate.Subject
        if (($subject -notmatch '(^|,\s*)CN=([^,]+)') -or ($Matches[2].Trim() -ne 'Microsoft Corporation')) {
            return "发布者不符：期望 CN=Microsoft Corporation，实际主体 = $subject"
        }
        return $null
    }
    $actual = Get-Sha256 $Path
    if ($actual -ne $Component.sha256) {
        return "哈希不符（清单=$($Component.sha256.Substring(0, 12))… 实测=$($actual.Substring(0, 12))…）"
    }
    return $null
}

# ---- 1) 清单加载与版本对齐 ----
if (-not (Test-Path -LiteralPath $ManifestPath)) { Write-Failure "安装清单不存在：$ManifestPath" }
$manifestJson = [IO.File]::ReadAllText($ManifestPath)
try { $manifest = $manifestJson | ConvertFrom-Json }
catch { Write-Failure "安装清单不是合法 JSON：$($_.Exception.Message)" }
if ($manifest.labelframeVersion -ne $Version) {
    Write-Failure "清单版本（$($manifest.labelframeVersion)）与 -Version（$Version）不一致——组包必须用当版清单"
}
if (-not $LatestPath) {
    $candidate = Join-Path (Split-Path -Parent $ManifestPath) 'latest.json'
    if (Test-Path -LiteralPath $candidate) { $LatestPath = $candidate }
}

# ---- 2) 引导 EXE（离线包必须内含；文件名即包内落位名）----
$bootstrapperFileName = "LabelFrame-Bootstrapper-$Version.exe"
if (-not $BootstrapperPath) { $BootstrapperPath = Join-Path $AssetsDir $bootstrapperFileName }
if (-not (Test-Path -LiteralPath $BootstrapperPath)) { Write-Failure "引导 EXE 不存在：$BootstrapperPath" }
if ((Split-Path -Leaf $BootstrapperPath) -ne $bootstrapperFileName) {
    Write-Failure "引导 EXE 文件名应为 $bootstrapperFileName，实际：$(Split-Path -Leaf $BootstrapperPath)"
}

# ---- 3) 组件源解析与校验（fail-closed：任一不符即中止）----
# 收录规则：排除 linux-server 归档（Windows 部署机无用，Linux 场景有专属离线包）；
# 运行时安装器仅进全量包（升级机已装运行时被引导链跳过，升级不重复携带）。
$excludedIds = @('linux-server')
$needFull = ($Kind -eq 'full') -or ($Kind -eq 'both')
if ($needFull -and -not $RuntimesDir) { Write-Failure '组全量包必须提供 -RuntimesDir（三个运行时安装器目录）' }

$payload = @{} # id → @{ Source=源路径; Name=布局文件名; FullOnly=仅全量包收录 }
foreach ($component in @($manifest.components)) {
    if ($excludedIds -contains $component.id) {
        Write-Info "排除组件：[$($component.id)]（Windows 包不收录 Linux 归档）"
        continue
    }
    $isRuntime = ($component.type -eq 'runtime')
    if ($isRuntime -and -not $needFull) {
        Write-Info "跳过运行时组件：[$($component.id)]（升级包不携带）"
        continue
    }

    $fileName = Get-LayoutFileName $component
    if (-not $fileName) {
        Write-Failure "组件 $($component.id) 无法推导布局文件名（urls[0] 无可辨识文件名且不在固定名兜底表）"
    }
    $sourceDir = if ($isRuntime) { $RuntimesDir } else { $AssetsDir }
    $sourcePath = Join-Path $sourceDir $fileName
    if (-not (Test-Path -LiteralPath $sourcePath)) {
        Write-Failure "组件 [$($component.id)] 源文件缺失：$sourcePath"
    }
    $verifyFailure = Verify-ComponentFile $sourcePath $component
    if ($verifyFailure) { Write-Failure "组件 [$($component.id)] 校验失败：$verifyFailure" }
    $payload[$component.id] = @{ Source = $sourcePath; Name = $fileName; FullOnly = $isRuntime }
    Write-Info "组件就绪（校验通过）：[$($component.id)] $fileName"
}

# ---- 4) 逐包组装（目录 + SHA256SUMS + README + 结构断言 + zip）----
$utf8NoBom = New-Object System.Text.UTF8Encoding($false)
# 平台坑：pwsh 7 内置压缩程序集，Windows PowerShell 5.1 须显式预载（ZipArchiveMode / CompressionLevel 在
# System.IO.Compression，ZipFile / ZipFileExtensions 在 System.IO.Compression.FileSystem）——少一个即运行期 TypeNotFound
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

function New-BundleDir([string]$KindSlug, [bool]$WithRuntimes, [string]$ReadmeTemplate) {
    $dirName = "labelframe-$KindSlug-$Version-win-x64"
    $bundleDir = Join-Path $OutputDir $dirName
    if (Test-Path -LiteralPath $bundleDir) { Remove-Item -LiteralPath $bundleDir -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $bundleDir | Out-Null

    # 组件平铺落位；SHA256SUMS 覆盖全部载荷文件（组件 + 引导 EXE；manifest / latest / README 不入册）
    $sumPairs = New-Object System.Collections.Generic.List[object]
    foreach ($entry in $payload.GetEnumerator()) {
        if ($entry.Value.FullOnly -and -not $WithRuntimes) { continue }
        $target = Join-Path $bundleDir $entry.Value.Name
        Copy-Item -LiteralPath $entry.Value.Source -Destination $target -Force
        $sumPairs.Add([pscustomobject]@{ Name = $entry.Value.Name; Hash = (Get-Sha256 $target) })
    }
    $exeTarget = Join-Path $bundleDir $bootstrapperFileName
    Copy-Item -LiteralPath $BootstrapperPath -Destination $exeTarget -Force
    $sumPairs.Add([pscustomobject]@{ Name = $bootstrapperFileName; Hash = (Get-Sha256 $exeTarget) })

    [IO.File]::WriteAllText((Join-Path $bundleDir 'install-manifest.json'), $manifestJson, $utf8NoBom)
    if ($LatestPath) {
        [IO.File]::WriteAllText((Join-Path $bundleDir 'latest.json'), ([IO.File]::ReadAllText($LatestPath)), $utf8NoBom)
    }

    $readme = [IO.File]::ReadAllText($ReadmeTemplate).Replace('__LABELFRAME_VERSION__', $Version)
    if ($readme -match '__LABELFRAME_VERSION__') { Write-Failure "README 模板占位替换不完全：$ReadmeTemplate" }
    [IO.File]::WriteAllText((Join-Path $bundleDir 'README.md'), $readme, $utf8NoBom)

    # sha256sum 兼容格式（小写哈希 + 双空格 + 相对路径，LF 行尾）；按文件名排序保证逐字节可复现
    $sums = ($sumPairs | Sort-Object Name | ForEach-Object { "$($_.Hash)  $($_.Name)" }) -join "`n"
    [IO.File]::WriteAllText((Join-Path $bundleDir 'SHA256SUMS'), $sums + "`n", $utf8NoBom)

    # 结构断言（fail-closed）：载荷齐备 + 升级包必须无运行时文件 + 册子非空
    foreach ($entry in $payload.GetEnumerator()) {
        if ($entry.Value.FullOnly -and -not $WithRuntimes) {
            if (Test-Path -LiteralPath (Join-Path $bundleDir $entry.Value.Name)) {
                Write-Failure "结构断言失败：升级包含运行时文件 $($entry.Value.Name)"
            }
            continue
        }
        if (-not (Test-Path -LiteralPath (Join-Path $bundleDir $entry.Value.Name))) {
            Write-Failure "结构断言失败：缺组件文件 $($entry.Value.Name)"
        }
    }
    foreach ($must in @($bootstrapperFileName, 'install-manifest.json', 'README.md', 'SHA256SUMS')) {
        if (-not (Test-Path -LiteralPath (Join-Path $bundleDir $must))) { Write-Failure "结构断言失败：缺 $must" }
    }
    if ($LatestPath -and -not (Test-Path -LiteralPath (Join-Path $bundleDir 'latest.json'))) {
        Write-Failure '结构断言失败：缺 latest.json'
    }
    $sumsPath = Join-Path $bundleDir 'SHA256SUMS'
    if (-not (Test-Path -LiteralPath $sumsPath) -or ((Get-Item -LiteralPath $sumsPath).Length -eq 0)) {
        Write-Failure '结构断言失败：SHA256SUMS 为空'
    }

    # zip（显式正斜杠条目名 + 顶层目录，解压得同名目录；条目逐文件写入避免 Compress-Archive 的 5.1 反斜杠条目）
    $zipPath = Join-Path $OutputDir "$dirName.zip"
    if (Test-Path -LiteralPath $zipPath) { Remove-Item -LiteralPath $zipPath -Force }
    $zip = [IO.Compression.ZipFile]::Open($zipPath, [IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($file in [IO.Directory]::EnumerateFiles($bundleDir, '*', [IO.SearchOption]::AllDirectories)) {
            $rel = $file.Substring($bundleDir.Length + 1).Replace('\', '/')
            [void][IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
                $zip, $file, "$dirName/$rel", [IO.Compression.CompressionLevel]::Optimal)
        }
    }
    finally { $zip.Dispose() }
    if (-not (Test-Path -LiteralPath $zipPath)) { Write-Failure "zip 生成失败：$zipPath" }

    $sizeMb = [Math]::Round((Get-Item -LiteralPath $zipPath).Length / 1MB, 1)
    Write-Info "包完成：$zipPath（$sizeMb MB，载荷 $($sumPairs.Count) 件）"
    return $zipPath
}

$readmeFull = Join-Path $root 'packaging\offline-win\README.md'
$readmeUpgrade = Join-Path $root 'packaging\offline-win\README-upgrade.md'
foreach ($template in @($readmeFull, $readmeUpgrade)) {
    if (-not (Test-Path -LiteralPath $template)) { Write-Failure "缺 README 模板：$template" }
}

New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null
$results = @()
if ($needFull) { $results += New-BundleDir 'offline' $true $readmeFull }
if (($Kind -eq 'upgrade') -or ($Kind -eq 'both')) { $results += New-BundleDir 'upgrade' $false $readmeUpgrade }

Write-Host ''
Write-Host "Windows 离线部署包组包完成：$($results.Count) 个" -ForegroundColor Green
foreach ($zip in $results) { Write-Host "  $zip" }
Write-Host '部署：拷到目标机解压，双击包内引导 EXE——全程零外网（详见包内 README.md）。'

# 显式成功退出码：本脚本全 cmdlet 实现、成功路径不触碰 $LASTEXITCODE（保持调用方上次值 / null），
# 调用方（release.yml 与本地壳）以 $LASTEXITCODE 判定成败——末尾定值 0 才可判
exit 0
