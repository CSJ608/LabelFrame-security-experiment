# 验证 MSI 内部结构：向导对话框表、UI 序列、品牌二进制、ARP 属性
# 用法：powershell -File scripts/verify-msi-ui.ps1 <msi 路径> [...]
param([Parameter(Mandatory = $true)][string[]]$MsiPaths)

$ErrorActionPreference = 'Stop'

function Invoke-MsiQuery {
    param($Database, [string]$Sql)
    $view = $Database.GetType().InvokeMember('OpenView', 'InvokeMethod', $null, $Database, @($Sql))
    $view.GetType().InvokeMember('Execute', 'InvokeMethod', $null, $view, $null) | Out-Null
    $rows = @()
    while ($true) {
        $rec = $view.GetType().InvokeMember('Fetch', 'InvokeMethod', $null, $view, $null)
        if ($null -eq $rec) { break }
        $n = $rec.GetType().InvokeMember('FieldCount', 'GetProperty', $null, $rec, $null)
        $vals = @()
        for ($i = 0; $i -lt $n; $i++) {
            $vals += $rec.GetType().InvokeMember('StringData', 'GetProperty', $null, $rec, @($i + 1))
        }
        $rows += , $vals
    }
    $view.GetType().InvokeMember('Close', 'InvokeMethod', $null, $view, $null) | Out-Null
    return $rows
}

