using Serilog;
using Serilog.Events;

namespace LabelFrame.WinHost;

/// <summary>
/// Serilog 文件日志装配。
/// 根因：装配抽取后 UseSerilog 曾留在 Main 里只用于配置绑定、从不 Build 的 builder 上，
/// 配置从未挂到运行宿主。修复约定：本方法是文件日志唯一的装配入口，
/// 只接受 <see cref="WinHostApp.BuildAsync"/> 的 configureBuilder 传入的**真实 builder**——
/// 挂到任何其他 builder 上都不会生效，回归测试以此结构性锚定。
/// </summary>
public static class SerilogSetup
{
    /// <summary>
    /// 在真实应用 builder 上启用 Serilog 文件日志 + SelfLog 失败可见化。
    /// 文件名 app-.log + RollingInterval.Day → Serilog 自动追加日期后缀（app-20260910.log；
    /// 模板里写 {Date} 是字面量不会被替换）。级别来自 options.LogLevel（LABELFRAME_LOG_LEVEL 优先）。
    /// </summary>
    public static void Apply(WebApplicationBuilder builder, HostOptions options)
    {
        try
        {
            Directory.CreateDirectory(options.AppLogDirectory);
        }
        catch (Exception)
        {
            // 目录创建失败不阻断启动：sink 首写失败会经 SelfLog 可见化
        }

        // SelfLog 写独立文件（serilog-self.log）：文件通道自身失效时原因仍可查（的失败可见化）
        try
        {
            var selfLogPath = Path.Combine(options.AppLogDirectory, "serilog-self.log");
            var selfLogWriter = new StreamWriter(
                new FileStream(selfLogPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
            {
                AutoFlush = true,
            };
            Serilog.Debugging.SelfLog.Enable(TextWriter.Synchronized(selfLogWriter));
        }
        catch (Exception)
        {
            // SelfLog 自身不可用时放弃可见化，不影响启动
        }

        builder.Host.UseSerilog((_, loggerConfig) => loggerConfig
            .MinimumLevel.Is(ParseLevel(options.LogLevel))
            .WriteTo.File(
                Path.Combine(options.AppLogDirectory, "app-.log"),
                formatProvider: System.Globalization.CultureInfo.InvariantCulture,
                rollingInterval: RollingInterval.Day,
                // 保留上限：按日轮转下文件数即天数，默认 31；0 或负值 = 不清理（retainedFileCountLimit 传 null）
                retainedFileCountLimit: options.AppLogRetentionDays > 0 ? options.AppLogRetentionDays : null,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}"));
    }

    /// <summary>解析级别文本（Trace/Debug/Information/Warning/Error/Critical/Fatal，大小写不敏感；非法值回退 Information）。</summary>
    public static LogEventLevel ParseLevel(string? text)
    {
        return Enum.TryParse<LogEventLevel>(text?.Trim(), ignoreCase: true, out var level)
            ? level
            : LogEventLevel.Information;
    }
}
