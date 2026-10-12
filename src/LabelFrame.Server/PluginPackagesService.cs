using LabelFrame.Core.IO;
using LabelFrame.Core.Transport.Plugins;
using LabelFrame.Core.Transport.Plugins.Package;

namespace LabelFrame.Server;

/// <summary>插件包视图（GET /api/plugin-packages 列表项；invalid 条目元数据字段缺失，仅文件信息有效；platforms 为增量可选字段——/ 跨端标记，未标记 = Windows 端既有包）。</summary>
public sealed record PluginPackageView(
    string FileName,
    string? PluginId,
    string? Name,
    string? Version,
    string? Description,
    long SizeBytes,
    DateTimeOffset ModifiedAt,
    string Url,
    bool Valid,
    string? InvalidReason,
    IReadOnlyList<string>? Platforms = null);

/// <summary>
/// 服务端插件包目录服务：独立 plugin-packages 目录 + /api/plugin-packages——
/// 上传 / 列表时只读 zip 根 manifest.json 展示插件元数据（不解压不加载）；zip / manifest 解析失败 → valid:false + 原因，仍列出便于管理删除；
/// 文件名一律拒绝路径分隔符 / .. / 非法字符（路径穿越防护，共享 Core <see cref="SafeFileName"/>）。
/// </summary>
public sealed class PluginPackagesService : FilePackageService<PluginPackageView>
{
    /// <summary>创建服务（目录不存在自动创建）。</summary>
    public PluginPackagesService(string directory)
        : base(directory, nameof(directory))
    {
    }

    /// <summary>
    /// 保存上传的插件包：先读入内存（64MB 上限）并校验 zip + 根 manifest + 必填字段（zip-slip），
    /// 校验通过才落盘（覆盖同名文件）；非法包直接拒绝（400），不留下 invalid 文件。
    /// </summary>
    public async Task<PluginPackageView> SaveAsync(string? fileName, Stream content, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        var path = ResolveSafePath(fileName)
            ?? throw new PluginPackageException("文件名无效（只允许普通文件名，不允许路径 / 特殊字符）。");

        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken);
        if (buffer.Length == 0)
        {
            throw new PluginPackageException("插件包为空。");
        }

        if (buffer.Length > PluginPackageLimits.MaxBytes)
        {
            throw new PluginPackageException($"插件包超过大小上限（{PluginPackageLimits.Display}）。");
        }

        var bytes = buffer.ToArray();
        var packageContent = PluginPackageReader.Read(bytes); // 非法抛 PluginPackageException（中文原因）

        // 内置传输保留 id 拒绝（⑤，DESIGN）：log / tcp9100 / winspool 为内置插件 id
        // 上传此类包会让客户端安装时与内置冲突——与客户端安装侧「注册表内置即拒绝」互为纵深；
        // 官方插件 id（labelframe- 前缀，如 labelframe-transport-zebra）放行，官方插件经本通道集中分发。
        if (TransportPluginIdPolicy.IsReservedBuiltin(packageContent.Manifest.PluginId))
        {
            throw new PluginPackageException($"插件 ID「{packageContent.Manifest.PluginId}」与内置传输插件冲突，禁止上传。");
        }

        await using (var stream = File.Create(path))
        {
            await stream.WriteAsync(bytes, cancellationToken);
        }

        return ToView(path);
    }

    protected override PluginPackageView ToView(string path)
    {
        var info = new FileInfo(path);
        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(path);
        }
        catch (Exception ex)
        {
            return InvalidView(path, info, $"读取失败：{ex.Message}");
        }

        if (PluginPackageReader.TryRead(bytes, out var content, out var error))
        {
            return new PluginPackageView(
                info.Name,
                content!.Manifest.PluginId,
                content!.Manifest.Name,
                content!.Manifest.Version,
                content!.Manifest.Description,
                info.Length,
                info.LastWriteTimeUtc,
                $"/api/plugin-packages/{Uri.EscapeDataString(info.Name)}",
                Valid: true,
                InvalidReason: null,
                content.Manifest.Platforms);
        }

        return InvalidView(path, info, error ?? "插件包无效。");
    }

    private static PluginPackageView InvalidView(string path, FileInfo info, string reason)
        => new(
            info.Name,
            null,
            null,
            null,
            null,
            info.Length,
            info.LastWriteTimeUtc,
            $"/api/plugin-packages/{Uri.EscapeDataString(info.Name)}",
            Valid: false,
            reason);
}
