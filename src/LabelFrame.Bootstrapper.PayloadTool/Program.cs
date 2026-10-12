using System.Diagnostics;
using System.Text;
using LabelFrame.Bootstrapper.Placement;
using LabelFrame.Bootstrapper.Prerequisites;

namespace LabelFrame.Bootstrapper.PayloadTool;

/// <summary>
/// 非 MSI 组件落位工具入口：管理界面 zip → <c>plugins\web-ui</c>；<c>.lfplugin</c> → <c>plugins\&lt;pluginId&gt;\</c>。
/// 卸载清理 <c>-clean</c> 与落位动作同目标对称；evergreen 安装 <c>-install-webview2</c> = 获取 → 发布者验签 → 静默安装；
/// <c>-verify</c> = 同源解析与验签但不执行（诊断 / 走查载体）。中文输出写入 Burn 包日志；退出码契约见工程文件注释。
/// </summary>
internal static class Program
{
    private const int ExitSuccess = 0;
    private const int ExitRefusedDowngrade = 20;
    private const int ExitInvalidArchive = 30;
    private const int ExitSignatureRejected = 40;
    private const int ExitSourceExhausted = 41;
    private const int ExitUnexpected = 90;

    private static int Main(string[] args)
    {
        // Burn 以重定向管道捕获输出：显式 UTF-8，中文消息不乱码
        try
        {
            Console.OutputEncoding = Encoding.UTF8;
        }
        catch (IOException)
        {
            // 无控制台句柄时忽略（不影响落位）
        }

        try
        {
            return Run(args);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"落位失败（未预期）：{ex}");
            return ExitUnexpected;
        }
    }

    private static int Run(string[] args)
    {
        if (args.Length == 0)
        {
            // 无参数 = 空操作成功（Burn 对无 UninstallArguments 包的缺省卸载调用形态——安全兜底，不可改为报错）
            Console.WriteLine("未指定落位参数，空操作退出。");
            return ExitSuccess;
        }

        string? mode = null;
        string? archive = null;
        string? target = null;
        string? version = null;
        string? source = null;
        string? publisher = null;
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "-place":
                case "-plugin":
                case "-clean":
                case "-install-webview2":
                case "-verify":
                    mode = args[i];
                    break;
                case "-archive" when i + 1 < args.Length:
                    archive = args[++i];
                    break;
                case "-target" when i + 1 < args.Length:
                    target = args[++i];
                    break;
                case "-version" when i + 1 < args.Length:
                    version = args[++i];
                    break;
                case "-source" when i + 1 < args.Length:
                    source = args[++i];
                    break;
                case "-publisher" when i + 1 < args.Length:
                    publisher = args[++i];
                    break;
                default:
                    Console.Error.WriteLine($"未知参数：{args[i]}");
                    Console.WriteLine(Usage);
                    return ExitUnexpected;
            }
        }

        // evergreen 安装 / 验签模式：独立参数契约（-source 可缺省走官方 fwlink 兜底）
        if (mode is "-install-webview2" or "-verify")
        {
            return RunEvergreenInstall(source, publisher, execute: mode == "-install-webview2");
        }

        // -clean 只需 -target（卸载会话无包文件参与）；落位模式还需 -archive
        if (mode is null || string.IsNullOrWhiteSpace(target) || (mode != "-clean" && string.IsNullOrWhiteSpace(archive)))
        {
            Console.Error.WriteLine("参数不完整：需要 -place/-plugin + -archive + -target，或 -clean + -target。");
            Console.WriteLine(Usage);
            return ExitUnexpected;
        }

        // 伴生包以相对文件名授权（Burn 将包与 Payload 缓存于同一目录）；同时容忍绝对路径（本地自验）
        var archivePath = archive is null
            ? null
            : Path.IsPathRooted(archive)
                ? archive
                : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, archive);

        try
        {
            if (mode == "-clean")
            {
                // 卸载清理：与落位同目标对称；目录缺失 = 幂等成功，删除失败 = 尽力而为不阻断卸载主链
                var cleanup = PayloadPlacer.CleanPlacement(target!);
                switch (cleanup)
                {
                    case PlacementCleanupOutcome.Cleaned:
                        Console.WriteLine($"已清理落位目录：{target}");
                        return ExitSuccess;
                    case PlacementCleanupOutcome.AlreadyAbsent:
                        Console.WriteLine($"落位目录不存在，视为已清理：{target}");
                        return ExitSuccess;
                    case PlacementCleanupOutcome.FailedBestEffort:
                        Console.Error.WriteLine($"落位目录清理失败（尽力而为，不阻断卸载）：{target}");
                        return ExitSuccess;
                    default:
                        Console.Error.WriteLine($"未知清理结果：{cleanup}");
                        return ExitUnexpected;
                }
            }

            if (mode == "-place")
            {
                PayloadPlacer.PlaceArchive(archivePath!, target!, version);
                Console.WriteLine($"已落位：{archive} -> {target}");
                return ExitSuccess;
            }

            var outcome = PayloadPlacer.PlacePlugin(archivePath!, target!, version);
            switch (outcome)
            {
                case PlacementOutcome.Placed:
                    Console.WriteLine($"已安装插件：{archive} -> {target}");
                    return ExitSuccess;
                case PlacementOutcome.SkippedSameVersion:
                    Console.WriteLine($"插件版本相同，幂等跳过（未重复解压）：{target}");
                    return ExitSuccess;
                case PlacementOutcome.RefusedDowngrade:
                    Console.Error.WriteLine($"拒绝降级安装：目标目录已是更新版本，请先卸载再安装（{target}）。");
                    return ExitRefusedDowngrade;
                default:
                    Console.Error.WriteLine($"未知落位结果：{outcome}");
                    return ExitUnexpected;
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or InvalidDataException)
        {
            // 包损坏 / 契约不符（非 zip、缺 manifest.json、缺 version 等）：明确中文原因
            Console.Error.WriteLine($"落位包无效：{ex.Message}");
            return ExitInvalidArchive;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            Console.Error.WriteLine($"落位文件系统错误：{ex.Message}");
            return ExitUnexpected;
        }
    }

    /// <summary>
    /// evergreen 安装 / 验签模式：源解析（本地布局文件优先 → urls 逐源；空 = 官方 fwlink 兜底）→
    /// 微软 Authenticode 发布者验签（WinVerifyTrust + 签名者 CN 双条件，fail-closed）→（<c>-install-webview2</c>）静默安装并透传退出码。
    /// 退出码：0 = 验签通过（-verify 到此为止）/ 安装成功；40 = 验签拒绝（签名无效或发布者不符）；41 = 全源获取失败；其余 = 安装器退出码透传。
    /// </summary>
    private static int RunEvergreenInstall(string? source, string? publisher, bool execute)
    {
        var resolved = EvergreenRuntime.ResolveSource(source);
        if (resolved.IsEmpty)
        {
            resolved = EvergreenRuntime.ResolveSource(EvergreenRuntime.WebView2DefaultDownloadUrl);
            Console.WriteLine($"未提供源，按官方 evergreen 直链兜底：{EvergreenRuntime.WebView2DefaultDownloadUrl}");
        }

        var expectedPublisher = string.IsNullOrWhiteSpace(publisher)
            ? AuthenticodePublisherPolicy.MicrosoftCorporation
            : publisher!;
        var acquirer = new EvergreenPayloadAcquirer(new WinTrustAuthenticodeVerifier(), expectedPublisher);
        var downloadPath = Path.Combine(
            Path.GetTempPath(), "labelframe-evergreen-" + Guid.NewGuid().ToString("N") + ".exe");

        var acquired = acquirer.Acquire(resolved, downloadPath);

        if (acquired.Outcome == EvergreenAcquireOutcome.Failed)
        {
            Console.Error.WriteLine("evergreen 载荷获取失败（全部源已尝试，fail-closed 不装不明文件）：");
            foreach (var failure in acquired.Failures)
            {
                Console.Error.WriteLine($"  - {failure}");
            }

            TryDelete(downloadPath);
            return acquired.AnySignatureRejected ? ExitSignatureRejected : ExitSourceExhausted;
        }

        Console.WriteLine(
            $"验签通过：{(acquired.FromLocalSource ? "本地布局文件" : "下载副本")} {acquired.VerifiedPath}");
        Console.WriteLine($"签名者主体：{acquired.SignerSubject}");
        if (acquired.Failures.Count > 0)
        {
            Console.WriteLine("换源留痕（此前失败的源）：");
            foreach (var failure in acquired.Failures)
            {
                Console.WriteLine($"  - {failure}");
            }
        }

        if (!execute)
        {
            Console.WriteLine("verify 模式：不执行安装（诊断 / 走查载体，决策 #151）。");
            if (!acquired.FromLocalSource)
            {
                TryDelete(downloadPath);
            }

            return ExitSuccess;
        }

        try
        {
            var startInfo = new ProcessStartInfo(acquired.VerifiedPath!, EvergreenRuntime.WebView2InstallArguments)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("无法启动 evergreen 安装器进程。");
            process.WaitForExit();
            Console.WriteLine($"evergreen 安装器退出码：{process.ExitCode}");
            return process.ExitCode;
        }
        finally
        {
            if (!acquired.FromLocalSource)
            {
                TryDelete(downloadPath);
            }
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
            // 临时文件清理失败不阻断（%TEMP% 兜底由系统清理）
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private const string Usage =
        "用法：LabelFrame.Bootstrapper.PayloadTool.exe -place|-plugin -archive <包文件名> -target <目标目录> [-version <Bundle 版本>]（-place = 覆盖解压；-plugin = 按 #123 版本比较落位；-version = 写落位凭据，决策 #133）\n"
        + "卸载清理：LabelFrame.Bootstrapper.PayloadTool.exe -clean -target <目标目录>（与落位同目标对称，决策 #133）\n"
        + "evergreen 安装（决策 #151）：-install-webview2 [-source <本地路径|URL;URL…>]（源缺省 = 官方 fwlink；验签失败不装）\n"
        + "evergreen 验签诊断：-verify [-source <…>] [-publisher <CN>]（同源解析与验签，不执行安装）\n"
        + "无参数 = 空操作成功（清理包 install 形态——登记包已装，卸载语义由 Burn 规划）";
}
