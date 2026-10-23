# Builder 组包 fixture：使用合成原子资源验证 Windows 双包、Docker 包与来源清单生成契约。
param()

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$temp = Join-Path ([IO.Path]::GetTempPath()) ("labelframe-offline-builder-fixture-" + [Guid]::NewGuid().ToString('N'))
$version = '9.8.7'

function Assert-True([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw "断言失败：$Message" }
}

function Get-Sha256([string]$Path) {
    (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function New-FixtureFile([string]$Path, [string]$Content) {
    $parent = Split-Path -Parent $Path
    if ($parent) { New-Item -ItemType Directory -Force -Path $parent | Out-Null }
    [IO.File]::WriteAllText($Path, $Content, (New-Object Text.UTF8Encoding($false)))
}

try {
    $assets = Join-Path $temp 'assets'
    $runtimes = Join-Path $temp 'runtimes'
    $templates = Join-Path $temp 'templates'
    $output = Join-Path $temp 'output'
    New-Item -ItemType Directory -Force -Path $assets, $runtimes, $templates, $output | Out-Null

    $files = [ordered]@{
        'server-msi' = "LabelFrame-Server-$version.msi"
        'client-msi' = "LabelFrame-Client-$version.msi"
        'webui' = "labelframe-server-webui-$version.zip"
        'pda-apk' = "LabelFrame-AndroidHost-$version.apk"
        'plugin-zebra' = "labelframe-transport-zebra-$version.lfplugin"
        'plugin-tspl' = "labelframe-transport-tspl-$version.lfplugin"
        'runtime-desktop' = 'windowsdesktop-runtime-10.0.12-win-x64.exe'
        'runtime-aspnetcore' = 'aspnetcore-runtime-10.0.12-win-x64.exe'
    }
    foreach ($entry in $files.GetEnumerator()) {
        $dir = if ($entry.Key -like 'runtime-*') { $runtimes } else { $assets }
        New-FixtureFile (Join-Path $dir $entry.Value) "fixture:$($entry.Key)"
    }
    $webViewName = 'MicrosoftEdgeWebView2RuntimeInstallerX64.exe'
    New-FixtureFile (Join-Path $runtimes $webViewName) 'fixture:webview2-standalone'

    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $webuiPath = Join-Path $assets $files.webui
    Remove-Item -LiteralPath $webuiPath -Force
    $webuiSource = Join-Path $temp 'webui-source'
    New-FixtureFile (Join-Path $webuiSource 'index.html') '<html>fixture</html>'
    [IO.Compression.ZipFile]::CreateFromDirectory($webuiSource, $webuiPath)

    $components = New-Object Collections.Generic.List[object]
    foreach ($entry in $files.GetEnumerator()) {
        $dir = if ($entry.Key -like 'runtime-*') { $runtimes } else { $assets }
        $path = Join-Path $dir $entry.Value
        $type = switch -Wildcard ($entry.Key) {
            'server-msi' { 'msi' }
            'client-msi' { 'msi' }
            'webui' { 'webui-zip' }
            'pda-apk' { 'apk' }
            'plugin-*' { 'lfplugin' }
            default { 'runtime' }
        }
        $components.Add([ordered]@{
            id = $entry.Key
            type = $type
            version = if ($type -eq 'runtime') { '10.0.12' } else { $version }
            dependsOn = @()
            urls = @("https://example.invalid/$($entry.Value)")
            sha256 = Get-Sha256 $path
            sizeBytes = (Get-Item -LiteralPath $path).Length
            silentArgs = if ($type -eq 'runtime') { '/install /quiet /norestart' } else { '' }
            topologies = @('offline')
            notes = 'fixture'
        })
    }
    $manifestPath = Join-Path $temp 'install-manifest.json'
    [IO.File]::WriteAllText($manifestPath, (([ordered]@{
        schemaVersion = 1
        labelframeVersion = $version
        generatedAt = '2026-01-01T00:00:00Z'
        components = $components
    } | ConvertTo-Json -Depth 8) + "`n"), (New-Object Text.UTF8Encoding($false)))

    New-FixtureFile (Join-Path $templates 'README.md') '# Full __LABELFRAME_VERSION__'
    New-FixtureFile (Join-Path $templates 'README-upgrade.md') '# Upgrade __LABELFRAME_VERSION__'
    New-FixtureFile (Join-Path $templates 'install-server.ps1') 'Write-Host fixture'
    New-FixtureFile (Join-Path $templates 'install-server.cmd') '@echo off'
    $buildInfoPath = Join-Path $temp 'offline-build-info.json'
    New-FixtureFile $buildInfoPath '{"builderVersion":"fixture"}'

    & (Join-Path $root 'scripts\make-offline-bundle-win.ps1') `
        -Version $version -AssetsDir $assets -ManifestPath $manifestPath `
        -RuntimesDir $runtimes -WebView2StandalonePath (Join-Path $runtimes $webViewName) `
        -BuildInfoPath $buildInfoPath -TemplateRoot $templates -OutputDir $output -Kind both
    Assert-True ($LASTEXITCODE -eq 0) 'Windows 底层组包应成功'

    $fullZip = Join-Path $output "labelframe-offline-$version-win-x64.zip"
    $upgradeZip = Join-Path $output "labelframe-upgrade-$version-win-x64.zip"
    Assert-True (Test-Path -LiteralPath $fullZip) '应生成 Windows Full'
    Assert-True (Test-Path -LiteralPath $upgradeZip) '应生成 Windows Upgrade'

    $full = [IO.Compression.ZipFile]::OpenRead($fullZip)
    $upgrade = [IO.Compression.ZipFile]::OpenRead($upgradeZip)
    try {
        $fullNames = @($full.Entries | ForEach-Object FullName)
        $upgradeNames = @($upgrade.Entries | ForEach-Object FullName)
        foreach ($name in @($files.'runtime-desktop', $files.'runtime-aspnetcore', $webViewName)) {
            Assert-True (@($fullNames | Where-Object { $_ -like "*/$name" }).Count -eq 1) "Full 应含 runtime $name"
            Assert-True (@($upgradeNames | Where-Object { $_ -like "*/$name" }).Count -eq 0) "Upgrade 不得含 runtime $name"
        }
        foreach ($name in @('install-server.ps1', 'install-server.cmd', 'managed-files.json', 'offline-build-info.json', 'SHA256SUMS')) {
            Assert-True (@($fullNames | Where-Object { $_ -like "*/$name" }).Count -eq 1) "Full 应含 $name"
            Assert-True (@($upgradeNames | Where-Object { $_ -like "*/$name" }).Count -eq 1) "Upgrade 应含 $name"
        }
    }
    finally { $full.Dispose(); $upgrade.Dispose() }

    $fullExtract = Join-Path $temp 'full-extract'
    [IO.Compression.ZipFile]::ExtractToDirectory($fullZip, $fullExtract)
    $managedPath = Get-ChildItem -LiteralPath $fullExtract -Recurse -Filter managed-files.json | Select-Object -First 1 -ExpandProperty FullName
    $managed = Get-Content -LiteralPath $managedPath -Raw -Encoding UTF8 | ConvertFrom-Json
    Assert-True (@($managed.files).Count -eq 4) '允许清单只能登记 Client、PDA、双官方插件'
    Assert-True (@($managed.files | Where-Object { $_.kind -eq 'plugin' }).Count -eq 2) '允许清单应登记双官方插件'
    Assert-True (@($managed.files | Where-Object { $_.fileName -match 'webui|runtime|Server' }).Count -eq 0) '允许清单不得登记 Web UI、runtime、Server MSI'

    $linuxTemplates = Join-Path $temp 'linux-templates'
    Copy-Item -LiteralPath (Join-Path $root 'packaging\offline\README.md') -Destination (New-Item -ItemType Directory -Force -Path $linuxTemplates) -Force
    Copy-Item -LiteralPath (Join-Path $root 'packaging\offline\install.sh') -Destination $linuxTemplates -Force
    $imageTar = Join-Path $temp 'server.image.tar.gz'
    New-FixtureFile $imageTar 'fixture:image'
    $compose = Join-Path $temp 'compose.yml'
    New-FixtureFile $compose 'services: { labelframe-server: { image: ghcr.io/marci-labs/labelframe-server:${LABELFRAME_VERSION} } }'
    $linuxOutput = Join-Path $temp 'linux-output'
    & bash (Join-Path $root 'scripts/make-offline-bundle.sh') `
        -v $version --image-tar $imageTar --client-package (Join-Path $assets $files.'client-msi') `
        --pda-package (Join-Path $assets $files.'pda-apk') `
        --plugin-package (Join-Path $assets $files.'plugin-zebra') `
        --plugin-package (Join-Path $assets $files.'plugin-tspl') `
        --webui-zip $webuiPath --compose-file $compose --template-root $linuxTemplates `
        --build-info $buildInfoPath -o $linuxOutput
    Assert-True ($LASTEXITCODE -eq 0) 'Docker 底层组包应成功'
    $linuxArchive = Join-Path $linuxOutput "labelframe-offline-$version-linux-x64.tar.gz"
    Assert-True (Test-Path -LiteralPath $linuxArchive) '应生成 Docker Full 归档'
    $listing = (& tar --force-local -tzf $linuxArchive) -join "`n"
    Write-Host "Docker 归档条目：`n$listing"
    Assert-True ($listing -match '(?m)/offline-build-info.json$') 'Docker 包应含 offline-build-info.json'
    $archiveSidecar = "$linuxArchive.sha256"
    Assert-True (Test-Path -LiteralPath $archiveSidecar) 'Docker 包应输出整个 tar.gz 的外部 SHA256 旁车'
    $linuxExtract = Join-Path $temp 'linux-extract'
    New-Item -ItemType Directory -Force -Path $linuxExtract | Out-Null
    & tar --force-local -xzf $linuxArchive -C $linuxExtract
    Assert-True ($LASTEXITCODE -eq 0) 'Docker 包应可解压'
    $linuxRoot = Join-Path $linuxExtract "labelframe-offline-$version-linux-x64"
    $ledgerNames = @(Get-Content (Join-Path $linuxRoot 'SHA256SUMS') | ForEach-Object { ($_ -split '  ', 2)[1] })
    foreach ($required in @('install.sh', 'compose.yml', '.env', 'offline-build-info.json', 'plugins/web-ui/index.html')) {
        Assert-True ($ledgerNames -contains $required) "Docker SHA256SUMS 应覆盖 $required"
    }
    Add-Content -LiteralPath (Join-Path $linuxRoot 'compose.yml') -Value '# tampered'
    & bash -lc "cd '$($linuxRoot.Replace('\','/'))' && sha256sum -c SHA256SUMS --quiet"
    Assert-True ($LASTEXITCODE -ne 0) 'Docker 控制面文件篡改后包内校验必须失败'

    $sourceOutput = Join-Path $temp 'offline-source-manifest.json'
    $composeHash = Get-Sha256 $compose
    & (Join-Path $root 'scripts\generate-offline-source-manifest.ps1') `
        -Version $version -InstallManifestPath $manifestPath `
        -ServerImageReference "ghcr.io/marci-labs/labelframe-server:$version" `
        -ServerImageDigest ('sha256:' + ('a' * 64)) `
        -ComposePath $compose -OutputPath $sourceOutput
    Assert-True ($LASTEXITCODE -eq 0) '来源清单生成应成功'
    $source = Get-Content -LiteralPath $sourceOutput -Raw -Encoding UTF8 | ConvertFrom-Json
    Assert-True ($source.labelframeVersion -eq $version) '来源清单版本应固定'
    Assert-True ($source.webView2Standalone.url -match 'LinkId=2124701') 'WebView2 Standalone 必须使用 LinkId 2124701'
    Assert-True ($source.webView2Standalone.minimumSizeBytes -eq 100000000) 'WebView2 最小体积策略应为 100000000'
    Assert-True ($source.installManifest.sha256 -eq (Get-Sha256 $manifestPath)) '来源清单应记录 install manifest 哈希'
    Assert-True ($source.artifacts[0].sha256 -eq $composeHash) '来源清单应记录 compose 哈希'

    Write-Host '通过：离线 Builder fixture 自动测试。' -ForegroundColor Green
}
finally {
    if (Test-Path -LiteralPath $temp) { Remove-Item -LiteralPath $temp -Recurse -Force }
}
