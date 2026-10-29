using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LabelFrame.Server;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace LabelFrame.Server.Tests;

/// <summary>
/// PDA（Android 宿主）安装包分发端点 HTTP 集成测试：
/// 上传仅接受 .apk；下载响应 MIME = application/vnd.android.package-archive（Android 浏览器识别为安装包）；
/// 不存在 404 + LF_SRV_010；目录直放文件照常列出。
/// </summary>
public sealed class PdaPackagesEndpointsTests : IDisposable
{
    private const string ApkContentType = "application/vnd.android.package-archive";

    private readonly IsolatedServerEnvironment _environment = new();
    private string _directory => _environment.DirectoryPath;
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public PdaPackagesEndpointsTests()
    {
        _factory = new WebApplicationFactory<Program>();
        _factory.UseKestrel(options => options.Listen(IPAddress.Loopback, 0));
        _client = _factory.CreateClient();
    }

    [Fact]
    public async Task Upload_then_download_should_return_apk_content_type()
    {
        await IsolatedServerEnvironment.LoginAsync(_client);
        using (var form = new MultipartFormDataContent())
        {
            var file = new ByteArrayContent([0x50, 0x4B, 0x03, 0x04, 0x05, 0x06]);
            form.Add(file, "file", "LabelFrame-AndroidHost-0.26.0.apk");

            using var uploaded = await _client.PostAsync("/api/pda-packages", form);
            Assert.True(uploaded.IsSuccessStatusCode, $"上传失败：{(int)uploaded.StatusCode}");
            var view = await uploaded.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("LabelFrame-AndroidHost-0.26.0.apk", view.GetProperty("fileName").GetString());
            Assert.EndsWith("/api/pda-packages/LabelFrame-AndroidHost-0.26.0.apk", view.GetProperty("url").GetString());
        }

        using var downloaded = await _client.GetAsync("/api/pda-packages/LabelFrame-AndroidHost-0.26.0.apk");
        Assert.Equal(HttpStatusCode.OK, downloaded.StatusCode);
        // APK 专用 MIME：Android 浏览器据此拉起安装
        Assert.Equal(ApkContentType, downloaded.Content.Headers.ContentType?.MediaType);
        var bytes = await downloaded.Content.ReadAsByteArrayAsync();
        Assert.Equal(6, bytes.Length);
    }

    [Fact]
    public async Task Upload_non_apk_should_be_400_with_actionable_message()
    {
        await IsolatedServerEnvironment.LoginAsync(_client);
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent([1, 2, 3]);
        form.Add(file, "file", "client-setup.msi");

        using var response = await _client.PostAsync("/api/pda-packages", form);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("LF_SRV_002", body.GetProperty("code").GetString());
        Assert.Contains(".apk", body.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Download_missing_should_be_404_with_pda_code()
    {
        using var response = await _client.GetAsync("/api/pda-packages/no-such.apk");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("LF_SRV_010", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Directory_dropped_file_should_list_without_upload()
    {
        var dir = Path.Combine(_directory, "pda-packages");
        await File.WriteAllTextAsync(Path.Combine(dir, "dropped.apk"), "apk-bytes");

        var list = await _client.GetFromJsonAsync<JsonElement>("/api/pda-packages");
        Assert.Equal(1, list.GetArrayLength());
        Assert.Equal("dropped.apk", list[0].GetProperty("fileName").GetString());
        Assert.Equal(9, list[0].GetProperty("sizeBytes").GetInt64());
    }

    [Fact]
    public async Task Delete_should_remove_file_and_second_delete_404()
    {
        await IsolatedServerEnvironment.LoginAsync(_client);
        using (var form = new MultipartFormDataContent())
        {
            var file = new ByteArrayContent([9]);
            form.Add(file, "file", "tmp.apk");
            using var uploaded = await _client.PostAsync("/api/pda-packages", form);
            Assert.True(uploaded.IsSuccessStatusCode);
        }

        using var deleted = await _client.DeleteAsync("/api/pda-packages/tmp.apk");
        Assert.Equal(HttpStatusCode.OK, deleted.StatusCode);

        using var again = await _client.DeleteAsync("/api/pda-packages/tmp.apk");
        Assert.Equal(HttpStatusCode.NotFound, again.StatusCode);
        var body = await again.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("LF_SRV_010", body.GetProperty("code").GetString());
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        _client.Dispose();
        _factory.Dispose();
        _environment.Dispose();
    }
}
