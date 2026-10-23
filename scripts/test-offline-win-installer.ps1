param(
    [string]$PowerShellPath = '',
    [switch]$KeepFixture
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$installer = Join-Path $root 'packaging\offline-win\install-server.ps1'
$fixtureRoot = Join-Path $root 'artifacts\offline-win-installer-tests'
$utf8NoBom = New-Object System.Text.UTF8Encoding($false)
$script:failed = 0
$script:results = New-Object System.Collections.Generic.List[string]

function Add-Result([string]$Name, [bool]$Pass, [string]$Detail) {
    $status = if ($Pass) { '通过' } else { '失败' }
    $script:results.Add("$Name $status — $Detail")
    if (-not $Pass) { $script:failed++ }
    Write-Host "[$status] $Name — $Detail" -ForegroundColor ($(if ($Pass) { 'Green' } else { 'Red' }))
}

function Assert-True([string]$Name, [bool]$Condition, [string]$Detail) {
    Add-Result $Name $Condition $Detail
}

function Write-Json([string]$Path, [object]$Value) {
    [IO.File]::WriteAllText($Path, ($Value | ConvertTo-Json -Depth 12), $utf8NoBom)
}

function Get-Sha256([string]$Path) {
    (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function New-WebUiZip([string]$Path, [string]$Marker) {
    $source = Join-Path ([IO.Path]::GetTempPath()) ("lf-webui-" + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path (Join-Path $source 'assets') -Force | Out-Null
    [IO.File]::WriteAllText((Join-Path $source 'index.html'), "<html>$Marker</html>", $utf8NoBom)
    [IO.File]::WriteAllText((Join-Path $source 'assets\app.js'), "window.fixture='$Marker';", $utf8NoBom)
    try {
        Add-Type -AssemblyName System.IO.Compression.FileSystem
        if (Test-Path -LiteralPath $Path) { Remove-Item -LiteralPath $Path -Force }
        [IO.Compression.ZipFile]::CreateFromDirectory($source, $Path)
    }
    finally {
        Remove-Item -LiteralPath $source -Recurse -Force -ErrorAction SilentlyContinue
    }
}

function New-Fixture([string]$Name, [ValidateSet('full', 'upgrade')][string]$Kind = 'upgrade', [bool]$RuntimePresent = $true, [bool]$Headless = $false) {
    $dir = Join-Path $fixtureRoot $Name
    $package = Join-Path $dir 'package'
    $machine = Join-Path $dir 'machine'
    $programData = Join-Path $machine 'ProgramData'
    $clientRoot = Join-Path $machine 'distribution\client'
    $pdaRoot = Join-Path $machine 'distribution\pda'
    $pluginRoot = Join-Path $machine 'distribution\plugin'
    $webUiRoot = Join-Path $machine 'web-ui'
    New-Item -ItemType Directory -Path $package, $programData, $clientRoot, $pdaRoot, $pluginRoot, $webUiRoot -Force | Out-Null

    $version = '9.8.7'
    $names = [ordered]@{
        Server = "LabelFrame-Server-$version.msi"
        Client = "LabelFrame-Client-$version.msi"
        Pda = "LabelFrame-AndroidHost-$version.apk"
        Zebra = "labelframe-transport-zebra-$version.lfplugin"
        Tspl = "labelframe-transport-tspl-$version.lfplugin"
        WebUi = "labelframe-server-webui-$version.zip"
    }
    foreach ($key in @('Server', 'Client', 'Pda', 'Zebra', 'Tspl')) {
        [IO.File]::WriteAllText((Join-Path $package $names[$key]), "$key-$version", $utf8NoBom)
    }
    New-WebUiZip (Join-Path $package $names.WebUi) 'new-ui'

    if ($Kind -eq 'full') {
        foreach ($runtimeName in @(
            'windowsdesktop-runtime-10.0.1-win-x64.exe',
            'aspnetcore-runtime-10.0.1-win-x64.exe',
            'MicrosoftEdgeWebView2RuntimeInstallerX64.exe')) {
            [IO.File]::WriteAllText((Join-Path $package $runtimeName), $runtimeName, $utf8NoBom)
        }
    }

    $managed = @(
        [ordered]@{ kind = 'client'; id = 'client-msi'; fileName = $names.Client; version = $version; sha256 = (Get-Sha256 (Join-Path $package $names.Client)) },
        [ordered]@{ kind = 'pda'; id = 'pda-apk'; fileName = $names.Pda; version = $version; sha256 = (Get-Sha256 (Join-Path $package $names.Pda)) },
        [ordered]@{ kind = 'plugin'; id = 'plugin-zebra'; fileName = $names.Zebra; version = $version; sha256 = (Get-Sha256 (Join-Path $package $names.Zebra)) },
        [ordered]@{ kind = 'plugin'; id = 'plugin-tspl'; fileName = $names.Tspl; version = $version; sha256 = (Get-Sha256 (Join-Path $package $names.Tspl)) }
    )
    Write-Json (Join-Path $package 'managed-files.json') ([ordered]@{ schemaVersion = 1; labelframeVersion = $version; files = $managed })
    Write-Json (Join-Path $package 'offline-build-info.json') ([ordered]@{ schemaVersion = 1; labelframeVersion = $version; packageKind = $Kind; generatedAtUtc = '2026-10-23T00:00:00Z' })

    $sumFiles = Get-ChildItem -LiteralPath $package -File | Where-Object { $_.Name -ne 'SHA256SUMS' } | Sort-Object Name
    $sumText = ($sumFiles | ForEach-Object { "$(Get-Sha256 $_.FullName)  $($_.Name)" }) -join "`n"
    [IO.File]::WriteAllText((Join-Path $package 'SHA256SUMS'), $sumText + "`n", $utf8NoBom)

    $environment = [ordered]@{
        LABELFRAME_SERVER_LISTEN = 'http://0.0.0.0:53961/base'
        LABELFRAME_SERVER_CLIENT_PACKAGES = $clientRoot
        LABELFRAME_SERVER_PDA_PACKAGES = $pdaRoot
        LABELFRAME_SERVER_PLUGIN_PACKAGES = $pluginRoot
        LABELFRAME_SERVER_WEB_UI = $(if ($Headless) { '   ' } else { $webUiRoot })
    }
    Write-Json (Join-Path $dir 'machine-environment.json') $environment
    [IO.File]::WriteAllText((Join-Path $dir 'base-runtime-present.txt'), ($(if ($RuntimePresent) { 'true' } else { 'false' })), $utf8NoBom)
    [IO.File]::WriteAllText((Join-Path $dir 'aspnet-runtime-present.txt'), ($(if ($RuntimePresent) { 'true' } else { 'false' })), $utf8NoBom)

    return [pscustomobject]@{
        Root = $dir; Package = $package; Machine = $machine; ProgramData = $programData
        ClientRoot = $clientRoot; PdaRoot = $pdaRoot; PluginRoot = $pluginRoot; WebUiRoot = $webUiRoot
        Version = $version; Names = $names
    }
}

function Invoke-Fixture([object]$Fixture, [string]$FailStage = '') {
    $stdout = Join-Path $Fixture.Root 'stdout.txt'
    $stderr = Join-Path $Fixture.Root 'stderr.txt'
    $args = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $installer, '-PackageRoot', $Fixture.Package, '-TestMode', '-TestRoot', $Fixture.Root)
    if ($FailStage) { $args += @('-TestFailStage', $FailStage) }
    $process = Start-Process -FilePath $PowerShellPath -ArgumentList $args -Wait -PassThru -NoNewWindow `
        -RedirectStandardOutput $stdout -RedirectStandardError $stderr
    $output = @()
    if (Test-Path -LiteralPath $stdout) { $output += Get-Content -LiteralPath $stdout -Encoding UTF8 }
    if (Test-Path -LiteralPath $stderr) { $output += Get-Content -LiteralPath $stderr -Encoding UTF8 }
    return [pscustomobject]@{ ExitCode = $process.ExitCode; Output = $output -join "`n" }
}

if (-not $PowerShellPath) {
    $hostName = if ($PSVersionTable.PSEdition -eq 'Core') { 'pwsh.exe' } else { 'powershell.exe' }
    $hostCandidate = Join-Path $PSHOME $hostName
    $PowerShellPath = if (Test-Path -LiteralPath $hostCandidate) { $hostCandidate } else { (Get-Process -Id $PID).Path }
}

if (Test-Path -LiteralPath $fixtureRoot) { Remove-Item -LiteralPath $fixtureRoot -Recurse -Force }
New-Item -ItemType Directory -Path $fixtureRoot -Force | Out-Null

try {
    Assert-True '前置 · Installer 脚本存在' (Test-Path -LiteralPath $installer) $installer
    if (-not (Test-Path -LiteralPath $installer)) { throw 'Installer 尚未实现，以下测试无法继续。' }

    . $installer -LibraryMode
    $uri1 = Resolve-HealthBaseUri 'http://0.0.0.0:53961/base'
    $uri2 = Resolve-HealthBaseUri 'https://[::]:5443/root/'
    $uri3 = Resolve-HealthBaseUri 'http://+:53961/'
    Assert-True 'L1 · IPv4 wildcard 归一为 loopback 并保留路径' ($uri1.AbsoluteUri -eq 'http://127.0.0.1:53961/base/') $uri1.AbsoluteUri
    $ipv6Loopback = $null
    $isIpv6Loopback = [Net.IPAddress]::TryParse($uri2.Host.Trim('[', ']'), [ref]$ipv6Loopback) -and [Net.IPAddress]::IPv6Loopback.Equals($ipv6Loopback)
    Assert-True 'L2 · IPv6 wildcard 归一为 loopback 并保留端口' ($isIpv6Loopback -and $uri2.Port -eq 5443 -and $uri2.AbsolutePath -eq '/root/') $uri2.AbsoluteUri
    Assert-True 'L3 · 加号 wildcard 归一为 IPv4 loopback' ($uri3.AbsoluteUri -eq 'http://127.0.0.1:53961/') $uri3.AbsoluteUri
    $apiArray = [object[]]@([pscustomobject]@{ fileName = 'fixture.msi' })
    $flatItems = @(ConvertTo-ApiItemArray $apiArray)
    Assert-True 'L4 · API JSON 数组响应展开为元素集合' ($flatItems.Count -eq 1 -and $flatItems[0].fileName -eq 'fixture.msi') "count=$($flatItems.Count)"
    $quotedRoot = ConvertTo-WindowsCommandLineArgument 'C:\offline package\'
    $argumentProbe = Join-Path $fixtureRoot 'argument-probe.ps1'
    [IO.File]::WriteAllText($argumentProbe, 'param([string]$PackageRoot,[switch]$Elevated) [Console]::WriteLine(($PackageRoot + "|" + $Elevated))', $utf8NoBom)
    $probeOut = Join-Path $fixtureRoot 'argument-probe.out'
    $probe = Start-Process -FilePath $PowerShellPath -ArgumentList @('-NoProfile','-File',(ConvertTo-WindowsCommandLineArgument $argumentProbe),'-PackageRoot',$quotedRoot,'-Elevated') -Wait -PassThru -NoNewWindow -RedirectStandardOutput $probeOut
    $probeValue = (Get-Content $probeOut -Raw).Trim()
    Assert-True 'L5 · UAC 子进程参数保留含空格且尾反斜杠的包路径' ($probe.ExitCode -eq 0 -and $probeValue -eq 'C:\offline package\|True') $probeValue

    $tamper = New-Fixture 'tamper' -Kind upgrade -RuntimePresent $true
    Add-Content -LiteralPath (Join-Path $tamper.Package $tamper.Names.Client) -Value 'tampered'
    $tamperResult = Invoke-Fixture $tamper
    Assert-True 'I1 · 坏哈希非零退出' ($tamperResult.ExitCode -ne 0) "exit=$($tamperResult.ExitCode)"
    Assert-True 'I2 · 完整性失败发生在 MSI/资源写入前' (-not (Test-Path (Join-Path $tamper.Root 'msi-invoked.txt')) -and -not (Test-Path (Join-Path $tamper.ClientRoot $tamper.Names.Client))) '无 MSI 标记且目标目录无文件'
    Assert-True 'I3 · 完整性失败前不创建持久日志目录' (-not (Test-Path (Join-Path $tamper.ProgramData 'LabelFrame\installer\logs'))) 'ProgramData 日志目录不存在'

    $upgradeMissing = New-Fixture 'upgrade-runtime-missing' -Kind upgrade -RuntimePresent $false
    $upgradeMissingResult = Invoke-Fixture $upgradeMissing
    Assert-True 'R1 · Upgrade 缺 Server 任一运行时前置时拒绝' ($upgradeMissingResult.ExitCode -ne 0 -and $upgradeMissingResult.Output -match 'Full') "exit=$($upgradeMissingResult.ExitCode)"
    Assert-True 'R2 · Upgrade 拒绝时不调用 MSI' (-not (Test-Path (Join-Path $upgradeMissing.Root 'msi-invoked.txt'))) '无 MSI 标记'

    $fullMissing = New-Fixture 'full-runtime-missing' -Kind full -RuntimePresent $false
    $fullMissingResult = Invoke-Fixture $fullMissing
    Assert-True 'R3 · Full 缺运行时时补齐 Server 完整前置并继续' ($fullMissingResult.ExitCode -eq 0) "exit=$($fullMissingResult.ExitCode)"
    $runtimeCalls = @(if (Test-Path (Join-Path $fullMissing.Root 'runtime-invoked.txt')) { Get-Content (Join-Path $fullMissing.Root 'runtime-invoked.txt') })
    Assert-True 'R4 · Full 依次安装 Desktop 与 ASP.NET Core，不安装 WebView2' ($runtimeCalls.Count -eq 2 -and $runtimeCalls[0] -like 'windowsdesktop-runtime-*' -and $runtimeCalls[1] -like 'aspnetcore-runtime-*') ($runtimeCalls -join ', ')

    $happy = New-Fixture 'happy' -Kind upgrade -RuntimePresent $true
    [IO.File]::WriteAllText((Join-Path $happy.WebUiRoot 'index.html'), '<html>old-ui</html>', $utf8NoBom)
    [IO.File]::WriteAllText((Join-Path $happy.WebUiRoot 'old-hash.js'), 'old', $utf8NoBom)
    $oldClient = Join-Path $happy.ClientRoot 'LabelFrame-Client-8.0.0.msi'
    $userChanged = Join-Path $happy.PluginRoot 'labelframe-transport-zebra-8.0.0.lfplugin'
    $thirdParty = Join-Path $happy.PluginRoot 'third-party.lfplugin'
    [IO.File]::WriteAllText($oldClient, 'old-client', $utf8NoBom)
    [IO.File]::WriteAllText($userChanged, 'user-replaced', $utf8NoBom)
    [IO.File]::WriteAllText($thirdParty, 'third-party', $utf8NoBom)
    $stateDir = Join-Path $happy.ProgramData 'LabelFrame\installer'
    New-Item -ItemType Directory -Path $stateDir -Force | Out-Null
    Write-Json (Join-Path $stateDir 'managed-files.json') ([ordered]@{
        schemaVersion = 1
        labelframeVersion = '8.0.0'
        roots = [ordered]@{ client = $happy.ClientRoot; pda = $happy.PdaRoot; plugin = $happy.PluginRoot }
        files = @(
            [ordered]@{ kind = 'client'; id = 'client-msi'; version = '8.0.0'; path = $oldClient; sha256 = Get-Sha256 $oldClient },
            [ordered]@{ kind = 'plugin'; id = 'plugin-zebra'; version = '8.0.0'; path = $userChanged; sha256 = ('0' * 64) }
        )
    })

    $happyResult = Invoke-Fixture $happy
    Assert-True 'H1 · 自定义 Machine 路径全链成功' ($happyResult.ExitCode -eq 0) "exit=$($happyResult.ExitCode)"
    Assert-True 'H2 · 四资源按类型分发' ((Test-Path (Join-Path $happy.ClientRoot $happy.Names.Client)) -and (Test-Path (Join-Path $happy.PdaRoot $happy.Names.Pda)) -and (Test-Path (Join-Path $happy.PluginRoot $happy.Names.Zebra)) -and (Test-Path (Join-Path $happy.PluginRoot $happy.Names.Tspl))) 'Client/PDA/Zebra/TSPL 在位'
    Assert-True 'H3 · Web UI 整体替换且无旧散列残留' ((Get-Content (Join-Path $happy.WebUiRoot 'index.html') -Raw) -match 'new-ui' -and -not (Test-Path (Join-Path $happy.WebUiRoot 'old-hash.js'))) '新 index 在位、old-hash.js 不存在'
    Assert-True 'H4 · 安全差集删除未改旧官方文件' (-not (Test-Path $oldClient)) $oldClient
    $happyLogDir = Join-Path $stateDir 'logs'
    $happyLogs = if (Test-Path $happyLogDir) { (Get-ChildItem $happyLogDir -Filter 'offline-install-*.log' | ForEach-Object { [IO.File]::ReadAllText($_.FullName, [Text.Encoding]::UTF8) }) -join "`n" } else { '' }
    Assert-True 'H5 · 用户替换旧文件与第三方文件保留并告警' ((Test-Path $userChanged) -and (Test-Path $thirdParty) -and $happyLogs -match '保留') '用户替换及第三方均在位，告警已写入非零安装日志'
    Assert-True 'H6 · 成功后写入 ProgramData 状态账本' (Test-Path (Join-Path $stateDir 'managed-files.json')) (Join-Path $stateDir 'managed-files.json')
    Assert-True 'H7 · 完整性通过后正式日志落入 ProgramData' (@(Get-ChildItem -LiteralPath (Join-Path $stateDir 'logs') -Filter 'offline-install-*.log' -ErrorAction SilentlyContinue).Count -ge 1) (Join-Path $stateDir 'logs')
    $rerun = Invoke-Fixture $happy
    Assert-True 'H8 · 同版本重跑幂等' ($rerun.ExitCode -eq 0) "exit=$($rerun.ExitCode)"

    $serviceRepair = New-Fixture 'service-repair' -Kind upgrade -RuntimePresent $true
    $serviceRepairResult = Invoke-Fixture $serviceRepair 'MsiServiceMissing'
    Assert-True 'S1 · MajorUpgrade 后服务缺失时同版 repair 自愈' ($serviceRepairResult.ExitCode -eq 0) "exit=$($serviceRepairResult.ExitCode)"
    Assert-True 'S2 · 服务缺失只执行一次 repair' ((Test-Path (Join-Path $serviceRepair.Root 'msi-repair-invoked.txt')) -and @((Get-Content (Join-Path $serviceRepair.Root 'msi-repair-invoked.txt'))).Count -eq 1) 'repair 标记恰好一条'

    $headless = New-Fixture 'headless' -Kind upgrade -RuntimePresent $true -Headless $true
    [IO.File]::WriteAllText((Join-Path $headless.WebUiRoot 'index.html'), '<html>keep-headless</html>', $utf8NoBom)
    $headlessResult = Invoke-Fixture $headless
    Assert-True 'M1 · WEB_UI Machine 空白按无头成功' ($headlessResult.ExitCode -eq 0) "exit=$($headlessResult.ExitCode)"
    Assert-True 'M2 · 无头不覆盖既有 Web UI 目录' ((Get-Content (Join-Path $headless.WebUiRoot 'index.html') -Raw) -match 'keep-headless') '既有内容保持'

    $rollback = New-Fixture 'rollback' -Kind upgrade -RuntimePresent $true
    [IO.File]::WriteAllText((Join-Path $rollback.WebUiRoot 'index.html'), '<html>rollback-old</html>', $utf8NoBom)
    [IO.File]::WriteAllText((Join-Path $rollback.WebUiRoot 'only-old.js'), 'old', $utf8NoBom)
    $rollbackResult = Invoke-Fixture $rollback 'Health'
    Assert-True 'W1 · Web UI 后续健康失败非零退出' ($rollbackResult.ExitCode -ne 0) "exit=$($rollbackResult.ExitCode)"
    Assert-True 'W2 · 健康失败恢复旧 Web UI' ((Get-Content (Join-Path $rollback.WebUiRoot 'index.html') -Raw) -match 'rollback-old' -and (Test-Path (Join-Path $rollback.WebUiRoot 'only-old.js'))) '旧 index 与旧文件均恢复'
    Assert-True 'W3 · 回滚后服务恢复 Running' ((Get-Content (Join-Path $rollback.Root 'service-state.txt') -Raw).Trim() -eq 'Running') 'service-state=Running'

    $summary = Join-Path $fixtureRoot 'summary.txt'
    $script:results | Set-Content -LiteralPath $summary -Encoding UTF8
    Write-Host "`n专项测试完成：失败 $script:failed 项；摘要：$summary"
    if ($script:failed -gt 0) { exit 1 }
    exit 0
}
finally {
    if (-not $KeepFixture -and $script:failed -eq 0) {
        Remove-Item -LiteralPath $fixtureRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}
