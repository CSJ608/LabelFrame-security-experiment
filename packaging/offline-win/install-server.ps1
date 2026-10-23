param(
    [string]$PackageRoot = $PSScriptRoot,
    [switch]$LibraryMode,
    [switch]$TestMode,
    [string]$TestRoot = '',
    [string]$TestFailStage = '',
    [switch]$Elevated
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$script:ServiceName = 'LabelFrameServer'
$script:InstallerLogPath = $null
$script:MsiLogPath = $null
$script:WebUiTarget = $null
$script:WebUiBackup = $null
$script:WebUiSwapped = $false
$script:TestMode = [bool]$TestMode
$script:TestRoot = $TestRoot
$script:TestFailStage = $TestFailStage
$script:Utf8NoBom = New-Object System.Text.UTF8Encoding($false)

function Write-InstallLog([string]$Message, [string]$Level = 'INFO') {
    $line = '[{0:yyyy-MM-dd HH:mm:ss}] [{1}] {2}' -f (Get-Date), $Level, $Message
    Write-Host $line
    if ($script:InstallerLogPath) {
        try { [IO.File]::AppendAllText($script:InstallerLogPath, $line + [Environment]::NewLine, $script:Utf8NoBom) }
        catch { Write-Warning "安装日志写入失败：$($_.Exception.Message)" }
    }
}

function Get-Sha256([string]$Path) {
    (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Get-PropertyValue([object]$Object, [string]$Name) {
    if ($null -eq $Object) { return $null }
    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property) { return $null }
    return $property.Value
}

function Test-SafeLeafName([string]$Name) {
    if ([string]::IsNullOrWhiteSpace($Name)) { return $false }
    if ([IO.Path]::IsPathRooted($Name)) { return $false }
    if ($Name -ne [IO.Path]::GetFileName($Name)) { return $false }
    if ($Name.IndexOfAny([IO.Path]::GetInvalidFileNameChars()) -ge 0) { return $false }
    return $true
}

function Get-NormalizedPath([string]$Path) {
    if ([string]::IsNullOrWhiteSpace($Path)) { throw '路径为空。' }
    [IO.Path]::GetFullPath($Path)
}

function Test-PathWithinRoot([string]$Path, [string]$Root) {
    $fullPath = Get-NormalizedPath $Path
    $fullRoot = (Get-NormalizedPath $Root).TrimEnd([char[]]@([char]'\', [char]'/'))
    if ($fullPath.Equals($fullRoot, [StringComparison]::OrdinalIgnoreCase)) { return $false }
    $prefix = $fullRoot + [IO.Path]::DirectorySeparatorChar
    return $fullPath.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)
}

function Read-JsonFile([string]$Path, [string]$Label) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { throw "缺少$Label：$Path" }
    try { return ([IO.File]::ReadAllText($Path) | ConvertFrom-Json) }
    catch { throw "$Label不是合法 JSON：$($_.Exception.Message)" }
}

function Read-Sha256Ledger([string]$Root) {
    $ledgerPath = Join-Path $Root 'SHA256SUMS'
    if (-not (Test-Path -LiteralPath $ledgerPath -PathType Leaf)) { throw '缺少 SHA256SUMS，包不完整。' }
    $lines = [IO.File]::ReadAllLines($ledgerPath)
    if ($lines.Count -eq 0) { throw 'SHA256SUMS 为空，包不完整。' }

    $entries = @{}
    foreach ($line in $lines) {
        if ([string]::IsNullOrWhiteSpace($line)) { continue }
        if ($line -notmatch '^([0-9a-fA-F]{64})  (.+)$') { throw "SHA256SUMS 行格式非法：$line" }
        $name = $Matches[2]
        if (-not (Test-SafeLeafName $name)) { throw "SHA256SUMS 含不安全文件名：$name" }
        if ($entries.ContainsKey($name)) { throw "SHA256SUMS 含重复文件名：$name" }
        $entries[$name] = $Matches[1].ToLowerInvariant()
    }
    if ($entries.Count -eq 0) { throw 'SHA256SUMS 没有有效条目。' }

    foreach ($name in $entries.Keys) {
        $path = Join-Path $Root $name
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "SHA256SUMS 登记文件缺失：$name" }
        $actual = Get-Sha256 $path
        if ($actual -ne $entries[$name]) { throw "SHA256SUMS 校验失败：$name" }
    }
    return $entries
}

function Assert-LedgerEntry([hashtable]$Ledger, [string]$Name) {
    if (-not $Ledger.ContainsKey($Name)) { throw "SHA256SUMS 未登记关键文件：$Name" }
}

function Get-SinglePackageFile([string]$Root, [string]$Pattern, [string]$Label) {
    $items = @(Get-ChildItem -LiteralPath $Root -File | Where-Object { $_.Name -like $Pattern })
    if ($items.Count -ne 1) { throw "$Label 应恰好存在 1 件，实际 $($items.Count) 件（模式 $Pattern）。" }
    return $items[0]
}

function Read-PackageContract([string]$Root) {
    $normalizedRoot = Get-NormalizedPath $Root
    if (-not (Test-Path -LiteralPath $normalizedRoot -PathType Container)) { throw "离线包目录不存在：$normalizedRoot" }

    Write-InstallLog '校验 SHA256SUMS 与全部登记文件。'
    $ledger = Read-Sha256Ledger $normalizedRoot
    foreach ($critical in @('offline-build-info.json', 'managed-files.json')) { Assert-LedgerEntry $ledger $critical }

    $buildInfo = Read-JsonFile (Join-Path $normalizedRoot 'offline-build-info.json') 'offline-build-info.json'
    $managed = Read-JsonFile (Join-Path $normalizedRoot 'managed-files.json') 'managed-files.json'
    $version = [string](Get-PropertyValue $buildInfo 'labelframeVersion')
    if ([string]::IsNullOrWhiteSpace($version)) { throw 'offline-build-info.json 缺少 labelframeVersion。' }
    if ([string](Get-PropertyValue $managed 'labelframeVersion') -ne $version) {
        throw 'offline-build-info.json 与 managed-files.json 的版本不一致。'
    }
    if ([int](Get-PropertyValue $managed 'schemaVersion') -ne 1) { throw 'managed-files.json schemaVersion 不受支持。' }

    $serverMsi = Get-SinglePackageFile $normalizedRoot ("LabelFrame-Server-{0}.msi" -f $version) 'Server MSI'
    $webUiZip = Get-SinglePackageFile $normalizedRoot ("labelframe-server-webui-{0}.zip" -f $version) 'Web UI zip'
    Assert-LedgerEntry $ledger $serverMsi.Name
    Assert-LedgerEntry $ledger $webUiZip.Name

    $runtimeDesktop = @(Get-ChildItem -LiteralPath $normalizedRoot -File | Where-Object { $_.Name -like 'windowsdesktop-runtime-*-win-x64.exe' })
    $runtimeAspNet = @(Get-ChildItem -LiteralPath $normalizedRoot -File | Where-Object { $_.Name -like 'aspnetcore-runtime-*-win-x64.exe' })
    $runtimeWebView = @(Get-ChildItem -LiteralPath $normalizedRoot -File | Where-Object { $_.Name -like 'MicrosoftEdgeWebView2RuntimeInstaller*X64.exe' })
    $runtimeAll = @($runtimeDesktop) + @($runtimeAspNet) + @($runtimeWebView)

    $kind = if ($runtimeAll.Count -eq 0) { 'upgrade' } else { 'full' }
    if ($kind -eq 'full') {
        if ($runtimeDesktop.Count -ne 1 -or $runtimeAspNet.Count -ne 1 -or $runtimeWebView.Count -ne 1 -or $runtimeAll.Count -ne 3) {
            throw 'Full 包运行时集合非法：必须且只能包含 Desktop、ASP.NET Core、WebView2 x64 Standalone 各一件。'
        }
        foreach ($runtime in $runtimeAll) { Assert-LedgerEntry $ledger $runtime.Name }
    }
    else {
        $unexpectedRuntime = @(Get-ChildItem -LiteralPath $normalizedRoot -File | Where-Object {
            $_.Name -match '(?i)(runtime.*\.exe$|windowsdesktop|aspnetcore|webview2runtime)'
        })
        if ($unexpectedRuntime.Count -gt 0) { throw 'Upgrade 包不得携带任何运行时安装器。' }
    }

    $declaredKind = [string](Get-PropertyValue $buildInfo 'packageKind')
    if (-not [string]::IsNullOrWhiteSpace($declaredKind) -and $declaredKind.ToLowerInvariant() -ne $kind) {
        throw "offline-build-info.json 声明的 packageKind 与实际运行时集合不一致：$declaredKind / $kind。"
    }

    $expectedIds = @{
        'client-msi' = 'client'
        'pda-apk' = 'pda'
        'plugin-zebra' = 'plugin'
        'plugin-tspl' = 'plugin'
    }
    $managedItems = @((Get-PropertyValue $managed 'files'))
    if ($managedItems.Count -ne 4) { throw "managed-files.json 必须恰好登记四项官方资源，实际 $($managedItems.Count) 项。" }
    $ids = @{}
    $names = @{}
    $validatedItems = @()
    foreach ($item in $managedItems) {
        $id = [string](Get-PropertyValue $item 'id')
        $itemKind = [string](Get-PropertyValue $item 'kind')
        $fileName = [string](Get-PropertyValue $item 'fileName')
        $itemVersion = [string](Get-PropertyValue $item 'version')
        $sha256 = ([string](Get-PropertyValue $item 'sha256')).ToLowerInvariant()
        if (-not $expectedIds.ContainsKey($id) -or $expectedIds[$id] -ne $itemKind) { throw "managed-files.json 含不允许的 id/kind：$id/$itemKind" }
        if ($ids.ContainsKey($id)) { throw "managed-files.json 含重复 id：$id" }
        if (-not (Test-SafeLeafName $fileName)) { throw "managed-files.json 含不安全文件名：$fileName" }
        if ($names.ContainsKey($fileName)) { throw "managed-files.json 含重复文件名：$fileName" }
        if ($itemVersion -ne $version) { throw "managed-files.json 资源版本不一致：$fileName" }
        if ($sha256 -notmatch '^[0-9a-f]{64}$') { throw "managed-files.json SHA256 非法：$fileName" }
        $sourcePath = Join-Path $normalizedRoot $fileName
        if (-not (Test-Path -LiteralPath $sourcePath -PathType Leaf)) { throw "managed-files.json 登记资源缺失：$fileName" }
        Assert-LedgerEntry $ledger $fileName
        if ($ledger[$fileName] -ne $sha256 -or (Get-Sha256 $sourcePath) -ne $sha256) {
            throw "managed-files.json、SHA256SUMS 与实测哈希不一致：$fileName"
        }
        $ids[$id] = $true
        $names[$fileName] = $true
        $validatedItems += [pscustomobject]@{
            kind = $itemKind; id = $id; fileName = $fileName; version = $version
            sha256 = $sha256; sourcePath = $sourcePath
        }
    }
    foreach ($id in $expectedIds.Keys) { if (-not $ids.ContainsKey($id)) { throw "managed-files.json 缺少官方资源：$id" } }

    return [pscustomobject]@{
        Root = $normalizedRoot; Version = $version; Kind = $kind; Ledger = $ledger
        ServerMsi = $serverMsi.FullName; WebUiZip = $webUiZip.FullName
        DesktopRuntime = $(if ($runtimeDesktop.Count -eq 1) { $runtimeDesktop[0].FullName } else { $null })
        AspNetRuntime = $(if ($runtimeAspNet.Count -eq 1) { $runtimeAspNet[0].FullName } else { $null })
        ManagedItems = $validatedItems
    }
}

function Get-MachineSetting([string]$Name) {
    if ($script:TestMode) {
        $path = Join-Path $script:TestRoot 'machine-environment.json'
        $environment = Read-JsonFile $path '测试 Machine 环境变量文件'
        $property = $environment.PSObject.Properties[$Name]
        if ($null -eq $property) { return [pscustomobject]@{ Defined = $false; Value = $null } }
        return [pscustomobject]@{ Defined = $true; Value = [string]$property.Value }
    }
    $value = [Environment]::GetEnvironmentVariable($Name, [EnvironmentVariableTarget]::Machine)
    return [pscustomobject]@{ Defined = ($null -ne $value); Value = $value }
}

function Resolve-InstallPaths {
    $programData = if ($script:TestMode) { Join-Path $script:TestRoot 'machine\ProgramData' } else { [Environment]::GetFolderPath([Environment+SpecialFolder]::CommonApplicationData) }
    $defaultData = Join-Path $programData 'LabelFrame\server'
    $listen = Get-MachineSetting 'LABELFRAME_SERVER_LISTEN'
    $client = Get-MachineSetting 'LABELFRAME_SERVER_CLIENT_PACKAGES'
    $pda = Get-MachineSetting 'LABELFRAME_SERVER_PDA_PACKAGES'
    $plugin = Get-MachineSetting 'LABELFRAME_SERVER_PLUGIN_PACKAGES'
    $webUi = Get-MachineSetting 'LABELFRAME_SERVER_WEB_UI'

    $listenValue = if (-not $listen.Defined -or [string]::IsNullOrWhiteSpace($listen.Value)) { 'http://127.0.0.1:53961' } else { $listen.Value.Trim() }
    $clientPath = if (-not $client.Defined -or [string]::IsNullOrWhiteSpace($client.Value)) { Join-Path $defaultData 'client-packages' } else { $client.Value.Trim() }
    $pdaPath = if (-not $pda.Defined -or [string]::IsNullOrWhiteSpace($pda.Value)) { Join-Path $defaultData 'pda-packages' } else { $pda.Value.Trim() }
    $pluginPath = if (-not $plugin.Defined -or [string]::IsNullOrWhiteSpace($plugin.Value)) { Join-Path $defaultData 'plugin-packages' } else { $plugin.Value.Trim() }
    $headless = $webUi.Defined -and [string]::IsNullOrWhiteSpace($webUi.Value)
    $webUiPath = if ($headless) { $null } elseif ($webUi.Defined) { $webUi.Value.Trim() } else { Join-Path $defaultData 'plugins\web-ui' }

    foreach ($path in @($clientPath, $pdaPath, $pluginPath)) {
        if (-not [IO.Path]::IsPathRooted($path)) { throw "Machine 分发路径必须是绝对路径：$path" }
    }
    if ($webUiPath -and -not [IO.Path]::IsPathRooted($webUiPath)) { throw "Machine Web UI 路径必须是绝对路径：$webUiPath" }

    return [pscustomobject]@{
        ProgramData = Get-NormalizedPath $programData
        Listen = $listenValue
        Client = Get-NormalizedPath $clientPath
        Pda = Get-NormalizedPath $pdaPath
        Plugin = Get-NormalizedPath $pluginPath
        WebUi = $(if ($webUiPath) { Get-NormalizedPath $webUiPath } else { $null })
        Headless = $headless
    }
}

function Resolve-HealthBaseUri([string]$ListenUrl) {
    $candidate = $ListenUrl.Trim()
    if ($candidate -match '^(https?)://([+*])(:(\d+))?(/.*)?$') {
        $scheme = $Matches[1]
        $port = $Matches[4]
        $path = $Matches[5]
        if (-not $path) { $path = '/' }
        $candidate = "$scheme`://127.0.0.1$(if ($port) { ":$port" })$path"
    }
    $uri = $null
    if (-not [Uri]::TryCreate($candidate, [UriKind]::Absolute, [ref]$uri)) { throw "LISTEN 不是绝对 URL：$ListenUrl" }
    if ($uri.Scheme -ne 'http' -and $uri.Scheme -ne 'https') { throw "LISTEN 只允许 http/https：$ListenUrl" }
    $builder = New-Object UriBuilder($uri)
    $listenHost = $uri.Host
    $parsedAddress = $null
    $isIpAddress = [Net.IPAddress]::TryParse($listenHost.Trim('[', ']'), [ref]$parsedAddress)
    $isIpv6Any = $isIpAddress -and [Net.IPAddress]::IPv6Any.Equals($parsedAddress)
    if ($listenHost -eq '0.0.0.0') { $builder.Host = '127.0.0.1' }
    elseif ($isIpv6Any) { $builder.Host = '::1' }
    if (-not $builder.Path.EndsWith('/')) { $builder.Path += '/' }
    $result = $builder.Uri
    if ($isIpv6Any) {
        $suffix = $(if ($result.IsDefaultPort) { '' } else { ":$($result.Port)" }) + $result.PathAndQuery
        return [Uri]("$($result.Scheme)://[::1]$suffix")
    }
    return $result
}

function New-CandidateState([object]$Contract, [object]$Paths) {
    $roots = [ordered]@{ client = $Paths.Client; pda = $Paths.Pda; plugin = $Paths.Plugin }
    $files = @()
    $seen = @{}
    foreach ($item in $Contract.ManagedItems) {
        $root = [string]$roots[$item.kind]
        $target = Get-NormalizedPath (Join-Path $root $item.fileName)
        if (-not (Test-PathWithinRoot $target $root)) { throw "官方资源目标越出 $($item.kind) 根目录：$target" }
        if ($seen.ContainsKey($target)) { throw "官方资源目标路径重复：$target" }
        $seen[$target] = $true
        $files += [pscustomobject]@{
            kind = $item.kind; id = $item.id; version = $item.version; path = $target
            sha256 = $item.sha256; sourcePath = $item.sourcePath; fileName = $item.fileName
        }
    }
    return [pscustomobject]@{ schemaVersion = 1; labelframeVersion = $Contract.Version; roots = $roots; files = $files }
}

function Read-PreviousState([string]$StatePath) {
    if (-not (Test-Path -LiteralPath $StatePath -PathType Leaf)) { return $null }
    $state = Read-JsonFile $StatePath '目标机 managed-files 状态账本'
    if ([int](Get-PropertyValue $state 'schemaVersion') -ne 1) { throw '目标机 managed-files 状态账本 schemaVersion 不受支持。' }
    $rootsObject = Get-PropertyValue $state 'roots'
    $roots = @{}
    foreach ($kind in @('client', 'pda', 'plugin')) {
        $root = [string](Get-PropertyValue $rootsObject $kind)
        if ([string]::IsNullOrWhiteSpace($root) -or -not [IO.Path]::IsPathRooted($root)) { throw "目标机状态账本缺少绝对根路径：$kind" }
        $roots[$kind] = Get-NormalizedPath $root
    }
    $files = @()
    $seen = @{}
    foreach ($entry in @((Get-PropertyValue $state 'files'))) {
        $kind = [string](Get-PropertyValue $entry 'kind')
        $path = [string](Get-PropertyValue $entry 'path')
        $hash = ([string](Get-PropertyValue $entry 'sha256')).ToLowerInvariant()
        if (-not $roots.ContainsKey($kind)) { throw "目标机状态账本含非法种类：$kind" }
        if ([string]::IsNullOrWhiteSpace($path) -or -not [IO.Path]::IsPathRooted($path)) { throw "目标机状态账本含非绝对路径：$path" }
        $normalized = Get-NormalizedPath $path
        if (-not (Test-PathWithinRoot $normalized $roots[$kind])) { throw "目标机状态账本路径越出已记录根目录：$normalized" }
        if ($hash -notmatch '^[0-9a-f]{64}$') { throw "目标机状态账本哈希非法：$normalized" }
        if ($seen.ContainsKey($normalized)) { throw "目标机状态账本含重复路径：$normalized" }
        $seen[$normalized] = $true
        $files += [pscustomobject]@{
            kind = $kind; id = [string](Get-PropertyValue $entry 'id'); version = [string](Get-PropertyValue $entry 'version')
            path = $normalized; sha256 = $hash
        }
    }
    return [pscustomobject]@{ roots = $roots; files = $files }
}

function Test-RuntimeDirectory([string]$FrameworkName, [string]$TestFlagName) {
    if ($script:TestMode) {
        $flag = Join-Path $script:TestRoot $TestFlagName
        if (-not (Test-Path -LiteralPath $flag)) { return $false }
        return ([IO.File]::ReadAllText($flag).Trim().ToLowerInvariant() -eq 'true')
    }
    $dotnetRoot = Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::ProgramFiles)) 'dotnet'
    if (-not (Test-Path -LiteralPath (Join-Path $dotnetRoot 'dotnet.exe') -PathType Leaf)) { return $false }
    if (-not (Test-Path -LiteralPath (Join-Path $dotnetRoot 'host\fxr') -PathType Container)) { return $false }
    $frameworkDir = Join-Path $dotnetRoot "shared\$FrameworkName"
    if (-not (Test-Path -LiteralPath $frameworkDir -PathType Container)) { return $false }
    foreach ($directory in @(Get-ChildItem -LiteralPath $frameworkDir -Directory -ErrorAction SilentlyContinue)) {
        $version = $null
        if ([Version]::TryParse($directory.Name.TrimStart('v'), [ref]$version) -and $version.Major -ge 10) { return $true }
    }
    return $false
}

