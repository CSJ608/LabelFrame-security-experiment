// 防复发断言（「单码单参数键集」）：
// 解析 src 全部 C# 源码中「携带 params 的错误构造调用点」——两种既有惯用法：
// Templated(码, "模板{key}", new() { ["key"] = … })（ServerService / LabelJobQueue 私有助手）
// new Dictionary<string, string> { ["key"] = … } + 邻近 ErrorView / LabelJobException 构造（端点层 / AndroidHost）。
// 按错误码分组断言：同码所有调用点的参数键集一致；并把键集与 web 端 zh-CN / en 两份码表词条占位符对齐
// （带占位符的词条必须与键集逐键相等——否则该变体在 en 态触发「参数不全」守卫回退中文，即验收失败项 1a 的缺陷形态）。
// 防假绿：站点数下限锚定；注册表 / 接线解析不完整、存在无法解析码表达式的站点 → 直接失败（宁可红不可静默漏检）。

using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;

namespace LabelFrame.Api.Tests;

public sealed class ErrorParamKeyConsistencyTests
{
    private sealed record Site(string Location, string CodeExpr, string Code, IReadOnlySet<string> Keys);

    private static string RepoRoot([CallerFilePath] string sourcePath = "")
        => Path.GetFullPath(Path.Combine(sourcePath, "..", "..", ".."));

