# Client MSI 附带官方插件包断言（迭代 67，#97；Windows Installer COM 查 MSI 表手法
# 参照 scripts/assert-client-deps-baseline.ps1——迭代 64 决策 #130 引入）
#
# 背景：release.yml 曾把「构建 Zebra 官方插件包」误排在「打包 Client MSI」之后（PR #80 引入），
# build-msi.ps1 打包时检测不到 artifacts\labelframe-transport-zebra-*.lfplugin 即按设计静默跳过
# 附带（本地裸构建与 CI「MSI 结构断言」必需检查依赖该行为），公网发版 Client MSI 因此不含
# plugin-packages\*.lfplugin——存量升级的 zebra 自动安装（决策 #123）不可能发生（#56 AC-04 实证）。
#
# 本脚本断言指定 Client MSI 的 File 表含 plugin-packages\labelframe-transport-zebra-<版本>.lfplugin
# （文件名 + 所属目录双条件，File / Component / Directory 三表联查），缺失即非零退出（fail-closed）；
# 由 release.yml「打包 Client MSI」后的显式断言步骤调用——缺包跳过是 build-msi.ps1 的合法本地行为，
# 发版链的强制拦截由该显式步骤承担。
#
# 兼容 Windows PowerShell 5.1（本地自验）与 PowerShell 7（GitHub Actions runner）。
#
# 用法：
#   powershell -File scripts/assert-msi-plugin-package.ps1 -MsiPath artifacts\LabelFrame-Client-0.0.1.msi
param(
    [Parameter(Mandatory = $true)][string]$MsiPath
)
$ErrorActionPreference = 'Stop'
# 官方插件 id 清单（迭代 143 起双插件，决策 #195）：逐 id 断言附带包在位
$pluginIds = @('labelframe-transport-zebra', 'labelframe-transport-tspl')
$pluginDirName = 'plugin-packages'

if (-not (Test-Path $MsiPath)) { throw "MSI 不存在：$MsiPath" }

$installer = New-Object -ComObject WindowsInstaller.Installer
$db = $installer.GetType().InvokeMember('OpenDatabase', 'InvokeMethod', $null, $installer, @((Resolve-Path $MsiPath).Path, 0))

function Read-MsiTable($db, [string]$sql, [int]$columns) {
    $view = $db.GetType().InvokeMember('OpenView', 'InvokeMethod', $null, $db, @($sql))
    $view.GetType().InvokeMember('Execute', 'InvokeMethod', $null, $view, $null) | Out-Null
    $rows = @()
    while ($true) {
        $rec = $view.GetType().InvokeMember('Fetch', 'InvokeMethod', $null, $view, $null)
        if ($null -eq $rec) { break }
        # 列索引用 for 循环变量取值：管道 $_ 经 InvokeMember 传参触发 DISP_E_TYPEMISMATCH（PS 5.1 本机实测）
        $row = New-Object 'object[]' $columns
        for ($i = 1; $i -le $columns; $i++) {
            $row[$i - 1] = $rec.GetType().InvokeMember('StringData', 'GetProperty', $null, $rec, @($i))
        }
        $rows += ,@($row)
    }
    $view.GetType().InvokeMember('Close', 'InvokeMethod', $null, $view, $null) | Out-Null
    return ,$rows
}

# FileName / DefaultDir 形如 "LABELT~1.LFP|labelframe-transport-zebra-0.0.1.lfplugin"（短名|长名），取长名
function Get-LongName([string]$msiName) {
    if ($msiName -like '*|*') { return ($msiName -split '\|')[-1] }
    return $msiName
}

# 三表联查：File（文件名 → 所属组件）→ Component（组件 → 所在目录）→ Directory（目录 → 目录名）
$files = Read-MsiTable $db 'SELECT FileName, Component_ FROM File' 2
$componentDir = @{}
foreach ($row in (Read-MsiTable $db 'SELECT Component, Directory_ FROM Component' 2)) {
    if (-not $componentDir.ContainsKey($row[0])) { $componentDir[$row[0]] = $row[1] }
}
$dirNames = @{}
foreach ($row in (Read-MsiTable $db 'SELECT Directory, DefaultDir FROM Directory' 2)) {
    if (-not $dirNames.ContainsKey($row[0])) { $dirNames[$row[0]] = Get-LongName $row[1] }
}

$hits = @()
$misplaced = @()
$missingIds = @()
foreach ($row in $files) {
    $fileName = Get-LongName $row[0]
    if ($fileName -notlike "labelframe-transport-*.lfplugin") { continue }
    $dirId = $componentDir[$row[1]]
    $dirName = ''
    if ($dirId -and $dirNames.ContainsKey($dirId)) { $dirName = $dirNames[$dirId] }
    if ($dirName -eq $pluginDirName) {
        $hits += "  命中：$pluginDirName\$fileName（File 表 FileName=$($row[0])，组件 $($row[1])，目录 $dirId）"
    } else {
        $misplaced += "  错位：$fileName 落在目录 '$dirName'（$dirId），期望 '$pluginDirName'"
    }
}

# 逐官方插件 id 断言附带包在位（缺任一即 fail-closed）
foreach ($pluginId in $pluginIds) {
    $matched = @($hits | Where-Object { $_ -like "*\ $pluginId-*" -or $_ -like "*\$pluginId-*" })
    if ($matched.Count -eq 0) { $missingIds += $pluginId }
}

if ($missingIds.Count -gt 0) {
    foreach ($pluginId in $missingIds) {
        Write-Host "断言失败：MSI File 表无 $pluginDirName\$pluginId-<版本>.lfplugin" -ForegroundColor Red
    }
    foreach ($m in $misplaced) { Write-Host $m -ForegroundColor Red }
    Write-Host '  （build-msi.ps1 打包时未检测到 artifacts 下的 .lfplugin 产物——发版链请确认插件包构建步骤先于 Client MSI 打包）' -ForegroundColor Red
    throw "Client MSI 未附带官方插件包（缺失：$($missingIds -join ' / ')，#97 / #378 fail-closed）"
}

foreach ($h in $hits) { Write-Host $h }
foreach ($m in $misplaced) { Write-Host $m -ForegroundColor Yellow }
Write-Host "断言通过：Client MSI 附带官方插件包（File 表 $($hits.Count) 处命中，决策 #123 / #195 存量升级自动安装源在位）。"
