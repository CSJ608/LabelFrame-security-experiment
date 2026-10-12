namespace LabelFrame.Core.Transport.Plugins;

/// <summary>传输插件上下文：宿主提供日志写入器与数据目录（插件可读写自己的状态文件）。</summary>
public interface ITransportPluginContext
{
    /// <summary>宿主日志写入器（与主机同一 host.log）。</summary>
    TextWriter HostLog { get; }

    /// <summary>宿主数据目录（%LOCALAPPDATA%\LabelFrame；插件状态文件放这里）。</summary>
    string DataDirectory { get; }
}
