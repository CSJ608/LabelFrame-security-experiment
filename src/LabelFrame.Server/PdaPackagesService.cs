namespace LabelFrame.Server;

/// <summary>PDA（Android 宿主）安装包视图（GET /api/pda-packages 列表项）。</summary>
public sealed record PdaPackageView(string FileName, long SizeBytes, DateTimeOffset ModifiedAt, string Url);

/// <summary>
/// PDA（Android 宿主）安装包目录服务（与 <see cref="ClientPackagesService"/> 模式对称）：
/// 服务端统一分发 PDA 的 APK——目录直放文件与页面上传都支持；文件名一律拒绝路径分隔符 / .. / 非法字符（路径穿越防护），
/// 只允许普通文件名（无子目录）。**页面上传仅接受 .apk 扩展名**（目录直放不限制，列表照常列出——服务端不对文件内容做格式断言）。
/// </summary>
public sealed class PdaPackagesService : FilePackageService<PdaPackageView>
{
    /// <summary>APK 下载响应的 MIME 类型（Android 浏览器据此识别为安装包并拉起安装流程）。</summary>
    public const string ApkContentType = "application/vnd.android.package-archive";

    /// <summary>创建服务（目录不存在自动创建）。</summary>
    public PdaPackagesService(string directory)
        : base(directory, nameof(directory))
    {
    }

    /// <summary>保存上传的安装包（仅接受 .apk；文件名路径穿越防护；覆盖同名文件）。</summary>
    public async Task<PdaPackageView> SaveAsync(string? fileName, Stream content, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        var path = ResolveSafePath(fileName)
            ?? throw new InvalidOperationException("文件名无效（只允许普通文件名，不允许路径 / 特殊字符）。");
        if (!string.Equals(Path.GetExtension(path), ".apk", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("PDA 安装包仅接受 .apk 文件（Android 宿主安装包），请勿上传其他格式。");
        }

        await using (var stream = File.Create(path))
        {
            await content.CopyToAsync(stream, cancellationToken);
        }

        return ToView(path);
    }

    protected override PdaPackageView ToView(string path)
    {
        var info = new FileInfo(path);
        return new PdaPackageView(
            info.Name,
            info.Length,
            info.LastWriteTimeUtc,
            $"/api/pda-packages/{Uri.EscapeDataString(info.Name)}");
    }
}
