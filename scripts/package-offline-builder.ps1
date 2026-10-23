param(
    [Parameter(Mandatory = $true)][string]$Version,
    [Parameter(Mandatory = $true)][string]$SourceManifestPath,
    [string]$OutputDir = ''
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if (-not $OutputDir) { $OutputDir = Join-Path $root 'artifacts\offline-builder' }
$sourceRoot = Join-Path $root 'packaging\offline-builder'
$windowsTemplates = Join-Path $root 'packaging\offline-win'
$dockerTemplates = Join-Path $root 'packaging\offline'
$staging = Join-Path ([IO.Path]::GetTempPath()) ("labelframe-offline-builder-" + [Guid]::NewGuid().ToString('N'))
$bundleName = "labelframe-offline-builder-$Version"
$bundleDir = Join-Path $staging $bundleName

try {
    if (-not (Test-Path -LiteralPath $SourceManifestPath -PathType Leaf)) { throw "来源清单不存在：$SourceManifestPath" }
    $manifest = Get-Content -LiteralPath $SourceManifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($manifest.labelframeVersion -ne $Version) { throw "来源清单版本与 Builder 包版本不一致。" }

    New-Item -ItemType Directory -Force -Path (Join-Path $bundleDir 'helpers'), (Join-Path $bundleDir 'templates\windows'), (Join-Path $bundleDir 'templates\docker'), $OutputDir | Out-Null
    foreach ($name in @('build-windows-offline.ps1','build-docker-offline.sh','README.md')) {
        $source = Join-Path $sourceRoot $name
        if (-not (Test-Path -LiteralPath $source -PathType Leaf)) { throw "Builder 源文件缺失：$source" }
        Copy-Item -LiteralPath $source -Destination (Join-Path $bundleDir $name) -Force
    }
    Copy-Item -LiteralPath $SourceManifestPath -Destination (Join-Path $bundleDir 'offline-source-manifest.json') -Force
    Copy-Item -LiteralPath (Join-Path $root 'scripts\make-offline-bundle-win.ps1') -Destination (Join-Path $bundleDir 'helpers\make-offline-bundle-win.ps1') -Force
    Copy-Item -LiteralPath (Join-Path $root 'scripts\make-offline-bundle.sh') -Destination (Join-Path $bundleDir 'helpers\make-offline-bundle.sh') -Force
    foreach ($name in @('README.md','README-upgrade.md','install-server.ps1','install-server.cmd')) {
        $source = Join-Path $windowsTemplates $name
        if (-not (Test-Path -LiteralPath $source -PathType Leaf)) { throw "Windows 模板缺失：$source" }
        Copy-Item -LiteralPath $source -Destination (Join-Path $bundleDir "templates\windows\$name") -Force
    }
    foreach ($name in @('README.md','install.sh')) {
        Copy-Item -LiteralPath (Join-Path $dockerTemplates $name) -Destination (Join-Path $bundleDir "templates\docker\$name") -Force
    }

    $zipPath = Join-Path $OutputDir "$bundleName.zip"
    $partial = "$zipPath.partial-$([Guid]::NewGuid().ToString('N'))"
    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zip = [IO.Compression.ZipFile]::Open($partial, [IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($file in [IO.Directory]::EnumerateFiles($bundleDir, '*', [IO.SearchOption]::AllDirectories)) {
            $relative = $file.Substring($staging.Length + 1).Replace('\','/')
            [void][IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $file, $relative, [IO.Compression.CompressionLevel]::Optimal)
        }
    }
    finally { $zip.Dispose() }
    Move-Item -LiteralPath $partial -Destination $zipPath -Force
    Write-Host "Offline Builder 已生成：$zipPath"
}
finally {
    Remove-Item -LiteralPath $staging -Recurse -Force -ErrorAction SilentlyContinue
}
exit 0
