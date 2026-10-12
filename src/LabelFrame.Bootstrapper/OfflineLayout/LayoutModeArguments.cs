namespace LabelFrame.Bootstrapper.OfflineLayout;

/// <summary>引导程序布局生成参数（/）：<c>--layout &lt;目录&gt;</c> 进入布局生成模式，<c>--manifest &lt;路径|URL&gt;</c> 覆写生成用清单来源。</summary>
/// <remarks>
/// <para>
/// **双横线与引擎原生开关区分**：Burn 引擎自身解析单横线 <c>-layout [目录]</c>（<c>LaunchAction.Layout</c> +
/// <c>WixBundleLayoutDirectory</c>，引擎级「下载全部载荷」语义）；双横线 <c>--layout</c> 按引擎单前缀剥离后为未知参数，
/// 经 <c>IBootstrapperCommand.CommandLine</c> 透传 BA——本类只认双横线形态（单横线路径由 BA 侧按
/// <c>LaunchAction.Layout</c> 路由，两者进入同一生成流程）。
/// </para>
/// <para>
/// 支持空格分隔（<c>--layout D:\dir</c>）与等号（<c>--layout=D:\dir</c>）两种形态；其余参数（变量赋值等）忽略不报错；
/// <c>--layout</c> 缺值记录入 <see cref="Errors"/>（fail-fast 由调用方决定呈现）。
/// </para>
/// </remarks>
public sealed record LayoutModeArguments
{
    public LayoutModeArguments(string? layoutDirectory, string? manifestSource, IReadOnlyList<string> errors)
    {
        LayoutDirectory = layoutDirectory;
        ManifestSource = manifestSource;
        Errors = errors;
    }

    /// <summary>布局目标目录（null = 非布局生成模式）。</summary>
    public string? LayoutDirectory { get; }

    /// <summary>生成用清单来源覆写（null = 默认官方稳定通道）。</summary>
    public string? ManifestSource { get; }

    /// <summary>参数解析错误（如 --layout 缺值；非空时调用方应显式失败而非静默忽略）。</summary>
    public IReadOnlyList<string> Errors { get; }

    /// <summary>是否进入布局生成模式。</summary>
    public bool IsLayoutMode => !string.IsNullOrWhiteSpace(LayoutDirectory);

    /// <summary>解析参数序列（<see cref="SplitCommandLine"/> 拆分 BA 透传命令行后调用）。</summary>
    public static LayoutModeArguments Parse(IEnumerable<string> args)
    {
#if NET10_0_OR_GREATER
        ArgumentNullException.ThrowIfNull(args);
#else
        // net48 腿无 ArgumentNullException.ThrowIfNull（.NET 6+ API）
        if (args is null)
        {
            throw new ArgumentNullException(nameof(args));
        }
#endif

        string? layout = null;
        string? manifest = null;
        var errors = new List<string>();
        var tokens = args as IList<string> ?? args.ToList();
        for (var i = 0; i < tokens.Count; i++)
        {
            var token = tokens[i];
            string? inlineValue = null;
            string? next = null;
            var name = token;
            var eq = token.IndexOf('=');
            if (eq >= 0)
            {
                name = token.Substring(0, eq);
                inlineValue = token.Substring(eq + 1);
            }
            else if (i + 1 < tokens.Count && (tokens[i + 1].Length == 0 || tokens[i + 1][0] != '-'))
            {
                // 等号形态之外的取值：下一 token 不以 '-' 开头才视为本参数的值（防把后续开关吞成路径）
                next = tokens[i + 1];
            }

            if (string.Equals(name, "--layout", StringComparison.Ordinal))
            {
                var value = inlineValue ?? next;
                if (string.IsNullOrWhiteSpace(value))
                {
                    errors.Add("--layout 缺少目标目录（用法：--layout <目录> 或 --layout=<目录>）。");
                }
                else
                {
                    layout = value;
                    if (inlineValue is null && next is not null)
                    {
                        i++;
                    }
                }
            }
            else if (string.Equals(name, "--manifest", StringComparison.Ordinal))
            {
                var value = inlineValue ?? next;
                if (string.IsNullOrWhiteSpace(value))
                {
                    errors.Add("--manifest 缺少来源（用法：--manifest <路径或 URL>）。");
                }
                else
                {
                    manifest = value;
                    if (inlineValue is null && next is not null)
                    {
                        i++;
                    }
                }
            }
            // 其余参数（变量赋值等）：非本类契约，忽略
        }

        return new LayoutModeArguments(layout, manifest, errors);
    }

    /// <summary>解析 BA 透传命令行字符串（<c>IBootstrapperCommand.CommandLine</c>——引擎未消费的参数串）。</summary>
    public static LayoutModeArguments Parse(string? commandLine) => Parse(SplitCommandLine(commandLine ?? string.Empty));

    /// <summary>命令行拆分（Windows 风格：引号分组、引号内 <c>""</c> 为字面引号、多空白分隔）。</summary>
    public static IReadOnlyList<string> SplitCommandLine(string commandLine)
    {
        var tokens = new List<string>();
        var current = new System.Text.StringBuilder();
        var inQuotes = false;
        for (var i = 0; i < commandLine.Length; i++)
        {
            var c = commandLine[i];
            if (c == '"')
            {
                if (inQuotes && i + 1 < commandLine.Length && commandLine[i + 1] == '"')
                {
                    current.Append('"'); // 引号内的 "" = 字面引号
                    i++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }
            }
            else if (!inQuotes && char.IsWhiteSpace(c))
            {
                Flush();
            }
            else
            {
                current.Append(c);
            }
        }

        Flush();
        return tokens;

        void Flush()
        {
            if (current.Length > 0)
            {
                tokens.Add(current.ToString());
                current.Length = 0;
            }
        }
    }
}
