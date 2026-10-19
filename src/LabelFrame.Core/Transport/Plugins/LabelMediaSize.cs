namespace LabelFrame.Core.Transport.Plugins;

/// <summary>
/// 连接级标签纸尺寸键约定：插件参数表声明「标签纸宽度 / 高度（毫米）」，宿主测试打印按此渲染版面——
/// TSPL 固件严格按 SIZE 走纸（与实体一致才出完整一张；ZPL 固件打完走到下一物理间隙、不依赖本参数）。
/// 键缺失 / 非整数 / 超出合理范围时回退默认 40×20；宽高两维独立解析。
/// </summary>
public static class LabelMediaSize
{
    /// <summary>宽度参数键（毫米整数，如 100）。</summary>
    public const string WidthParameterKey = "labelWidthMm";

    /// <summary>高度参数键（毫米整数，如 80）。</summary>
    public const string HeightParameterKey = "labelHeightMm";

    /// <summary>缺省测试页宽度（毫米）。</summary>
    public const double DefaultWidthMm = 40;

    /// <summary>缺省测试页高度（毫米）。</summary>
    public const double DefaultHeightMm = 20;

    /// <summary>合理范围下限（毫米，含端点）——低于视为无效配置。</summary>
    private const int MinMm = 10;

    /// <summary>合理范围上限（毫米，含端点）——高于视为无效配置。</summary>
    private const int MaxMm = 500;

    /// <summary>从连接参数解析标签纸尺寸。</summary>
    public static (double WidthMm, double HeightMm) Resolve(IReadOnlyDictionary<string, string> parameters)
        => (ResolveDimension(parameters, WidthParameterKey, DefaultWidthMm), ResolveDimension(parameters, HeightParameterKey, DefaultHeightMm));

    private static double ResolveDimension(IReadOnlyDictionary<string, string> parameters, string key, double fallback)
        => parameters.TryGetValue(key, out var raw)
            && int.TryParse(raw.Trim(), out var value)
            && value is >= MinMm and <= MaxMm
                ? value
                : fallback;
}
