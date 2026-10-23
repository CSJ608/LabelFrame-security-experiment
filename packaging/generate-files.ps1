# 生成 WiX 文件清单（fragment wxs）：根目录文件 + web/dist（含 assets 子目录树）
param(
    [string]$PublishDir = '',
    [string]$OutFile = '',
    [string]$GuidSalt = 'default',
    [string]$ServerServiceName = '',
    [string]$ServerServiceDisplayName = ''
)
$root = Split-Path -Parent $PSScriptRoot
if (-not $PublishDir) { $PublishDir = Join-Path $root 'artifacts\win-x64' }
if (-not $OutFile) { $OutFile = Join-Path $PSScriptRoot 'files.wxs' }
if (-not (Test-Path (Join-Path $PublishDir 'LabelFrame.WinHost.exe')) -and -not (Test-Path (Join-Path $PublishDir 'LabelFrame.Server.exe'))) { throw 'publish dir invalid' }

function New-GuidFromPath([string]$rel) {
    $sha = [System.Security.Cryptography.SHA1]::Create()
    $bytes = $sha.ComputeHash([System.Text.Encoding]::UTF8.GetBytes($rel + '|' + $GuidSalt))
    $b = $bytes[0..15]
    $b[7] = (($b[7] -band 0x0f) -bor 0x50)
    $b[8] = (($b[8] -band 0x3f) -bor 0x80)
    return [Guid]::Parse([BitConverter]::ToString($b).Replace('-', '')).ToString().ToUpperInvariant()
}

function Add-FileLine([System.Text.StringBuilder]$sb, [int]$idx, [string]$guid, [string]$rel, [string]$fileId, [string]$prefix, [string]$extra = '') {
    $ext = [System.IO.Path]::GetExtension($rel)
    if ($ext.Length -gt 0) { $ext = $ext.Substring(1) }
    if ($ext.Length -gt 3) { $ext = $ext.Substring(0, 3) }
    $shortName = ('LF{0:D6}.{1}' -f $idx, $ext.ToUpperInvariant())
    [void]$sb.AppendLine('      <Component Id="' + $prefix + $idx + '" Guid="' + $guid + '">')
    [void]$sb.AppendLine('        <File' + $fileId + ' ShortName="' + $shortName + '" Source="$(var.PublishDir)/' + $rel + '" />')
    if ($extra) { [void]$sb.Append($extra) }
    [void]$sb.AppendLine('      </Component>')
}