function Test-BaseRuntimePresent { Test-RuntimeDirectory 'Microsoft.NETCore.App' 'base-runtime-present.txt' }
function Test-AspNetCoreRuntimePresent { Test-RuntimeDirectory 'Microsoft.AspNetCore.App' 'aspnet-runtime-present.txt' }

function Install-Runtime([object]$Contract, [string]$Name, [string]$Path, [string]$TestFlagName) {
    if ($Contract.Kind -eq 'upgrade') { throw "目标机缺少 $Name；Upgrade 包不含运行时，请改用同版本 Full 包。" }
    if (-not $Path) { throw "Full 包缺少 $Name 安装器。" }
    Write-InstallLog "缺少 $Name，静默补装：$(Split-Path -Leaf $Path)"
    if ($script:TestMode) {
        Add-Content -LiteralPath (Join-Path $script:TestRoot 'runtime-invoked.txt') -Value (Split-Path -Leaf $Path) -Encoding UTF8
        [IO.File]::WriteAllText((Join-Path $script:TestRoot $TestFlagName), 'true', $script:Utf8NoBom)
        return
    }
    $process = Start-Process -FilePath $Path -ArgumentList @('/install', '/quiet', '/norestart') -Wait -PassThru
    if (@(0, 1641, 3010) -notcontains $process.ExitCode) { throw "$Name 静默安装失败，退出码 $($process.ExitCode)。" }
}

