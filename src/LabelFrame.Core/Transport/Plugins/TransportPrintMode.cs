namespace LabelFrame.Core.Transport.Plugins;

/// <summary>
/// 连接级打印方式参数（printMode）：实现文档编译能力的插件在参数规格中声明 Select 参数
/// （Key printMode、必填、默认 image）；参数值随 connection.json 走，先测试后生效。
/// 宿主读取口径：缺失 / 非法值回退 image（默认值语义）；「缺省回默认」与「显式 native 但能力不在」
/// 是两个分支，后者由保存层校验与提交兜底显式失败（LF_ENC_003），不静默按图片打印。
/// </summary>
public static class TransportPrintMode
{
    /// <summary>参数键（连接参数字典键）。</summary>
    public const string ParameterKey = "printMode";

    /// <summary>图片模式（默认）：整版 Skia 渲染 → ^GF 位图指令。</summary>
    public const string Image = "image";

    /// <summary>原生指令模式：插件文档编译器逐张产出品牌原生指令。</summary>
    public const string Native = "native";

    /// <summary>解析打印方式：native（忽略大小写与首尾空白）以外的一切取值（含缺失 / 空白 / 非法）回退 image。</summary>
    public static string Resolve(string? raw)
        => string.Equals(raw?.Trim(), Native, StringComparison.OrdinalIgnoreCase) ? Native : Image;
}
