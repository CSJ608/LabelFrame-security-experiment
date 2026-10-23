param()

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$temp = Join-Path ([IO.Path]::GetTempPath()) ("labelframe-builder-failclosed-" + [Guid]::NewGuid().ToString('N'))
$utf8NoBom = New-Object Text.UTF8Encoding($false)
$version = '9.8.7'

function Assert-True([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw "断言失败：$Message" }
}
function Get-Sha256([string]$Path) { (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant() }
function Write-Text([string]$Path, [string]$Text) {
    $parent = Split-Path -Parent $Path
    if ($parent) { New-Item -ItemType Directory -Force -Path $parent | Out-Null }
    [IO.File]::WriteAllText($Path, $Text, $utf8NoBom)
}
function Write-Json([string]$Path, [object]$Value) { Write-Text $Path (($Value | ConvertTo-Json -Depth 10) + "`n") }

try {
    $builder = Join-Path $temp 'builder'
    $cache = Join-Path $builder 'cache'
    $output = Join-Path $builder 'dist'
    New-Item -ItemType Directory -Force -Path $cache, $output, (Join-Path $builder 'helpers'), (Join-Path $builder 'templates\windows') | Out-Null
    Copy-Item (Join-Path $root 'packaging\offline-builder\build-windows-offline.ps1') $builder
    Copy-Item (Join-Path $root 'scripts\make-offline-bundle-win.ps1') (Join-Path $builder 'helpers')
    foreach ($name in @('README.md','README-upgrade.md','install-server.ps1','install-server.cmd')) {
        Copy-Item (Join-Path $root "packaging\offline-win\$name") (Join-Path $builder 'templates\windows')
    }

    $names = [ordered]@{
        'server-msi' = "LabelFrame-Server-$version.msi"
        'client-msi' = "LabelFrame-Client-$version.msi"
        'webui' = "labelframe-server-webui-$version.zip"
        'pda-apk' = "LabelFrame-AndroidHost-$version.apk"
        'plugin-zebra' = "labelframe-transport-zebra-$version.lfplugin"
        'plugin-tspl' = "labelframe-transport-tspl-$version.lfplugin"
        'runtime-desktop' = 'windowsdesktop-runtime-10.0.12-win-x64.exe'
        'runtime-aspnetcore' = 'aspnetcore-runtime-10.0.12-win-x64.exe'
    }
    $components = @()
    foreach ($entry in $names.GetEnumerator()) {
        $path = Join-Path $cache $entry.Value
        Write-Text $path "fixture:$($entry.Key)"
        $components += [ordered]@{
            id=$entry.Key; type=$(if ($entry.Key -like 'runtime-*') {'runtime'} else {'asset'}); version=$(if ($entry.Key -like 'runtime-*') {'10.0.12'} else {$version})
            dependsOn=@(); urls=@("https://127.0.0.1:1/$($entry.Value)"); sha256=Get-Sha256 $path; sizeBytes=(Get-Item $path).Length; silentArgs=''; topologies=@('offline'); notes='fixture'
        }
    }
    $manifestPath = Join-Path $cache 'install-manifest.json'
    Write-Json $manifestPath ([ordered]@{schemaVersion=1;labelframeVersion=$version;generatedAt='2026-01-01T00:00:00Z';components=$components})
    $sourcePath = Join-Path $builder 'offline-source-manifest.json'
    Write-Json $sourcePath ([ordered]@{
        schemaVersion=1;labelframeVersion=$version
        installManifest=[ordered]@{url='https://127.0.0.1:1/install-manifest.json';sha256=Get-Sha256 $manifestPath}
        serverImage=[ordered]@{reference="example.invalid/labelframe-server:$version";digest=('sha256:' + ('a'*64))}
        artifacts=@([ordered]@{id='compose';fileName='compose.yml';url='https://127.0.0.1:1/compose.yml';sha256=('b'*64)})
        webView2Standalone=[ordered]@{url='https://127.0.0.1:1/MicrosoftEdgeWebView2RuntimeInstallerX64.exe';fileName='MicrosoftEdgeWebView2RuntimeInstallerX64.exe';architecture='x64';publisher='Microsoft Corporation';minimumSizeBytes=100000000;verification='authenticode-publisher-file-name-minimum-size'}
        builder=[ordered]@{contractVersion='1';generator='fixture'}
    })

    $badClient = Join-Path $cache $names.'client-msi'
    Add-Content -LiteralPath $badClient -Value 'tampered'
    & (Join-Path $builder 'build-windows-offline.ps1') -OutputDir $output -CacheDir $cache -SourceManifestPath $sourcePath
    Assert-True ($LASTEXITCODE -ne 0) '坏哈希缓存且断源时应非零退出'
    Assert-True (-not (Test-Path $badClient)) '坏哈希缓存应先删除'
    Assert-True (@(Get-ChildItem $output -Filter 'labelframe-*.zip' -ErrorAction SilentlyContinue).Count -eq 0) '坏哈希失败不得产残包'

    Write-Text $badClient 'fixture:client-msi'
    $client = @($components | Where-Object id -eq 'client-msi')[0]
    $client.sha256 = Get-Sha256 $badClient
    $client.sizeBytes = (Get-Item $badClient).Length
    Write-Json $manifestPath ([ordered]@{schemaVersion=1;labelframeVersion=$version;generatedAt='2026-01-01T00:00:00Z';components=$components})
    $source = Get-Content $sourcePath -Raw -Encoding UTF8 | ConvertFrom-Json
    $source.installManifest.sha256 = Get-Sha256 $manifestPath
    Write-Json $sourcePath $source
    $webViewPath = Join-Path $cache 'MicrosoftEdgeWebView2RuntimeInstallerX64.exe'
    Write-Text $webViewPath 'small-and-unsigned'
    & (Join-Path $builder 'build-windows-offline.ps1') -OutputDir $output -CacheDir $cache -SourceManifestPath $sourcePath
    Assert-True ($LASTEXITCODE -ne 0) 'WebView2 小文件或坏签名且断源时应非零退出'
    Assert-True (-not (Test-Path $webViewPath)) '无效 WebView2 缓存应先删除'
    Assert-True (@(Get-ChildItem $output -Filter 'labelframe-*.zip' -ErrorAction SilentlyContinue).Count -eq 0) 'WebView2 失败不得产残包'

    Write-Host '通过：离线 Builder fail-closed fixture。' -ForegroundColor Green
}
finally {
    Remove-Item -LiteralPath $temp -Recurse -Force -ErrorAction SilentlyContinue
}
