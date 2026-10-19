# 一键构建 LabelFrame Server（服务端）MSI 安装包（迭代 16/17：安装到 Program Files\LabelFrame\Server）
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

# 1) 发布 Server（framework-dependent，无 Web UI）
$publishDir = Join-Path $root "artifacts\server\$Runtime"
# 清理旧发布目录，避免残留旧 web/dist 被打包
if (Test-Path -LiteralPath $publishDir) { Remove-Item -LiteralPath $publishDir -Recurse -Force }
dotnet publish (Join-Path $root 'src\LabelFrame.Server\LabelFrame.Server.csproj') `
    -c Release -f net10.0-windows -r $Runtime -p:SelfContained=false `
    -o $publishDir -p:DebugType=None -p:DebugSymbols=false | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Server publish failed' }

# 迭代 18：服务端无头化——不再打包 web/dist（Web UI 由 Client 托管）。
# 2) 复制服务端默认配置到发布目录（先于文件清单，确保 appsettings.json 被打包）
Copy-Item (Join-Path $root 'packaging\appsettings-server.json') (Join-Path $publishDir 'appsettings.json') -Force

# 3) 生成 WiX 文件清单（GUID 加盐 server，避免与 Client 包组件 GUID 冲突）
$filesWxs = Join-Path $root 'packaging\files-server.wxs'
& (Join-Path $root 'packaging\generate-files.ps1') -PublishDir $publishDir -OutFile $filesWxs -GuidSalt 'server' -ServerServiceName 'LabelFrameServer' -ServerServiceDisplayName 'LabelFrame 服务端'

# 4) wix build
$msi = Join-Path $root "artifacts\LabelFrame-Server-$Version.msi"
$global:LASTEXITCODE = 0
& $wix eula accept wix7 2>$null | Out-Null
& $wix build (Join-Path $root 'packaging\main-server.wxs') $filesWxs -d PublishDir=$publishDir -d Version=$Version -d AssetsDir=$(Join-Path $root 'assets') -d LicenseRtf=$(Join-Path $root 'packaging\license.rtf') -o $msi -arch x64 -ext WixToolset.NetFx.wixext -ext WixToolset.UI.wixext -culture zh-cn 2>&1 | Write-Host
if ($LASTEXITCODE -ne 0) { throw 'wix build failed' }

# 5) 代码签名（可选：-Sign，与 Client 同证书）
if ($Sign) {
    if (-not $PfxPassword) { throw '未提供签名密码：请用 -PfxPassword 或设置环境变量 MSI_SIGN_PASSWORD。' }
    if (-not $PfxPath) { $PfxPath = Join-Path $root 'artifacts\cert\labelframe.pfx' }
    if (-not (Test-Path $PfxPath)) { throw "未找到证书 $PfxPath，请先运行 scripts\create-signing-cert.ps1" }
    $signtoolPath = $env:SIGNFILE
    if (-not $signtoolPath) {
        $found = Get-ChildItem 'C:\Program Files (x86)\Windows Kits\10\bin' -Recurse -Filter signtool.exe -ErrorAction SilentlyContinue | Sort-Object FullName -Descending | Select-Object -First 1
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