function Ensure-ServerRuntimes([object]$Contract) {
    if (-not (Test-BaseRuntimePresent)) {
        Install-Runtime $Contract '.NET 10 Desktop Runtime（基础 host/runtime）' $Contract.DesktopRuntime 'base-runtime-present.txt'
        if (-not (Test-BaseRuntimePresent)) { throw 'Desktop Runtime 安装器返回成功，但仍未探测到 dotnet host / Microsoft.NETCore.App 10+。' }
    }
    else { Write-InstallLog '.NET 10 基础 host/runtime 已在场，不重复安装 Desktop Runtime。' }

    if (-not (Test-AspNetCoreRuntimePresent)) {
        Install-Runtime $Contract 'ASP.NET Core 10 Runtime' $Contract.AspNetRuntime 'aspnet-runtime-present.txt'
        if (-not (Test-AspNetCoreRuntimePresent)) { throw 'ASP.NET Core Runtime 安装器返回成功，但仍未探测到 Microsoft.AspNetCore.App 10+。' }
    }
    else { Write-InstallLog 'ASP.NET Core 10+ Runtime 已在场，不重复安装。' }
}

function Invoke-ServerMsi([object]$Contract, [string]$LogDirectory) {
    $script:MsiLogPath = Join-Path $LogDirectory ("server-msi-{0:yyyyMMdd-HHmmss}.log" -f (Get-Date))
    Write-InstallLog "Server MSI 详细日志：$script:MsiLogPath"
    if ($script:TestMode) {
        [IO.File]::WriteAllText((Join-Path $script:TestRoot 'msi-invoked.txt'), $Contract.ServerMsi, $script:Utf8NoBom)
        [IO.File]::WriteAllText($script:MsiLogPath, 'TestMode: msiexec /qn /norestart /l*v', $script:Utf8NoBom)
        if ($script:TestFailStage -eq 'MsiServiceMissing') {
            Remove-Item -LiteralPath (Join-Path $script:TestRoot 'service-state.txt') -Force -ErrorAction SilentlyContinue
        }
        else { [IO.File]::WriteAllText((Join-Path $script:TestRoot 'service-state.txt'), 'Running', $script:Utf8NoBom) }
        return
    }
    $arguments = @('/i', ('"{0}"' -f $Contract.ServerMsi), '/qn', '/norestart', '/l*v', ('"{0}"' -f $script:MsiLogPath))
    $process = Start-Process -FilePath 'msiexec.exe' -ArgumentList $arguments -Wait -PassThru
    if (@(0, 1641, 3010) -notcontains $process.ExitCode) { throw "Server MSI 静默安装失败，退出码 $($process.ExitCode)；日志：$script:MsiLogPath" }
}

