namespace LabelFrame.Core.Layout;

/// <summary>
/// 字宽度量接口：按元素字体测量单行文本的渲染宽度（毫米，与渲染 DPI 无关——矢量度量线性换算）。
/// 解析器保持纯几何层不直接依赖渲染库（度量与绘制须同源，否则锚定偏移与实际绘制错位）。
/// </summary>
public interface ITextWidthMeasurer
{
    /// <summary>测量文本按元素字体单行渲染的宽度（毫米）；空文本返回 0。</summary>
    double MeasureWidthMm(LabelTextElement element, string value);
}
