using System.Text.Json;
using System.Text.Json.Serialization;

namespace LabelFrame.Bootstrapper.Manifest;

/// <summary>安装清单（install manifest，DESIGN）——由 CI 随发版生成并作为 Release 附件发布，引导程序只读解析。</summary>
public sealed record InstallManifest(
    int SchemaVersion,
    string LabelframeVersion,
    string GeneratedAt,
    IReadOnlyList<ManifestComponent> Components)
{
    /// <summary>引导程序支持的 schema 版本上限（fail-closed：遇到更照新清单拒绝并提示先升级引导程序， 演进规则）。</summary>
    public const int SupportedSchemaVersion = 1;

    /// <summary>schema 允许的组件 type 枚举（组件条目表）。</summary>
    public static readonly IReadOnlyCollection<string> AllowedTypes = new HashSet<string>(
        ["msi", "lfplugin", "webui-zip", "apk", "runtime", "archive"], StringComparer.Ordinal);

    /// <summary>schema 允许的拓扑标记枚举（预设 id + offline / pda 补充语义）。</summary>
    public static readonly IReadOnlyCollection<string> AllowedTopologies = new HashSet<string>(
        ["standalone", "server-win", "server-docker", "server-linux", "client", "offline", "pda"], StringComparer.Ordinal);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        // 兼容 演进规则：新增可选字段 = 兼容（旧引导程序忽略未知字段）
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip,
    };

    /// <summary>解析并校验清单 JSON（字段完整性与枚举合法性；任何不符抛 <see cref="InstallManifestFormatException"/>，fail-closed）。</summary>
    public static InstallManifest Parse(string json)
    {
#if NET10_0_OR_GREATER
        ArgumentNullException.ThrowIfNull(json);
#else
        // net48 腿无 ArgumentNullException.ThrowIfNull（.NET 6+ API）
        if (json is null)
        {
            throw new ArgumentNullException(nameof(json));
        }
#endif

        InstallManifest manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<InstallManifest>(json, JsonOptions)
                ?? throw new InstallManifestFormatException("清单内容为空（null JSON）。");
        }
        catch (JsonException ex)
        {
            throw new InstallManifestFormatException($"清单不是合法 JSON：{ex.Message}", ex);
        }

        if (manifest.SchemaVersion > SupportedSchemaVersion)
        {
            // fail-closed：schema 超出支持范围即拒绝，提示先升级引导程序（演进规则）
            throw new InstallManifestFormatException(
                $"清单 schema 版本过新（{manifest.SchemaVersion}，本引导程序支持至 {SupportedSchemaVersion}），请先升级引导程序。");
        }

        if (manifest.SchemaVersion < 1)
        {
            throw new InstallManifestFormatException($"清单 schemaVersion 非法：{manifest.SchemaVersion}。");
        }

        if (string.IsNullOrWhiteSpace(manifest.LabelframeVersion))
        {
            throw new InstallManifestFormatException("清单缺顶层必填字段 labelframeVersion。");
        }

        if (string.IsNullOrWhiteSpace(manifest.GeneratedAt))
        {
            throw new InstallManifestFormatException("清单缺顶层必填字段 generatedAt。");
        }

        if (manifest.Components is not { Count: > 0 })
        {
            throw new InstallManifestFormatException("清单 components 为空数组。");
        }

        var seenIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var component in manifest.Components)
        {
            ValidateComponent(component, seenIds);
        }

        foreach (var component in manifest.Components)
        {
            foreach (var dependency in component.DependsOn)
            {
                if (!seenIds.Contains(dependency))
                {
                    throw new InstallManifestFormatException(
                        $"[{component.Id}] dependsOn 引用不存在的组件：{dependency}（依赖只约束同集合内组件的安装顺序）。");
                }
            }
        }

        return manifest;
    }

    private static void ValidateComponent(ManifestComponent component, HashSet<string> seenIds)
    {
        var id = string.IsNullOrWhiteSpace(component.Id) ? "<无 id>" : component.Id;

        if (string.IsNullOrWhiteSpace(component.Id))
        {
            throw new InstallManifestFormatException("组件条目缺必填字段 id。");
        }

        if (!seenIds.Add(component.Id))
        {
            throw new InstallManifestFormatException($"组件 id 重复：{component.Id}。");
        }

        if (string.IsNullOrWhiteSpace(component.Type))
        {
            throw new InstallManifestFormatException($"[{id}] 缺必填字段 type。");
        }

        if (!AllowedTypes.Contains(component.Type))
        {
            throw new InstallManifestFormatException($"[{id}] type 非法：{component.Type}（允许：{string.Join(" / ", AllowedTypes)}）。");
        }

        if (string.IsNullOrWhiteSpace(component.Version))
        {
            throw new InstallManifestFormatException($"[{id}] 缺必填字段 version。");
        }

        if (component.Urls is not { Count: > 0 })
        {
            throw new InstallManifestFormatException($"[{id}] 缺必填字段 urls 或为空数组（多源数组首期至少含主源）。");
        }

        if (string.IsNullOrWhiteSpace(component.Sha256) || component.Sha256.Length != 64 || !component.Sha256.All(IsLowerHex))
        {
            throw new InstallManifestFormatException($"[{id}] sha256 非法：应为 64 位小写十六进制字符。");
        }

        if (component.SizeBytes <= 0)
        {
            throw new InstallManifestFormatException($"[{id}] sizeBytes 应为正整数，实际：{component.SizeBytes}。");
        }

        if (component.Topologies is not { Count: > 0 })
        {
            throw new InstallManifestFormatException($"[{id}] 缺必填字段 topologies 或为空数组。");
        }

        foreach (var topology in component.Topologies)
        {
            if (!AllowedTopologies.Contains(topology))
            {
                throw new InstallManifestFormatException($"[{id}] topologies 含未知预设 id：{topology}（允许：{string.Join(" / ", AllowedTopologies)}）。");
            }
        }
    }

    private static bool IsLowerHex(char c) => c is (>= '0' and <= '9') or (>= 'a' and <= 'f');
}

/// <summary>清单组件条目（组件条目表；字段名与 CI 生成的 camelCase JSON 一致）。</summary>
public sealed record ManifestComponent(
    string Id,
    string Type,
    string Version,
    IReadOnlyList<string> DependsOn,
    IReadOnlyList<string> Urls,
    string Sha256,
    long SizeBytes,
    string? SilentArgs,
    IReadOnlyList<string> Topologies,
    string? Notes);
