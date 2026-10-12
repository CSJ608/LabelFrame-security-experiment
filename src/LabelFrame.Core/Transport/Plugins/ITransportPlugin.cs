using LabelFrame.Core.Transport;

namespace LabelFrame.Core.Transport.Plugins;

/// <summary>
/// 传输插件：描述 + 参数模型 + 工厂。第三方插件 DLL 实现并导出此接口；
/// 返回的传输实例为 <see cref="IPrintTransport"/>（发送）+ 可选 <see cref="IPrinterStatusProvider"/>（状态）
/// + 可选 <see cref="ITestableTransport"/>（连接测试）。
/// </summary>
public interface ITransportPlugin
{
    /// <summary>稳定插件 ID（如 "log" / "tcp9100" / "winspool" / "zebra" / 厂商自定义）。</summary>
    string Id { get; }

    /// <summary>中文展示名（如 "TCP 9100"）。</summary>
    string DisplayName { get; }

    /// <summary>一句中文说明。</summary>
    string Description { get; }

    /// <summary>参数规格（前端动态表单 / 后端校验）。</summary>
    IReadOnlyList<TransportParameterSpec> Parameters { get; }

    /// <summary>连接展示文本（状态栏 / 徽标），如「TCP 192.168.1.50:9100」。</summary>
    string Describe(TransportPluginParameters parameters);

    /// <summary>按参数创建传输实例。</summary>
    IPrintTransport Create(TransportPluginParameters parameters, ITransportPluginContext context);
}
