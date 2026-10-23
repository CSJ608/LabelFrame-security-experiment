param(
    [string]$OutputDir = '',
    [string]$CacheDir = '',
    [string]$SourceManifestPath = ''
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$builderRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
if (-not $SourceManifestPath) { $SourceManifestPath = Join-Path $builderRoot 'offline-source-manifest.json' }
if (-not $OutputDir) { $OutputDir = Join-Path $builderRoot 'dist' }
if (-not $CacheDir) { $CacheDir = Join-Path $builderRoot 'cache' }
$utf8NoBom = New-Object System.Text.UTF8Encoding($false)
$stagingRoot = ''

function Fail([string]$Message) { throw $Message }
function Get-Sha256([string]$Path) { (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant() }

function Get-FileNameFromComponent([object]$Component) {
    foreach ($url in @($Component.urls)) {
        try {
            $uri = [Uri]$url
            $leaf = [Uri]::UnescapeDataString(($uri.Segments | Select-Object -Last 1).TrimEnd('/'))
            if ($leaf -and $leaf.IndexOf('.') -ge 1 -and $leaf -notmatch '[\\/:*?"<>|]') { return $leaf }
        } catch { }
    }
    Fail "组件 [$($Component.id)] 无法从 URL 推导文件名。"
}

function Invoke-Download([string[]]$Urls, [string]$TargetPath) {
    $temp = "$TargetPath.download-$([Guid]::NewGuid().ToString('N'))"
    $errors = New-Object System.Collections.Generic.List[string]
    try {
        foreach ($url in $Urls) {
            if (-not $url.StartsWith('https://', [StringComparison]::OrdinalIgnoreCase)) {
                $errors.Add("拒绝非 HTTPS 来源：$url")
                continue
            }
            try {
                Invoke-WebRequest -Uri $url -OutFile $temp -UseBasicParsing -TimeoutSec 600
                if (-not (Test-Path -LiteralPath $temp) -or (Get-Item -LiteralPath $temp).Length -le 0) { throw '下载结果为空' }
                Move-Item -LiteralPath $temp -Destination $TargetPath -Force
                return $url
            } catch {
                Remove-Item -LiteralPath $temp -Force -ErrorAction SilentlyContinue
                $errors.Add("$url：$($_.Exception.Message)")
            }
        }
    }
    finally { Remove-Item -LiteralPath $temp -Force -ErrorAction SilentlyContinue }
    Fail "所有下载源均失败：$($errors -join '；')"
}

function Get-VerifiedFile([string]$Id, [string]$FileName, [string[]]$Urls, [string]$ExpectedSha256, [long]$ExpectedSize = 0) {
    if ($FileName -ne [IO.Path]::GetFileName($FileName)) { Fail "文件名非法：$FileName" }
    $path = Join-Path $CacheDir $FileName
    $valid = $false
    if (Test-Path -LiteralPath $path) {
        $actual = Get-Sha256 $path
        $valid = $actual -eq $ExpectedSha256 -and ($ExpectedSize -le 0 -or (Get-Item -LiteralPath $path).Length -eq $ExpectedSize)
        if (-not $valid) {
            Write-Host "缓存不符，删除重取：[$Id] $FileName"
            Remove-Item -LiteralPath $path -Force
        }
    }
    $source = 'cache'
    if (-not $valid) {
        $source = Invoke-Download $Urls $path
        if ((Get-Sha256 $path) -ne $ExpectedSha256) { Remove-Item $path -Force; Fail "组件 [$Id] 下载后 SHA256 不符。" }
        if ($ExpectedSize -gt 0 -and (Get-Item -LiteralPath $path).Length -ne $ExpectedSize) { Remove-Item $path -Force; Fail "组件 [$Id] 下载后大小不符。" }
    }
    [pscustomobject]@{ Id = $Id; Path = $path; FileName = $FileName; Source = $source; Sha256 = Get-Sha256 $path; SizeBytes = (Get-Item $path).Length }
}

function Get-WebView2Standalone([object]$Policy) {
    $fileName = [string]$Policy.fileName
    if ($fileName -ne 'MicrosoftEdgeWebView2RuntimeInstallerX64.exe') { Fail "WebView2 Standalone 文件名策略非法：$fileName" }
    $path = Join-Path $CacheDir $fileName
    $source = 'cache'
    $valid = $false
    if (Test-Path -LiteralPath $path) {
        $signature = Get-AuthenticodeSignature -LiteralPath $path
        $valid = (Get-Item $path).Length -gt [long]$Policy.minimumSizeBytes -and $signature.Status -eq 'Valid' -and $signature.SignerCertificate.Subject -match '(^|,\s*)CN=Microsoft Corporation(,|$)'
        if (-not $valid) { Write-Host 'WebView2 缓存验签或体积不符，删除重取。'; Remove-Item $path -Force }
    }
    if (-not $valid) { $source = Invoke-Download @([string]$Policy.url) $path }
    $signature = Get-AuthenticodeSignature -LiteralPath $path
    if ((Get-Item $path).Length -le [long]$Policy.minimumSizeBytes) { Remove-Item $path -Force; Fail 'WebView2 Standalone 体积过小，疑似误取联网 Bootstrapper。' }
    if ($signature.Status -ne 'Valid') { Remove-Item $path -Force; Fail "WebView2 Authenticode 无效：$($signature.Status)" }
    if ($signature.SignerCertificate.Subject -notmatch '(^|,\s*)CN=Microsoft Corporation(,|$)') { Remove-Item $path -Force; Fail "WebView2 发布者不符：$($signature.SignerCertificate.Subject)" }
    [pscustomobject]@{ Id = 'runtime-webview2-standalone'; Path = $path; FileName = $fileName; Source = $source; Sha256 = Get-Sha256 $path; SizeBytes = (Get-Item $path).Length; Publisher = $signature.SignerCertificate.Subject }
}

try {
    if (-not (Test-Path -LiteralPath $SourceManifestPath -PathType Leaf)) { Fail "来源清单不存在：$SourceManifestPath" }
    try { $sourceManifest = Get-Content -LiteralPath $SourceManifestPath -Raw -Encoding UTF8 | ConvertFrom-Json }
    catch { Fail "来源清单不是合法 JSON：$($_.Exception.Message)" }
    if ($sourceManifest.schemaVersion -ne 1) { Fail "不支持的 Builder schemaVersion：$($sourceManifest.schemaVersion)" }
    $version = [string]$sourceManifest.labelframeVersion
    if ($version -notmatch '^\d+\.\d+\.\d+(?:[-+][0-9A-Za-z.-]+)?$') { Fail "来源清单版本非法：$version" }

    New-Item -ItemType Directory -Force -Path $OutputDir, $CacheDir | Out-Null
    $stagingRoot = Join-Path ([IO.Path]::GetTempPath()) ("labelframe-windows-builder-" + [Guid]::NewGuid().ToString('N'))
    $assetsDir = Join-Path $stagingRoot 'assets'
    New-Item -ItemType Directory -Force -Path $assetsDir | Out-Null

    $manifestFile = Get-VerifiedFile 'install-manifest' 'install-manifest.json' @([string]$sourceManifest.installManifest.url) ([string]$sourceManifest.installManifest.sha256)
    try { $installManifest = Get-Content -LiteralPath $manifestFile.Path -Raw -Encoding UTF8 | ConvertFrom-Json }
    catch { Fail "install manifest 不是合法 JSON：$($_.Exception.Message)" }
    if ($installManifest.labelframeVersion -ne $version) { Fail 'install manifest 与 Builder 版本不一致。' }

    $required = @('server-msi','client-msi','webui','pda-apk','plugin-zebra','plugin-tspl','runtime-desktop','runtime-aspnetcore')
    $downloaded = New-Object System.Collections.Generic.List[object]
    foreach ($id in $required) {
        $component = @($installManifest.components | Where-Object id -eq $id)
        if ($component.Count -ne 1) { Fail "install manifest 组件 [$id] 数量应为 1，实际 $($component.Count)。" }
        $item = $component[0]
        $file = Get-VerifiedFile $id (Get-FileNameFromComponent $item) @($item.urls) ([string]$item.sha256) ([long]$item.sizeBytes)
        $downloaded.Add($file)
        Copy-Item -LiteralPath $file.Path -Destination (Join-Path $assetsDir $file.FileName) -Force
    }
    $webView = Get-WebView2Standalone $sourceManifest.webView2Standalone
    $downloaded.Add($webView)

    $buildInfoPath = Join-Path $stagingRoot 'offline-build-info.json'
    $buildInfo = [ordered]@{
        schemaVersion = 1
        builderContractVersion = [string]$sourceManifest.builder.contractVersion
        labelframeVersion = $version
        packageKind = 'windows-both'
        generatedAtUtc = [DateTimeOffset]::UtcNow.ToString('o')
        files = @($downloaded | ForEach-Object { [ordered]@{ id=$_.Id; fileName=$_.FileName; source=$_.Source; sha256=$_.Sha256; sizeBytes=$_.SizeBytes; publisher=$_.Publisher } })
    }
    [IO.File]::WriteAllText($buildInfoPath, (($buildInfo | ConvertTo-Json -Depth 8) + "`n"), $utf8NoBom)

    $bundleOutput = Join-Path $stagingRoot 'output'
    $helper = Join-Path $builderRoot 'helpers\make-offline-bundle-win.ps1'
    $templates = Join-Path $builderRoot 'templates\windows'
    & $helper -Version $version -AssetsDir $assetsDir -ManifestPath $manifestFile.Path -RuntimesDir $CacheDir `
        -WebView2StandalonePath $webView.Path -BuildInfoPath $buildInfoPath -TemplateRoot $templates -OutputDir $bundleOutput -Kind both
    if ($LASTEXITCODE -ne 0) { Fail 'Windows 双包底层组装失败。' }

    foreach ($name in @("labelframe-offline-$version-win-x64.zip", "labelframe-upgrade-$version-win-x64.zip")) {
        $source = Join-Path $bundleOutput $name
        if (-not (Test-Path -LiteralPath $source -PathType Leaf)) { Fail "组包缺少产物：$name" }
        $target = Join-Path $OutputDir $name
        $temporaryTarget = "$target.partial-$([Guid]::NewGuid().ToString('N'))"
        Copy-Item -LiteralPath $source -Destination $temporaryTarget -Force
        Move-Item -LiteralPath $temporaryTarget -Destination $target -Force
        Write-Host "已生成：$target"
    }
}
catch {
    Write-Host "失败：$($_.Exception.Message)" -ForegroundColor Red
    exit 1
}
finally {
    if ($stagingRoot) { Remove-Item -LiteralPath $stagingRoot -Recurse -Force -ErrorAction SilentlyContinue }
}
exit 0
