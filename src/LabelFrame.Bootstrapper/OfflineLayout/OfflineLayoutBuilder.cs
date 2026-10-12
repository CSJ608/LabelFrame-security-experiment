namespace LabelFrame.Bootstrapper.OfflineLayout;

using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using LabelFrame.Bootstrapper.Manifest;
using LabelFrame.Bootstrapper.Prerequisites;

/// <summary>离线布局目录生成异常（fail-closed：任一组件全源失败 / 无法定位引导 EXE 等致命条件）。</summary>
public sealed class OfflineLayoutException : Exception
{
    public OfflineLayoutException(string message)
        : base(message)
    {
    }
}

/// <summary>布局生成进度（BA 进度窗消费；阶段见 <see cref="OfflineLayoutPhase"/>）。</summary>
public sealed record OfflineLayoutProgress(
    OfflineLayoutPhase Phase,
    string ComponentId,
    int ComponentIndex,
    int ComponentCount,
    string? CurrentUrl,
    long BytesReceived,
    long TotalBytes);

/// <summary>布局生成阶段。</summary>
public enum OfflineLayoutPhase
{
    /// <summary>组件文件已存在且哈希与 manifest 一致（幂等复用，零下载）。</summary>
    Reused,

    /// <summary>正在从源下载组件。</summary>
    Downloading,

    /// <summary>正在校验组件哈希。</summary>
    Verifying,

    /// <summary>正在复制引导 EXE / 落盘清单。</summary>
    Finalizing,

    /// <summary>全部完成。</summary>
    Completed,
}

/// <summary>布局生成结果（统计口径，BA 成功提示 / 日志消费）。</summary>
/// <param name="ComponentCount">manifest 组件总数。</param>
/// <param name="DownloadedCount">本次实际下载的组件数。</param>
/// <param name="ReusedCount">已存在且哈希一致被复用的组件数（幂等重生成）。</param>
/// <param name="BootstrapperFileName">复制入目录的引导 EXE 文件名（未提供 EXE 源时为 null）。</param>
public sealed record OfflineLayoutResult(
    int ComponentCount,
    int DownloadedCount,
    int ReusedCount,
    string? BootstrapperFileName);

