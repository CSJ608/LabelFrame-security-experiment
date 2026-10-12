namespace LabelFrame.Core.Transport.Plugins;

/// <summary>传输实例可选连接测试：切换前「先测试后生效」、打印测试页 / 在线状态共用。</summary>
public interface ITestableTransport
{
    /// <summary>连接测试：返回 null = 成功；否则返回中文错误消息。</summary>
    Task<string?> TestAsync(CancellationToken cancellationToken = default);
}
