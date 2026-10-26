using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using LabelFrame.Api;
using LabelFrame.Core.Contracts;
using LabelFrame.Core.Data;
using LabelFrame.Core.Layout;
using LabelFrame.Core.Templates;
using Microsoft.Data.Sqlite;
using Xunit.Abstractions;

namespace LabelFrame.Server.Tests.Perf;

/// <summary>在独立数据库中对照真实服务与分阶段提交编排。</summary>
[Trait("Category", "Perf")]
public sealed class SubmissionStagesTests(ITestOutputHelper output)
{
    [Fact]
    public async Task Measure_submission_coordination_and_autocommit()
    {
        for (var round = 0; round < 4; round++)
        {
            foreach (var diagnostic in round % 2 == 0 ? new[] { false, true } : new[] { true, false })
                await RunAsync(round, diagnostic);
        }
    }

    private async Task RunAsync(int round, bool diagnostic)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"lf-submit-stages-{Guid.NewGuid():N}");
        var connectionString = SqliteSupport.BuildConnectionString(Path.Combine(directory, "server.db"));
        var templateConnection = SqliteSupport.BuildConnectionString(Path.Combine(directory, "templates.db"));
        try
        {
            var db = new ServerDb(Path.Combine(directory, "server.db"));
            await db.InitializeAsync();
            var templates = new TemplateStore(Path.Combine(directory, "templates.db"));
            await templates.InitializeAsync();
            await templates.SaveAsync(new TemplatePackage { Name = "性能模板", Group = "压测", Contract = new LabelContract
            {
                Name = "perf", Version = "1.0",
                Fields = [new LabelField { Key = "code", DisplayName = "编码", IsRequired = true }],
            }, Layout = new LabelLayout
            {
                Name = "l", ContractName = "perf", ContractVersion = "1.0", WidthMm = 60, HeightMm = 40,
                Elements = [new LabelTextElement { SourceKey = "code", XMm = 2, YMm = 2, FontHeightMm = 5 }],
            }});
            using var service = new ServerService(db, templates);
            var submitGate = Field<SemaphoreSlim>(service, "_gate");
            var writeGate = Field<SemaphoreSlim>(db, "_writeGate");
            var resolve = typeof(ServerService).GetMethod("ResolveTemplateAsync", BindingFlags.NonPublic | BindingFlags.Instance)!
                .CreateDelegate<Func<SubmitJobRequest, CancellationToken, Task<TemplateDto?>>>(service);
            var view = typeof(ServerService).GetMethod("ToJobViewAsync", BindingFlags.NonPublic | BindingFlags.Instance)!
                .CreateDelegate<Func<ServerJob, CancellationToken, Task<ServerJobView>>>(service);
            for (var d = 0; d < 20; d++) await service.RegisterDeviceAsync($"perf-{d}", $"设备{d}");
            var samples = new ConcurrentBag<double[]>();
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var workers = Enumerable.Range(0, 20).Select(d => Task.Run(async () =>
            {
                await start.Task;
                for (var i = 0; i < 20; i++)
                {
                    var times = new double[11];
                    var total = Stopwatch.GetTimestamp();
                    var stamp = total;
                    var request = new SubmitJobRequest($"perf-{d}-{i}", null,
                        [new LabelDto(new Dictionary<string, string> { ["code"] = $"P-{d}-{i}" }),
                         new LabelDto(new Dictionary<string, string> { ["code"] = $"P-{d}-{i}-b" })],
                        TargetDeviceId: $"perf-{d}", TemplateName: "性能模板");
                    times[0] = Lap(ref stamp);
                    ServerJobView submitted;
                    if (!diagnostic) submitted = await service.SubmitJobAsync(request);
                    else
                    {
                        var template = await resolve(request, default);
                        times[1] = Lap(ref stamp);
                        await submitGate.WaitAsync();
                        times[2] = Lap(ref stamp);
                        try
                        {
                            Assert.NotNull(await db.GetDeviceAsync(request.TargetDeviceId!));
                            Assert.Null(await db.GetJobByRequestIdAsync(request.RequestId!));
                            times[3] = Lap(ref stamp);
                            var payload = JsonSerializer.Serialize(new JobPayload(template!, request.Labels!), RoutingJson.Options);
                            times[4] = Lap(ref stamp);
                            var job = new ServerJob
                            {
                                Id = Guid.NewGuid().ToString("N"), RequestId = request.RequestId!,
                                TargetDeviceId = request.TargetDeviceId!, Status = ServerJobStatus.Pending,
                                CreatedAt = DateTimeOffset.UtcNow, TotalItems = 2, PayloadJson = payload,
                            };
                            await writeGate.WaitAsync();
                            times[5] = Lap(ref stamp);
                            try { await InsertAsync(job, times); }
                            finally { writeGate.Release(); }
                            stamp = Stopwatch.GetTimestamp();
                            submitted = await view(job, default);
                            times[8] = Lap(ref stamp);
                        }
                        finally { submitGate.Release(); }
                    }
                    times[9] = Stopwatch.GetElapsedTime(total).TotalMilliseconds;
                    var claimed = Assert.Single(await service.ClaimPendingJobsAsync(request.TargetDeviceId!));
                    Assert.Equal(submitted.JobId, claimed.JobId);
                    Assert.Equal(request.RequestId, claimed.RequestId);
                    Assert.Equal(request.Labels![0].Data!["code"], claimed.Payload.Labels[0].Data!["code"]);
                    Assert.Equal(request.Labels[1].Data!["code"], claimed.Payload.Labels[1].Data!["code"]);
                    await service.ReportResultAsync(request.TargetDeviceId!, submitted.JobId,
                        new ReportResultRequest("Completed", 2, 0, null));
                    times[10] = Stopwatch.GetElapsedTime(total).TotalMilliseconds;
                    samples.Add(times);
                }
            })).ToArray();
            start.SetResult();
            await Task.WhenAll(workers);
            Assert.Equal(400, samples.Count);
            var stored = await db.ListJobsAsync(500);
            Assert.Equal(400, stored.Count);
            Assert.Equal(400, stored.Select(job => job.RequestId).Distinct(StringComparer.Ordinal).Count());
            foreach (var job in stored)
            {
                Assert.Equal(ServerJobStatus.Completed, job.Status);
                Assert.Equal(2, job.CompletedItems);
                Assert.Equal(0, job.FailedItems);
                Assert.Null(job.CallbackUrl);
                var payload = JsonSerializer.Deserialize<JobPayload>(job.PayloadJson, RoutingJson.Options)!;
                Assert.Equal(2, payload.Labels.Count);
                Assert.Equal("性能模板", payload.Template.Name);
                var code = job.RequestId.Replace("perf-", "P-", StringComparison.Ordinal);
                Assert.Equal(code, payload.Labels[0].Data!["code"]);
                Assert.Equal(code + "-b", payload.Labels[1].Data!["code"]);
            }
            for (var d = 0; d < 20; d++) Assert.Empty(await service.ClaimPendingJobsAsync($"perf-{d}"));
            output.WriteLine($"round={round} diagnostic={diagnostic} samples={samples.Count}");
            string[] names = ["request", "template", "submit-wait", "device-and-idempotency-read", "serialize", "write-wait-and-job", "open-prepare", "insert-autocommit", "view", "submit-total", "pipeline"];
            for (var k = 0; k < names.Length; k++)
            {
                if (!diagnostic && k is > 0 and < 9) continue;
                var values = samples.Select(s => s[k]).Order().ToArray();
                output.WriteLine($"{names[k]} p50={values[200]:F3} p95={values[380]:F3} max={values[^1]:F3} sum={values.Sum():F3} ms");
            }
            await using (var connection = await SqliteSupport.OpenAsync(connectionString))
            {
                foreach (var pragma in new[] { "journal_mode", "synchronous", "wal_autocheckpoint" })
                {
                    await using var command = connection.CreateCommand();
                    command.CommandText = "PRAGMA " + pragma;
                    output.WriteLine($"{pragma}={await command.ExecuteScalarAsync()}");
                }
            }

            async Task InsertAsync(ServerJob job, double[] times)
            {
                var stamp = Stopwatch.GetTimestamp();
                await using var connection = await SqliteSupport.OpenAsync(connectionString);
                await using var command = connection.CreateCommand();
                command.CommandText = """
                    INSERT OR IGNORE INTO server_jobs
                        (id, request_id, target_device_id, status, created_at, claimed_at, finished_at,
                         total_items, completed_items, failed_items, error_message, payload_json, callback_url)
                    VALUES ($id, $requestId, $targetDeviceId, $status, $createdAt, NULL, NULL,
                            $totalItems, 0, 0, NULL, $payloadJson, $callbackUrl);
                    """;
                command.Parameters.AddWithValue("$id", job.Id);
                command.Parameters.AddWithValue("$requestId", job.RequestId);
                command.Parameters.AddWithValue("$targetDeviceId", job.TargetDeviceId);
                command.Parameters.AddWithValue("$status", job.Status.ToString());
                command.Parameters.AddWithValue("$createdAt", SqliteSupport.Format(job.CreatedAt));
                command.Parameters.AddWithValue("$totalItems", job.TotalItems);
                command.Parameters.AddWithValue("$payloadJson", job.PayloadJson);
                command.Parameters.AddWithValue("$callbackUrl", DBNull.Value);
                times[6] = Lap(ref stamp);
                Assert.Equal(1, await command.ExecuteNonQueryAsync());
                times[7] = Lap(ref stamp);
            }
        }
        finally
        {
            using var serverPool = new SqliteConnection(connectionString);
            using var templatePool = new SqliteConnection(templateConnection);
            SqliteConnection.ClearPool(serverPool);
            SqliteConnection.ClearPool(templatePool);
            Directory.Delete(directory, true);
        }
    }

    private static T Field<T>(object instance, string name)
        => (T)instance.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(instance)!;

    private static double Lap(ref long stamp)
    {
        var now = Stopwatch.GetTimestamp();
        var result = Stopwatch.GetElapsedTime(stamp, now).TotalMilliseconds;
        stamp = now;
        return result;
    }
}
