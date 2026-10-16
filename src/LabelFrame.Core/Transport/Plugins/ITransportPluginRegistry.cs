using LabelFrame.Core.Transport;

namespace LabelFrame.Core.Transport.Plugins;

/// <summary>
/// 传输插件注册表：按插件 ID 装配（内置 + 外部 DLL 统一管理），配置指定 pluginId 与参数即启用。
/// </summary>
public interface ITransportPluginRegistry
{
    /// <summary>已装配插件列表（含描述与参数规格）。</summary>
    IReadOnlyList<TransportPluginDescriptor> ListPlugins();

    /// <summary>按 ID 取插件描述（不存在返回 null）。</summary>
    TransportPluginDescriptor? GetPlugin(string id);

    /// <summary>按 ID 取插件的文档编译能力（插件不存在或未实现 ILabelCommandCompiler 返回 null）。</summary>
    ILabelCommandCompiler? GetCommandCompiler(string id);

    /// <summary>按 ID 取插件的图片模式编码能力（插件不存在或未实现 ILabelImageEncoder 返回 null；null 时宿主用默认 ^GF 编码器）。</summary>
    ILabelImageEncoder? GetImageEncoder(string id);

    /// <summary>按 ID + 参数创建传输实例（插件不存在抛异常）。</summary>
    IPrintTransport CreateTransport(string id, TransportPluginParameters parameters, ITransportPluginContext context);

    /// <summary>按 ID + 参数计算连接展示文本（插件不存在回退 ID）。</summary>
    string Describe(string id, TransportPluginParameters parameters);
}
