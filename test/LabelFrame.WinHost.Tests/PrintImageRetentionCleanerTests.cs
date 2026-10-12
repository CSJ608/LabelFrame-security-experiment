using LabelFrame.Api;
using LabelFrame.Core.Encoding;
using LabelFrame.Core.Jobs;
using LabelFrame.Core.Templates;
using LabelFrame.Rendering;
using LabelFrame.WinHost.Jobs;
using LabelFrame.WinHost.Tests.Samples;
using LabelFrame.WinHost.Tests.Transport;
using LabelFrame.WinHost.Transport;

namespace LabelFrame.WinHost.Tests;

/// <summary>
/// 出图目录保留清理测试：超期删除 / 保留期内不删 / 关闭语义（≤0）/ 清理失败降级留痕（含打印主链路不受影响）。
/// </summary>
public class PrintImageRetentionCleanerTests
{
    [Fact]
    public void Expired_job_directory_should_be_deleted_with_png_and_summary_logged()
    {
        var root = CreateTempRoot();
        try
        {
            var expired = CreateJobDirectory(root, "job-expired", daysOld: 40);
            var retained = CreateJobDirectory(root, "job-today", daysOld: 0);
            var log = new StringWriter();

            new PrintImageRetentionCleaner(root, retentionDays: 31, log).CleanupExpired();

            // 超期作业目录连同内部 PNG 删除；当日作业保留
            Assert.False(Directory.Exists(expired));
            Assert.False(File.Exists(Path.Combine(expired, "label-1.png")));
            Assert.True(Directory.Exists(retained));
            // 清理摘要一行留痕
            Assert.Contains("出图目录清理：删除超期作业目录 1 个", log.ToString());
        }
        finally
        {
            TryCleanup(root);
        }
    }

    [Fact]
    public void Job_directories_within_retention_should_not_be_deleted()
    {
        var root = CreateTempRoot();
        try
        {
            var recent = CreateJobDirectory(root, "job-recent", daysOld: 5);
            var today = CreateJobDirectory(root, "job-today", daysOld: 0);
            var log = new StringWriter();

            new PrintImageRetentionCleaner(root, retentionDays: 31, log).CleanupExpired();

            Assert.True(Directory.Exists(recent));
            Assert.True(File.Exists(Path.Combine(recent, "label-1.png")));
            Assert.True(Directory.Exists(today));
            // 无删除即无摘要行
            Assert.DoesNotContain("出图目录清理", log.ToString());
        }
        finally
        {
            TryCleanup(root);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Non_positive_retention_days_should_disable_cleanup(int retentionDays)
    {
        var root = CreateTempRoot();
        try
        {
            var expired = CreateJobDirectory(root, "job-expired", daysOld: 40);
            var log = new StringWriter();

            new PrintImageRetentionCleaner(root, retentionDays, log).CleanupExpired();

            // ≤0 = 不清理（关闭语义）
            Assert.True(Directory.Exists(expired));
            Assert.True(File.Exists(Path.Combine(expired, "label-1.png")));
            Assert.DoesNotContain("出图目录清理", log.ToString());
        }
        finally
        {
            TryCleanup(root);
        }
    }

    [Fact]
    public void Cleanup_failure_should_be_logged_without_throwing()
    {
        var root = CreateTempRoot();
        try
        {
            var expired = CreateJobDirectory(root, "job-locked", daysOld: 40);
            var log = new StringWriter();
            var cleaner = new PrintImageRetentionCleaner(root, retentionDays: 31, log);

            // 目录内文件被占用（独占句柄）→ 递归删除失败（场景：占用 / 权限失败）
            using (var hold = new FileStream(
                Path.Combine(expired, "label-1.png"), FileMode.Open, FileAccess.Read, FileShare.None))
            {
                cleaner.CleanupExpired();
            }

            // 不抛出、目录留存（下次触发再试）、失败留痕一行
            Assert.True(Directory.Exists(expired));
            Assert.Contains("删除超期作业目录失败", log.ToString());
        }
        finally
        {
            TryCleanup(root);
        }
    }

    /// <summary>
    /// 清理失败不影响打印与出图主链路：提交服务挂接的清理器遇被占用超期目录时
    /// 模拟打印仍成功、当日出图正常落盘、不抛异常。
    /// </summary>
    [Fact]
    public async Task Log_print_submission_should_succeed_when_cleanup_fails()
    {
        var root = CreateTempRoot();
        var dbPath = Path.Combine(Path.GetTempPath(), $"lfhost-{Guid.NewGuid():N}.db");
        var templatesDb = Path.Combine(Path.GetTempPath(), $"lftpl-{Guid.NewGuid():N}.db");
        try
        {
            var expired = CreateJobDirectory(root, "job-locked", daysOld: 40);
            var log = new StringWriter();

            var store = new SqliteLabelJobStore(dbPath);
            await store.InitializeAsync();
            var templates = new TemplateStore(templatesDb);
            await templates.InitializeAsync();
            var transportManager = TestTransportRegistry.CreateManager(new HostOptions { Transport = TransportMode.Log });
            var service = new JobSubmissionService(
                new LabelJobQueue(store),
                new ZplImageEncoder(),
                dpi: 203,
                new SkiaLabelRenderer(),
                templates,
                transportManager,
                TestTransportRegistry.Create(),
                TestTransportRegistry.CreateContext(),
                log,
                root,
                new PrintImageRetentionCleaner(root, retentionDays: 31, log));

            using (var hold = new FileStream(
                Path.Combine(expired, "label-1.png"), FileMode.Open, FileAccess.Read, FileShare.None))
            {
                var request = new SubmitJobRequest(
                    "req-cleanup-failure",
                    new TemplateDto(LocationLabelSamples.Contract, LocationLabelSamples.Layout),
                    [new LabelDto(new Dictionary<string, string> { ["zone"] = "A-01", ["locationCode"] = "A-01-02-03" })]);
                var result = await service.SubmitAsync(request);

                // 主链路不受影响：作业创建成功、PNG 正常出图
                Assert.NotNull(result.Job);
                Assert.True(result.Created);
                var image = Path.Combine(root, result.Job!.Id, "label-1.png");
                Assert.True(File.Exists(image), $"出图未落盘：{image}");
            }

            // 清理失败已留痕（不抛出）
            Assert.Contains("删除超期作业目录失败", log.ToString());
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            TryDelete(dbPath);
            TryDelete(templatesDb);
            TryCleanup(root);
        }
    }

    private static string CreateTempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), $"lfprint-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        return root;
    }

    /// <summary>造一个作业子目录（含一张 PNG），LastWriteTime 回拨到指定天数前。</summary>
    private static string CreateJobDirectory(string root, string jobId, int daysOld)
    {
        var dir = Path.Combine(root, jobId);
        Directory.CreateDirectory(dir);
        File.WriteAllBytes(Path.Combine(dir, "label-1.png"), [0x89, 0x50, 0x4E, 0x47]);
        if (daysOld > 0)
        {
            Directory.SetLastWriteTime(dir, DateTime.Now.AddDays(-daysOld));
        }

        return dir;
    }

    private static void TryCleanup(string directory)
    {
        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch (IOException)
        {
            // 句柄延迟释放时忽略清理失败
        }
        catch (UnauthorizedAccessException)
        {
            // 忽略清理失败
        }
    }

    private static void TryDelete(string file)
    {
        try
        {
            File.Delete(file);
        }
        catch (IOException)
        {
            // 忽略清理失败
        }
    }
}
