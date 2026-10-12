namespace LabelFrame.Core.Layout;

/// <summary>元素内容取值：优先固定值（Literal），否则按 SourceKey 从数据取；两者皆无返回空。各消费方共用保证一致。</summary>
public static class LabelElementContent
{
    /// <summary>取元素显示内容。</summary>
    public static string Get(LabelElement element, IReadOnlyDictionary<string, string> data)
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentNullException.ThrowIfNull(data);

        var (literal, sourceKey) = element switch
        {
            LabelTextElement text => (text.Literal, text.SourceKey),
            LabelBarcodeElement barcode => (barcode.Literal, barcode.SourceKey),
            LabelQrCodeElement qrCode => (qrCode.Literal, qrCode.SourceKey),
            _ => (null, null),
        };

        if (!string.IsNullOrEmpty(literal))
        {
            return literal;
        }

        if (string.IsNullOrEmpty(sourceKey))
        {
            return string.Empty;
        }

        if (!data.TryGetValue(sourceKey, out var value))
        {
            throw new ArgumentException($"标签文档缺少字段数据：{sourceKey}。", nameof(data));
        }

        return value;
    }

    /// <summary>容错取值：缺数据时返回 false（预览用）。</summary>
    public static bool TryGet(LabelElement element, IReadOnlyDictionary<string, string> data, out string value)
    {
        try
        {
            value = Get(element, data);
            return true;
        }
        catch (ArgumentException)
        {
            value = string.Empty;
            return false;
        }
    }
}