    /// <summary>src 全部 C# 源文件（排除 bin / obj 构建产物）。</summary>
    private static IEnumerable<string> SourceFiles()
        => Directory.EnumerateFiles(Path.Combine(RepoRoot(), "src"), "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                && !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"));

    // ---- 注册表常量解析：类名 → (常量名 → 码字面量) ----

    private static readonly Dictionary<string, Dictionary<string, string>> RegistryConstants = BuildRegistryConstants();

    private static Dictionary<string, Dictionary<string, string>> BuildRegistryConstants()
    {
        var registries = new Dictionary<string, Dictionary<string, string>>();
        foreach (var file in SourceFiles())
        {
            foreach (Match match in Regex.Matches(File.ReadAllText(file),
                """class (?<name>\w*ErrorCodes\w*)\s*\{(?<body>[^{}]*)\}"""))
            {
                var constants = Regex.Matches(match.Groups["body"].Value,
                        """const string (?<const>\w+) = "(?<code>LF_[A-Z0-9_]+)""")
                    .ToDictionary(x => x.Groups["const"].Value, x => x.Groups["code"].Value);
                if (constants.Count > 0)
                {
                    registries[match.Groups["name"].Value] = constants;
                }
            }
        }

        return registries;
    }

    // ---- 宿主共享端点 options.XxxCode 伪码 → 接线码集（Server / WinHost 两处接线展开为各自实际码）----

    private static readonly Dictionary<string, string> EndpointsFileToOptionsType = new()
    {
        ["TemplateEndpoints.cs"] = "TemplateApiOptions",
        ["ImportEndpoints.cs"] = "ImportApiOptions",
    };

    private static readonly Dictionary<string, HashSet<string>> OptionsWiring = BuildOptionsWiring();

    private static Dictionary<string, HashSet<string>> BuildOptionsWiring()
    {
        var wiring = new Dictionary<string, HashSet<string>>();
        foreach (var file in SourceFiles())
        {
            foreach (Match match in Regex.Matches(File.ReadAllText(file),
                @"new (?<type>TemplateApiOptions|ImportApiOptions)\((?<args>[^)]*)\)"))
            {
                var args = match.Groups["args"].Value.Split(',');
                // 位置参数接线：TemplateApiOptions(Store, Renderer, Dpi, InvalidRequestCode, TemplateNotFoundCode)；
                // ImportApiOptions(InvalidRequestCode)。params 站点只用到 TemplateNotFound / InvalidRequest 两个码位
                var isTemplate = match.Groups["type"].Value == "TemplateApiOptions";
                var argIndex = isTemplate ? 4 : 0;
                if (args.Length <= argIndex)
                {
                    continue;
                }

                var codeExpr = args[argIndex].Trim();
                var parts = codeExpr.Split('.');
                if (parts.Length == 2
                    && RegistryConstants.TryGetValue(parts[0], out var constants)
                    && constants.TryGetValue(parts[1], out var code))
                {
                var key = isTemplate ? "TemplateApiOptions:TemplateNotFoundCode" : "ImportApiOptions:InvalidRequestCode";
                if (!wiring.TryGetValue(key, out var wired))
                {
                    wired = [];
                    wiring[key] = wired;
                }

                wired.Add(code);
                }
            }
        }

        return wiring;
    }

    // ---- 调用点提取 ----

    private static readonly Regex TemplatedSiteRegex = new(
        """Templated\(\s*(?<code>"LF_[A-Z0-9_]+"|\w+\.\w+)\s*,\s*"(?<tpl>(?:[^"\\]|\\.)*)"\s*,\s*new\(\)\s*\{(?<keys>[^}]*)\}""",
        RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex DictionarySiteRegex = new(
        """new Dictionary<string, string> \{(?<keys>[^}]*)\}""",
        RegexOptions.Compiled);

    private static readonly Regex CodeExpressionRegex = new(
        @"""LF_[A-Z0-9_]+""|options\.\w+Code|(?:ApiErrorCodes|ServerErrorCodes|JobErrorCodes|PluginErrorCodes)\.\w+",
        RegexOptions.Compiled);

    /// <summary>提取全部携带 params 的调用点（两种惯用法）；options 伪码按所在端点文件的接线展开为多码多站点。</summary>
    private static List<Site> ExtractSites()
    {
        var sites = new List<Site>();
        var unresolved = new List<string>();

        foreach (var file in SourceFiles())
        {
            var source = File.ReadAllText(file);
            var relative = Path.GetRelativePath(RepoRoot(), file);
            var fileName = Path.GetFileName(file);

            void AddSite(string codeExpr, string keysText, int index)
            {
                var keys = Regex.Matches(keysText, @"\[""(?<key>\w+)""\]").Select(m => m.Groups["key"].Value).ToHashSet();
                var line = 1 + source.AsSpan(0, index).Count('\n');
                var location = $"{relative}:{line}";

                if (codeExpr.StartsWith("options.", StringComparison.Ordinal))
                {
                    // 共享端点伪码：按所在文件映射接线类型，展开为每个接线码一条站点记录（Server / WinHost 各自的码）
                    if (!EndpointsFileToOptionsType.TryGetValue(fileName, out var optionsType)
                        || !OptionsWiring.TryGetValue($"{optionsType}:{codeExpr["options.".Length..]}", out var wiredCodes))
                    {
                        unresolved.Add($"{location}：{codeExpr}（端点文件未登记或接线未解析）");
                        return;
                    }

                    foreach (var wiredCode in wiredCodes)
                    {
                        sites.Add(new Site(location, codeExpr, wiredCode, keys));
                    }

                    return;
                }

                var code = ResolveCodeExpression(codeExpr);
                if (code is null)
                {
                    unresolved.Add($"{location}：{codeExpr}");
                    return;
                }

                sites.Add(new Site(location, codeExpr, code, keys));
            }

            foreach (Match match in TemplatedSiteRegex.Matches(source))
            {
                AddSite(match.Groups["code"].Value, match.Groups["keys"].Value, match.Index);
            }

            foreach (Match match in DictionarySiteRegex.Matches(source))
            {
                // 站点邻域（前后各 900 字符）内取「距字典最近」的码表达式（ErrorView / 异常构造的码位）——
                // 取最近而非最先：邻域可能覆盖上一个端点Handler 的码位（如 TemplateEndpoints 的 POST → GET 相邻）
                var windowStart = Math.Max(0, match.Index - 900);
                var windowLength = Math.Min(source.Length, match.Index + match.Length + 900) - windowStart;
                var window = source.AsSpan(windowStart, windowLength).ToString();
                Match? nearest = null;
                var nearestDistance = int.MaxValue;
                foreach (Match candidate in CodeExpressionRegex.Matches(window))
                {
                    var distance = Math.Abs(candidate.Index - (match.Index - windowStart));
                    if (distance < nearestDistance)
                    {
                        nearest = candidate;
                        nearestDistance = distance;
                    }
                }

                if (nearest is null)
                {
                    unresolved.Add($"{relative}:{1 + source.AsSpan(0, match.Index).Count('\n')}：字典参数站点邻域未找到码表达式");
                    continue;
                }

                AddSite(nearest.Value, match.Groups["keys"].Value, match.Index);
            }
        }

        Assert.True(
            unresolved.Count == 0,
            $"存在无法解析码表达式的 params 调用点（新惯用法请同步本测试扫描器）：\n{string.Join("\n", unresolved)}");
        return sites;
    }

    /// <summary>解析调用点的码表达式：注册表常量引用 / "LF_…" 字面量。</summary>
    private static string? ResolveCodeExpression(string expression)
    {
        expression = expression.Trim();
        if (Regex.IsMatch(expression, "^\"LF_[A-Z0-9_]+\"$"))
        {
            return expression.Trim('"');
        }

        var parts = expression.Split('.');
        if (parts.Length == 2
            && RegistryConstants.TryGetValue(parts[0], out var constants)
            && constants.TryGetValue(parts[1], out var code))
        {
            return code;
        }

        return null;
    }

    // ---- 断言 ----

    [Fact]
    public void 扫描器解析完整性_防注册表或接线失配后空集合假绿()
    {
        foreach (var expected in new[] { "ApiErrorCodes", "ServerErrorCodes", "JobErrorCodes" })
        {
            Assert.True(RegistryConstants.ContainsKey(expected), $"注册表 {expected} 未解析到（源文件移动或类名变更？请同步本测试）");
        }

        Assert.True(RegistryConstants.TryGetValue("PluginErrorCodes", out var pluginCodes) && pluginCodes.Count >= 3,
            "AndroidHost PluginErrorCodes 未解析到（EmbeddedHttpServer 结构变更？请同步本测试）");

        Assert.True(OptionsWiring.TryGetValue("TemplateApiOptions:TemplateNotFoundCode", out var tplCodes) && tplCodes.Count >= 2,
            $"TemplateApiOptions 接线解析不足 2 处（Server / WinHost）：现 {tplCodes?.Count ?? 0} 处");

        var sites = ExtractSites();
        Assert.True(sites.Count >= 35, $"params 调用点解析数量异常偏少：{sites.Count}（扫描器失配或调用点被删？）");
    }

    [Fact]
    public void 同码跨调用点参数键集一致_变体键不一致即红()
    {
        var violations = new List<string>();
        foreach (var group in ExtractSites().GroupBy(s => s.Code))
        {
            var keySets = group.Select(s => string.Join(",", s.Keys.Order())).Distinct().ToList();
            if (keySets.Count > 1)
            {
                violations.Add(
                    $"{group.Key}：{group.Count()} 个调用点存在 {keySets.Count} 种参数键集 [{string.Join(" | ", keySets)}]，站点：{string.Join("; ", group.Select(s => s.Location))}");
            }
        }

        Assert.True(violations.Count == 0,
            $"同码多参数键集（决策 #166 ⑥ 单码单参数键集——语义不同应拆码）：\n{string.Join("\n", violations)}");
    }

    [Fact]
    public void 带占位符的码表词条与后端键集逐键相等_不匹配即回退中文()
    {
        var zhEntryPlaceholders = CatalogPlaceholders("zh-CN");
        var enEntryPlaceholders = CatalogPlaceholders("en");
        var violations = new List<string>();

        // 后端键集 ↔ 两份码表词条占位符（词条带占位符才约束：通用桶码词条无占位符时 params 仅面向调用方透传，不插值）
        foreach (var group in ExtractSites().GroupBy(s => s.Code))
        {
            var keys = group.First().Keys;
            foreach (var (locale, entries) in new[] { ("zh-CN", zhEntryPlaceholders), ("en", enEntryPlaceholders) })
            {
                if (entries.TryGetValue(group.Key, out var placeholders) && placeholders.Count > 0
                    && !placeholders.SetEquals(keys))
                {
                    violations.Add(
                        $"{group.Key}：{locale} 词条占位符 [{string.Join(",", placeholders.Order())}] ≠ 后端键集 [{string.Join(",", keys.Order())}]（该变体 en 态将触发参数不全守卫回退中文）");
                }
            }
        }

        // zh ↔ en 同码占位符键集一致（插值槽同名对接）
        foreach (var (code, zh) in zhEntryPlaceholders)
        {
            if (enEntryPlaceholders.TryGetValue(code, out var en) && !zh.SetEquals(en))
            {
                violations.Add($"{code}：zh / en 词条占位符键集不一致");
            }
        }

        Assert.True(violations.Count == 0, $"码表词条占位符与后端参数键不对齐：\n{string.Join("\n", violations)}");
    }

    [Fact]
    public void 返修锚点_LF_SRV_006两变体与LF_SRV_001各变体键集()
    {
        var byCode = ExtractSites().GroupBy(s => s.Code).ToDictionary(g => g.Key, g => g.ToList());

        // 验收失败项 1a：LF_SRV_006 模板库端点变体（TemplateEndpoints ×3，经 Server 接线展开）+ 作业提交链 ×1，统一 templateName
        Assert.True(byCode.TryGetValue("LF_SRV_006", out var srv006) && srv006.Count >= 4,
            "LF_SRV_006 调用点不足 4 处（扫描器失配？）");
        Assert.All(srv006, s => Assert.True(s.Keys.SetEquals(["templateName"]),
            $"{s.Location}：LF_SRV_006 参数键必须为 templateName（#242 返修统一）"));

        // LF_TPL_001（WinHost 接线的同源变体）同样统一 templateName
        Assert.True(byCode.TryGetValue("LF_TPL_001", out var tpl001) && tpl001.Count >= 3,
            "LF_TPL_001 调用点不足 3 处（扫描器失配？）");
        Assert.All(tpl001, s => Assert.True(s.Keys.SetEquals(["templateName"]),
            $"{s.Location}：LF_TPL_001 参数键必须为 templateName（#242 返修统一）"));

        // LF_SRV_001（设备未注册）三个调用点统一 deviceId；LF_SRV_012（按 IP 未找到）两个调用点统一 ip
        Assert.True(byCode.TryGetValue("LF_SRV_001", out var srv001) && srv001.Count >= 3,
            "LF_SRV_001 调用点不足 3 处（扫描器失配？）");
        Assert.All(srv001, s => Assert.True(s.Keys.SetEquals(["deviceId"]),
            $"{s.Location}：LF_SRV_001 参数键必须为 deviceId（按 IP 变体应使用 LF_SRV_012）"));
        Assert.True(byCode.TryGetValue("LF_SRV_012", out var srv012) && srv012.Count >= 2,
            "LF_SRV_012 调用点不足 2 处（按 IP 未找到设备应拆码并覆盖两调用点）");
        Assert.All(srv012, s => Assert.True(s.Keys.SetEquals(["ip"]), $"{s.Location}：LF_SRV_012 参数键必须为 ip"));
    }

    // ---- 辅助 ----

    private static Dictionary<string, HashSet<string>> CatalogPlaceholders(string locale)
    {
        var path = Path.Combine(RepoRoot(), "web", "src", "i18n", "locales", locale, "errorCodes.json");
        Assert.True(File.Exists(path), $"码表文件缺失：{path}");
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        return doc.RootElement.EnumerateObject().ToDictionary(
            p => p.Name,
            p => Regex.Matches(p.Value.GetString() ?? string.Empty, @"\{\{\s*(?<key>\w+)\s*\}\}")
                .Select(m => m.Groups["key"].Value)
                .ToHashSet());
    }
}
