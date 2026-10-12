using LabelFrame.WinHost;

namespace LabelFrame.WinHost.Tests;

/// <summary>
/// host.log 按日轮转写入器测试：
/// 写入落到按日文件 host-yyyyMMdd.log（不再写单名 host.log）、启动即清理超期、
/// 历史单名 / 非日期文件不动、停用后写入不抛异常。
/// </summary>
public class DailyRotatingFileWriterTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"lfhostlog-{Guid.NewGuid():N}");

    public DailyRotatingFileWriterTests()
    {
        Directory.CreateDirectory(_directory);
    }

    [Fact]
    public void WriteLine_should_write_to_dated_file()
    {
        var basePath = Path.Combine(_directory, "host.log");
        using (var writer = new DailyRotatingFileWriter(basePath))
        {
            writer.WriteLine("[2026-09-11 12:00:00] LabelFrame 启动");
        }

        Assert.False(File.Exists(basePath), "不应再写单名 host.log（历史文件不动，新写入走按日文件）");
        var dated = Path.Combine(_directory, $"host-{DateTime.Now:yyyyMMdd}.log");
        Assert.True(File.Exists(dated), $"按日文件未创建：{dated}");
        Assert.Contains("LabelFrame 启动", ReadShared(dated));
    }

    [Fact]
    public void Startup_should_cleanup_files_over_retention_limit()
    {
        // 保留上限含当日文件（与 Serilog retainedFileCountLimit 口径一致）：6 个按日文件、上限 3 → 删最旧 3 个
        var basePath = Path.Combine(_directory, "host.log");
        foreach (var day in new[] { "20200101", "20200102", "20200103", "20200104", "20200105" })
        {
            File.WriteAllText(Path.Combine(_directory, $"host-{day}.log"), "旧文件");
        }

        using var writer = new DailyRotatingFileWriter(basePath, retainedFileCountLimit: 3);

        Assert.False(File.Exists(Path.Combine(_directory, "host-20200101.log")));
        Assert.False(File.Exists(Path.Combine(_directory, "host-20200102.log")));
        Assert.False(File.Exists(Path.Combine(_directory, "host-20200103.log")));
        Assert.True(File.Exists(Path.Combine(_directory, "host-20200104.log")));
        Assert.True(File.Exists(Path.Combine(_directory, "host-20200105.log")));
        Assert.True(File.Exists(Path.Combine(_directory, $"host-{DateTime.Now:yyyyMMdd}.log")));
    }

    [Fact]
    public void Non_dated_files_should_not_be_touched_by_cleanup()
    {
        var basePath = Path.Combine(_directory, "host.log");
        File.WriteAllText(basePath, "历史单名文件");
        File.WriteAllText(Path.Combine(_directory, "host-备份.log"), "非日期文件");

        using var writer = new DailyRotatingFileWriter(basePath, retainedFileCountLimit: 1);

        Assert.True(File.Exists(basePath));
        Assert.True(File.Exists(Path.Combine(_directory, "host-备份.log")));
    }

    [Fact]
    public void WriteLine_should_not_throw_after_dispose()
    {
        // 停用（等价写入失败）后写入不抛异常——宿主日志失效不影响业务路径
        var basePath = Path.Combine(_directory, "host.log");
        var writer = new DailyRotatingFileWriter(basePath);
        writer.Dispose();

        writer.WriteLine("通道停用后的写入");
        writer.Flush();
    }

    /// <summary>以兼容写入句柄的共享方式读取（写入器仍持写句柄时 File.ReadAllText 会因共享冲突失败）。</summary>
    private static string ReadShared(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
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