foreach ($path in $MsiPaths) {
    Write-Host "=== $path ==="
    $installer = New-Object -ComObject WindowsInstaller.Installer
    $db = $installer.GetType().InvokeMember('OpenDatabase', 'InvokeMethod', $null, $installer, @((Resolve-Path $path).Path, 0))

    Write-Host '-- Dialog 表（向导 + 自定义）--'
    Invoke-MsiQuery $db 'SELECT Dialog, Width, Height FROM Dialog ORDER BY Dialog' | ForEach-Object { Write-Host ("  {0}  ({1}x{2})" -f $_[0], $_[1], $_[2]) }

    Write-Host '-- InstallUISequence（关键顺序）--'
    # MSI SQL 不支持 IN，用 OR 链
    $seqSql = "SELECT Action, Sequence FROM InstallUISequence WHERE Action = 'RuntimeMissingDlg' OR Action = 'WebView2MissingDlg' OR Action = 'ClearDataDlg' OR Action = 'WelcomeDlg' OR Action = 'LicenseAgreementDlg' OR Action = 'InstallDirDlg' OR Action = 'VerifyReadyDlg' OR Action = 'ExitDialog' OR Action = 'ExecuteAction' ORDER BY Sequence"
    $seq = Invoke-MsiQuery $db $seqSql
    $seq | ForEach-Object { Write-Host ("  {0} = {1}" -f $_[0], $_[1]) }

    Write-Host '-- 品牌二进制（WixUI 位图 / 图标 / 许可）--'
    Invoke-MsiQuery $db 'SELECT Name FROM Binary' | Where-Object { $_[0] -like 'WixUI*' } | ForEach-Object { Write-Host ("  {0}" -f $_[0]) }

    Write-Host '-- ARP 属性（控制面板）--'
    Invoke-MsiQuery $db 'SELECT Property, Value FROM Property' | Where-Object { $_[0] -like 'ARP*' } | ForEach-Object { Write-Host ("  {0} = {1}" -f $_[0], $_[1]) }

    # 安装选项页 / 中文文案 / 开机自启注册表（仅 Client）
    Write-Host '-- 选项页与链路（Client）--'
    # 直读记录循环（Invoke-MsiQuery 对单行结果的嵌套数组会被 PS 展平，索引会错位）
    $optView = $db.GetType().InvokeMember('OpenView', 'InvokeMethod', $null, $db, @("SELECT Control, Text FROM Control WHERE Dialog_ = 'OptionsDlg' AND Control = 'AutoStartCheck'"))
    $optView.GetType().InvokeMember('Execute', 'InvokeMethod', $null, $optView, $null) | Out-Null
    $optRec = $optView.GetType().InvokeMember('Fetch', 'InvokeMethod', $null, $optView, $null)
    $optView.GetType().InvokeMember('Close', 'InvokeMethod', $null, $optView, $null) | Out-Null
    if ($null -ne $optRec) {
        $checkText = $optRec.GetType().InvokeMember('StringData', 'GetProperty', $null, $optRec, @(2))
        if ($checkText -notlike '*LabelFrame*') { throw '选项页复选框文本异常' }
        Write-Host '  选项页复选框：OK（开机自动启动 LabelFrame）'
        # Event 为 MSI SQL 保留字：全表拉取后客户端过滤
        $chain = Invoke-MsiQuery $db 'SELECT * FROM ControlEvent' |
            Where-Object { $_[0] -eq 'InstallDirDlg' -and $_[1] -eq 'Next' -and $_[3] -eq 'OptionsDlg' }
        if ($chain.Count -eq 0) { throw '向导链路缺失：InstallDirDlg.Next 应跳转 OptionsDlg' }
        Write-Host '  向导链路：目录页 Next -> 选项页 -> 确认页 OK'
        $reg = Invoke-MsiQuery $db 'SELECT * FROM Registry' | Where-Object { $_[2] -like '*CurrentVersion*Run*' }
        if ($reg.Count -gt 0) { Write-Host ('  Run 键：' + $reg[0][3] + ' = ' + $reg[0][4]) } else { throw '开机自启 Run 键缺失' }
        $autoStart = Invoke-MsiQuery $db "SELECT Value FROM Property WHERE Property = 'AUTO_START'"
        Write-Host ('  AUTO_START 默认：' + $autoStart[0][0])
    }
    else { Write-Host '  （无选项页——Server 包跳过本节）' }

    Write-Host '-- 中文文案抽查（WelcomeDlg）--'
    $wv = $db.GetType().InvokeMember('OpenView', 'InvokeMethod', $null, $db, @("SELECT Text FROM Control WHERE Dialog_ = 'WelcomeDlg' AND Control = 'Next'"))
    $wv.GetType().InvokeMember('Execute', 'InvokeMethod', $null, $wv, $null) | Out-Null
    $wr = $wv.GetType().InvokeMember('Fetch', 'InvokeMethod', $null, $wv, $null)
    $wv.GetType().InvokeMember('Close', 'InvokeMethod', $null, $wv, $null) | Out-Null
    $nextText = $wr.GetType().InvokeMember('StringData', 'GetProperty', $null, $wr, @(1))
    Write-Host ('  欢迎页下一步按钮：' + $nextText)
    if ($nextText -notlike '*下一步*') { throw '向导文案未中文化（-culture zh-cn 未生效）' }

    # 关键断言：向导齐全 + 运行时检查在欢迎页之前
    # 取行模式说明：管道过滤后的单行结果是裸行数组，[1] = 该行第 2 列（序号）；
    # 转数值用 [int]::Parse——非数值直接抛错（PS 的 [int] 比较转换失败会静默回退字符串比较，形成空转断言）
    $dialogs = Invoke-MsiQuery $db 'SELECT Dialog FROM Dialog' | ForEach-Object { $_[0] }
    foreach ($required in @('WelcomeDlg', 'LicenseAgreementDlg', 'InstallDirDlg', 'VerifyReadyDlg', 'ExitDialog', 'RuntimeMissingDlg', 'WebView2MissingDlg', 'ClearDataDlg')) {
        if ($required -notin $dialogs) { throw "缺少对话框：$required" }
    }
    $runtimeSeq = ($seq | Where-Object { $_[0] -eq 'RuntimeMissingDlg' })[1]
    $welcomeSeq = ($seq | Where-Object { $_[0] -eq 'WelcomeDlg' })[1]
    if ([int]::Parse($runtimeSeq) -ge [int]::Parse($welcomeSeq)) { throw "RuntimeMissingDlg($runtimeSeq) 应早于 WelcomeDlg($welcomeSeq)" }
    $webview2Seq = ($seq | Where-Object { $_[0] -eq 'WebView2MissingDlg' })[1]
    if ([int]::Parse($webview2Seq) -ge [int]::Parse($welcomeSeq)) { throw "WebView2MissingDlg($webview2Seq) 应早于 WelcomeDlg($welcomeSeq)" }
    if ([int]::Parse($webview2Seq) -le [int]::Parse($runtimeSeq)) { throw "WebView2MissingDlg($webview2Seq) 应晚于 RuntimeMissingDlg($runtimeSeq)（两者都缺时先引导 .NET）" }
    $clearSeq = ($seq | Where-Object { $_[0] -eq 'ClearDataDlg' })[1]
    $execSeq = ($seq | Where-Object { $_[0] -eq 'ExecuteAction' })[1]
    if ([int]::Parse($clearSeq) -ge [int]::Parse($execSeq)) { throw "ClearDataDlg($clearSeq) 应早于 ExecuteAction($execSeq)，否则勾选属性传不进清理动作" }

    # WebView2 运行时检测契约（迭代 44）：双注册表视图 + LaunchCondition 拦截 + 向导内重新检测
    $regLocators = Invoke-MsiQuery $db 'SELECT * FROM RegLocator' | Where-Object { ($_ | Where-Object { "$_" -like '*EdgeUpdate*' }).Count -gt 0 }
    if ($regLocators.Count -lt 2) { throw "WebView2 注册表检测应含 per-machine / per-user 双视图（RegLocator EdgeUpdate 行数 $($regLocators.Count)）" }
    Write-Host "WebView2 注册表检测：$($regLocators.Count) 个 EdgeUpdate 视图 OK"
    $webview2Launch = Invoke-MsiQuery $db 'SELECT Condition, Description FROM LaunchCondition' |
        Where-Object { "$($_[0])" -like '*WEBVIEW2*' }
    if ($webview2Launch.Count -lt 1) { throw '缺少 WebView2 LaunchCondition（静默 / 基础 UI 拦截提示）' }
    Write-Host 'WebView2 LaunchCondition：OK'
    $customActions = Invoke-MsiQuery $db 'SELECT Action FROM CustomAction' | ForEach-Object { $_[0] }
    if ('RecheckWebView2' -notin $customActions) { throw '缺少 RecheckWebView2 自定义动作（重新检测）' }
    $retryChain = Invoke-MsiQuery $db 'SELECT * FROM ControlEvent' |
        Where-Object { $_[0] -eq 'WebView2MissingDlg' -and $_[1] -eq 'RetryButton' -and $_[3] -eq 'RecheckWebView2' }
    if ($retryChain.Count -lt 1) { throw 'WebView2MissingDlg.RetryButton 应触发 RecheckWebView2（装完运行时无需重启安装程序）' }
    Write-Host 'WebView2 缺失对话框：重新检测链路 OK'

    # Client MSI 运行中升级协调（迭代 106 / #229）：安全退出必须早于 InstallValidate，且卸载跳过。
    if ($null -ne $optRec) {
        $updater = @(Invoke-MsiQuery $db 'SELECT Name, ServiceType, StartType, StartName, Arguments, Component_ FROM ServiceInstall' | Where-Object { $_[0] -eq 'LabelFrameClientUpdater' })
        if ($updater.Count -ne 1 -or $updater[0][1] -ne '16' -or $updater[0][2] -ne '2' -or $updater[0][3] -ne 'LocalSystem' -or $updater[0][4] -ne '--update-service') {
            throw 'Client MSI 必须安装唯一的 LocalSystem 自动启动升级服务，独立进程参数为 --update-service'
        }
        $serviceControl = @(Invoke-MsiQuery $db 'SELECT Name, Event, Wait FROM ServiceControl' | Where-Object { $_[0] -eq 'LabelFrameClientUpdater' })
        if ($serviceControl.Count -ne 1 -or $serviceControl[0][1] -ne '163' -or $serviceControl[0][2] -ne '1') {
            throw '升级服务必须安装时启动、升级与卸载时停止、卸载时移除并等待完成'
        }
        $properties = @(Invoke-MsiQuery $db 'SELECT Property, Value FROM Property')
        foreach ($entry in @(@('LABELFRAME_MIN_SERVER_VERSION', '1.0.0'), @('LABELFRAME_MAX_SERVER_VERSION', '2.0.0'), @('LABELFRAME_RUNTIME_MAJOR', '10'))) {
            $property = @($properties | Where-Object { $_[0] -eq $entry[0] })
            if ($property.Count -ne 1 -or $property[0][1] -ne $entry[1]) { throw "升级兼容属性缺失或错误：$($entry[0])" }
        }
        $files = @(Invoke-MsiQuery $db 'SELECT File, FileName, Component_ FROM File')
        $hostFile = @($files | Where-Object { $_[0] -eq 'WinHostExe' })
        if ($hostFile.Count -ne 1 -or $hostFile[0][2] -ne $updater[0][5]) { throw '升级服务必须使用已安装的 WinHost 可执行文件组件' }
        foreach ($requiredFile in @('update-publisher.cer', 'LabelFrame.Updates.dll')) {
            if (@($files | Where-Object { ($_[1] -split '\|')[-1] -eq $requiredFile }).Count -ne 1) { throw "升级文件必须唯一打包：$requiredFile" }
        }
        Write-Host '受控升级：服务身份、启动与卸载行为、兼容属性、发布者证书与组件打包 OK'
        $executeRows = Invoke-MsiQuery $db 'SELECT Action, Condition, Sequence FROM InstallExecuteSequence'
        $shutdownRow = @($executeRows | Where-Object { $_[0] -eq 'ShutdownWinHost' })
        $validateRow = @($executeRows | Where-Object { $_[0] -eq 'InstallValidate' })
        $costRow = @($executeRows | Where-Object { $_[0] -eq 'CostFinalize' })
        if ($shutdownRow.Count -ne 1 -or $validateRow.Count -ne 1 -or $costRow.Count -ne 1) { throw 'Client MSI 缺少唯一的 ShutdownWinHost / CostFinalize / InstallValidate 序列行' }
        $shutdownSeq = [int]::Parse($shutdownRow[0][2])
        if ($shutdownSeq -le [int]::Parse($costRow[0][2]) -or $shutdownSeq -ge [int]::Parse($validateRow[0][2])) {
            throw "ShutdownWinHost($shutdownSeq) 必须位于 CostFinalize 与 InstallValidate 之间"
        }
        if ($shutdownRow[0][1] -notlike '*REMOVE*ALL*') { throw 'ShutdownWinHost 必须在完整卸载时跳过' }
        $shutdownAction = @(Invoke-MsiQuery $db "SELECT Action, Type, Target FROM CustomAction WHERE Action = 'ShutdownWinHost'")
        if ($shutdownAction.Count -ne 1 -or [int]::Parse($shutdownAction[0][1]) -ne 34) { throw 'ShutdownWinHost 必须是同步、失败阻断的立即 EXE 自定义动作' }
        foreach ($required in @('/api/jobs?limit=500', '/api/host/shutdown', 'Printing', '15 秒内安全退出')) {
            if ($shutdownAction[0][2] -notlike "*$required*") { throw "ShutdownWinHost MSI 命令缺少安全退出逻辑：$required" }
        }
        if ($customActions -contains 'KillWinHost') { throw 'Client MSI 不得再用 taskkill 强制结束 WinHost' }

        # 迭代 106 返修（#229）：CA Target 属 MSI Formatted 语义——引擎剥除空花括号对 {}、把 [x] 序列按属性替换
        # （无效属性名置空），非空且无 [属性] 引用的花括号原样保留。曾因空 catch {} 被剥成裸 catch，CA 实际执行的
        # 32 位 powershell.exe（SystemFolder = SysWOW64）解析失败 exit 1 → CA 1722 → 全新安装与覆盖升级中止。
        # 静态防复发：① 表内原文不得含空花括号；② 经真实引擎 Formatted 转换（只读会话 FormatRecord，不安装）
        # 后的内层 PowerShell 必须仍可解析（本脚本类语法与 32 位 / 64 位解析器等价，用宿主 Parser 静态校验）。
        $caTarget = $shutdownAction[0][2]
        if ($caTarget -match '\{\}') { throw 'ShutdownWinHost 命令含空花括号 {}（Formatted 语义剥除花括号对，破坏 PowerShell 语法）' }
        $pkgSession = $installer.GetType().InvokeMember('OpenPackage', 'InvokeMethod', $null, $installer, @($db, 0))
        $fmtRecord = $installer.GetType().InvokeMember('CreateRecord', 'InvokeMethod', $null, $installer, @(1))
        $fmtRecord.GetType().InvokeMember('StringData', 'SetProperty', $null, $fmtRecord, @(0, $caTarget)) | Out-Null
        $formattedTarget = $pkgSession.GetType().InvokeMember('FormatRecord', 'InvokeMethod', $null, $pkgSession, @($fmtRecord))
        $commandMarker = '-Command "'
        $commandIndex = $formattedTarget.IndexOf($commandMarker)
        if ($commandIndex -lt 0) { throw 'ShutdownWinHost 命令缺少 -Command " 前缀，无法提取内层脚本' }
        $innerScript = $formattedTarget.Substring($commandIndex + $commandMarker.Length)
        if ($innerScript.EndsWith('"')) { $innerScript = $innerScript.Substring(0, $innerScript.Length - 1) }
        $parseTokens = $null; $parseErrors = $null
        [System.Management.Automation.Language.Parser]::ParseInput($innerScript, [ref]$parseTokens, [ref]$parseErrors) | Out-Null
        if ($parseErrors.Count -gt 0) {
            $firstError = $parseErrors[0]
            throw "ShutdownWinHost 命令经 Formatted 语义转换后不是可解析 PowerShell（首个错误 offset $($firstError.Extent.StartOffset)：$($firstError.Message)）——检查空花括号 / [x] 方括号序列"
        }
        $selectionEnd = $innerScript.IndexOf('; if (!$p)')
        if ($selectionEnd -lt 0) { throw 'ShutdownWinHost 缺少进程选择后的空进程判断' }
        $selection = [scriptblock]::Create($innerScript.Substring(0, $selectionEnd))
        $serviceProcess = [pscustomobject]@{ ProcessId = 101; CommandLine = '"C:\Program Files\LabelFrame\Client\LabelFrame.WinHost.exe" --update-service' }
        $waiterProcess = [pscustomobject]@{ ProcessId = 102; CommandLine = '"C:\Program Files\LabelFrame\Client\LabelFrame.WinHost.exe" --restart-wait 100 123 event args' }
        $clientProcess = [pscustomobject]@{ ProcessId = 103; CommandLine = '"C:\Program Files\LabelFrame\Client\LabelFrame.WinHost.exe" --autostart' }
        foreach ($case in @(
            @{ Processes = @($serviceProcess, $waiterProcess, $clientProcess); Expected = 103 },
            @{ Processes = @($serviceProcess, $waiterProcess); Expected = $null },
            @{ Processes = @($clientProcess); Expected = 103 }
        )) {
            & {
                param($fixture, $script)
                function Get-CimInstance { param($ClassName, $Filter, $ErrorAction) $fixture.Processes }
                function Get-Process { throw '进程选择必须读取命令行角色，不能只按进程名选择' }
                . $script
                if ($p.ProcessId -ne $fixture.Expected) { throw 'ShutdownWinHost 误选更新服务或重启等待器，或漏选打印客户端' }
            } $case $selection
        }
        Write-Host '运行中升级进程角色：真实 MSI 格式化脚本排除更新服务与等待器，保留打印客户端；无客户端时返回空。'
        Write-Host "运行中升级协调：同步动作 ShutdownWinHost($shutdownSeq) 位于 CostFinalize 后、InstallValidate 前；命令含旧版作业预检与 15 秒退出等待，卸载跳过；命令经引擎 Formatted 转换后仍可解析。"
    }

    Write-Host "断言通过：向导齐全，运行时检查（.NET $runtimeSeq / WebView2 $webview2Seq）先于欢迎页（$welcomeSeq）。"
    Write-Host ''
    [System.Runtime.InteropServices.Marshal]::ReleaseComObject($db) | Out-Null
}