/// <summary>
/// 离线布局目录生成器（/ ，DESIGN）——把当版全部组件 + 官方 manifest / latest 原样字节 +
/// 引导 EXE 汇集到目标目录；逐组件按 <c>urls</c> 顺序下载、按策略校验一致才落位（fail-closed）：
/// 常规条目 = sha256 与 manifest 逐字节一致；evergreen 条目（version = <c>evergreen</c>）= 微软 Authenticode
/// 发布者验签（——微软轮换直链文件后重新生成不再因哈希漂移失败）。
/// </summary>
/// <remarks>
/// 与 <c>scripts/make-offline-layout.ps1</c>（IT 脚本形态）语义一致：命名约定共用 <see cref="OfflineLayoutNaming"/>；
/// 重复生成幂等（已存在且校验一致跳过下载；不符 = 篡改 / 旧版残留，重新下载）。
/// UI 无关（net48 / net10 双腿，net10 测试锚定——验签器可注入）；BA 仅承担进度呈现与 EXE 源定位（<c>WixBundleOriginalSource</c>）。
/// </remarks>
public static class OfflineLayoutBuilder
{
    /// <summary>生成布局目录。<paramref name="http"/> / <paramref name="authenticodeVerifier"/> 可注入用于测试（注入时归调用方释放）。</summary>
    /// <param name="manifest">已解析的安装清单（组件集合权威）。</param>
    /// <param name="manifestJson">官方 manifest 原样 JSON 文本（原样落盘，不重序列化——与官方发布字节一致）。</param>
    /// <param name="latestJson">官方 latest.json 原样文本（null = 不写入指针文件）。</param>
    /// <param name="targetDirectory">布局目标目录（不存在则创建）。</param>
    /// <param name="bootstrapperSourcePath">引导 EXE 源路径（复制入目录；null = 跳过——脚本形态由调用方自备）。</param>
    /// <param name="http">HTTP 客户端（缺省自建）。</param>
    /// <param name="authenticodeVerifier">Authenticode 验签器（evergreen 条目校验用；缺省 WinVerifyTrust 实现）。</param>
    public static async Task<OfflineLayoutResult> BuildAsync(
        InstallManifest manifest,
        string manifestJson,
        string? latestJson,
        string targetDirectory,
        string? bootstrapperSourcePath,
        HttpClient? http = null,
        IProgress<OfflineLayoutProgress>? progress = null,
        IAuthenticodeVerifier? authenticodeVerifier = null,
        CancellationToken cancellationToken = default)
    {
#if NET10_0_OR_GREATER
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(manifestJson);
        ArgumentNullException.ThrowIfNull(targetDirectory);
#else
        // net48 腿无 ArgumentNullException.ThrowIfNull（.NET 6+ API）
        if (manifest is null)
        {
            throw new ArgumentNullException(nameof(manifest));
        }

        if (manifestJson is null)
        {
            throw new ArgumentNullException(nameof(manifestJson));
        }

        if (targetDirectory is null)
        {
            throw new ArgumentNullException(nameof(targetDirectory));
        }
#endif

        if (string.IsNullOrWhiteSpace(manifestJson))
        {
            throw new OfflineLayoutException("清单内容为空，无法生成布局目录。");
        }

        var verifier = authenticodeVerifier ?? new WinTrustAuthenticodeVerifier();
        if (http is null)
        {
            using var owned = new HttpClient();
            return await BuildCoreAsync(manifest, manifestJson, latestJson, targetDirectory, bootstrapperSourcePath, owned, progress, verifier, cancellationToken).ConfigureAwait(false);
        }

        return await BuildCoreAsync(manifest, manifestJson, latestJson, targetDirectory, bootstrapperSourcePath, http, progress, verifier, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<OfflineLayoutResult> BuildCoreAsync(
        InstallManifest manifest,
        string manifestJson,
        string? latestJson,
        string targetDirectory,
        string? bootstrapperSourcePath,
        HttpClient client,
        IProgress<OfflineLayoutProgress>? progress,
        IAuthenticodeVerifier verifier,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(targetDirectory);

        var total = manifest.Components.Count;
        var downloaded = 0;
        var reused = 0;
        for (var index = 0; index < total; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var component = manifest.Components[index];
            var fileName = OfflineLayoutNaming.TryDeriveFileName(component)
                ?? throw new OfflineLayoutException(
                    $"组件 {component.Id} 无法推导布局文件名（urls[0] 无可辨识文件名且不在固定名兜底表）——布局目录必须收录全部组件，拒绝生成不完整目录。");

            var targetPath = Path.Combine(targetDirectory, fileName);

            // 幂等复用：已存在且按策略校验一致（sha256 / evergreen 发布者验签——生成机重复生成 / 局部续传场景零下载）
            if (File.Exists(targetPath))
            {
                progress?.Report(new OfflineLayoutProgress(OfflineLayoutPhase.Verifying, component.Id, index, total, null, 0, component.SizeBytes));
                if (VerifyComponentByPolicy(targetPath, component, verifier) is null)
                {
                    reused++;
                    progress?.Report(new OfflineLayoutProgress(OfflineLayoutPhase.Reused, component.Id, index, total, null, component.SizeBytes, component.SizeBytes));
                    continue;
                }
            }

            await AcquireComponentAsync(component, targetPath, client, verifier, progress, index, total, cancellationToken).ConfigureAwait(false);
            downloaded++;
        }

        // 清单与指针：官方原样字节落盘（UTF-8 无 BOM，与 generate-install-manifest.ps1 输出一致）
        progress?.Report(new OfflineLayoutProgress(OfflineLayoutPhase.Finalizing, OfflineLayoutNaming.ManifestFileName, total, total, null, 0, 0));
        WriteAllText(Path.Combine(targetDirectory, OfflineLayoutNaming.ManifestFileName), manifestJson);
        if (latestJson is not null)
        {
            WriteAllText(Path.Combine(targetDirectory, OfflineLayoutNaming.LatestFileName), latestJson);
        }

        // 引导 EXE：复制入目录（目标机「拷目录 + 双击 EXE」即离线首装）；运行中的 EXE 可读复制
        string? bootstrapperFileName = null;
        if (!string.IsNullOrWhiteSpace(bootstrapperSourcePath))
        {
            if (!File.Exists(bootstrapperSourcePath))
            {
                throw new OfflineLayoutException($"引导 EXE 不存在：{bootstrapperSourcePath}——布局目录必须内含引导 EXE。");
            }

            bootstrapperFileName = Path.GetFileName(bootstrapperSourcePath);
            File.Copy(bootstrapperSourcePath, Path.Combine(targetDirectory, bootstrapperFileName), overwrite: true);
        }

        progress?.Report(new OfflineLayoutProgress(OfflineLayoutPhase.Completed, string.Empty, total, total, null, 0, 0));
        return new OfflineLayoutResult(total, downloaded, reused, bootstrapperFileName);
    }

    /// <summary>单组件获取：按 urls 顺序尝试（下载到同目录临时文件 → 按策略校验 → 一致才落位）；全源失败 fail-closed。</summary>
    private static async Task AcquireComponentAsync(
        ManifestComponent component,
        string targetPath,
        HttpClient client,
        IAuthenticodeVerifier verifier,
        IProgress<OfflineLayoutProgress>? progress,
        int index,
        int total,
        CancellationToken cancellationToken)
    {
        var tempPath = targetPath + ".download";
        var failures = new List<string>();
        foreach (var url in component.Urls)
        {
            try
            {
                await DownloadToTempAsync(url, tempPath, client, component, progress, index, total, cancellationToken).ConfigureAwait(false);

                progress?.Report(new OfflineLayoutProgress(OfflineLayoutPhase.Verifying, component.Id, index, total, url, component.SizeBytes, component.SizeBytes));
                var verifyFailure = VerifyComponentByPolicy(tempPath, component, verifier);
                if (verifyFailure is not null)
                {
                    failures.Add($"{url} → {verifyFailure}");
                    TryDelete(tempPath);
                    continue;  // 换下一源（源内容被替换 / 损坏——与引擎安装期坏哈希换源同语义）
                }

                if (File.Exists(targetPath))
                {
                    File.Delete(targetPath);
                }

                File.Move(tempPath, targetPath);
                return;
            }
            catch (OperationCanceledException)
            {
                TryDelete(tempPath);
                throw;
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidOperationException)
            {
                TryDelete(tempPath);
                failures.Add($"{url} → {ex.Message}");
            }
        }

        throw new OfflineLayoutException(
            $"组件 {component.Id} 全部 {component.Urls.Count} 个源获取失败（{DescribePolicy(component)}，fail-closed）：{string.Join("；", failures)}");
    }

    /// <summary>evergreen 条目判定（version = <c>evergreen</c>：恒变直链无摘要可钉——校验策略 = 发布者验签）。</summary>
    private static bool IsEvergreenEntry(ManifestComponent component) =>
        string.Equals(component.Version, "evergreen", StringComparison.OrdinalIgnoreCase);

    private static string DescribePolicy(ManifestComponent component) =>
        IsEvergreenEntry(component) ? "微软 Authenticode 发布者验签通过才落位" : "sha256 与 manifest 一致才落位";

    /// <summary>按策略校验组件文件：常规条目 sha256 比对；evergreen 条目 WinVerifyTrust + 发布者 CN 双条件。返回 null = 通过。</summary>
    private static string? VerifyComponentByPolicy(string path, ManifestComponent component, IAuthenticodeVerifier verifier)
    {
        if (!IsEvergreenEntry(component))
        {
            var actual = ComputeSha256(path);
            return string.Equals(actual, component.Sha256, StringComparison.Ordinal)
                ? null
                : $"哈希不符（manifest={component.Sha256.Substring(0, Math.Min(12, component.Sha256.Length))}… 实测={actual.Substring(0, 12)}…）";
        }

        var result = verifier.Verify(path);
        if (!result.SignatureValid)
        {
            return $"验签拒绝：{result.FailureReason ?? "签名无效"}";
        }

        return AuthenticodePublisherPolicy.IsAllowedPublisher(result.SignerSubject, AuthenticodePublisherPolicy.MicrosoftCorporation)
            ? null
            : $"发布者不符：期望 CN={AuthenticodePublisherPolicy.MicrosoftCorporation}，实际主体 = {result.SignerSubject}";
    }

    /// <summary>流式下载到临时文件（HttpCompletionOption.ResponseHeadersRead——HttpClient.Timeout 只约束响应头，大文件主体按流读取）。</summary>
    private static async Task DownloadToTempAsync(
        string url,
        string tempPath,
        HttpClient client,
        ManifestComponent component,
        IProgress<OfflineLayoutProgress>? progress,
        int index,
        int total,
        CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

#if NET10_0_OR_GREATER
        using var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
#else
        // net48 腿无带 CancellationToken 的重载
        using var source = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
#endif
        using var target = File.Create(tempPath);
        var buffer = new byte[81920];
        long received = 0;
        var totalBytes = response.Content.Headers.ContentLength ?? component.SizeBytes;
#if NET10_0_OR_GREATER
        int read;
        while ((read = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false)) > 0)
        {
            await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            received += read;
            progress?.Report(new OfflineLayoutProgress(OfflineLayoutPhase.Downloading, component.Id, index, total, url, received, totalBytes));
        }
#else
        // net48 腿仅数组偏移重载（CA1835 Memory 重载为 .NET Standard 2.1+）
        int readLegacy;
        while ((readLegacy = await source.ReadAsync(buffer, 0, buffer.Length, cancellationToken).ConfigureAwait(false)) > 0)
        {
            await target.WriteAsync(buffer, 0, readLegacy, cancellationToken).ConfigureAwait(false);
            received += readLegacy;
            progress?.Report(new OfflineLayoutProgress(OfflineLayoutPhase.Downloading, component.Id, index, total, url, received, totalBytes));
        }
#endif
    }

    private static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(stream);
        var builder = new StringBuilder(hash.Length * 2);
        foreach (var b in hash)
        {
            builder.Append(b.ToString("x2", System.Globalization.CultureInfo.InvariantCulture));
        }

        return builder.ToString();
    }

    private static void WriteAllText(string path, string content) =>
        // 小文件同步写即可（UTF-8 无 BOM——与官方 generate-install-manifest.ps1 落盘口径一致）
        File.WriteAllText(path, content, new UTF8Encoding(false));

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
            // 临时文件清理失败不阻断（下一次生成同路径 File.Create 覆写）
        }
    }
}