function Test-ServerServiceExists {
    if ($script:TestMode) { return Test-Path -LiteralPath (Join-Path $script:TestRoot 'service-state.txt') }
    return $null -ne (Get-Service -Name $script:ServiceName -ErrorAction SilentlyContinue)
}

function Repair-ServerMsi([object]$Contract, [string]$LogDirectory) {
    $repairLog = Join-Path $LogDirectory ("server-msi-repair-{0:yyyyMMdd-HHmmss}.log" -f (Get-Date))
    Write-InstallLog "MajorUpgrade 后服务缺失，执行一次同版 MSI repair；详细日志：$repairLog" 'WARN'
    if ($script:TestMode) {
        Add-Content -LiteralPath (Join-Path $script:TestRoot 'msi-repair-invoked.txt') -Value $Contract.ServerMsi -Encoding UTF8
        [IO.File]::WriteAllText($repairLog, 'TestMode: msiexec /fa /qn /norestart /l*v', $script:Utf8NoBom)
        [IO.File]::WriteAllText((Join-Path $script:TestRoot 'service-state.txt'), 'Running', $script:Utf8NoBom)
        return
    }
    $arguments = @('/fa', ('"{0}"' -f $Contract.ServerMsi), '/qn', '/norestart', '/l*v', ('"{0}"' -f $repairLog))
    $process = Start-Process -FilePath 'msiexec.exe' -ArgumentList $arguments -Wait -PassThru
    if (@(0, 1641, 3010) -notcontains $process.ExitCode) { throw "Server MSI repair 失败，退出码 $($process.ExitCode)；日志：$repairLog" }
}

