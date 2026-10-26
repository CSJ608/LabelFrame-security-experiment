using Microsoft.Extensions.Time.Testing;

namespace LabelFrame.WinHost.Tests;

public sealed class PrintImageLifecycleTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"lf-images-{Guid.NewGuid():N}");
    private readonly FakeTimeProvider _clock = new(DateTimeOffset.UtcNow);

    [Fact]
    public void Capacity_should_remove_oldest_even_with_time_cleanup_disabled()
    {
        var oldest = Create(800000, 2);
        var newest = Create(800000, 1);
        new PrintImageRetentionCleaner(_root, 0, TextWriter.Null, 1, _clock).CleanupExpired();
        Assert.False(Directory.Exists(oldest));
        Assert.True(Directory.Exists(newest));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Both_rules_disabled_should_preserve_all(int disabled)
    {
        var dir = Create(1100000, 90);
        new PrintImageRetentionCleaner(_root, disabled, TextWriter.Null, disabled, _clock).CleanupExpired();
        Assert.True(Directory.Exists(dir));
    }

    [Fact]
    public void Time_rule_should_work_without_capacity_rule_and_keep_boundary()
    {
        var expired = Create(10, 32);
        var boundary = Create(10, 31);
        new PrintImageRetentionCleaner(_root, 31, TextWriter.Null, 0, _clock).CleanupExpired();
        Assert.False(Directory.Exists(expired));
        Assert.True(Directory.Exists(boundary));
    }

    [Fact]
    public void Submission_scans_should_be_throttled_but_forced_scans_should_run()
    {
        var cleaner = new PrintImageRetentionCleaner(_root, 1, TextWriter.Null, 0, _clock);
        cleaner.CleanupExpired(throttle: true);
        var expired = Create(10, 2);
        cleaner.CleanupExpired(throttle: true);
        Assert.True(Directory.Exists(expired));
        _clock.Advance(TimeSpan.FromMinutes(5));
        cleaner.CleanupExpired(throttle: true);
        Assert.False(Directory.Exists(expired));
        expired = Create(10, 2);
        cleaner.CleanupExpired();
        Assert.False(Directory.Exists(expired));
    }

    [Theory]
    [InlineData("unmarked")]
    [InlineData("bad-marker")]
    [InlineData("foreign-file")]
    [InlineData("nested")]
    [InlineData("bad-name")]
    public void Unowned_or_mixed_directories_should_be_preserved(string kind)
    {
        var path = Create(10, 40);
        switch (kind)
        {
            case "unmarked": File.Delete(Path.Combine(path, PrintImageRetentionCleaner.MarkerName)); break;
            case "bad-marker": File.WriteAllText(Path.Combine(path, PrintImageRetentionCleaner.MarkerName), new string('a', 32)); break;
            case "foreign-file": File.WriteAllText(Path.Combine(path, "notes.txt"), "保留"); break;
            case "nested": Directory.CreateDirectory(Path.Combine(path, "nested")); break;
            case "bad-name":
                var renamed = Path.Combine(_root, "not-a-job");
                Directory.Move(path, renamed);
                path = renamed;
                break;
        }
        Directory.SetLastWriteTimeUtc(path, _clock.GetUtcNow().AddDays(-40).UtcDateTime);
        File.WriteAllText(Path.Combine(_root, "loose.png"), "保留");
        new PrintImageRetentionCleaner(_root, 1, TextWriter.Null, 0, _clock).CleanupExpired();
        Assert.True(File.Exists(Path.Combine(path, "label-1.png")));
        Assert.True(File.Exists(Path.Combine(_root, "loose.png")));
    }

    [Fact]
    public void Failed_image_generation_should_leave_owned_partial_output_for_later_cleanup()
    {
        var cleaner = new PrintImageRetentionCleaner(_root, 1, TextWriter.Null, 0, _clock);
        var id = Guid.NewGuid().ToString("N");
        cleaner.SaveImages(id, FailAfterFirst());
        var path = Path.Combine(_root, id);
        Assert.True(File.Exists(Path.Combine(path, "label-1.png")));
        Assert.Equal(id, File.ReadAllText(Path.Combine(path, PrintImageRetentionCleaner.MarkerName)));
        _clock.Advance(TimeSpan.FromDays(2));
        cleaner.CleanupExpired();
        Assert.False(Directory.Exists(path));
    }

    [Fact]
    public void Partial_deletion_failure_should_preserve_age_for_next_retry()
    {
        var path = Create(10, 40);
        var second = Path.Combine(path, "label-2.png");
        File.WriteAllBytes(second, [1, 2]);
        var oldTime = _clock.GetUtcNow().AddDays(-40).UtcDateTime;
        Directory.SetLastWriteTimeUtc(path, oldTime);
        var cleaner = new PrintImageRetentionCleaner(_root, 31, TextWriter.Null, 0, _clock);
        using (var locked = new FileStream(second, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            cleaner.CleanupExpired();
            Assert.False(File.Exists(Path.Combine(path, "label-1.png")));
            Assert.True(File.Exists(second));
            Assert.Equal(oldTime, Directory.GetLastWriteTimeUtc(path));
        }
        cleaner.CleanupExpired();
        Assert.False(Directory.Exists(path));
    }

    [Fact]
    public void Writer_should_reject_traversal_and_preserve_existing_directory()
    {
        var cleaner = new PrintImageRetentionCleaner(_root, 31, TextWriter.Null);
        cleaner.SaveImages("../escape", [[1]]);
        Assert.False(Directory.Exists(_root));
        var path = Create(10, 0);
        cleaner.SaveImages(Path.GetFileName(path), [[1, 2, 3]]);
        Assert.Equal(10, new FileInfo(Path.Combine(path, "label-1.png")).Length);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(11000)]
    public async Task Active_writer_should_be_protected_from_cleanup(int continuationDelayMs)
    {
        var cleaner = new PrintImageRetentionCleaner(_root, 1, TextWriter.Null, 0, _clock);
        var id = Guid.NewGuid().ToString("N");
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        IEnumerable<byte[]> Images()
        {
            yield return [1];
            entered.SetResult();
            // 写入必须保持活动直到断言结束，不能因夹具计时到期提前释放保护锁。
            release.Wait();
            yield return [2];
        }
        var writer = Task.Factory.StartNew(() => cleaner.SaveImages(id, Images()),
            CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await Task.Delay(continuationDelayMs);
            Assert.False(writer.IsCompleted);
            _clock.Advance(TimeSpan.FromDays(2));
            cleaner.CleanupExpired();
            Assert.True(File.Exists(Path.Combine(_root, id, "label-1.png")));
        }
        finally
        {
            release.Set();
            await writer.WaitAsync(TimeSpan.FromSeconds(10));
        }
        Assert.True(File.Exists(Path.Combine(_root, id, "label-2.png")));
        cleaner.CleanupExpired();
        Assert.False(Directory.Exists(Path.Combine(_root, id)));
    }

    [Fact]
    public async Task Background_service_should_clean_at_start_and_while_idle_and_stop()
    {
        var startup = Create(10, 40);
        var cleaner = new PrintImageRetentionCleaner(_root, 1, TextWriter.Null, 0, _clock);
        using var service = new PrintImageCleanupService(cleaner, _clock);
        await service.StartAsync(CancellationToken.None);
        try
        {
            await WaitUntilAsync(() => !Directory.Exists(startup));
            var later = Create(10, 0);
            for (var i = 0; i < 30 && Directory.Exists(later); i++)
            {
                _clock.Advance(TimeSpan.FromHours(1));
                await Task.Delay(10);
            }
            Assert.False(Directory.Exists(later));
            await service.StopAsync(CancellationToken.None);
            var stopped = Create(10, 40);
            _clock.Advance(TimeSpan.FromHours(2));
            Assert.True(Directory.Exists(stopped));
        }
        finally { await service.StopAsync(CancellationToken.None); }
    }

    [Fact]
    public void Links_should_not_be_followed_by_writer_or_cleaner()
    {
        Directory.CreateDirectory(_root);
        var target = Path.Combine(_root, "target");
        Directory.CreateDirectory(target);
        var link = Path.Combine(_root, Guid.NewGuid().ToString("N"));
        if (OperatingSystem.IsWindows())
        {
            var start = new System.Diagnostics.ProcessStartInfo("cmd.exe")
            {
                CreateNoWindow = true, UseShellExecute = false,
                RedirectStandardOutput = true, RedirectStandardError = true,
                Arguments = $"/d /c mklink /J \"{link}\" \"{target}\"",
            };
            using var process = System.Diagnostics.Process.Start(start)!;
            if (!process.WaitForExit(10000))
            {
                process.Kill();
                Assert.True(process.WaitForExit(10000), "测试目录联接子进程未退出。");
                throw new TimeoutException("测试目录联接创建超时。");
            }
            Assert.True(process.ExitCode == 0, process.StandardError.ReadToEnd() + process.StandardOutput.ReadToEnd());
            Assert.True((File.GetAttributes(link) & FileAttributes.ReparsePoint) != 0);
        }
        else Directory.CreateSymbolicLink(link, target);
        try
        {
            var id = Guid.NewGuid().ToString("N");
            new PrintImageRetentionCleaner(link, 1, TextWriter.Null).SaveImages(id, [[1]]);
            Assert.Empty(Directory.GetFileSystemEntries(target));
            new PrintImageRetentionCleaner(_root, 1, TextWriter.Null).CleanupExpired();
            Assert.True(Directory.Exists(link));
            Assert.True(Directory.Exists(target));
        }
        finally { Directory.Delete(link); }
    }

    private string Create(int size, int ageDays)
    {
        var id = Guid.NewGuid().ToString("N");
        var path = Path.Combine(_root, id);
        Directory.CreateDirectory(path);
        File.WriteAllText(Path.Combine(path, PrintImageRetentionCleaner.MarkerName), id);
        File.WriteAllBytes(Path.Combine(path, "label-1.png"), new byte[size]);
        Directory.SetLastWriteTimeUtc(path, _clock.GetUtcNow().AddDays(-ageDays).UtcDateTime);
        return path;
    }

    private static IEnumerable<byte[]> FailAfterFirst()
    {
        yield return [1, 2];
        throw new IOException("隔离测试模拟编码失败");
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (var i = 0; i < 200 && !condition(); i++) await Task.Delay(10);
        Assert.True(condition());
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}
