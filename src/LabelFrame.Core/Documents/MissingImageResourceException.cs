namespace LabelFrame.Core.Documents;

/// <summary>
/// 图片元素引用的资源缺失（模板库无此键且请求未附带）——缺图按打印侧可达性判定显式报错，
/// 不静默灰占位照打；供 Skia（Server / WinHost）与 Android 渲染器共用。
/// </summary>
public sealed class MissingImageResourceException : Exception
{
    /// <summary>缺失的图片资源键。</summary>
    public string ImageKey { get; }

    /// <summary>创建缺资源异常（消息含键名，供端点 400 / 作业失败直接透出）。</summary>
    public MissingImageResourceException(string imageKey)
        : base($"模板图片资源缺失：{imageKey}。")
    {
        ImageKey = imageKey;
    }
}
