namespace LabelFrame.Server.Tests;

/// <summary>PDA（Android 宿主）安装包目录服务测试（与 ClientPackagesService 对称 + .apk 上传限制）。</summary>
public class PdaPackagesServiceTests
{
    private static (PdaPackagesService Service, string Dir) Create()
    {
        var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"lfpdapkgs-{Guid.NewGuid():N}");
        return (new PdaPackagesService(dir), dir);
    }

    [Fact]
    public async Task Save_then_list_should_return_file_with_size_and_url()
    {
        var (svc, dir) = Create();
        try
        {
            var saved = await svc.SaveAsync("LabelFrame-AndroidHost-0.26.0.apk", new MemoryStream(new byte[] { 1, 2, 3, 4 }));
            Assert.Equal(4, saved.SizeBytes);

            var list = svc.List();
            var view = Assert.Single(list);
            Assert.Equal("LabelFrame-AndroidHost-0.26.0.apk", view.FileName);
            Assert.Equal(4, view.SizeBytes);
            Assert.StartsWith("/api/pda-packages/", view.Url);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task Save_should_reject_non_apk_extension()
    {
        var (svc, dir) = Create();
        try
        {
            // 上传仅接受 .apk（大小写不敏感）；目录直放不受限（由端点集成测试覆盖直放列出）
            await Assert.ThrowsAsync<InvalidOperationException>(() => svc.SaveAsync("client.msi", new MemoryStream(new byte[] { 1 })));
            await Assert.ThrowsAsync<InvalidOperationException>(() => svc.SaveAsync("archive.zip", new MemoryStream(new byte[] { 1 })));
            await svc.SaveAsync("Host.UPPER.APK", new MemoryStream(new byte[] { 1 }));
            Assert.Single(svc.List());
            Assert.Empty(Directory.GetFiles(dir, "*.msi"));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task Traversal_file_name_should_be_rejected()
    {
        var (svc, dir) = Create();
        try
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => svc.SaveAsync("..\\..\\evil.apk", new MemoryStream()));
            Assert.Null(svc.GetDownloadPath("..\\..\\evil.apk"));
            Assert.Null(svc.GetDownloadPath("../evil.apk"));
            Assert.Empty(svc.List());
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task Directory_dropped_non_apk_file_should_still_list()
    {
        // 目录直放不做扩展名限制（服务端不对文件内容做格式断言）——列表照常列出
        var (svc, dir) = Create();
        try
        {
            await File.WriteAllTextAsync(Path.Combine(dir, "readme.txt"), "说明");
            var view = Assert.Single(svc.List());
            Assert.Equal("readme.txt", view.FileName);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task Download_path_should_resolve_uploaded_file()
    {
        var (svc, dir) = Create();
        try
        {
            await svc.SaveAsync("host.apk", new MemoryStream(new byte[] { 9 }));

            var path = svc.GetDownloadPath("host.apk");
            Assert.NotNull(path);
            Assert.True(File.Exists(path));
            Assert.Equal("host.apk", Path.GetFileName(path));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task Delete_existing_should_return_true_and_remove()
    {
        var (svc, dir) = Create();
        try
        {
            await svc.SaveAsync("a.apk", new MemoryStream(new byte[] { 1 }));
            Assert.True(svc.Delete("a.apk"));
            Assert.Null(svc.Get("a.apk"));
            Assert.False(svc.Delete("a.apk"));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task Save_should_overwrite_same_name()
    {
        var (svc, dir) = Create();
        try
        {
            await svc.SaveAsync("x.apk", new MemoryStream(new byte[] { 1 }));
            await svc.SaveAsync("x.apk", new MemoryStream(new byte[] { 2, 2 }));

            var view = Assert.Single(svc.List());
            Assert.Equal(2, view.SizeBytes);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
