using System.Text.Json;
using System.Text.Json.Serialization;
using LabelFrame.Core.Transport.Plugins;

namespace LabelFrame.WinHost.Transport;

/// <summary>
/// 连接配置（传输插件化）：pluginId + params 字典（新格式）；旧字段（Mode / TcpHost 等）保留用于
/// 旧 connection.json 兼容反序列化与保存时同步写出（旧前端 / 环境变量回退）。
/// </summary>
public sealed class TransportConfig
{
    /// <summary>当前生效传输插件 ID（如 log / tcp9100 / winspool / zebra / 外部插件）。</summary>
    public string PluginId { get; set; } = "log";

    /// <summary>插件参数字典（键 → 字符串值，按插件参数规格解释）。</summary>
    public Dictionary<string, string> Params { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    // ── 旧字段（0.17 及以前 connection.json 格式；保存时同步写出）──

    /// <summary>旧连接方式（与 PluginId 映射）。</summary>
    public TransportMode Mode { get; set; } = TransportMode.Log;

    /// <summary>TCP 打印机主机 / IP（Tcp 或 Zebra-Tcp 用）。</summary>
    public string TcpHost { get; set; } = string.Empty;

    /// <summary>TCP 端口（默认 9100）。</summary>
    public int TcpPort { get; set; } = 9100;

    /// <summary>Windows 驱动打印机名（WindowsDriver 或 Zebra-Driver 用）。</summary>
    public string PrinterName { get; set; } = string.Empty;

    /// <summary>Zebra 连接类型（Transport=Zebra 时生效）。</summary>
    public ZebraTransportKind ZebraKind { get; set; } = ZebraTransportKind.Tcp;

    /// <summary>Zebra USB 打印机名（为空时自动发现第一台）。</summary>
    public string ZebraUsbName { get; set; } = string.Empty;

    /// <summary>旧连接方式 → 插件 ID 映射（与前端 MODE_TO_PLUGIN_ID 映射保持一致）。</summary>
    public static string MapModeToPluginId(TransportMode mode) => mode switch
    {
        TransportMode.Log => "log",
        TransportMode.Tcp => "tcp9100",
        TransportMode.WindowsDriver => "winspool",
        // Zebra 外置官方插件：连接配置 pluginId 用官方 id
        TransportMode.Zebra => TransportPluginIdPolicy.ZebraPluginId,
        _ => "log",
    };

    /// <summary>插件 ID → 旧连接方式映射（回填 Mode 用）。</summary>
    public static TransportMode MapPluginIdToMode(string pluginId) => pluginId switch
    {
        "tcp9100" => TransportMode.Tcp,
        "winspool" => TransportMode.WindowsDriver,
        // 官方插件 id 与旧内置时代别名都归入 Zebra
        TransportPluginIdPolicy.ZebraPluginId or TransportPluginIdPolicy.LegacyZebraPluginId => TransportMode.Zebra,
        _ => TransportMode.Log,
    };

    /// <summary>把插件字段同步到旧字段（保存 / 兼容消费用）。</summary>
    public void SyncLegacyFields()
    {
        Mode = MapPluginIdToMode(PluginId);
        TcpHost = Params.TryGetValue("host", out var host) ? host : string.Empty;
        if (int.TryParse(Params.TryGetValue("port", out var port) ? port : null, out var portValue))
        {
            TcpPort = portValue;
        }

        PrinterName = Params.TryGetValue("printerName", out var printer) ? printer : string.Empty;
        if (Enum.TryParse<ZebraTransportKind>(Params.TryGetValue("kind", out var kind) ? kind : null, ignoreCase: true, out var zebraKind))
        {
            ZebraKind = zebraKind;
        }

        ZebraUsbName = Params.TryGetValue("usbName", out var usb) ? usb : string.Empty;
    }

    /// <summary>从旧字段构造插件字段（旧 connection.json 兼容）。</summary>
    public void MigrateFromLegacy()
    {
        PluginId = MapModeToPluginId(Mode);
        Params = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        switch (Mode)
        {
            case TransportMode.Tcp:
                Params["host"] = TcpHost;
                Params["port"] = TcpPort.ToString(System.Globalization.CultureInfo.InvariantCulture);
                break;
            case TransportMode.WindowsDriver:
                Params["printerName"] = PrinterName;
                break;
            case TransportMode.Zebra:
                Params["kind"] = ZebraKind.ToString();
                if (ZebraKind == ZebraTransportKind.Tcp)
                {
                    Params["host"] = TcpHost;
                    Params["port"] = TcpPort.ToString(System.Globalization.CultureInfo.InvariantCulture);
                }
                else if (ZebraKind == ZebraTransportKind.Driver)
                {
                    Params["printerName"] = PrinterName;
                }
                else
                {
                    if (!string.IsNullOrWhiteSpace(ZebraUsbName))
                    {
                        Params["usbName"] = ZebraUsbName;
                    }
                }

                break;
            case TransportMode.Log:
            default:
                break;
        }
    }

    /// <summary>序列化为 JSON（connection.json；新格式 pluginId + params，旧字段同步写出）。</summary>
    public string ToJson()
    {
        SyncLegacyFields();
        return JsonSerializer.Serialize(this, JsonOptions);
    }

    /// <summary>从 JSON 反序列化（失败返回 null）。</summary>
    public static TransportConfig? FromJson(string json)
    {
        try
        {
            var config = JsonSerializer.Deserialize<TransportConfig>(json, JsonOptions);
            if (config is null)
            {
                return null;
            }

            // 旧格式（无 PluginId，只有 Mode / 平铺参数；非 Log 必有参数）→ 迁移为 pluginId + params
            if (config.Params.Count == 0 && config.Mode != TransportMode.Log)
            {
                config.MigrateFromLegacy();
            }

            // 旧内置时代 zebra id（≤0.26）读取别名 → 官方插件 id（内存态，不落盘迁移）
            config.PluginId = TransportPluginIdPolicy.NormalizeAlias(config.PluginId);

            config.SyncLegacyFields();
            return config;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };
}
