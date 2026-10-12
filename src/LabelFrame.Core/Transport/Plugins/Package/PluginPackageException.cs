namespace LabelFrame.Core.Transport.Plugins.Package;

/// <summary>
/// 插件包业务性校验 / 安装失败（消息为中文可行动提示）。
/// 与框架 zip 抛出的 <see cref="InvalidDataException"/>（英文原话）显式区分：API 边界据本类型分流——
/// 本类异常直接透出消息（四类高频：非 zip / zip 损坏 / manifest 缺失或非法 / DLL 无效等，全中文），
/// 其余框架 InvalidDataException 转通用中文提示，不直出英文框架原话。
/// </summary>
public sealed class PluginPackageException : Exception
{
    /// <summary>创建业务性失败异常（中文消息）。</summary>
    public PluginPackageException(string message) : base(message)
    {
    }

    /// <summary>创建业务性失败异常（中文消息 + 原始异常供日志排障）。</summary>
    public PluginPackageException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
