# 组装 LabelFrame Windows Full / Upgrade 离线部署包。
# 契约：docs/DESIGN.md §6.13；用户入口由 Offline Builder 调用，本脚本只负责已校验原料的确定性落位与归档。
#
# Full 含 Desktop Runtime、ASP.NET Core Runtime 与 WebView2 x64 Standalone；Upgrade 不含运行时。
# 双包入口均为 install-server.cmd / install-server.ps1，另含受管资源清单、Builder 审计记录与覆盖全包的 SHA256SUMS。
# 常规组件按 install-manifest SHA256 校验；WebView2 Standalone 已由上层 Builder 完成文件名、体积与 Authenticode 校验。
# BootstrapperPath 仅保留旧布局调用兼容；新 Builder 不传该参数，也不把 Burn EXE 作为目标机入口。
#
# 用法（Windows PowerShell 5.1+ / PowerShell 7）：
#   powershell -ExecutionPolicy Bypass -File scripts\make-offline-bundle-win.ps1 -Version 1.4.0 `
#     -AssetsDir release -ManifestPath inputs\install-manifest.json -LatestPath inputs\latest.json `
#     -RuntimesDir cache -WebView2StandalonePath cache\MicrosoftEdgeWebView2RuntimeInstallerX64.exe `
#     -BuildInfoPath inputs\offline-build-info.json -TemplateRoot packaging\offline-win -OutputDir bundle
param(
    [Parameter(Mandatory = $true)][string]$Version,
    [Parameter(Mandatory = $true)][string]$AssetsDir,
    [Parameter(Mandatory = $true)][string]$ManifestPath,
    # 当版本地产物目录：双 MSI / 管理界面 zip / .lfplugin / PDA APK / Linux 归档（按清单文件名匹配）
    [string]$LatestPath = '',
    # latest.json 指针（缺省取清单同目录，仍不存在则不收录）
    [string]$BootstrapperPath = '',
    [string]$RuntimesDir = '',
    # 三个运行时安装器目录（清单生成期下载缓存；组全量包必填）
    [string]$OutputDir = '',
    [ValidateSet('full', 'upgrade', 'both')][string]$Kind = 'both',
    # Builder 可传入解包后的模板根；缺省保持仓库 packaging/offline-win。
    [string]$TemplateRoot = '',
    # 新 Builder 的 Full 独占 WebView2 x64 Standalone；为空时保持旧发版组包语义。
    [string]$WebView2StandalonePath = '',
    # Builder 生成的审计记录；提供时原样收录进双包。
    [string]$BuildInfoPath = ''
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

# ---- 2) 旧布局可继续收录引导 EXE；Builder Installer 模式不再要求该入口 ----
$bootstrapperFileName = "LabelFrame-Bootstrapper-$Version.exe"
if (-not $BootstrapperPath) {
    $candidateBootstrapper = Join-Path $AssetsDir $bootstrapperFileName
    if (Test-Path -LiteralPath $candidateBootstrapper) { $BootstrapperPath = $candidateBootstrapper }
}
if ($BootstrapperPath) {
    if (-not (Test-Path -LiteralPath $BootstrapperPath)) { Write-Failure "引导 EXE 不存在：$BootstrapperPath" }
    if ((Split-Path -Leaf $BootstrapperPath) -ne $bootstrapperFileName) {
        Write-Failure "引导 EXE 文件名应为 $bootstrapperFileName，实际：$(Split-Path -Leaf $BootstrapperPath)"
    }
}

# ---- 3) 组件源解析与校验（fail-closed：任一不符即中止）----
# 收录规则：排除 linux-server 归档（Windows 部署机无用，Linux 场景有专属离线包）；
# 运行时安装器仅进全量包（升级机已装运行时被引导链跳过，升级不重复携带）。
$excludedIds = @('linux-server')
$needFull = ($Kind -eq 'full') -or ($Kind -eq 'both')
if ($needFull -and -not $RuntimesDir) { Write-Failure '组全量包必须提供 -RuntimesDir（.NET 运行时安装器目录）' }
if ($BuildInfoPath -and -not (Test-Path -LiteralPath $BuildInfoPath)) { Write-Failure "offline-build-info 不存在：$BuildInfoPath" }
if ($WebView2StandalonePath -and -not (Test-Path -LiteralPath $WebView2StandalonePath)) { Write-Failure "WebView2 Standalone 不存在：$WebView2StandalonePath" }

$payload = @{} # id → @{ Source=源路径; Name=布局文件名; FullOnly=仅全量包收录 }
foreach ($component in @($manifest.components)) {
    if ($excludedIds -contains $component.id) {
        Write-Info "排除组件：[$($component.id)]（Windows 包不收录 Linux 归档）"
        continue
    }
    $isRuntime = ($component.type -eq 'runtime')
    if ($component.id -eq 'runtime-webview2' -and $WebView2StandalonePath) {
        Write-Info '排除在线 WebView2 Bootstrapper：Builder Full 使用 x64 Standalone'
        continue
    }
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
if ($WebView2StandalonePath) {
    $payload['runtime-webview2-standalone'] = @{
        Source = $WebView2StandalonePath
        Name = Split-Path -Leaf $WebView2StandalonePath
        FullOnly = $true
    }
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

    # 组件平铺落位；SHA256SUMS 覆盖除清单自身外的包内全部文件。
    $sumPairs = New-Object System.Collections.Generic.List[object]
    foreach ($entry in $payload.GetEnumerator()) {
        if ($entry.Value.FullOnly -and -not $WithRuntimes) { continue }
        $target = Join-Path $bundleDir $entry.Value.Name
        Copy-Item -LiteralPath $entry.Value.Source -Destination $target -Force
        $sumPairs.Add([pscustomobject]@{ Name = $entry.Value.Name; Hash = (Get-Sha256 $target) })
    }
    if ($BootstrapperPath) {
        $exeTarget = Join-Path $bundleDir $bootstrapperFileName
        Copy-Item -LiteralPath $BootstrapperPath -Destination $exeTarget -Force
        $sumPairs.Add([pscustomobject]@{ Name = $bootstrapperFileName; Hash = (Get-Sha256 $exeTarget) })
    }

    $manifestTarget = Join-Path $bundleDir 'install-manifest.json'
    [IO.File]::WriteAllText($manifestTarget, $manifestJson, $utf8NoBom)
    $sumPairs.Add([pscustomobject]@{ Name = 'install-manifest.json'; Hash = (Get-Sha256 $manifestTarget) })
    if ($LatestPath) {
        $latestTarget = Join-Path $bundleDir 'latest.json'
        [IO.File]::WriteAllText($latestTarget, ([IO.File]::ReadAllText($LatestPath)), $utf8NoBom)
        $sumPairs.Add([pscustomobject]@{ Name = 'latest.json'; Hash = (Get-Sha256 $latestTarget) })
    }

    $readme = [IO.File]::ReadAllText($ReadmeTemplate).Replace('__LABELFRAME_VERSION__', $Version)
    if ($readme -match '__LABELFRAME_VERSION__') { Write-Failure "README 模板占位替换不完全：$ReadmeTemplate" }
    $readmeTarget = Join-Path $bundleDir 'README.md'
    [IO.File]::WriteAllText($readmeTarget, $readme, $utf8NoBom)
    $sumPairs.Add([pscustomobject]@{ Name = 'README.md'; Hash = (Get-Sha256 $readmeTarget) })

    foreach ($installerName in @('install-server.ps1', 'install-server.cmd')) {
        $installerTemplate = Join-Path $TemplateRoot $installerName
        if (Test-Path -LiteralPath $installerTemplate) {
            Copy-Item -LiteralPath $installerTemplate -Destination (Join-Path $bundleDir $installerName) -Force
            $sumPairs.Add([pscustomobject]@{ Name = $installerName; Hash = (Get-Sha256 (Join-Path $bundleDir $installerName)) })
        }
    }
    if ($BuildInfoPath) {
        $buildInfo = Get-Content -LiteralPath $BuildInfoPath -Raw -Encoding UTF8 | ConvertFrom-Json
        $buildInfo | Add-Member -NotePropertyName packageKind -NotePropertyValue $(if ($WithRuntimes) { 'full' } else { 'upgrade' }) -Force
        $buildInfoTarget = Join-Path $bundleDir 'offline-build-info.json'
        [IO.File]::WriteAllText($buildInfoTarget, (($buildInfo | ConvertTo-Json -Depth 10) + "`n"), $utf8NoBom)
        $sumPairs.Add([pscustomobject]@{ Name = 'offline-build-info.json'; Hash = (Get-Sha256 $buildInfoTarget) })
    }

    $managedKinds = @{ 'client-msi' = 'client'; 'pda-apk' = 'pda'; 'plugin-zebra' = 'plugin'; 'plugin-tspl' = 'plugin' }
    $managedFiles = @()
    foreach ($managedId in @('client-msi', 'pda-apk', 'plugin-zebra', 'plugin-tspl')) {
        if (-not $payload.ContainsKey($managedId)) { Write-Failure "允许清单缺官方资源：$managedId" }
        $entry = $payload[$managedId]
        $managedFiles += [ordered]@{
            kind = $managedKinds[$managedId]
            id = $managedId
            fileName = $entry.Name
            version = $Version
            sha256 = Get-Sha256 (Join-Path $bundleDir $entry.Name)
        }
    }
    $managedJson = ([ordered]@{ schemaVersion = 1; labelframeVersion = $Version; files = $managedFiles } | ConvertTo-Json -Depth 5) + "`n"
    [IO.File]::WriteAllText((Join-Path $bundleDir 'managed-files.json'), $managedJson, $utf8NoBom)
    $sumPairs.Add([pscustomobject]@{ Name = 'managed-files.json'; Hash = (Get-Sha256 (Join-Path $bundleDir 'managed-files.json')) })

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
    $mustFiles = @('install-manifest.json', 'README.md', 'SHA256SUMS', 'managed-files.json')
    if ($BootstrapperPath) { $mustFiles += $bootstrapperFileName }
    if ($BuildInfoPath) { $mustFiles += 'offline-build-info.json' }
    foreach ($installerName in @('install-server.ps1', 'install-server.cmd')) {
        if (Test-Path -LiteralPath (Join-Path $TemplateRoot $installerName)) { $mustFiles += $installerName }
    }
    foreach ($must in $mustFiles) {
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

if (-not $TemplateRoot) { $TemplateRoot = Join-Path $root 'packaging\offline-win' }
$readmeFull = Join-Path $TemplateRoot 'README.md'
$readmeUpgrade = Join-Path $TemplateRoot 'README-upgrade.md'
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
Write-Host '部署：拷到目标机解压，以管理员身份运行 install-server.cmd（详见包内 README.md）。'

# 显式成功退出码：本脚本全 cmdlet 实现、成功路径不触碰 $LASTEXITCODE（保持调用方上次值 / null），
# 调用方（release.yml 与本地壳）以 $LASTEXITCODE 判定成败——末尾定值 0 才可判
exit 0