function Ensure-ServerServiceInstalled([object]$Contract, [string]$LogDirectory) {
    if (Test-ServerServiceExists) { return }
    Repair-ServerMsi $Contract $LogDirectory
    if (-not (Test-ServerServiceExists)) { throw 'Server MSI 返回成功，但 LabelFrameServer 服务缺失；同版 repair 后仍未恢复。' }
}

function Stop-ServerService {
    if ($script:TestMode) {
        [IO.File]::WriteAllText((Join-Path $script:TestRoot 'service-state.txt'), 'Stopped', $script:Utf8NoBom)
        return
    }
    $service = Get-Service -Name $script:ServiceName -ErrorAction Stop
    if ($service.Status -ne [ServiceProcess.ServiceControllerStatus]::Stopped) {
        Stop-Service -Name $script:ServiceName -Force -ErrorAction Stop
        $service.WaitForStatus([ServiceProcess.ServiceControllerStatus]::Stopped, [TimeSpan]::FromSeconds(30))
    }
}

function Start-ServerService {
    if ($script:TestMode) {
        [IO.File]::WriteAllText((Join-Path $script:TestRoot 'service-state.txt'), 'Running', $script:Utf8NoBom)
        return
    }
    $service = Get-Service -Name $script:ServiceName -ErrorAction Stop
    if ($service.Status -ne [ServiceProcess.ServiceControllerStatus]::Running) {
        Start-Service -Name $script:ServiceName -ErrorAction Stop
        $service.WaitForStatus([ServiceProcess.ServiceControllerStatus]::Running, [TimeSpan]::FromSeconds(30))
    }
}

