using LabelFrame.Core.Jobs;
using LabelFrame.WinHost;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Xunit;

namespace LabelFrame.WinHost.Integration;

/// <summary>验证作业库拒绝初始化时宿主不进入后台服务装配及恢复阶段。</summary>
public sealed class JobStorageStartupTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Invalid_job_storage_stops_build_before_worker_and_preserves_inflight_state(bool missingPayload)
    {
        var directory = Path.Combine(Path.GetTempPath(), "lf-startup-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var options = new HostOptions
        {
            ListenUrl = "http://127.0.0.1:0",
            DatabasePath = Path.Combine(directory, "jobs.db"),
            TemplatesDbPath = Path.Combine(directory, "templates.db"),
            LogsDbPath = Path.Combine(directory, "logs.db"),
            PluginsPath = Path.Combine(directory, "plugins"),
            ConfigPath = Path.Combine(directory, "settings.json"),
            PrintSettingsPath = Path.Combine(directory, "print-settings.json"),
            ConnectionPath = Path.Combine(directory, "connection.json"),
            HostLogPath = Path.Combine(directory, "host.log"),
            PrintOutputPath = Path.Combine(directory, "print"),
            AppLogDirectory = Path.Combine(directory, "logs"),
            ServerUrl = null,
            Transport = TransportMode.Log,
            OpenBrowser = false,
            EnableTray = false,
        };
        try
        {
            await new SqliteLabelJobStore(options.DatabasePath).InitializeAsync();
            using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = options.DatabasePath, Pooling = false,
            }.ToString());
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO jobs VALUES ('job','request','Printing','2026-10-26T00:00:00Z','2026-10-26T00:00:00Z');
                INSERT INTO job_items VALUES ('item','job',0,'Printing',NULL,NULL,'2026-10-26T00:00:00Z');
                """;
            command.ExecuteNonQuery();
            if (!missingPayload)
            {
                command.CommandText = "INSERT INTO job_payloads VALUES ('item','unchanged',NULL); PRAGMA user_version=99;";
                command.ExecuteNonQuery();
            }
            var configuredServices = false;
            await Assert.ThrowsAsync<InvalidDataException>(() => WinHostApp.BuildAsync(
                options, TextWriter.Null, _ => { }, builder => builder.WebHost.UseTestServer(),
                _ => configuredServices = true));
            Assert.False(configuredServices);
            Assert.False(File.Exists(options.TemplatesDbPath));
            Assert.False(File.Exists(options.LogsDbPath));
            Assert.Empty(Directory.GetFiles(directory, "*.before-*.db"));
            command.CommandText = "SELECT status FROM jobs WHERE id='job';";
            Assert.Equal("Printing", command.ExecuteScalar());
            command.CommandText = "SELECT status FROM job_items WHERE id='item';";
            Assert.Equal("Printing", command.ExecuteScalar());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
