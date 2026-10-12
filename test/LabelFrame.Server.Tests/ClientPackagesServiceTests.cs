namespace LabelFrame.Server.Tests;

public class ClientPackagesServiceTests
{
    private static (ClientPackagesService Service, string Dir) Create()
    {
        var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"lfpkgs-{Guid.NewGuid():N}");
        return (new ClientPackagesService(dir), dir);
    }

    [Fact]
    public async Task Save_then_list_should_return_file_with_size_and_url()
    {
        var (svc, dir) = Create();
        try
        {
            var saved = await svc.SaveAsync("LabelFrame-Client-0.18.0.msi", new MemoryStream(new byte[] { 1, 2, 3, 4 }));
            Assert.Equal(4, saved.SizeBytes); // 返回视图在流关闭后取值

            var list = svc.List();
            var view = Assert.Single(list);
            Assert.Equal("LabelFrame-Client-0.18.0.msi", view.FileName);
            Assert.Equal(4, view.SizeBytes);
            Assert.StartsWith("/api/client-packages/", view.Url);
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
            await svc.SaveAsync("client.msi", new MemoryStream(new byte[] { 9 }));

            var path = svc.GetDownloadPath("client.msi");
            Assert.NotNull(path);
            Assert.True(File.Exists(path));
            Assert.Equal("client.msi", Path.GetFileName(path));
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
            await Assert.ThrowsAsync<InvalidOperationException>(() => svc.SaveAsync("..\\..\\evil.msi", new MemoryStream()));
            Assert.Null(svc.GetDownloadPath("..\\..\\evil.msi"));
            Assert.Null(svc.GetDownloadPath("../evil.msi"));
            Assert.Empty(svc.List());
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
            await svc.SaveAsync("a.msi", new MemoryStream(new byte[] { 1 }));
            Assert.True(svc.Delete("a.msi"));
            Assert.Null(svc.Get("a.msi"));
            Assert.False(svc.Delete("a.msi"));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Get_missing_should_return_null()
    {
        var (svc, dir) = Create();
        try
        {
            Assert.Null(svc.Get("no-such.msi"));
            Assert.Null(svc.GetDownloadPath("no-such.msi"));
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
            await svc.SaveAsync("x.msi", new MemoryStream(new byte[] { 1 }));
            await svc.SaveAsync("x.msi", new MemoryStream(new byte[] { 2, 2 }));

            var view = Assert.Single(svc.List());
            Assert.Equal(2, view.SizeBytes);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
