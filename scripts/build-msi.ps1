# 一键构建 LabelFrame Client（打印客户端）MSI 安装包（迭代 16/17：安装到 Program Files\LabelFrame\Client）
param(
    [Parameter(Mandatory = $true)][string]$Version,
    [string]$Runtime = 'win-x64',
    [string]$WixPath = '',
    [string]$PfxPath = '',
    [string]$PfxPassword = $env:MSI_SIGN_PASSWORD,
    [switch]$Sign
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$wix = $WixPath
if (-not $wix) { $wix = $env:WIX_PATH }
if (-not $wix) { $wix = 'C:\Program Files\WiX Toolset v7.0\bin\wix.exe' }
if (-not (Test-Path $wix)) { throw "未找到 WiX（$wix），请安装 WiX Toolset v7 或通过 -WixPath 指定 wix.exe。" }

& $wix extension add -g WixToolset.Util.wixext/7.0.0
if ($LASTEXITCODE -ne 0) { throw 'WiX Util 隐藏执行扩展准备失败' }

# 1) 发布 WinHost（Client，framework-dependent）+ 复制 web/dist
$publishDir = Join-Path $root "artifacts\client\$Runtime"
& (Join-Path $PSScriptRoot 'publish-winhost.ps1') -Runtime $Runtime -OutputDir (Join-Path $root 'artifacts\client')
if (-not $?) { throw 'publish failed' }

# 2) 复制默认配置到发布目录（先于文件清单，确保 appsettings.json 被打包）
Copy-Item (Join-Path $root 'packaging\appsettings.json') (Join-Path $publishDir 'appsettings.json') -Force

# 3) 生成 WiX 文件清单（GUID 加盐 client，避免与 Server 包组件 GUID 冲突）
$filesWxs = Join-Path $root 'packaging\files-client.wxs'
& (Join-Path $root 'packaging\generate-files.ps1') -PublishDir $publishDir -OutFile $filesWxs -GuidSalt 'client'

# 3b) 官方插件附带包（迭代 63，决策 #123；迭代 143 扩 TSPL，决策 #195）：检测 artifacts 下的官方 .lfplugin → 随 MSI 携带
#     （plugin-packages\ 目录，供 OfficialPluginMigration 存量升级自动安装；无产物时跳过——本地裸构建）
$pluginPackageArgs = @()
$officialPlugins = @(
    @{ Id = 'zebra'; Var = 'ZebraPluginPackage' },
    @{ Id = 'tspl';  Var = 'TsplPluginPackage' }
)
foreach ($plugin in $officialPlugins) {
    $package = Get-ChildItem (Join-Path $root 'artifacts') -Filter "labelframe-transport-$($plugin.Id)-*.lfplugin" -ErrorAction SilentlyContinue |
        Sort-Object Name -Descending | Select-Object -First 1
    if ($package) {
        $pluginPackageArgs += @('-d', "$($plugin.Var)=$($package.FullName)")
        Write-Host "随 MSI 附带官方插件包：$($package.Name)"
    } else {
        Write-Host "未找到 $($plugin.Id) 官方插件包产物（artifacts\labelframe-transport-$($plugin.Id)-*.lfplugin），本次 MSI 不附带（先运行 scripts\build-$($plugin.Id)-plugin.ps1）"
    }
}

# 4) wix build
$msi = Join-Path $root "artifacts\LabelFrame-Client-$Version.msi"
$global:LASTEXITCODE = 0
& $wix eula accept wix7 2>$null | Out-Null
& $wix build (Join-Path $root 'packaging\main.wxs') $filesWxs -d PublishDir=$publishDir -d Version=$Version -d AssetsDir=$(Join-Path $root 'assets') -d LicenseRtf=$(Join-Path $root 'packaging\license.rtf') -d PackagingDir=$(Join-Path $root 'packaging') @pluginPackageArgs -o $msi -arch x64 -ext WixToolset.NetFx.wixext -ext WixToolset.UI.wixext -ext WixToolset.Util.wixext/7.0.0 -culture zh-cn 2>&1 | Write-Host
if ($LASTEXITCODE -ne 0) { throw 'wix build failed' }

# 4b) 发布工件依赖降版回归断言（迭代 64 返修，#57 AC-01，决策 #130）：
#     File 表关键程序集不低于上一发版基线——Windows Installer 组件规则拒装降版 keyfile，
#     覆盖升级会净丢失降版文件（v0.27.0 实证）。本入口为 CI「MSI 结构断言」与 release.yml 共用。
& (Join-Path $PSScriptRoot 'assert-client-deps-baseline.ps1') -MsiPath $msi
if (-not $?) { throw '依赖基线断言失败（发布工件降版）' }

# 5) 代码签名（可选：-Sign）
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
    & $signtoolPath sign /f $PfxPath /p $PfxPassword /fd SHA256 /tr http://timestamp.digicert.com /td SHA256 $msi 2>&1 | Write-Host
    if ($LASTEXITCODE -ne 0) { throw 'MSI 签名失败' }
    Write-Host "MSI 已签名：$msi"
}

Write-Host "MSI 生成完成：$msi"
Write-Host "大小：$([Math]::Round((Get-Item $msi).Length / 1MB, 1)) MB"
