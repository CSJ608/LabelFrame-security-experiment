// LF_* 错误码 PDA 侧码表测试（/ ， ③ / ——语义对齐的 web 端测试）：
// ① 覆盖断言：解析后端错误码注册表源码（ApiErrorCodes / ServerErrorCodes / Core JobErrorCodes）提取码字面量，
//    断言 zh + en 两份码表全覆盖且无多余死码——新增码漏表即红（与 web errorCodes.test.ts 同一道防线）；
// ② 展示端翻译行为：en 已知码按码表插值渲染、未知码 / 参数不全回退后端中文、zh 直用后端 message。
// 取证说明：本断言的失败形态 = 逐条列出缺失 / 多余码后 Assert 失败。

using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using LabelFrame.AndroidHost.Errors;
using Xunit;

namespace LabelFrame.AndroidHost.Tests;

public class LfErrorCatalogTests
{
    /// <summary>错误码注册表（后端源码即权威，；新增码须同步两份码表）。</summary>
    private static readonly (string Name, string File)[] Registries =
    [
        ("ApiErrorCodes（LabelFrame.Api）", "src/LabelFrame.Api/ApiErrorCodes.cs"),
        ("ServerErrorCodes（LabelFrame.Server）", "src/LabelFrame.Server/ServerErrorCodes.cs"),
        ("JobErrorCodes（LabelFrame.Core，宿主本地 HTTP 同表暴露）", "src/LabelFrame.Core/Jobs/JobErrorCodes.cs"),
    ];

    private static string RepoRoot([CallerFilePath] string sourcePath = "")
        => Path.GetFullPath(Path.Combine(sourcePath, "..", "..", ".."));

    /// <summary>解析注册表源码中的码字面量（"LF_XXX"；注释中的约定说明不在引号内，不会误提取）。</summary>
    private static HashSet<string> ExtractRegistryCodes(string file) =>
        [.. Regex.Matches(File.ReadAllText(Path.Combine(RepoRoot(), file)), "\"(LF_[A-Z0-9_]+)\"")
            .Select(m => m.Groups[1].Value)];

    [Fact]
    public void 注册表源码解析非空_防文件路径失效后空集合假绿()
    {
        var registryCodes = Registries.SelectMany(r => ExtractRegistryCodes(r.File)).ToHashSet();
        Assert.True(registryCodes.Count >= 20, $"注册表解析数量异常：{registryCodes.Count}");
        foreach (var (name, file) in Registries)
        {
            Assert.True(ExtractRegistryCodes(file).Count > 0, $"{name}（{file}）解析为空");
        }
    }

    [Fact]
    public void zh码表覆盖全部注册码且无死码_删一条即红()
        => AssertCatalogCoversRegistries(LfErrorCatalog.Zh, nameof(LfErrorCatalog.Zh));

    [Fact]
    public void en码表覆盖全部注册码且无死码_删一条即红()
        => AssertCatalogCoversRegistries(LfErrorCatalog.En, nameof(LfErrorCatalog.En));

    private static void AssertCatalogCoversRegistries(IReadOnlyDictionary<string, string> table, string tableName)
    {
        var registryCodes = Registries.SelectMany(r => ExtractRegistryCodes(r.File)).ToHashSet();

        Assert.True(table.Count > 0, "码表不为空（防误删整表后空集合假绿）");
        var missing = registryCodes.Where(code => !table.ContainsKey(code)).Order().ToList();
        var extra = table.Keys.Where(code => !registryCodes.Contains(code)).Order().ToList();
        Assert.True(
            missing.Count == 0 && extra.Count == 0,
            $"注册表 ↔ {tableName} 码表不一致：缺 [{string.Join(", ", missing)}]，多 [{string.Join(", ", extra)}]");
    }

    [Fact]
    public void 两份码表键集一致_en落后zh即红()
    {
        var onlyZh = LfErrorCatalog.Zh.Keys.Except(LfErrorCatalog.En.Keys).Order().ToList();
        var onlyEn = LfErrorCatalog.En.Keys.Except(LfErrorCatalog.Zh.Keys).Order().ToList();
        Assert.True(onlyZh.Count == 0 && onlyEn.Count == 0, $"zh/en 键集不一致：zh 独有 [{string.Join(", ", onlyZh)}]，en 独有 [{string.Join(", ", onlyEn)}]");
    }

