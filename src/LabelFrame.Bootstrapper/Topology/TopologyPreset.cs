namespace LabelFrame.Bootstrapper.Topology;

/// <summary>本机角色（DESIGN ， + 问卷收敛）：有限枚举 + 仅两项自由开关，不提供任意组件勾选。</summary>
/// <remarks>
/// 问卷面收敛为本机角色三选：基础模式默认「仅打印客户端」不出现角色页
/// server-docker / server-linux 移出问卷（manifest topology 标记保留，Linux 承接 = install.sh、Docker 承接 = Release compose）；
/// <c>offline</c>（离线全量包）是一等形态但不进问卷枚举（布局目录隐式检测），<c>pda</c> 经服务端下载中心分发。
/// </remarks>
public enum TopologyPreset
{
    /// <summary>本机作为服务端 + 同机打印客户端（原「单机一体」）：一台 Windows PC 承载 Server 服务 + Client 同机。</summary>
    Standalone,

    /// <summary>本机作为服务端（Windows 服务，分离部署）；其他机器走「仅打印客户端」接入。</summary>
    ServerWin,

    /// <summary>仅打印客户端：已有服务端的网络追加一台打印 PC（基础模式默认角色）。</summary>
    Client,
}

public static class TopologyPresetExtensions
{
    /// <summary>manifest 拓扑标记 id（topologies 枚举中的预设部分）。</summary>
    public static string ToManifestId(this TopologyPreset preset) => preset switch
    {
        TopologyPreset.Standalone => "standalone",
        TopologyPreset.ServerWin => "server-win",
        TopologyPreset.Client => "client",
        _ => throw new ArgumentOutOfRangeException(nameof(preset), preset, "未知拓扑预设。"),
    };

    /// <summary>角色页展示名（中文单语，REQUIREMENTS §7 边界：i18n 不进 setup 问卷）。</summary>
    public static string DisplayName(this TopologyPreset preset) => preset switch
    {
        TopologyPreset.Standalone => "本机作为服务端，并安装打印客户端",
        TopologyPreset.ServerWin => "本机作为服务端",
        TopologyPreset.Client => "仅打印客户端",
        _ => preset.ToString(),
    };

    /// <summary>角色页描述（一句话说明场景）。</summary>
    public static string Description(this TopologyPreset preset) => preset switch
    {
        TopologyPreset.Standalone => "这台电脑既当服务端又当打印客户端（一套完整系统，客户端自动指向本机）。",
        TopologyPreset.ServerWin => "这台电脑只当服务端，供局域网内其他电脑连接；其他电脑安装打印客户端。",
        TopologyPreset.Client => "这台电脑只装打印客户端，连接局域网内已有的服务端。",
        _ => string.Empty,
    };

    /// <summary>角色是否包含打印客户端（打印机品牌页 / 服务端地址页适用性，/）。</summary>
    public static bool IncludesClient(this TopologyPreset preset) =>
        preset is TopologyPreset.Standalone or TopologyPreset.Client;

    /// <summary>角色是否包含服务端（管理界面开关页适用性，/）。</summary>
    public static bool IncludesServer(this TopologyPreset preset) =>
        preset is TopologyPreset.Standalone or TopologyPreset.ServerWin;
}
