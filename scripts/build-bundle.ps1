# 构建 LabelFrame 安装引导 Bundle（WiX Burn，DESIGN §6.1 决策 #122；链序与 runtime 消费 = §6.9 决策 #124）
# 前置：Server / Client MSI 已生成（bundle 构建需读取 MSI 包元数据）：
#   .\scripts\build-server-msi.ps1 -Version x.y.z -WixPath <wix.exe>
#   .\scripts\build-msi.ps1       -Version x.y.z -WixPath <wix.exe>
#       .\scripts\package-server-webui.ps1 -Version x.y.z（管理界面 zip）
#       .\scripts\build-zebra-plugin.ps1 -Version x.y.z（官方插件 .lfplugin）
# runtime 安装器按 install manifest 获取（#115：厂商直链 + CI 锁哈希）：本地缓存缺失即按 urls[0] 下载，
# sha256 与 manifest 逐字节校验不符即构建失败（fail-closed）——仅 .NET 双 runtime（版本化直链，字节不轮换）；
# WebView2 evergreen 例外（决策 #151，#173）：不进引擎获取（链包 = PayloadTool 包装，下载 / Authenticode 验签在
# 安装期包执行期完成），构建期不下载、不钉哈希——免疫微软轮换直链文件。
# 迭代 68（#101，决策 #132）起随发版构建：release.yml bundle job 以 -ManifestSource 指向当版本地 manifest
# 调用本脚本，产物 LabelFrame-Bootstrapper-<版本>.exe 纳入 Release 附件；Secrets 在场时以 -Sign 复用 MSI 自签证书签名。
param(
    [Parameter(Mandatory = $true)][string]$Version,
    [string]$WixPath = '',
    [string]$MsiDir = '',
    [string]$DownloadBase = '',
    [string]$ManifestSource = 'https://github.com/marci-labs/LabelFrame/releases/latest/download/install-manifest.json',
    [string]$RuntimeCacheDir = '',
    # 代码签名（迭代 68，#101 决议：复用 MSI 自签证书通道；signtool 定位与签名参数与 build-msi.ps1 对齐）
    [string]$PfxPath = '',
    [string]$PfxPassword = $env:MSI_SIGN_PASSWORD,
    [switch]$Sign
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

$wix = $WixPath
if (-not $wix) { $wix = $env:WIX_PATH }
if (-not $wix) { $wix = 'C:\Program Files\WiX Toolset v7.0\bin\wix.exe' }
if (-not (Test-Path $wix)) { $wix = Join-Path $env:USERPROFILE '.dotnet\tools\wix.exe' }
if (-not (Test-Path $wix)) { throw "未找到 WiX（$wix），请安装 WiX Toolset v7（dotnet tool install --global wix --version 7.0.0）或通过 -WixPath 指定 wix.exe。" }
if (-not $MsiDir) { $MsiDir = Join-Path $root 'artifacts' }
if (-not $DownloadBase) { $DownloadBase = "https://github.com/marci-labs/LabelFrame/releases/download/v$Version" }
if (-not $RuntimeCacheDir) { $RuntimeCacheDir = Join-Path $root 'artifacts\runtimes' }

function Get-Sha256([string]$Path) {
    (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

# 1) 发布 BA（net48；RID win-x64 使 mbanative.dll 等 runtimes 资产落位发布目录）
$baDir = Join-Path $root 'artifacts\bootstrapper\ba'
if (Test-Path -LiteralPath $baDir) { Remove-Item -LiteralPath $baDir -Recurse -Force }
dotnet publish (Join-Path $root 'src\LabelFrame.Bootstrapper.Ba\LabelFrame.Bootstrapper.Ba.csproj') `
    -c Release -f net48 -r win-x64 -o $baDir -p:DebugType=None -p:DebugSymbols=false | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'BA publish failed' }

# 2) 发布落位工具（net48 控制台，随 bundle 压缩内嵌，§6.9 机制 A）
$payloadToolDir = Join-Path $root 'artifacts\bootstrapper\payloadtool'
if (Test-Path -LiteralPath $payloadToolDir) { Remove-Item -LiteralPath $payloadToolDir -Recurse -Force }
dotnet publish (Join-Path $root 'src\LabelFrame.Bootstrapper.PayloadTool\LabelFrame.Bootstrapper.PayloadTool.csproj') `
    -c Release -f net48 -r win-x64 -o $payloadToolDir -p:DebugType=None -p:DebugSymbols=false | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'PayloadTool publish failed' }

# 3) 校验本仓产物在场（MSI：Compressed=no 仍需 SourceFile 提取 ProductCode 等元数据；落位包同理需 zip 提取摘要）
$serverMsi = Join-Path $MsiDir "LabelFrame-Server-$Version.msi"
$clientMsi = Join-Path $MsiDir "LabelFrame-Client-$Version.msi"
$webUiZip = Join-Path $MsiDir "labelframe-server-webui-$Version.zip"
$zebraPlugin = Join-Path $MsiDir "labelframe-transport-zebra-$Version.lfplugin"
$tsplPlugin = Join-Path $MsiDir "labelframe-transport-tspl-$Version.lfplugin"
foreach ($artifact in @($serverMsi, $clientMsi, $webUiZip, $zebraPlugin, $tsplPlugin)) {
    if (-not (Test-Path -LiteralPath $artifact)) {
        throw "未找到 $artifact —— 请先运行 scripts\build-server-msi.ps1、scripts\build-msi.ps1、scripts\package-server-webui.ps1、scripts\build-zebra-plugin.ps1 与 scripts\build-tspl-plugin.ps1 生成产物。"
    }
}

# 4) 按 install manifest 获取 runtime 安装器（#115 消费：DownloadUrl + sha256 校验）
if (-not (Test-Path -LiteralPath $RuntimeCacheDir)) { New-Item -ItemType Directory -Force -Path $RuntimeCacheDir | Out-Null }
if ($ManifestSource -match '^https?://') {
    $manifest = Invoke-RestMethod -Uri $ManifestSource -UseBasicParsing
} else {
    $manifest = Get-Content -LiteralPath $ManifestSource -Raw -Encoding UTF8 | ConvertFrom-Json
}

function Resolve-RuntimePackage([string]$ComponentId, [string]$CacheFileName) {
    $entry = @($manifest.components | Where-Object { $_.id -eq $ComponentId })
    if ($entry.Count -ne 1) { throw "install manifest 缺少 runtime 条目：$ComponentId（当前清单 $($manifest.labelframeVersion)）——引导 Bundle 链依赖该条目提供厂商直链与哈希。" }
    $entry = $entry[0]

    $path = Join-Path $RuntimeCacheDir $CacheFileName
    if (Test-Path -LiteralPath $path) {
        $existing = Get-Sha256 $path
        if ($existing -eq $entry.sha256) {
            Write-Host "runtime 缓存命中（哈希与 manifest 一致）：$path"
        } else {
            Write-Host "runtime 缓存哈希与 manifest 不符（厂商轮转或清单更新），重新下载：$path"
            Remove-Item -LiteralPath $path -Force
        }
    }

    if (-not (Test-Path -LiteralPath $path)) {
        $url = @($entry.urls)[0]
        Write-Host "下载 runtime 安装器：$url"
        Invoke-WebRequest -Uri $url -OutFile $path -UseBasicParsing
    }

    $actual = Get-Sha256 $path
    if ($actual -ne $entry.sha256) {
        throw "runtime 组件 $ComponentId 哈希校验失败：manifest=$($entry.sha256) 实测=$actual —— 版本化直链内容不应轮换，请核对清单与直链（§6.2 校验策略，决策 #151：仅 .NET 双 runtime 走哈希钉死）。"
    }
    Write-Host "runtime 哈希校验通过：[$ComponentId] $CacheFileName（$actual）"

    [pscustomobject]@{ Path = $path; Url = @($entry.urls)[0]; Version = $entry.version }
}

$desktopVersion = ($manifest.components | Where-Object { $_.id -eq 'runtime-desktop' } | Select-Object -First 1).version
$desktopRuntime = Resolve-RuntimePackage 'runtime-desktop' "windowsdesktop-runtime-$desktopVersion-win-x64.exe"
$aspnetCoreVersion = ($manifest.components | Where-Object { $_.id -eq 'runtime-aspnetcore' } | Select-Object -First 1).version
$aspnetCoreRuntime = Resolve-RuntimePackage 'runtime-aspnetcore' "aspnetcore-runtime-$aspnetCoreVersion-win-x64.exe"
# WebView2（runtime-webview2）不在此获取（决策 #151）：evergreen 直链无摘要可钉，安装期由 PayloadTool 验签获取

# 5) wix build（Burn 核心内置，自研 BA 无需 Bal 扩展）
$bundleExe = Join-Path $root "artifacts\LabelFrame-Bootstrapper-$Version.exe"
$global:LASTEXITCODE = 0
& $wix eula accept wix7 2>$null | Out-Null
& $wix build (Join-Path $root 'packaging\bootstrapper\Bundle.wxs') `
    -d "BaDir=$baDir\" -d "MsiDir=$MsiDir\" `
    -d "ProductVersion=$Version" -d "BundleVersion=$Version" -d "DownloadBase=$DownloadBase" `
    -d "AssetsDir=$(Join-Path $root 'assets')\" `
    -d "PayloadToolPath=$payloadToolDir\LabelFrame.Bootstrapper.PayloadTool.exe" -d "PayloadToolDir=$payloadToolDir\" `
    -d "WebUiZipPath=$webUiZip" -d "ZebraPluginPath=$zebraPlugin" -d "TsplPluginPath=$tsplPlugin" `
    -d "RuntimeDesktopPath=$($desktopRuntime.Path)" -d "RuntimeDesktopUrl=$($desktopRuntime.Url)" -d "RuntimeDesktopVersion=$($desktopRuntime.Version)" `
    -d "RuntimeAspNetCorePath=$($aspnetCoreRuntime.Path)" -d "RuntimeAspNetCoreUrl=$($aspnetCoreRuntime.Url)" -d "RuntimeAspNetCoreVersion=$($aspnetCoreRuntime.Version)" `
    -o $bundleExe -arch x64 2>&1 | Write-Host
if ($LASTEXITCODE -ne 0) { throw 'wix build failed' }

# 6) 代码签名（可选：-Sign；wix build 成功后对引导 EXE 签名，手法与 build-msi.ps1 一致）
if ($Sign) {
    if (-not $PfxPassword) { throw '未提供签名密码：请用 -PfxPassword 或设置环境变量 MSI_SIGN_PASSWORD。' }
    if (-not $PfxPath) { $PfxPath = Join-Path $root 'artifacts\cert\labelframe.pfx' }
    if (-not (Test-Path $PfxPath)) { throw "未找到证书 $PfxPath，请先运行 scripts\create-signing-cert.ps1" }
    $signtoolPath = $env:SIGNFILE
    if (-not $signtoolPath) {
        $found = Get-ChildItem 'C:\Program Files (x86)\Windows Kits\10\bin' -Recurse -Filter signtool.exe -ErrorAction SilentlyContinue |
            Sort-Object FullName -Descending | Select-Object -First 1
        if ($found) { $signtoolPath = $found.FullName }
    }
    if (-not $signtoolPath) {
        $toolsDir = Join-Path $root 'artifacts\tools'
        $cachedSig = Get-ChildItem $toolsDir -Recurse -Filter signtool.exe -ErrorAction SilentlyContinue | Sort-Object FullName -Descending | Select-Object -First 1
        if ($cachedSig) { $signtoolPath = $cachedSig.FullName } else {
            Write-Host '未找到 signtool，正在从 NuGet 下载 Windows SDK BuildTools 提取…'
            New-Item -ItemType Directory -Force -Path $toolsDir | Out-Null
            $ver = (Invoke-RestMethod 'https://api.nuget.org/v3-flatcontainer/microsoft.windows.sdk.buildtools/index.json' -TimeoutSec 60).versions | Select-Object -Last 1
            $zip = Join-Path $toolsDir 'sdkbt.zip'
            Invoke-WebRequest "https://api.nuget.org/v3-flatcontainer/microsoft.windows.sdk.buildtools/$ver/microsoft.windows.sdk.buildtools.$ver.nupkg" -OutFile $zip -TimeoutSec 300
            Expand-Archive -Path $zip -DestinationPath $toolsDir -Force
            $extracted = Get-ChildItem $toolsDir -Recurse -Filter signtool.exe | Sort-Object FullName -Descending | Select-Object -First 1
            if (-not $extracted) { throw 'signtool 提取失败。' }
            $signtoolPath = $extracted.FullName
        }
    }
    if (-not $signtoolPath) { throw '未找到 signtool.exe：请安装 Windows SDK，或设置环境变量 SIGNFILE 指向 signtool.exe' }
    Write-Host "使用 signtool：$signtoolPath"
    $global:LASTEXITCODE = 0
    & $signtoolPath sign /f $PfxPath /p $PfxPassword /fd SHA256 /tr http://timestamp.digicert.com /td SHA256 $bundleExe 2>&1 | Write-Host
    if ($LASTEXITCODE -ne 0) { throw '引导 EXE 签名失败' }
    Write-Host "引导 EXE 已签名：$bundleExe"
}

Write-Host "Bundle 生成完成：$bundleExe"
Write-Host "大小：$([Math]::Round((Get-Item $bundleExe).Length / 1MB, 2)) MB"
