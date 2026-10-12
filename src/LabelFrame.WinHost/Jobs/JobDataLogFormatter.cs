namespace LabelFrame.WinHost.Jobs;

/// <summary>
/// 模拟打印作业数据留痕格式化（纯函数）：多张标签按数据集合去重（相同集合记一条＋重复张数），
/// 单条上限 2048 字符超限截断并标注完整长度。去重键＝紧凑文本（键按 Ordinal 排序）；
/// 分隔符「; 」/「=」未转义，极端值理论可碰撞（仅影响重复张数统计）。
/// </summary>
public static class JobDataLogFormatter
{
    /// <summary>单条留痕日志长度上限（字符，≈2KB；含前缀与截断标注，超限截断数据文本）。</summary>
    public const int MaxLineLength = 2048;

    private const string EmptyDataText = "（无字段数据）";
    private const string UnnamedTemplateText = "（未命名）";

    /// <summary>
    /// 格式化作业数据留痕日志行：每个去重后的数据集合一行，每行自包含作业 ID / 模板名 / 张数 / 数据组序号。
    /// </summary>
    /// <param name="jobId">作业 ID。</param>
    /// <param name="templateName">模板名称（null / 空白回退「未命名」占位）。</param>
    /// <param name="labels">各张标签的字段数据（null 数据集合按空数据占位处理；空列表返回空集合）。</param>
    /// <returns>日志行列表（每个去重数据集合一条）。</returns>
    public static IReadOnlyList<string> Format(
        string jobId,
        string? templateName,
        IReadOnlyList<IReadOnlyDictionary<string, string>?>? labels)
    {
        ArgumentNullException.ThrowIfNull(jobId);

        if (labels is null || labels.Count == 0)
        {
            return Array.Empty<string>();
        }

        var name = string.IsNullOrWhiteSpace(templateName) ? UnnamedTemplateText : templateName;

        // 去重：紧凑文本相等即同一数据集合，合并计数并保留首次出现顺序
        var order = new List<string>();
        var counts = new Dictionary<string, int>();
        foreach (var label in labels)
        {
            var text = FormatData(label);
            if (counts.TryGetValue(text, out var count))
            {
                counts[text] = count + 1;
            }
            else
            {
                counts[text] = 1;
                order.Add(text);
            }
        }

        var lines = new List<string>(order.Count);
        for (var i = 0; i < order.Count; i++)
        {
            var dataText = order[i];
            lines.Add(FormatLine(jobId, name, labels.Count, i + 1, order.Count, counts[dataText], dataText));
        }

        return lines;
    }

    /// <summary>单张数据集合的紧凑文本：键按 Ordinal 排序，「k=v」以「; 」连接；值内换行转义为 \n。</summary>
    private static string FormatData(IReadOnlyDictionary<string, string>? data)
    {
        if (data is null || data.Count == 0)
        {
            return EmptyDataText;
        }

        static string Escape(string value)
            => value.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Replace("\n", "\\n", StringComparison.Ordinal);

        return string.Join("; ", data.Keys
            .OrderBy(key => key, StringComparer.Ordinal)
            .Select(key => $"{key}={Escape(data[key])}"));
    }

    /// <summary>拼装单条留痕行；前缀保持完整，数据文本超预算时截断并标注完整长度。</summary>
    private static string FormatLine(string jobId, string name, int total, int seq, int groups, int count, string dataText)
    {
        // 重复张数仅在实际重复（>1）时标注，单张 / 唯一数据集合保持简洁
        var repeat = count > 1 ? $"（重复 {count} 张）" : string.Empty;
        var prefix = $"模拟打印（Log）作业数据：作业 {jobId}，模板 {name}，共 {total} 张，数据组 {seq}/{groups}{repeat}：";

        if (prefix.Length + dataText.Length <= MaxLineLength)
        {
            return prefix + dataText;
        }

        // 超限（≈2KB）：截断数据文本并标注完整长度（防异常大数据刷爆日志）
        var annotation = $"…（数据已截断，完整 {dataText.Length} 字符）";
        var budget = Math.Max(0, MaxLineLength - prefix.Length - annotation.Length);
        return prefix + dataText[..budget] + annotation;
    }
}
