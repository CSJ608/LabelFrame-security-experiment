namespace LabelFrame.Core.Layout;

/// <summary>
/// 字宽度量接口：按元素字体（字体族 / 字高 / 加粗）测量单行文本的渲染宽度（毫米）。
/// 供版式解析计算区域水平锚定偏移（方案 A）——解析器保持纯几何层
/// 不直接依赖具体渲染库；默认实现见 LabelFrame.Rendering（Skia），AndroidHost 提供平台实现。
/// 毫米口径与渲染 DPI 无关（矢量字体度量线性换算）；未来命令直发打印
/// （打印机内置字体）可提供各自的度量实现复用同一解析几何。
/// </summary>
public interface ITextWidthMeasurer
{
    /// <summary>测量文本按元素字体单行渲染的宽度（毫米）；空文本返回 0。</summary>
    double MeasureWidthMm(LabelTextElement element, string value);
}