function Expand-WebUiToStaging([string]$Archive, [string]$Target) {
    $parent = Split-Path -Parent $Target
    $leaf = Split-Path -Leaf $Target
    if ([string]::IsNullOrWhiteSpace($parent) -or [string]::IsNullOrWhiteSpace($leaf)) { throw "Web UI 目标目录非法：$Target" }
    New-Item -ItemType Directory -Path $parent -Force | Out-Null
    $staging = Join-Path $parent ('.{0}.staging-{1}' -f $leaf, [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $staging -Force | Out-Null

    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zip = [IO.Compression.ZipFile]::OpenRead($Archive)
    try {
        foreach ($entry in $zip.Entries) {
            if ([string]::IsNullOrEmpty($entry.FullName)) { continue }
            $destination = Get-NormalizedPath (Join-Path $staging $entry.FullName.Replace('/', [IO.Path]::DirectorySeparatorChar))
            if (-not (Test-PathWithinRoot $destination $staging)) { throw "Web UI zip 含路径穿越条目：$($entry.FullName)" }
        }
    }
    finally { $zip.Dispose() }
    [IO.Compression.ZipFile]::ExtractToDirectory($Archive, $staging)
    if (-not (Test-Path -LiteralPath (Join-Path $staging 'index.html') -PathType Leaf)) {
        Remove-Item -LiteralPath $staging -Recurse -Force -ErrorAction SilentlyContinue
        throw 'Web UI zip 解压后缺少 index.html。'
    }
    return $staging
}

function Switch-WebUi([string]$Staging, [string]$Target) {
    $parent = Split-Path -Parent $Target
    $leaf = Split-Path -Leaf $Target
    $backup = Join-Path $parent ('.{0}.backup-{1}' -f $leaf, [Guid]::NewGuid().ToString('N'))
    Stop-ServerService
    if (Test-Path -LiteralPath $Target) { Move-Item -LiteralPath $Target -Destination $backup }
    try { Move-Item -LiteralPath $Staging -Destination $Target }
    catch {
        if (Test-Path -LiteralPath $backup) { Move-Item -LiteralPath $backup -Destination $Target }
        Start-ServerService
        throw
    }
    $script:WebUiTarget = $Target
    $script:WebUiBackup = $(if (Test-Path -LiteralPath $backup) { $backup } else { $null })
    $script:WebUiSwapped = $true
}

function Restore-WebUi {
    if (-not $script:WebUiSwapped) { return }
    Write-InstallLog '后续阶段失败，恢复旧 Web UI。' 'WARN'
    try { Stop-ServerService } catch { Write-InstallLog "回滚停服务失败：$($_.Exception.Message)" 'WARN' }
    try {
        if ($script:WebUiTarget -and (Test-Path -LiteralPath $script:WebUiTarget)) { Remove-Item -LiteralPath $script:WebUiTarget -Recurse -Force }
        if ($script:WebUiBackup -and (Test-Path -LiteralPath $script:WebUiBackup)) { Move-Item -LiteralPath $script:WebUiBackup -Destination $script:WebUiTarget }
    }
    finally {
        try { Start-ServerService } catch { Write-InstallLog "回滚后启动服务失败：$($_.Exception.Message)" 'ERROR' }
        $script:WebUiSwapped = $false
    }
}

function Copy-ManagedResources([object]$Candidate) {
    foreach ($file in $Candidate.files) {
        $directory = Split-Path -Parent $file.path
        New-Item -ItemType Directory -Path $directory -Force | Out-Null
        Copy-Item -LiteralPath $file.sourcePath -Destination $file.path -Force
        if ((Get-Sha256 $file.path) -ne $file.sha256) { throw "资源复制后哈希不一致：$($file.path)" }
        Write-InstallLog "已分发 [$($file.kind)] $($file.fileName) -> $directory"
    }
}

function Join-HealthUri([Uri]$BaseUri, [string]$RelativePath) {
    New-Object Uri($BaseUri, $RelativePath.TrimStart('/'))
}

function Invoke-HttpJson([Uri]$Uri) {
    Invoke-RestMethod -Uri $Uri.AbsoluteUri -Method Get -TimeoutSec 5 -UseBasicParsing
}

function ConvertTo-ApiItemArray([object]$Value) {
    if ($null -eq $Value) { return }
    if ($Value -is [Array]) {
        foreach ($item in $Value) { Write-Output $item }
        return
    }
    Write-Output $Value
}

function Test-ServiceAndApi([object]$Contract, [object]$Paths, [object]$Candidate) {
    if ($script:TestFailStage -eq 'Health') { throw 'TestMode 注入：Health 阶段失败。' }
    $baseUri = Resolve-HealthBaseUri $Paths.Listen
    Write-InstallLog "健康检查基地址：$($baseUri.AbsoluteUri)"

    if ($script:TestMode) {
        $serviceState = ([IO.File]::ReadAllText((Join-Path $script:TestRoot 'service-state.txt'))).Trim()
        if ($serviceState -ne 'Running') { throw '服务未处于 Running。' }
        foreach ($file in $Candidate.files) {
            if (-not (Test-Path -LiteralPath $file.path) -or (Get-Sha256 $file.path) -ne $file.sha256) { throw "TestMode API 下载哈希验证失败：$($file.fileName)" }
        }
        return
    }

    $healthUri = Join-HealthUri $baseUri 'healthz'
    $infoUri = Join-HealthUri $baseUri 'api/server/info'
    $info = $null
    $lastError = $null
    for ($attempt = 1; $attempt -le 30; $attempt++) {
        try {
            $service = Get-Service -Name $script:ServiceName -ErrorAction Stop
            if ($service.Status -ne [ServiceProcess.ServiceControllerStatus]::Running) { throw "服务状态为 $($service.Status)" }
            $health = Invoke-HttpJson $healthUri
            if ([string](Get-PropertyValue $health 'status') -ne 'ok') { throw '/healthz 未返回 status=ok。' }
            $info = Invoke-HttpJson $infoUri
            if ([string](Get-PropertyValue $info 'version') -ne $Contract.Version) { throw "/api/server/info.version 与包版本不一致：$([string](Get-PropertyValue $info 'version')) / $($Contract.Version)" }
            $actualUi = [bool](Get-PropertyValue $info 'uiEnabled')
            $expectedUi = -not $Paths.Headless
            if ($actualUi -ne $expectedUi) { throw "/api/server/info.uiEnabled 不符合安装模式：$actualUi / $expectedUi" }
            $lastError = $null
            break
        }
        catch {
            $lastError = $_.Exception.Message
            Start-Sleep -Seconds 1
        }
    }
    if ($lastError) { throw "服务未在 30 秒内通过 Running/health/info 验证：$lastError" }

    $endpointByKind = @{ client = 'api/client-packages'; pda = 'api/pda-packages'; plugin = 'api/plugin-packages' }
    foreach ($kind in @('client', 'pda', 'plugin')) {
        $listUri = Join-HealthUri $baseUri $endpointByKind[$kind]
        $items = @(ConvertTo-ApiItemArray (Invoke-HttpJson $listUri))
        foreach ($file in @($Candidate.files | Where-Object { $_.kind -eq $kind })) {
            $matches = @($items | Where-Object { [string](Get-PropertyValue $_ 'fileName') -eq $file.fileName })
            if ($matches.Count -ne 1) { throw "下载中心 API 未唯一返回 fileName=$($file.fileName)" }
            $downloadPath = [string](Get-PropertyValue $matches[0] 'url')
            if ([string]::IsNullOrWhiteSpace($downloadPath)) { throw "下载中心 API 未返回下载 URL：$($file.fileName)" }
            $downloadUri = Join-HealthUri $baseUri $downloadPath
            $temporary = Join-Path ([IO.Path]::GetTempPath()) ('labelframe-download-' + [Guid]::NewGuid().ToString('N'))
            try {
                Invoke-WebRequest -Uri $downloadUri.AbsoluteUri -OutFile $temporary -TimeoutSec 15 -UseBasicParsing
                if ((Get-Sha256 $temporary) -ne $file.sha256) { throw "下载中心 API 下载哈希不一致：$($file.fileName)" }
            }
            finally { Remove-Item -LiteralPath $temporary -Force -ErrorAction SilentlyContinue }
        }
    }
}

function Remove-SafeDifference([object]$PreviousState, [object]$Candidate) {
    if ($null -eq $PreviousState) { return }
    $current = @{}
    foreach ($file in $Candidate.files) { $current[(Get-NormalizedPath $file.path)] = $true }
    foreach ($old in $PreviousState.files) {
        $oldPath = Get-NormalizedPath $old.path
        if ($current.ContainsKey($oldPath)) { continue }
        $currentRoot = [string]$Candidate.roots[$old.kind]
        $oldRoot = [string]$PreviousState.roots[$old.kind]
        if (-not (Test-PathWithinRoot $oldPath $currentRoot) -and -not (Test-PathWithinRoot $oldPath $oldRoot)) {
            throw "拒绝清理越出当前与上次受管根目录的路径：$oldPath"
        }
        if (-not (Test-Path -LiteralPath $oldPath -PathType Leaf)) { continue }
        $actual = Get-Sha256 $oldPath
        if ($actual -ne $old.sha256) {
            Write-InstallLog "保留用户已替换的旧官方文件：$oldPath" 'WARN'
            continue
        }
        Remove-Item -LiteralPath $oldPath -Force
        Write-InstallLog "已清理旧官方文件：$oldPath"
    }
}

function Write-StateLedger([string]$StatePath, [object]$Candidate) {
    $directory = Split-Path -Parent $StatePath
    New-Item -ItemType Directory -Path $directory -Force | Out-Null
    $stateFiles = @()
    foreach ($file in $Candidate.files) {
        $stateFiles += [ordered]@{
            kind = $file.kind; id = $file.id; version = $file.version
            path = (Get-NormalizedPath $file.path); sha256 = $file.sha256
        }
    }
    $document = [ordered]@{
        schemaVersion = 1
        labelframeVersion = $Candidate.labelframeVersion
        roots = [ordered]@{ client = $Candidate.roots.client; pda = $Candidate.roots.pda; plugin = $Candidate.roots.plugin }
        files = $stateFiles
    }
    $temporary = Join-Path $directory ('.managed-files-{0}.tmp' -f [Guid]::NewGuid().ToString('N'))
    [IO.File]::WriteAllText($temporary, (($document | ConvertTo-Json -Depth 8) + [Environment]::NewLine), $script:Utf8NoBom)
    if (Test-Path -LiteralPath $StatePath) {
        $backup = Join-Path $directory ('.managed-files-{0}.bak' -f [Guid]::NewGuid().ToString('N'))
        try { [IO.File]::Replace($temporary, $StatePath, $backup, $true) }
        finally { Remove-Item -LiteralPath $temporary, $backup -Force -ErrorAction SilentlyContinue }
    }
    else { Move-Item -LiteralPath $temporary -Destination $StatePath }
}

function Test-IsAdministrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function ConvertTo-WindowsCommandLineArgument([string]$Value) {
    if ($null -eq $Value) { return '""' }
    $builder = New-Object Text.StringBuilder
    [void]$builder.Append('"')
    $backslashes = 0
    foreach ($character in $Value.ToCharArray()) {
        if ($character -eq '\') { $backslashes++; continue }
        if ($character -eq '"') {
            [void]$builder.Append(('\' * ($backslashes * 2 + 1)))
            [void]$builder.Append('"')
        }
        else {
            if ($backslashes -gt 0) { [void]$builder.Append(('\' * $backslashes)) }
            [void]$builder.Append($character)
        }
        $backslashes = 0
    }
    if ($backslashes -gt 0) { [void]$builder.Append(('\' * ($backslashes * 2))) }
    [void]$builder.Append('"')
    return $builder.ToString()
}

function Invoke-SelfElevation {
    $powerShellPath = (Get-Process -Id $PID).Path
    $arguments = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', (ConvertTo-WindowsCommandLineArgument $PSCommandPath), '-PackageRoot', (ConvertTo-WindowsCommandLineArgument $PackageRoot), '-Elevated')
    try {
        $process = Start-Process -FilePath $powerShellPath -ArgumentList $arguments -Verb RunAs -Wait -PassThru
        exit $process.ExitCode
    }
    catch { throw "无法取得管理员权限：$($_.Exception.Message)" }
}

function Initialize-InstallerLog([string]$ProgramDataPath) {
    $logDirectory = Join-Path $ProgramDataPath 'LabelFrame\installer\logs'
    New-Item -ItemType Directory -Path $logDirectory -Force | Out-Null
    $script:InstallerLogPath = Join-Path $logDirectory ("offline-install-{0:yyyyMMdd-HHmmss}-{1}.log" -f (Get-Date), [Guid]::NewGuid().ToString('N').Substring(0, 8))
    [IO.File]::WriteAllText($script:InstallerLogPath, '', $script:Utf8NoBom)
    return $logDirectory
}

function Invoke-Installer {
    if (-not $script:TestMode) {
        if (-not (Test-IsAdministrator)) {
            if ($Elevated) { throw '已请求 UAC，但当前进程仍没有管理员权限。' }
            Invoke-SelfElevation
            return
        }
    }
    elseif ([string]::IsNullOrWhiteSpace($script:TestRoot)) { throw 'TestMode 必须提供 -TestRoot。' }

    Write-InstallLog "离线包目录：$PackageRoot"
    $contract = Read-PackageContract $PackageRoot

    $paths = Resolve-InstallPaths
    $logDirectory = Initialize-InstallerLog $paths.ProgramData
    Write-InstallLog "安装日志：$script:InstallerLogPath"
    Write-InstallLog "包完整性通过：版本 $($contract.Version)，类型 $($contract.Kind)。"

    $candidate = New-CandidateState $contract $paths
    [void](Resolve-HealthBaseUri $paths.Listen)
    $statePath = Join-Path $paths.ProgramData 'LabelFrame\installer\managed-files.json'
    $previousState = Read-PreviousState $statePath

    Ensure-ServerRuntimes $contract

    Invoke-ServerMsi $contract $logDirectory
    Ensure-ServerServiceInstalled $contract $logDirectory

    $paths = Resolve-InstallPaths
    $candidate = New-CandidateState $contract $paths
    [void](Resolve-HealthBaseUri $paths.Listen)

    $staging = $null
    if ($paths.Headless) { Write-InstallLog 'LABELFRAME_SERVER_WEB_UI 已定义为空白：按无头模式部署，不解压或改写 Web UI。' }
    else {
        $staging = Expand-WebUiToStaging $contract.WebUiZip $paths.WebUi
        Switch-WebUi $staging $paths.WebUi
        $staging = $null
        Write-InstallLog "Web UI 已整体替换：$($paths.WebUi)"
    }

    try {
        Copy-ManagedResources $candidate
        Start-ServerService
        Test-ServiceAndApi $contract $paths $candidate
        Remove-SafeDifference $previousState $candidate
        Write-StateLedger $statePath $candidate
        if ($script:WebUiBackup -and (Test-Path -LiteralPath $script:WebUiBackup)) { Remove-Item -LiteralPath $script:WebUiBackup -Recurse -Force }
        $script:WebUiSwapped = $false
    }
    catch {
        Restore-WebUi
        throw
    }
    finally {
        if ($staging -and (Test-Path -LiteralPath $staging)) { Remove-Item -LiteralPath $staging -Recurse -Force -ErrorAction SilentlyContinue }
    }

    Write-InstallLog "安装成功：LabelFrame Server $($contract.Version)，服务 Running，health/info/下载哈希验证通过。"
    Write-Host ''
    Write-Host '================ LabelFrame Server 离线安装完成 ================'
    Write-Host "版本：$($contract.Version)"
    Write-Host "类型：$($contract.Kind)"
    Write-Host "健康检查：$((Join-HealthUri (Resolve-HealthBaseUri $paths.Listen) 'healthz').AbsoluteUri)"
    Write-Host "安装日志：$script:InstallerLogPath"
    Write-Host "MSI 日志：$script:MsiLogPath"
    Write-Host '================================================================'
}

if ($LibraryMode) { return }

try {
    Invoke-Installer
    exit 0
}
catch {
    $message = $_.Exception.Message
    Write-InstallLog "失败阶段：$message" 'ERROR'
    if ($script:MsiLogPath) { Write-Host "MSI 日志：$script:MsiLogPath" }
    if ($script:InstallerLogPath) { Write-Host "安装日志：$script:InstallerLogPath" }
    exit 1
}
