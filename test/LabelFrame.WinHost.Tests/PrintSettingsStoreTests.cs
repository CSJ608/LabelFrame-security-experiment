using LabelFrame.WinHost;

namespace LabelFrame.WinHost.Tests;

public class PrintSettingsStoreTests
{
    private static string NewPath() => System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"lf-printsettings-{Guid.NewGuid():N}.json");

    [Fact]
    public void Load_missing_file_should_return_defaults()
    {
        var store = new PrintSettingsStore(NewPath());
        Assert.Equal(PrintSettings.Defaults, store.Load());
    }

    [Fact]
    public void Load_corrupt_file_should_return_defaults()
    {
        var path = NewPath();
        try
        {
            File.WriteAllText(path, "{ not json");
            var store = new PrintSettingsStore(path);
            Assert.Equal(PrintSettings.Defaults, store.Load());
        }
        finally
        {
            TryDelete(path);
        }
    }

    [Fact]
    public void Load_out_of_range_values_should_normalize()
    {
        var path = NewPath();
        try
        {
            File.WriteAllText(path, """{ "batchEnabled": true, "batchSize": 0, "batchIntervalMs": -5 }""");
            var store = new PrintSettingsStore(path);
            // batchSize 0 → 10、interval -5 → 500
            Assert.Equal(new PrintSettingsDto(true, 10, 500), store.Load());
        }
        finally
        {
            TryDelete(path);
        }
    }

    [Fact]
    public void Load_non_bool_enabled_should_default_false()
    {
        var path = NewPath();
        try
        {
            File.WriteAllText(path, """{ "batchEnabled": "yes", "batchSize": 10, "batchIntervalMs": 500 }""");
            var store = new PrintSettingsStore(path);
            Assert.False(store.Load().BatchEnabled);
        }
        finally
        {
            TryDelete(path);
        }
    }

    [Fact]
    public void Load_partial_missing_fields_should_fill_defaults()
    {
        var path = NewPath();
        try
        {
            File.WriteAllText(path, """{ "batchSize": 5 }""");
            var store = new PrintSettingsStore(path);
            Assert.Equal(new PrintSettingsDto(false, 5, 500), store.Load());
        }
        finally
        {
            TryDelete(path);
        }
    }

    [Fact]
    public void Save_then_load_should_round_trip()
    {
        var path = NewPath();
        try
        {
            var store = new PrintSettingsStore(path);
            store.Save(new PrintSettingsDto(true, 15, 300));

            Assert.Equal(new PrintSettingsDto(true, 15, 300), store.Load());
        }
        finally
        {
            TryDelete(path);
        }
    }

    [Fact]
    public void Save_should_overwrite_previous_value()
    {
        var path = NewPath();
        try
        {
            var store = new PrintSettingsStore(path);
            store.Save(new PrintSettingsDto(false, 5, 100));
            store.Save(new PrintSettingsDto(true, 8, 250));

            Assert.Equal(new PrintSettingsDto(true, 8, 250), store.Load());
        }
        finally
        {
            TryDelete(path);
        }
    }

    [Fact]
    public void Save_should_be_atomic_and_leave_no_temp_file()
    {
        var path = NewPath();
        try
        {
            var store = new PrintSettingsStore(path);
            store.Save(new PrintSettingsDto(true, 10, 500));

            Assert.True(File.Exists(path));
            Assert.False(File.Exists(path + ".tmp"));
            Assert.Contains("batchEnabled", File.ReadAllText(path));
        }
        finally
        {
            TryDelete(path);
            TryDelete(path + ".tmp");
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) { File.Delete(path); }
        }
        catch
        {
            // 清理失败不影响断言
        }
    }

    [Fact]
    public void Load_with_dpi_should_roundtrip_and_reject_out_of_whitelist()
    {
        var path = NewPath();
        try
        {
            File.WriteAllText(path, "{ \"batchEnabled\": false, \"batchSize\": 10, \"batchIntervalMs\": 500, \"dpi\": 300 }");
            Assert.Equal(300, new PrintSettingsStore(path).Load().Dpi);

            File.WriteAllText(path, "{ \"batchEnabled\": false, \"batchSize\": 10, \"batchIntervalMs\": 500, \"dpi\": 600 }");
            Assert.Null(new PrintSettingsStore(path).Load().Dpi); // 白名单外 → 继承宿主配置
        }
        finally
        {
            TryDelete(path);
        }
    }

    [Fact]
    public void Save_should_persist_dpi()
    {
        var path = NewPath();
        try
        {
            new PrintSettingsStore(path).Save(new PrintSettingsDto(false, 10, 500, 300));
            Assert.Equal(300, new PrintSettingsStore(path).Load().Dpi);
        }
        finally
        {
            TryDelete(path);
        }
    }
}
