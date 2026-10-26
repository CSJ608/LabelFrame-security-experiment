using System.Net;
using System.Text;
using System.Text.Json;
using LabelFrame.WinHost;
using LabelFrame.WinHost.Transport;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using SkiaSharp;
using Xunit;
using ZXing;
using ClientHostOptions = LabelFrame.WinHost.HostOptions;

namespace LabelFrame.ClientHost.Tests;

public sealed class LinuxClientHostTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Linux_target_should_be_headless_and_expose_only_log_transport(bool saveImages)
    {
        var root = Path.Combine(Path.GetTempPath(), $"labelframe-clienthost-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        var options = new ClientHostOptions
        {
            ListenUrl = "http://127.0.0.1:0",
            DatabasePath = Path.Combine(root, "jobs.db"),
            TemplatesDbPath = Path.Combine(root, "templates.db"),
            LogsDbPath = Path.Combine(root, "logs.db"),
            ConfigPath = Path.Combine(root, "settings.json"),
            PrintSettingsPath = Path.Combine(root, "print-settings.json"),
            ConnectionPath = Path.Combine(root, "connection.json"),
            HostLogPath = Path.Combine(root, "host.log"),
            PrintOutputPath = Path.Combine(root, "print"),
            SaveLogPrintImages = saveImages,
            PluginsPath = Path.Combine(root, "plugins"),
            WebUiPath = Path.Combine(root, "web"),
            Transport = TransportMode.Tcp,
            OpenBrowser = true,
            EnableTray = true,
        };

        WebApplication? app = null;
        try
        {
            app = await WinHostApp.BuildAsync(
                options,
                TextWriter.Null,
                _ => { },
                builder => builder.WebHost.UseTestServer(),
                services => services.RemoveAll<IHostedService>());
            await app.StartAsync();
            using var client = app.GetTestClient();

            using var health = JsonDocument.Parse(await client.GetStringAsync("/healthz"));
            var healthRoot = health.RootElement;
            Assert.Equal("LabelFrame.ClientHost", healthRoot.GetProperty("service").GetString());
            Assert.Equal("linux", healthRoot.GetProperty("platform").GetString());
            Assert.True(healthRoot.GetProperty("headless").GetBoolean());
            Assert.Equal("log", healthRoot.GetProperty("pluginId").GetString());

            using var plugins = JsonDocument.Parse(await client.GetStringAsync("/api/transport/plugins"));
            var plugin = Assert.Single(plugins.RootElement.EnumerateArray());
            Assert.Equal("log", plugin.GetProperty("id").GetString());

            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/plugins/installed")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/")).StatusCode);

            var submit = await client.PostAsync("/api/jobs", new StringContent("""
                {
                  "requestId": "linux-log-output",
                  "template": {
                    "contract": { "name": "linux", "version": "1.0", "fields": [ { "key": "code", "displayName": "编码", "isRequired": true, "type": "text" } ] },
                    "layout": { "name": "linux", "contractName": "linux", "contractVersion": "1.0", "widthMm": 40, "heightMm": 20,
                      "elements": [ { "type": "barcode", "sourceKey": "code", "xMm": 2, "yMm": 2, "widthMm": 36, "heightMm": 12, "displayValue": true } ] }
                  },
                  "labels": [ { "data": { "code": "LF-LINUX" } } ]
                }
                """, Encoding.UTF8, "application/json"));
            Assert.Equal(HttpStatusCode.Accepted, submit.StatusCode);
            using var submitted = JsonDocument.Parse(await submit.Content.ReadAsStringAsync());
            var jobId = submitted.RootElement.GetProperty("jobId").GetString()!;
            var expectedImage = Path.Combine(options.PrintOutputPath, jobId, "label-1.png");
            Assert.Equal(Path.GetDirectoryName(expectedImage), submitted.RootElement.GetProperty("printImageDir").GetString());
            Assert.Equal(saveImages ? 1 : 0, submitted.RootElement.GetProperty("printImageCount").GetInt32());
            if (!saveImages)
            {
                Assert.False(Directory.Exists(options.PrintOutputPath));
                return;
            }
            Assert.True(new FileInfo(expectedImage).Length > 0);
            using var bitmap = SKBitmap.Decode(expectedImage);
            var luminance = new RGBLuminanceSource(
                bitmap.Bytes,
                bitmap.Width,
                bitmap.Height,
                RGBLuminanceSource.BitmapFormat.BGRA32);
            Assert.Equal("LF-LINUX", new BarcodeReaderGeneric().Decode(luminance)?.Text);
        }
        finally
        {
            if (app is not null)
            {
                await app.StopAsync();
                await app.DisposeAsync();
            }

            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch (IOException)
            {
                // Windows 测试运行器偶尔延迟释放 SQLite 文件句柄，不影响测试行为。
            }
        }
    }
}
