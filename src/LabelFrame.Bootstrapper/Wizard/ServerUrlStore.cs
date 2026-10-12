using System.Text.Json;

namespace LabelFrame.Bootstrapper.Wizard;

/// <summary>客户端服务端地址存取：与 WinHost <c>HostConfigStore</c> 同一事实源——<c>%ProgramData%\LabelFrame\Client\settings.json</c> 的 <c>{"serverUrl": …}</c>。
/// 问卷侧只读（<see cref="WizardSession"/> 只读契约：探测已配地址用于预填）；装后写入归 BA（Apply 成功后调用 <see cref="Save"/>）。</summary>
public sealed class ServerUrlStore
{
    /// <summary>默认存取路径（%ProgramData%\LabelFrame\Client\settings.json，与 WinHost HostConfigStore 一致）。</summary>
    public static string DefaultPath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "LabelFrame", "Client", "settings.json");

    private readonly string _path;

    public ServerUrlStore(string? path = null)
    {
        _path = string.IsNullOrWhiteSpace(path) ? DefaultPath() : path!;
    }

    /// <summary>读取已配置的服务端地址；文件缺失 / 损坏返回 null（问卷回退空预填或本机默认）。</summary>
    public string? Load()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return null;
            }

            using var stream = File.OpenRead(_path);
            using var doc = JsonDocument.Parse(stream);
            return doc.RootElement.TryGetProperty("serverUrl", out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null; // 读不到 = 无预填，不阻断问卷
        }
    }

    /// <summary>写入服务端地址（先建目录再原子替换，目录 / 文件 ACL 与 WinHost 运行期写入同一语义：ProgramData 下用户可创建）。</summary>
    public void Save(string serverUrl)
    {
        var directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temp = _path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(new { serverUrl }), new System.Text.UTF8Encoding(false));
        if (File.Exists(_path))
        {
            File.Delete(_path); // net48 腿无 File.Move(overwrite) 重载：先删后移（写临时文件仍保证内容完整）
        }

        File.Move(temp, _path);
    }
}

/// <summary>服务端地址问卷输入规范化：裸主机名 / host:port 补 http:// 前缀；空值或非法输入返回 null。</summary>
public static class ServerUrlInput
{
    /// <summary>同机服务端默认地址（服务端 + 同机客户端角色预填，可改）。</summary>
    public const string LocalServerDefault = "http://127.0.0.1:53961";

    /// <summary>规范化用户输入：去空白 → 无 scheme 补 http:// → 按 http/https 绝对 URL 校验（其他 scheme 如 ftp:// 直接拒绝）；不可用返回 null。</summary>
    public static string? Normalize(string? input)
    {
        var trimmed = (input ?? string.Empty).Trim();
        if (trimmed.Length == 0)
        {
            return null;
        }

        var hasScheme = trimmed.Contains("://", StringComparison.Ordinal);
        var candidate = hasScheme
            ? trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
              || trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                ? trimmed
                : null // 其他 scheme（ftp:// 等）非服务端地址形态
            : "http://" + trimmed;
        if (candidate is null)
        {
            return null;
        }

        return Uri.TryCreate(candidate, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
            && !string.IsNullOrWhiteSpace(uri.Host)
            ? candidate
            : null;
    }
}
