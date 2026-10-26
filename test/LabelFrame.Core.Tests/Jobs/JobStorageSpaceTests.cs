using LabelFrame.Core.Jobs;

namespace LabelFrame.Core.Tests.Jobs;

public sealed class JobStorageSpaceTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("LabelFrame-space-").FullName;

    [Fact]
    public void Preflight_queries_data_directory_not_path_root()
    {
        var directory = Directory.CreateDirectory(Path.Combine(_directory, "data", "mounted", "jobs")).FullName;
        var database = Path.Combine(directory, "jobs.db");
        File.WriteAllBytes(database, []);
        var queried = new List<string>();
        var free = JobStorageSpace.AvailableForDatabase(database, path =>
        {
            queried.Add(path);
            return path == directory ? 1234 : 0;
        });
        Assert.Equal(1234, free);
        Assert.Equal(new[] { directory, directory }, queried);
        Assert.NotEqual(Path.GetPathRoot(database), directory);
    }

    [Fact]
    public void Query_failure_is_not_replaced_with_root_space()
    {
        var database = Path.Combine(_directory, "jobs.db");
        File.WriteAllBytes(database, []);
        var expected = new IOException("隔离空间查询失败");
        Assert.Same(expected, Assert.Throws<IOException>(() =>
            JobStorageSpace.AvailableForDatabase(database, _ => throw expected)));
    }

    [Fact]
    public void Real_directory_query_returns_available_space_and_missing_path_fails_closed()
    {
        Assert.True(JobStorageSpace.AvailableForDirectory(_directory) > 0);
        var missing = Path.Combine(_directory, "missing");
        Assert.True(new DriveInfo(Path.GetPathRoot(missing)!).AvailableFreeSpace > 0);
        Assert.ThrowsAny<IOException>(() => JobStorageSpace.AvailableForDirectory(missing));
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);
}
