using LabelFrame.WinHost.Jobs;

namespace LabelFrame.WinHost.Tests.Jobs;

/// <summary>作业数据留痕格式化单测：单张 / 批量去重 / 混合 / 键序无关 / 截断 / 空数据。</summary>
public class JobDataLogFormatterTests
{
    private static Dictionary<string, string> Data(params (string Key, string Value)[] entries)
        => entries.ToDictionary(e => e.Key, e => e.Value);

    [Fact]
    public void Single_label_should_record_job_id_template_count_and_fields()
    {
        var lines = JobDataLogFormatter.Format("job-1", "库位标签", new List<IReadOnlyDictionary<string, string>?>
        {
            Data(("zone", "A-01"), ("locationCode", "A-01-02-03")),
        });

        var line = Assert.Single(lines);
        Assert.Contains("作业 job-1", line);
        Assert.Contains("模板 库位标签", line);
        Assert.Contains("共 1 张", line);
        Assert.Contains("数据组 1/1", line);
        // 紧凑形式：键按 Ordinal 排序（locationCode < zone）
        Assert.Contains("：locationCode=A-01-02-03; zone=A-01", line);
        // 唯一数据集合不标注重复张数
        Assert.DoesNotContain("重复", line);
    }

    [Fact]
    public void Batch_with_identical_data_should_deduplicate_into_one_line_with_repeat_count()
    {
        var data = Data(("zone", "A-01"), ("locationCode", "A-01-02-03"));
        var lines = JobDataLogFormatter.Format("job-2", "库位标签", [data, data, data]);

        var line = Assert.Single(lines);
        Assert.Contains("共 3 张", line);
        Assert.Contains("数据组 1/1（重复 3 张）", line);
        Assert.Contains("：locationCode=A-01-02-03; zone=A-01", line);
    }

    [Fact]
    public void Mixed_batch_should_record_each_distinct_set_in_first_appearance_order()
    {
        var a = Data(("zone", "A-01"), ("locationCode", "A-01-02-03"));
        var b = Data(("zone", "A-02"), ("locationCode", "A-02-01-01"));
        var c = Data(("zone", "A-03"), ("locationCode", "A-03-01-01"));
        var lines = JobDataLogFormatter.Format("job-3", "库位标签", [a, b, a, c, b]);

        Assert.Equal(3, lines.Count);
        Assert.Contains("共 5 张", lines[0]);
        Assert.Contains("数据组 1/3（重复 2 张）", lines[0]);
        Assert.Contains("zone=A-01", lines[0]);
        Assert.Contains("数据组 2/3（重复 2 张）", lines[1]);
        Assert.Contains("zone=A-02", lines[1]);
        Assert.Contains("数据组 3/3", lines[2]);
        Assert.DoesNotContain("重复", lines[2]);
        Assert.Contains("zone=A-03", lines[2]);
    }

    [Fact]
    public void Dedup_should_treat_different_key_order_as_same_data_set()
    {
        var first = new Dictionary<string, string> { ["a"] = "1", ["b"] = "2" };
        var second = new Dictionary<string, string> { ["b"] = "2", ["a"] = "1" };
        var lines = JobDataLogFormatter.Format("job-4", "t", new List<IReadOnlyDictionary<string, string>?> { first, second });

        var line = Assert.Single(lines);
        Assert.Contains("（重复 2 张）", line);
        // 输出键序恒定（Ordinal），不受传入顺序影响
        Assert.Contains("：a=1; b=2", line);
    }

    [Fact]
    public void Oversized_data_should_truncate_within_limit_and_annotate_total_length()
    {
        var bigValue = new string('A', 5000);
        var lines = JobDataLogFormatter.Format("job-5", "t", new List<IReadOnlyDictionary<string, string>?>
        {
            Data(("big", bigValue)),
        });

        var line = Assert.Single(lines);
        Assert.True(line.Length <= JobDataLogFormatter.MaxLineLength, $"行长度 {line.Length} 超上限 {JobDataLogFormatter.MaxLineLength}");
        Assert.Contains("数据已截断", line);
        // 完整数据文本 = "big=" + 5000 字符 = 5004 字符
        Assert.Contains("完整 5004 字符", line);
        // 截断行仍自包含作业标识与张数
        Assert.Contains("作业 job-5", line);
        Assert.Contains("共 1 张", line);
    }

    [Fact]
    public void Boundary_length_data_should_not_truncate()
    {
        const string jobId = "job-6";
        const string name = "t";
        var prefix = $"模拟打印（Log）作业数据：作业 {jobId}，模板 {name}，共 1 张，数据组 1/1：";
        var valueLength = JobDataLogFormatter.MaxLineLength - prefix.Length - "k=".Length;
        var lines = JobDataLogFormatter.Format(jobId, name, new List<IReadOnlyDictionary<string, string>?>
        {
            Data(("k", new string('x', valueLength))),
        });

        var line = Assert.Single(lines);
        Assert.Equal(JobDataLogFormatter.MaxLineLength, line.Length);
        Assert.DoesNotContain("数据已截断", line);
    }

    [Fact]
    public void Null_or_empty_data_should_deduplicate_into_placeholder()
    {
        var lines = JobDataLogFormatter.Format("job-7", "t", new List<IReadOnlyDictionary<string, string>?>
        {
            null,
            new Dictionary<string, string>(),
        });

        var line = Assert.Single(lines);
        Assert.Contains("（无字段数据）", line);
        // null 与空数据集合语义相同（渲染层同口径），去重合并
        Assert.Contains("（重复 2 张）", line);
    }

    [Fact]
    public void Newline_in_value_should_be_escaped_to_single_line()
    {
        var lines = JobDataLogFormatter.Format("job-8", "t", new List<IReadOnlyDictionary<string, string>?>
        {
            Data(("text", "a\r\nb\rc\nd")),
        });

        var line = Assert.Single(lines);
        Assert.DoesNotContain('\n', line);
        Assert.DoesNotContain('\r', line);
        Assert.Contains("text=a\\nb\\nc\\nd", line);
    }

    [Fact]
    public void Missing_template_name_should_fallback_to_unnamed_placeholder()
    {
        var lines = JobDataLogFormatter.Format("job-9", null, new List<IReadOnlyDictionary<string, string>?>
        {
            Data(("k", "v")),
        });

        var line = Assert.Single(lines);
        Assert.Contains("模板 （未命名）", line);
    }

    [Fact]
    public void Empty_label_list_should_return_no_lines()
    {
        var lines = JobDataLogFormatter.Format("job-10", "t", new List<IReadOnlyDictionary<string, string>?>());

        Assert.Empty(lines);
    }
}
