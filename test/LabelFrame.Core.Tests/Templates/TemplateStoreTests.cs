using LabelFrame.Core.Contracts;
using LabelFrame.Core.Layout;
using LabelFrame.Core.Templates;

namespace LabelFrame.Core.Tests.Templates;

public class TemplateStoreTests
{
    private static TemplatePackage CreatePackage(
        string name,
        string group,
        string? uid = null,
        IReadOnlyDictionary<string, string>? testData = null) => new()
    {
        Name = name,
        Uid = uid,
        Group = group,
        TestData = testData ?? new Dictionary<string, string>(),
        Contract = new LabelContract
        {
            Name = "location-label",
            Version = "1.0",
            Fields = [new LabelField { Key = "locationCode", DisplayName = "库位码", IsRequired = true }],
        },
        Layout = new LabelLayout
        {
            Name = "l",
            ContractName = "location-label",
            ContractVersion = "1.0",
            WidthMm = 100,
            HeightMm = 60,
            Elements = [new LabelTextElement { SourceKey = "locationCode", XMm = 5, YMm = 5, FontHeightMm = 8, FontWidthMm = 8 }],
        },
        Images = new Dictionary<string, byte[]> { ["logo.png"] = new byte[] { 9, 8, 7 } },
    };

    [Fact]
    public async Task Save_get_should_preserve_package()
    {
        var dbPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"lftpl-{Guid.NewGuid():N}.db");
        try
        {
            var store = new TemplateStore(dbPath);
            await store.InitializeAsync();
            await store.SaveAsync(CreatePackage("location-label", "项目A"));

            var loaded = await store.GetAsync("location-label");

            Assert.NotNull(loaded);
            Assert.Equal("项目A", loaded!.Group);
            Assert.Equal("location-label", loaded.Contract.Name);
            Assert.True(loaded.Images.ContainsKey("logo.png"));
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(dbPath)) { File.Delete(dbPath); }
        }
    }

    [Fact]
    public async Task Save_get_should_preserve_test_data()
    {
        var dbPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"lftpl-{Guid.NewGuid():N}.db");
        try
        {
            var store = new TemplateStore(dbPath);
            await store.InitializeAsync();
            var package = CreatePackage("location-label", "项目A");
            package = new TemplatePackage
            {
                Name = package.Name,
                Group = package.Group,
                Contract = package.Contract,
                Layout = package.Layout,
                Images = package.Images,
                TestData = new Dictionary<string, string> { ["locationCode"] = "A-01-02-03", ["zone"] = "成品仓" },
            };
            await store.SaveAsync(package);

            var loaded = await store.GetAsync("location-label");

            Assert.NotNull(loaded);
            Assert.Equal("A-01-02-03", loaded!.TestData["locationCode"]);
            Assert.Equal("成品仓", loaded.TestData["zone"]);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(dbPath)) { File.Delete(dbPath); }
        }
    }

    [Fact]
    public async Task Save_get_should_default_test_data_empty()
    {
        var dbPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"lftpl-{Guid.NewGuid():N}.db");
        try
        {
            var store = new TemplateStore(dbPath);
            await store.InitializeAsync();
            await store.SaveAsync(CreatePackage("t1", "项目A"));

            var loaded = await store.GetAsync("t1");

            Assert.NotNull(loaded);
            Assert.Empty(loaded!.TestData);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(dbPath)) { File.Delete(dbPath); }
        }
    }

    [Fact]
    public async Task Save_should_upsert_and_replace_images()
    {
        var dbPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"lftpl-{Guid.NewGuid():N}.db");
        try
        {
            var store = new TemplateStore(dbPath);
            await store.InitializeAsync();
            await store.SaveAsync(CreatePackage("t1", "项目A"));
            await store.SaveAsync(CreatePackage("t1", "项目B"));

            var loaded = await store.GetAsync("t1");

            Assert.Equal("项目B", loaded!.Group);
            Assert.Single(await store.ListAsync());
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(dbPath)) { File.Delete(dbPath); }
        }
    }

    [Fact]
    public async Task List_should_filter_by_group()
    {
        var dbPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"lftpl-{Guid.NewGuid():N}.db");
        try
        {
            var store = new TemplateStore(dbPath);
            await store.InitializeAsync();
            await store.SaveAsync(CreatePackage("a1", "项目A"));
            await store.SaveAsync(CreatePackage("b1", "项目B"));

            var groupA = await store.ListAsync("项目A");
            var all = await store.ListAsync();

            Assert.Single(groupA);
            Assert.Equal(2, all.Count);
            Assert.Equal("项目A", all[0].Group);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(dbPath)) { File.Delete(dbPath); }
        }
    }

    [Fact]
    public async Task Save_should_derive_test_data_from_preview_values()
    {
        var dbPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"lftpl-{Guid.NewGuid():N}.db");
        try
        {
            var store = new TemplateStore(dbPath);
            await store.InitializeAsync();
            var package = CreatePackage("t1", "项目A");
            package = new TemplatePackage
            {
                Name = package.Name,
                Group = package.Group,
                Contract = package.Contract,
                Layout = new LabelLayout
                {
                    Name = "l",
                    ContractName = "location-label",
                    ContractVersion = "1.0",
                    WidthMm = 100,
                    HeightMm = 60,
                    Elements = [new LabelTextElement { SourceKey = "locationCode", PreviewValue = "A-01-02-03", XMm = 5, YMm = 5, FontHeightMm = 8, FontWidthMm = 8 }],
                },
                Images = package.Images,
                TestData = new Dictionary<string, string>(),
            };
            await store.SaveAsync(package);

            var loaded = await store.GetAsync("t1");
            Assert.NotNull(loaded);
            Assert.Equal("A-01-02-03", loaded!.TestData["locationCode"]);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(dbPath)) { File.Delete(dbPath); }
        }
    }

    [Fact]
    public async Task Save_should_keep_existing_test_data_when_no_preview_values()
    {
        var dbPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"lftpl-{Guid.NewGuid():N}.db");
        try
        {
            var store = new TemplateStore(dbPath);
            await store.InitializeAsync();
            var first = CreatePackage("t1", "项目A");
            first = new TemplatePackage
            {
                Name = first.Name,
                Group = first.Group,
                Contract = first.Contract,
                Layout = first.Layout,
                Images = first.Images,
                TestData = new Dictionary<string, string> { ["locationCode"] = "OLD-EXPLICIT" },
            };
            await store.SaveAsync(first);

            // 第二次保存不传 testData（空）、布局无 previewValue → 旧显式值保留
            var second = CreatePackage("t1", "项目A");
            await store.SaveAsync(second);

            var loaded = await store.GetAsync("t1");
            Assert.NotNull(loaded);
            Assert.Equal("OLD-EXPLICIT", loaded!.TestData["locationCode"]);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(dbPath)) { File.Delete(dbPath); }
        }
    }

    [Fact]
    public async Task Save_preview_values_should_override_explicit_test_data()
    {
        var dbPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"lftpl-{Guid.NewGuid():N}.db");
        try
        {
            var store = new TemplateStore(dbPath);
            await store.InitializeAsync();
            var package = CreatePackage("t1", "项目A");
            package = new TemplatePackage
            {
                Name = package.Name,
                Group = package.Group,
                Contract = package.Contract,
                Layout = new LabelLayout
                {
                    Name = "l",
                    ContractName = "location-label",
                    ContractVersion = "1.0",
                    WidthMm = 100,
                    HeightMm = 60,
                    Elements = [new LabelTextElement { SourceKey = "locationCode", PreviewValue = "PREVIEW-VALUE", XMm = 5, YMm = 5, FontHeightMm = 8, FontWidthMm = 8 }],
                },
                Images = package.Images,
                TestData = new Dictionary<string, string> { ["locationCode"] = "EXPLICIT-VALUE" },
            };
            await store.SaveAsync(package);

            var loaded = await store.GetAsync("t1");
            Assert.NotNull(loaded);
            Assert.Equal("PREVIEW-VALUE", loaded!.TestData["locationCode"]);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(dbPath)) { File.Delete(dbPath); }
        }
    }
    [Fact]
    public async Task Delete_should_remove_template_and_images()
    {
        var dbPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"lftpl-{Guid.NewGuid():N}.db");
        try
        {
            var store = new TemplateStore(dbPath);
            await store.InitializeAsync();
            await store.SaveAsync(CreatePackage("t1", "项目A"));

            await store.DeleteAsync("t1");

            Assert.Null(await store.GetAsync("t1"));
            Assert.Empty(await store.ListAsync());
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(dbPath)) { File.Delete(dbPath); }
        }
    }

    [Fact]
    public async Task Initialize_should_migrate_legacy_db_and_backfill_name_from_id()
    {
        var dbPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"lftpl-{Guid.NewGuid():N}.db");
        try
        {
            // 手工构造旧库：无 name / test_data_json 列，id 即旧模板名
            await using (var raw = new Microsoft.Data.Sqlite.SqliteConnection(LabelFrame.Core.Data.SqliteSupport.BuildConnectionString(dbPath)))
            {
                await raw.OpenAsync();
                await using var setup = raw.CreateCommand();
                setup.CommandText = """
                    CREATE TABLE templates (
                        id            TEXT PRIMARY KEY,
                        group_name    TEXT NOT NULL,
                        contract_json TEXT NOT NULL,
                        layout_json   TEXT NOT NULL,
                        created_at    TEXT NOT NULL,
                        updated_at    TEXT NOT NULL
                    );
                    INSERT INTO templates (id, group_name, contract_json, layout_json, created_at, updated_at)
                    VALUES ('legacy-tpl', '旧组',
                        '{"name":"c1","version":"1.0","fields":[]}',
                        '{"name":"l","contractName":"c1","contractVersion":"1.0","widthMm":100,"heightMm":60,"elements":[]}',
                        '2024-01-01T00:00:00.0000000+00:00', '2024-01-01T00:00:00.0000000+00:00');
                    """;
                await setup.ExecuteNonQueryAsync();
            }

            var store = new TemplateStore(dbPath);
            await store.InitializeAsync();
            await store.InitializeAsync(); // 迁移幂等

            var loaded = await store.GetAsync("legacy-tpl");
            var summaries = await store.ListAsync();

            Assert.NotNull(loaded);
            Assert.Equal("legacy-tpl", loaded!.Uid);
            Assert.Equal("旧组", loaded.Group);
            var summary = Assert.Single(summaries);
            Assert.Equal("legacy-tpl", summary.Name);
            Assert.Equal("legacy-tpl", summary.Uid);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(dbPath)) { File.Delete(dbPath); }
        }
    }

    [Fact]
    public async Task Save_with_uid_rename_should_keep_uid_and_drop_old_name()
    {
        var dbPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"lftpl-{Guid.NewGuid():N}.db");
        try
        {
            var store = new TemplateStore(dbPath);
            await store.InitializeAsync();
            await store.SaveAsync(CreatePackage("n1", "项目A", testData: new Dictionary<string, string> { ["locationCode"] = "OLD-VALUE" }));
            var uid = (await store.GetAsync("n1"))!.Uid;
            Assert.False(string.IsNullOrWhiteSpace(uid));

            await store.SaveAsync(CreatePackage("n2", "项目B", uid));

            Assert.Null(await store.GetAsync("n1"));
            var loaded = await store.GetAsync("n2");
            Assert.NotNull(loaded);
            Assert.Equal(uid, loaded!.Uid);
            Assert.Equal("项目B", loaded.Group);
            // testData 随行（uid）携带，改名保存不丢
            Assert.Equal("OLD-VALUE", loaded.TestData["locationCode"]);
            Assert.True(loaded.Images.ContainsKey("logo.png"));
            Assert.Single(await store.ListAsync());
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(dbPath)) { File.Delete(dbPath); }
        }
    }

    [Fact]
    public async Task Save_with_uid_rename_colliding_should_delete_collided_row()
    {
        var dbPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"lftpl-{Guid.NewGuid():N}.db");
        try
        {
            var store = new TemplateStore(dbPath);
            await store.InitializeAsync();
            await store.SaveAsync(CreatePackage("a", "项目A"));
            await store.SaveAsync(CreatePackage("b", "项目B"));
            var uidA = (await store.GetAsync("a"))!.Uid;

            // a 改名为 b：撞 b 行 → b 行（连同图片）删除，本行改名且内容为改名方新版
            await store.SaveAsync(CreatePackage("b", "项目A2", uidA));

            var summaries = await store.ListAsync();
            var loaded = await store.GetAsync("b");
            Assert.Single(summaries);
            Assert.Equal(uidA, summaries[0].Uid);
            Assert.NotNull(loaded);
            Assert.Equal(uidA, loaded!.Uid);
            Assert.Equal("项目A2", loaded.Group);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(dbPath)) { File.Delete(dbPath); }
        }
    }

    [Fact]
    public async Task Save_without_uid_colliding_name_should_replace_by_name()
    {
        var dbPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"lftpl-{Guid.NewGuid():N}.db");
        try
        {
            var store = new TemplateStore(dbPath);
            await store.InitializeAsync();
            await store.SaveAsync(CreatePackage("t1", "项目A", testData: new Dictionary<string, string> { ["locationCode"] = "OLD-VALUE" }));
            var firstUid = (await store.GetAsync("t1"))!.Uid;

            await store.SaveAsync(CreatePackage("t1", "项目B"));

            var loaded = await store.GetAsync("t1");
            Assert.NotNull(loaded);
            Assert.Equal("项目B", loaded!.Group);
            // 行替换＋testData 合并基底取被替换行，均与旧按名覆盖行为等价
            Assert.Equal("OLD-VALUE", loaded.TestData["locationCode"]);
            Assert.Single(await store.ListAsync());
            // 无 uid＝新建行换新 uid，旧行按名覆盖删除
            Assert.NotEqual(firstUid, loaded.Uid);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(dbPath)) { File.Delete(dbPath); }
        }
    }
}