    [Fact]
    public void 同码占位符键集zh_en一致_插值槽同名对接()
    {
        foreach (var (code, zhTemplate) in LfErrorCatalog.Zh)
        {
            var zhKeys = LfErrorCatalog.PlaceholderKeys(zhTemplate);
            var enKeys = LfErrorCatalog.PlaceholderKeys(LfErrorCatalog.En[code]);
            Assert.True(zhKeys.OrderBy(k => k).SequenceEqual(enKeys.OrderBy(k => k)),
                $"{code} 占位符键集不一致：zh [{string.Join(", ", zhKeys)}] vs en [{string.Join(", ", enKeys)}]");
        }
    }

    [Fact]
    public void en已知码_带全参按码表插值渲染英文()
    {
        var rendered = LfErrorCatalog.Resolve(
            "LF_JOB_001",
            new Dictionary<string, string> { ["jobId"] = "job-42" },
            "作业不存在：job-42。",
            english: true);
        Assert.Equal("Job not found: job-42.", rendered);
    }

    [Fact]
    public void en已知码_多参数逐槽插值()
    {
        var rendered = LfErrorCatalog.Resolve(
            "LF_SRV_005",
            new Dictionary<string, string> { ["jobId"] = "j1", ["status"] = "Suspended" },
            "作业 j1 当前状态 Suspended 不允许该操作。",
            english: true);
        Assert.Equal("Job j1 in status Suspended does not allow this operation.", rendered);
    }

    [Fact]
    public void en已知码_模板带参而后端未提供_回退后端中文不输出插值残缺()
    {
        const string backend = "发送失败：连接超时。";
        var rendered = LfErrorCatalog.Resolve("LF_IO_001", null, backend, english: true);
        Assert.Equal(backend, rendered);

        var partialParams = LfErrorCatalog.Resolve(
            "LF_SRV_004",
            new Dictionary<string, string> { ["jobId"] = "j1" }, // 缺 deviceId
            "设备 dev1 不是作业 j1 的领取者。",
            english: true);
        Assert.Equal("设备 dev1 不是作业 j1 的领取者。", partialParams);
    }

    [Fact]
    public void en未知码_回退后端中文message()
    {
        const string backend = "服务端专属新错误（码未入表）。";
        var rendered = LfErrorCatalog.Resolve("LF_SRV_999", null, backend, english: true);
        Assert.Equal(backend, rendered);
    }

    [Fact]
    public void en无参码_直接采用码表英文()
    {
        var rendered = LfErrorCatalog.Resolve("LF_PLUGIN_INSTALL_FAILED", null, "插件安装失败，请重试；问题持续请联系管理员。", english: true);
        Assert.Equal("Plugin installation failed. Please retry; if the problem persists, contact your administrator.", rendered);
    }

    [Fact]
    public void zh态_已知码也直用后端message_中文权威在后端()
    {
        const string backend = "作业不存在：job-42。";
        var withParams = LfErrorCatalog.Resolve("LF_JOB_001", new Dictionary<string, string> { ["jobId"] = "job-42" }, backend, english: false);
        Assert.Equal(backend, withParams);

        var noParams = LfErrorCatalog.Resolve("LF_IO_001", null, backend, english: false);
        Assert.Equal(backend, noParams);
    }

    [Fact]
    public void 空码或空message_走回退不抛()
    {
        Assert.Equal("兜底", LfErrorCatalog.Resolve(null, null, "兜底", english: true));
        Assert.Equal("兜底", LfErrorCatalog.Resolve(string.Empty, null, "兜底", english: true));
    }

    [Fact]
    public void 参数值内嵌占位形态_单遍替换不级联()
    {
        var rendered = LfErrorCatalog.Resolve(
            "LF_PLUGIN_INVALID",
            new Dictionary<string, string> { ["detail"] = "清单缺失 {version}" },
            "插件包无效：清单缺失 {version}",
            english: true);
        Assert.Equal("Invalid plugin package: 清单缺失 {version}", rendered);
    }

    [Fact]
    public void 多余参数不影响渲染()
    {
        var rendered = LfErrorCatalog.Resolve(
            "LF_JOB_001",
            new Dictionary<string, string> { ["jobId"] = "j9", ["extra"] = "ignored" },
            "作业不存在：j9。",
            english: true);
        Assert.Equal("Job not found: j9.", rendered);
    }
}
