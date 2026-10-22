using System.Text.Json.Serialization;

namespace LabelFrame.Core.Layout;

/// <summary>
/// 版式元素的抽象基类，所有元素使用毫米坐标。
/// JSON 序列化统一由 <see cref="LabelElementJsonConverter"/> 按 "type" 判别子类型处理。
/// </summary>
public abstract class LabelElement
{
    /// <summary>左上角 X 坐标（毫米）。</summary>
    public double XMm { get; init; }

    /// <summary>左上角 Y 坐标（毫米）。</summary>
    public double YMm { get; init; }

    /// <summary>元素内边距（毫米，兼容保留；新模板由 <see cref="PaddingHMm"/> / <see cref="PaddingVMm"/> 双边内边距取代单值近似）。</summary>
    public double PaddingMm { get; init; }

    /// <summary>水平内边距（毫米，null = 未设回退 <see cref="PaddingMm"/>，0 = 显式零）。</summary>
    [JsonPropertyName("paddingH")]
    public double? PaddingHMm { get; init; }

    /// <summary>垂直内边距（毫米，null = 未设回退 <see cref="PaddingMm"/>，0 = 显式零）。</summary>
    [JsonPropertyName("paddingV")]
    public double? PaddingVMm { get; init; }

    /// <summary>有效水平内边距（毫米）：未设时回退单值 <see cref="PaddingMm"/>。</summary>
    public double EffectivePaddingHMm => PaddingHMm ?? PaddingMm;

    /// <summary>有效垂直内边距（毫米）：未设时回退单值 <see cref="PaddingMm"/>。</summary>
    public double EffectivePaddingVMm => PaddingVMm ?? PaddingMm;

    /// <summary>元素边框线宽（毫米，0 = 无边框）。</summary>
    public double BorderMm { get; init; }

    /// <summary>锚定的区域（格子）标识；为空表示绝对定位。</summary>
    public string? RegionId { get; init; }

    /// <summary>区域内水平对齐（RegionId 非空且未指定时默认居中）。</summary>
    public LabelRegionAlign? RegionHAlign { get; init; }

    /// <summary>区域内垂直对齐（RegionId 非空且未指定时默认居中）。</summary>
    public LabelRegionAlign? RegionVAlign { get; init; }

    /// <summary>元素类型。</summary>
    public abstract LabelElementType Type { get; }

    /// <summary>字段填充模式的预览值（仅画布 / 测试默认值用，打印以外界数据为准）；固定值模式用 Literal，不写此项。</summary>
    public string? PreviewValue { get; init; }
}