$allFiles = Get-ChildItem -Path $PublishDir -Recurse -File
# 先收集 web 文件，确定子目录集合
$webFiles = @($allFiles | Where-Object {
    $r = $_.FullName.Substring($PublishDir.Length).TrimStart('\') -replace '\\', '/'
    $r -like 'web/dist/*'
})
$webSubDirs = @{}   # relSubDir -> dirId
foreach ($f in $webFiles) {
    $rel = $f.FullName.Substring($PublishDir.Length).TrimStart('\') -replace '\\', '/'
    $relWeb = $rel.Substring('web/dist/'.Length)
    $slash = $relWeb.IndexOf('/')
    if ($slash -ge 0) {
        $sub = $relWeb.Substring(0, $slash)
        if (-not $webSubDirs.ContainsKey($sub)) {
            $dirId = 'WebDir_' + (($sub -replace '[^a-zA-Z0-9]', '') + '_' + (Get-Random -Maximum 99999))
            $webSubDirs[$sub] = $dirId
        }
    }
}

$sb = New-Object System.Text.StringBuilder
[void]$sb.AppendLine('<?xml version="1.0" encoding="utf-8"?>')
[void]$sb.AppendLine('<Wix xmlns="http://wixtoolset.org/schemas/v4/wxs">')
[void]$sb.AppendLine('  <Fragment>')
if ($webFiles.Count -gt 0) {
[void]$sb.AppendLine('    <DirectoryRef Id="INSTALLFOLDER">')
[void]$sb.AppendLine('      <Directory Id="WebDir" Name="web">')
[void]$sb.AppendLine('        <Directory Id="WebDistDir" Name="dist">')
foreach ($sub in $webSubDirs.Keys) {
    [void]$sb.AppendLine('          <Directory Id="' + $webSubDirs[$sub] + '" Name="' + $sub + '" />')
}
[void]$sb.AppendLine('        </Directory>')
[void]$sb.AppendLine('      </Directory>')
[void]$sb.AppendLine('    </DirectoryRef>')
}

$webSb = New-Object System.Text.StringBuilder
if ($webFiles.Count -gt 0) {
[void]$webSb.AppendLine('    <ComponentGroup Id="WebFiles">')
}

$index = 0
$webIndex = 0
foreach ($f in $allFiles) {
    $rel = $f.FullName.Substring($PublishDir.Length).TrimStart('\') -replace '\\', '/'
    $guid = New-GuidFromPath $rel
    if ($rel -eq 'appsettings.json') {
        # 用户配置：不放入自动文件清单，由 main.wxs 独立组件管理（NeverOverwrite + Permanent，升级/修复/卸载均保留用户数据）
        continue
    }
    if ($rel -eq 'LabelFrame.WinHost.exe') {
        $updaterXml = @'
        <ServiceInstall Id="ClientUpdaterInstall" Name="LabelFrameClientUpdater" DisplayName="LabelFrame Client Updater" Type="ownProcess" Start="auto" Account="LocalSystem" ErrorControl="normal" Arguments="--update-service" />
        <ServiceControl Id="ClientUpdaterControl" Name="LabelFrameClientUpdater" Start="install" Stop="both" Remove="uninstall" Wait="yes" />
'@
        Add-FileLine $sb $index $guid $rel ' Id="WinHostExe"' 'f' $updaterXml
        $index++
    } elseif ($rel -eq 'LabelFrame.Server.exe') {
        $serviceXml = ''
        if ($ServerServiceName) {
            $serviceXml = "`n        <ServiceInstall Id=`"LabelFrameServerInstall`" Name=`"$ServerServiceName`" DisplayName=`"$ServerServiceDisplayName`" Type=`"ownProcess`" Start=`"auto`" Account=`"LocalSystem`" ErrorControl=`"normal`" />`n        <ServiceControl Id=`"LabelFrameServerControl`" Name=`"$ServerServiceName`" Start=`"install`" Stop=`"both`" Remove=`"uninstall`" Wait=`"yes`" />`n"
        }
        Add-FileLine $sb $index $guid $rel ' Id="ServerExe"' 'f' $serviceXml
        $index++
    } elseif ($rel -like 'web/dist/*') {
        $relWeb = $rel.Substring('web/dist/'.Length)
        $slash = $relWeb.IndexOf('/')
        if ($slash -ge 0) {
            $sub = $relWeb.Substring(0, $slash)
            $dirId = $webSubDirs[$sub]
            [void]$sb.AppendLine('    <DirectoryRef Id="' + $dirId + '">')
            Add-FileLine $sb $webIndex $guid $rel (' Id="wf' + $webIndex + '"') 'w'
            [void]$sb.AppendLine('    </DirectoryRef>')
        } else {
            [void]$sb.AppendLine('    <DirectoryRef Id="WebDistDir">')
            Add-FileLine $sb $webIndex $guid $rel (' Id="wf' + $webIndex + '"') 'w'
            [void]$sb.AppendLine('    </DirectoryRef>')
        }
        [void]$webSb.AppendLine('      <ComponentRef Id="w' + $webIndex + '" />')
        $webIndex++
    } else {
        Add-FileLine $sb $index $guid $rel (' Id="ff' + $index + '"') 'f'
        $index++
    }
}

[void]$sb.AppendLine('    <ComponentGroup Id="AppFiles" Directory="INSTALLFOLDER">')
for ($i = 0; $i -lt $index; $i++) { [void]$sb.AppendLine('      <ComponentRef Id="f' + $i + '" />') }
[void]$sb.AppendLine('    </ComponentGroup>')
if ($webFiles.Count -gt 0) {
[void]$webSb.AppendLine('    </ComponentGroup>')
[void]$sb.Append($webSb.ToString())
}
[void]$sb.AppendLine('  </Fragment>')
[void]$sb.AppendLine('</Wix>')
[System.IO.File]::WriteAllText($OutFile, $sb.ToString(), (New-Object System.Text.UTF8Encoding($false)))
Write-Host "generated $($index + $webIndex) files -> $OutFile"
