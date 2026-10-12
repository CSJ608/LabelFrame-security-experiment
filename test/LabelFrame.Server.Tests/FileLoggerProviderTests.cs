using LabelFrame.Server;
using Microsoft.Extensions.Logging;

namespace LabelFrame.Server.Tests;

/// <summary>
/// FileLoggerProvider 启动防护与轮转 / 保留测试：路径无效不抛异常（宿主可正常启动）、按日文件写入、
/// 单文件大小超限切序号文件、单日多次轮转总文件数受限、清理按（日期,序号）元组序删最旧（写入中文件不被选中）、
/// 轮转失败回退旧文件、通道停用后写入不抛异常。
/// </summary>
public class FileLoggerProviderTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"lffilelog-{Guid.NewGuid():N}");

    public FileLoggerProviderTests()
    {
        Directory.CreateDirectory(_directory);
    }

    [Fact]
    public void Constructor_with_invalid_path_should_not_throw_and_report_reason()
    {
        // 目录不存在且不可创建（父路径是一个文件）——构造不抛异常、通道禁用、给出原因
        var blocker = Path.Combine(_directory, "blocker.txt");
        File.WriteAllText(blocker, "占位文件：以其为目录必然创建失败");
        var invalidPath = Path.Combine(blocker, "logs", "server.log");

        var provider = new FileLoggerProvider(invalidPath);

        Assert.False(provider.FileChannelEnabled);
        Assert.NotNull(provider.InactiveReason);
    }

    [Fact]
    public void Provider_should_write_to_dated_file()
    {
        var basePath = Path.Combine(_directory, "server.log");
        using var provider = new FileLoggerProvider(basePath);

        Assert.True(provider.FileChannelEnabled);
        var logger = provider.CreateLogger("TestCategory");
        logger.LogInformation("业务事件行：jobId=abc");

        var dated = Path.Combine(_directory, $"server-{DateTime.Now:yyyyMMdd}.log");
        Assert.True(File.Exists(dated), $"按日文件未创建：{dated}");
        Assert.Contains("业务事件行：jobId=abc", ReadShared(dated));
    }

    [Fact]
    public void Startup_should_cleanup_files_over_retention_limit()
    {
        // 构造（启动）即清理超期——保留上限含当日文件（与 Serilog retainedFileCountLimit 口径一致）
        // 6 个按日文件、上限 3 → 删最旧 3 个
        var basePath = Path.Combine(_directory, "server.log");
        foreach (var day in new[] { "20200101", "20200102", "20200103", "20200104", "20200105" })
        {
            File.WriteAllText(Path.Combine(_directory, $"server-{day}.log"), "旧文件");
        }

        using var provider = new FileLoggerProvider(basePath, retainedFileCountLimit: 3);

        Assert.True(provider.FileChannelEnabled);
        Assert.False(File.Exists(Path.Combine(_directory, "server-20200101.log")));
        Assert.False(File.Exists(Path.Combine(_directory, "server-20200102.log")));
        Assert.False(File.Exists(Path.Combine(_directory, "server-20200103.log")));
        Assert.True(File.Exists(Path.Combine(_directory, "server-20200104.log")));
        Assert.True(File.Exists(Path.Combine(_directory, "server-20200105.log")));
        Assert.True(File.Exists(Path.Combine(_directory, $"server-{DateTime.Now:yyyyMMdd}.log")));
    }

    [Fact]
    public void Non_dated_files_should_not_be_touched_by_cleanup()
    {
        // 历史遗留的单名 server.log 与不匹配 <名>-yyyyMMdd 模式的文件不迁移不删除
        var basePath = Path.Combine(_directory, "server.log");
        File.WriteAllText(basePath, "历史单名文件");
        File.WriteAllText(Path.Combine(_directory, "server-备注.log"), "非日期文件");

        using var provider = new FileLoggerProvider(basePath, retainedFileCountLimit: 1);

        Assert.True(File.Exists(basePath));
        Assert.True(File.Exists(Path.Combine(_directory, "server-备注.log")));
    }

    [Fact]
    public void Log_should_not_throw_after_channel_disabled()
    {
        // 运行中写入失败防护：文件句柄已关闭（等价写入失败）后 Log 不抛异常（不打断请求路径），通道自我禁用
        var basePath = Path.Combine(_directory, "server.log");
        var provider = new FileLoggerProvider(basePath);
        var logger = provider.CreateLogger("TestCategory");
        provider.Dispose();

        logger.LogInformation("通道已停用后的写入");

        Assert.False(provider.FileChannelEnabled);
    }

    [Fact]
    public void Size_rotation_should_switch_to_sequence_file_and_seal_old_file()
    {
        // 单文件超上限切当日序号文件继续写（服务不中断）；已封存的旧文件不再增长
        var basePath = Path.Combine(_directory, "server.log");
        using var provider = new FileLoggerProvider(basePath, retainedFileCountLimit: 31, maxFileSizeBytes: 4 * 1024);
        var logger = provider.CreateLogger("TestCategory");
        var dated = Path.Combine(_directory, $"server-{DateTime.Now:yyyyMMdd}.log");
        var first = Path.Combine(_directory, $"server-{DateTime.Now:yyyyMMdd}.1.log");
        var second = Path.Combine(_directory, $"server-{DateTime.Now:yyyyMMdd}.2.log");

        var i = 0;
        while (!File.Exists(first) && i < 100)
        {
            logger.LogInformation("大小轮转压力行 {Index}：{Pad}", i, new string('x', 60));
            i++;
        }

        Assert.True(File.Exists(first), $"超限未切序号文件：{first}");
        var sealedLength = SharedLength(dated);

        while (!File.Exists(second) && i < 200)
        {
            logger.LogInformation("大小轮转压力行 {Index}：{Pad}", i, new string('x', 60));
            i++;
        }

        Assert.True(File.Exists(second), $"序号文件再次超限未继续切：{second}");
        for (var tail = 0; tail < 5; tail++)
        {
            logger.LogInformation("尾批行 {Index}", tail);
        }

        Assert.True(provider.FileChannelEnabled);
        Assert.Equal(sealedLength, SharedLength(dated)); // 旧文件封存不再增长
        Assert.Contains("大小轮转压力行 0", ReadShared(dated)); // 首行落在基文件
        Assert.Contains("尾批行 4", ReadShared(second)); // 最新写入落在最新序号文件
    }

    [Fact]
    public void Multiple_size_rotations_within_one_day_should_stay_within_file_count_limit()
    {
        // 大小轮转分支同样触发清理——单日内多次超限轮转后文件总数不超个数上限
        // 写入中文件按（日期,序号）元组序总在队尾，清理不会删到它（通道保持可用、末行可落盘）
        var basePath = Path.Combine(_directory, "server.log");
        using var provider = new FileLoggerProvider(basePath, retainedFileCountLimit: 3, maxFileSizeBytes: 512);
        var logger = provider.CreateLogger("TestCategory");
        for (var i = 0; i < 80; i++)
        {
            logger.LogInformation("连续轮转压力行 {Index}：{Pad}", i, new string('y', 60));
        }

        var matching = Directory.GetFiles(_directory, "server-*.log");
        Assert.True(matching.Length <= 3, $"文件总数超出个数上限：{matching.Length} —— {string.Join(", ", matching)}");
        Assert.True(provider.FileChannelEnabled, "清理误删了写入中文件（通道被自我禁用）");
        Assert.Contains(matching, file => ReadShared(file).Contains("连续轮转压力行 79")); // 末行仍写入成功
    }

    [Fact]
    public void Cleanup_should_delete_oldest_by_date_then_sequence_and_keep_current_file()
    {
        // 清理按（日期, 序号〔基名=0〕）元组序删最旧，替代文件名 Ordinal 串序——
        // Ordinal 序下 ".10" 排在 ".2" 之前、基名文件（当日最旧）排在全部序号文件之后；
        // 6 个文件上限 3 → 删 (20200101,0)、(20200102,0)、(20200102,2)，保留 (20200102,10)、(20200103,0) 与写入中当日文件
        var basePath = Path.Combine(_directory, "server.log");
        foreach (var name in new[]
        {
            "server-20200101.log",
            "server-20200102.log",
            "server-20200102.2.log",
            "server-20200102.10.log",
            "server-20200103.log",
        })
        {
            File.WriteAllText(Path.Combine(_directory, name), "旧文件");
        }

        using var provider = new FileLoggerProvider(basePath, retainedFileCountLimit: 3);
        var logger = provider.CreateLogger("TestCategory");
        logger.LogInformation("写入中文件行");

        Assert.False(File.Exists(Path.Combine(_directory, "server-20200101.log")));
        Assert.False(File.Exists(Path.Combine(_directory, "server-20200102.log")));
        Assert.False(File.Exists(Path.Combine(_directory, "server-20200102.2.log")));
        Assert.True(File.Exists(Path.Combine(_directory, "server-20200102.10.log"))); // 序号按数值序（2 < 10）而非串序
        Assert.True(File.Exists(Path.Combine(_directory, "server-20200103.log")));
        var today = Path.Combine(_directory, $"server-{DateTime.Now:yyyyMMdd}.log");
        Assert.True(File.Exists(today), "写入中文件被清理选中");
        Assert.Contains("写入中文件行", ReadShared(today));
    }

    [Fact]
    public void Size_rotation_open_failure_should_fall_back_to_old_file_and_keep_channel()
    {
        // 大小轮转开新文件失败沿用「退回旧文件继续写、下次再试」防护——轮转失败 ≠ 写入失败，通道不自我禁用
        var basePath = Path.Combine(_directory, "server.log");
        // 预占当日序号文件路径（目录占位使开新文件必然失败）
        Directory.CreateDirectory(Path.Combine(_directory, $"server-{DateTime.Now:yyyyMMdd}.1.log"));
        using var provider = new FileLoggerProvider(basePath, retainedFileCountLimit: 31, maxFileSizeBytes: 512);
        var logger = provider.CreateLogger("TestCategory");
        for (var i = 0; i < 40; i++)
        {
            logger.LogInformation("轮转失败回退行 {Index}：{Pad}", i, new string('z', 60));
        }

        Assert.True(provider.FileChannelEnabled, "轮转失败不应自我禁用通道");
        var dated = Path.Combine(_directory, $"server-{DateTime.Now:yyyyMMdd}.log");
        Assert.Contains("轮转失败回退行 0", ReadShared(dated));
        Assert.Contains("轮转失败回退行 39", ReadShared(dated)); // 超限后全部行退回基文件继续写
    }

    /// <summary>以兼容写入句柄的共享方式读取（写入器仍持写句柄时 File.ReadAllText 会因共享冲突失败）。</summary>
    private static string ReadShared(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>以共享方式读取文件字节长度（写入器仍持写句柄时可用；封存断言用字节口径）。</summary>
    private static long SharedLength(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        return stream.Length;
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // 句柄延迟释放时忽略清理失败
        }
    }
}
