namespace LabelFrame.AndroidHost.Transport.Plugins;

/// <summary>
/// Core 插件组件 TextWriter 日志桥：写入行转发 <see cref="HostLog"/>（logcat + 本地滚动文件，tag = Plugin）——
/// Core 的加载器 / 安装器 / 插件上下文共用同一宿主日志通道（与 WinHost host.log 同构）。
/// </summary>
public sealed class HostLogWriter : TextWriter
{
    /// <inheritdoc />
    public override System.Text.Encoding Encoding => System.Text.Encoding.UTF8;

    /// <inheritdoc />
    public override void WriteLine(string? value) => HostLog.Info(HostLog.Tags.Plugin, value ?? string.Empty);
}
