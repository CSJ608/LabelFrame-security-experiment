using LabelFrame.Core.Transport;
using LabelFrame.Core.Transport.Plugins;

namespace LabelFrame.WinHost.Transport;

/// <summary>连接管理结果：ok + 中文消息 + 当前生效连接。</summary>
public sealed record TransportChangeResult(bool Ok, string Message, TransportConfig Config);

/// <summary>运行时连接管理：持有当前唯一生效的连接配置与传输实例，支持「先测试后生效」切换并持久化。</summary>
public interface ITransportManager
{
    /// <summary>当前生效连接配置。</summary>
    TransportConfig CurrentConfig { get; }

    /// <summary>当前生效传输实例（作业 Worker / 状态 / 测试页统一从这里取）。</summary>
    IPrintTransport CurrentTransport { get; }

    /// <summary>connection.json 路径（用户数据目录）。</summary>
    string ConfigFilePath { get; }

    /// <summary>应用连接：校验 → 测试 → 切换 + 持久化；testOnly 只测试不保存不切换。</summary>
    Task<TransportChangeResult> ApplyAsync(TransportConfig config, bool testOnly, CancellationToken cancellationToken = default);
}

/// <summary>
/// 连接管理器实现：启动时按 connection.json &gt; appsettings / 环境变量 &gt; 默认 Log 初始化；
/// 同一时间只有单一连接生效；配置 pluginId + params 经传输插件注册表装配（内置 + 外部 DLL）；
/// 切换成功才持久化到 %LOCALAPPDATA%\LabelFrame\connection.json（新格式，旧格式自动迁移）。
/// </summary>
public sealed class TransportManager : ITransportManager, IDisposable
{
    private readonly ITransportPluginRegistry _registry;
    private readonly ITransportPluginContext _context;
    private readonly TextWriter _hostLogWriter;
    // 并发防护：_config/_transport 被打印 Worker（每张发送）与 API 并发读，切换时写入——
    // 读写经 _stateLock；ApplyAsync 整体再经 _applyLock 串行化（避免两个「先测试后生效」交错切换）
    private readonly object _stateLock = new();
    private readonly SemaphoreSlim _applyLock = new(1, 1);
    private TransportConfig _config;
    private IPrintTransport _transport;

    /// <summary>创建连接管理器。</summary>
    /// <param name="registry">传输插件注册表（内置 + 外部 DLL 已装配）。</param>
    /// <param name="context">插件上下文（宿主日志 + 数据目录）。</param>
    /// <param name="options">宿主配置（默认传输 / 参数）。</param>
    /// <param name="hostLogWriter">宿主日志写入器。</param>
    /// <param name="configFilePath">connection.json 路径（默认 %LOCALAPPDATA%\\LabelFrame\\connection.json；测试可注入临时路径）。</param>
    public TransportManager(
        ITransportPluginRegistry registry,
        ITransportPluginContext context,
        HostOptions options,
        TextWriter hostLogWriter,
        string? configFilePath = null)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _hostLogWriter = hostLogWriter ?? throw new ArgumentNullException(nameof(hostLogWriter));
        ConfigFilePath = configFilePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LabelFrame",
            "connection.json");

        var baseConfig = BuildBaseConfig(options);
        _config = LoadPersisted(baseConfig);
        _transport = CreateTransportOrDefault(_config, baseConfig);
    }

    /// <inheritdoc />
    public TransportConfig CurrentConfig
    {
        get { lock (_stateLock) return _config; }
    }

    /// <inheritdoc />
    public IPrintTransport CurrentTransport
    {
        get { lock (_stateLock) return _transport; }
    }

    /// <inheritdoc />
    public string ConfigFilePath { get; }

    /// <inheritdoc />
    public async Task<TransportChangeResult> ApplyAsync(TransportConfig config, bool testOnly, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(config);
        await _applyLock.WaitAsync(cancellationToken);
        try
        {
            return await ApplyCoreAsync(config, testOnly, cancellationToken);
        }
        finally
        {
            _applyLock.Release();
        }
    }

    private async Task<TransportChangeResult> ApplyCoreAsync(TransportConfig config, bool testOnly, CancellationToken cancellationToken)
    {
        // 旧格式（Mode + 平铺参数）→ 迁移为 pluginId + params；再同步旧字段供兼容消费
        if (config.Params.Count == 0 && config.Mode != TransportMode.Log)
        {
            config.MigrateFromLegacy();
        }

        config.SyncLegacyFields();

        var validationMessage = Validate(config);
        if (validationMessage is not null)
        {
            return new TransportChangeResult(false, validationMessage, _config);
        }

        IPrintTransport candidate;
        try
        {
            candidate = CreateTransport(config);
        }
        catch (Exception ex)
        {
            return new TransportChangeResult(false, $"连接配置无效：{ex.Message}", _config);
        }

        var testError = await TestAsync(candidate, cancellationToken);
        if (testError is not null)
        {
            return new TransportChangeResult(false, testError, _config);
        }

        if (testOnly)
        {
            return new TransportChangeResult(true, $"连接测试成功：{Describe(config)}。", _config);
        }

        TransportConfig applied;
        lock (_stateLock)
        {
            _config = config;
            _transport = candidate;
            applied = _config;
        }

        Persist(applied);
        return new TransportChangeResult(true, $"已切换为 {Describe(applied)}。", applied);
    }

    /// <summary>按插件参数规格校验：插件存在、必填、Int / Select 取值；含打印方式合法性（§5.4.2）。</summary>
    private string? Validate(TransportConfig config)
    {
        var plugin = _registry.GetPlugin(config.PluginId);
        if (plugin is null)
        {
            return $"传输插件不存在：{config.PluginId}。";
        }

        foreach (var spec in plugin.Parameters)
        {
            var visible = spec.IsVisible(config.Params);
            var present = config.Params.TryGetValue(spec.Key, out var rawValue) && !string.IsNullOrWhiteSpace(rawValue);
            // 必填只对当前可见字段生效（字段集按连接类型切换：不可见字段跳过必填，
            // 条件键自身的必填校验兜底）；类型 / Select 校验对已提供字段照常（存量配置携带隐藏字段值不报错）
            if (spec.Required && visible && !present)
            {
                return $"缺少必填参数：{spec.Label}（{spec.Key}）。";
            }

            if (!present)
            {
                continue;
            }

            switch (spec.Type)
            {
                case TransportParameterType.Int:
                    if (!int.TryParse(rawValue, out _))
                    {
                        return $"参数「{spec.Label}」必须是整数。";
                    }

                    break;
                case TransportParameterType.Select when spec.Options is { Count: > 0 }:
                    if (!spec.Options.Any(o => string.Equals(o.Value, rawValue, StringComparison.OrdinalIgnoreCase)))
                    {
                        return $"参数「{spec.Label}」取值无效：{rawValue}。";
                    }

                    break;
            }
        }

        // 打印方式合法性（§5.4.2 保存层校验）：printMode=native 仅对有文档编译能力的插件合法；
        // 缺失 / 非法取值不在此拦截（读取口径回退 image），存量异常配置（手改 connection.json /
        // 插件降级后能力消失）由提交链路兜底显式失败（LF_ENC_003），不静默按图片打印。
        if (TransportPrintMode.Resolve(config.Params.TryGetValue(TransportPrintMode.ParameterKey, out var printMode) ? printMode : null)
            == TransportPrintMode.Native
            && !plugin.SupportsDocumentCompile)
        {
            return $"当前打印方式为原生指令，但连接的插件 {config.PluginId} 不支持指令编译，请切回图片或更换插件。";
        }

        return null;
    }

    /// <summary>连接测试：传输实例实现 ITestableTransport 才测试（内置全部实现；外部插件可选实现）。</summary>
    private static async Task<string?> TestAsync(IPrintTransport transport, CancellationToken cancellationToken)
    {
        if (transport is not ITestableTransport testable)
        {
            return null;
        }

        try
        {
            return await testable.TestAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            return $"连接测试异常：{ex.Message}";
        }
    }

    /// <summary>按配置经注册表创建传输实例。</summary>
    private IPrintTransport CreateTransport(TransportConfig config)
        => _registry.CreateTransport(config.PluginId, new TransportPluginParameters(config.Params), _context);

    /// <summary>
    /// 启动装配护栏（迭代 63，决策 #123 / AC-01）：生效配置引用的插件未注册（如 zebra 外置后未安装、
    /// 升级自动安装失败）时回退默认连接并留痕，不因缺插件崩溃——「未装外置包则该品牌不可用」的可控行为；
    /// 默认连接本身也引用缺失插件（appsettings / 环境变量 Transport=Zebra 而插件未装）时再回退 Log。
    /// </summary>
    private IPrintTransport CreateTransportOrDefault(TransportConfig config, TransportConfig baseConfig)
    {
        try
        {
            return CreateTransport(config);
        }
        catch (Exception ex)
        {
            _hostLogWriter.WriteLine(
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] 当前连接引用的传输插件不可用（{config.PluginId}：{ex.Message}），已回退默认连接 {Describe(baseConfig)}。");
            try
            {
                _config = baseConfig;
                return CreateTransport(baseConfig);
            }
            catch (Exception baseEx)
            {
                var logConfig = new TransportConfig();
                _hostLogWriter.WriteLine(
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] 默认连接也引用不可用插件（{baseConfig.PluginId}：{baseEx.Message}），回退 Log 模拟连接。");
                _config = logConfig;
                return CreateTransport(logConfig);
            }
        }
    }

    /// <summary>连接展示文本（状态栏 / 徽标）：插件 Describe 优先，未知插件回退 ID。</summary>
    private string Describe(TransportConfig config)
        => _registry.Describe(config.PluginId, new TransportPluginParameters(config.Params));

    /// <summary>由 HostOptions（appsettings / 环境变量）构造基础配置（pluginId + params）。</summary>
    private static TransportConfig BuildBaseConfig(HostOptions options)
    {
        var config = new TransportConfig { PluginId = TransportConfig.MapModeToPluginId(options.Transport) };
        switch (options.Transport)
        {
            case TransportMode.Tcp:
                config.Params["host"] = options.TcpHost;
                config.Params["port"] = options.TcpPort.ToString(System.Globalization.CultureInfo.InvariantCulture);
                break;
            case TransportMode.WindowsDriver:
                config.Params["printerName"] = options.PrinterName;
                break;
            case TransportMode.Zebra:
                config.Params["kind"] = options.ZebraKind.ToString();
                if (options.ZebraKind == ZebraTransportKind.Tcp)
                {
                    config.Params["host"] = options.TcpHost;
                    config.Params["port"] = options.TcpPort.ToString(System.Globalization.CultureInfo.InvariantCulture);
                }
                else if (options.ZebraKind == ZebraTransportKind.Driver)
                {
                    config.Params["printerName"] = options.PrinterName;
                }
                else if (!string.IsNullOrWhiteSpace(options.ZebraUsbName))
                {
                    config.Params["usbName"] = options.ZebraUsbName;
                }

                break;
            case TransportMode.Log:
            default:
                break;
        }

        config.SyncLegacyFields();
        return config;
    }

    private TransportConfig LoadPersisted(TransportConfig baseConfig)
    {
        if (!File.Exists(ConfigFilePath))
        {
            return baseConfig;
        }

        try
        {
            var json = File.ReadAllText(ConfigFilePath);
            var persisted = TransportConfig.FromJson(json);
            if (persisted is null)
            {
                // 解析失败（损坏 JSON）回退默认连接：留痕原因与回退目标（决策 #108——此前静默回退，用户打印机配置「消失」无痕迹）
                WriteHostLog($"connection.json 解析失败（{ConfigFilePath}）：内容不是有效的连接配置，已回退默认连接 {Describe(baseConfig)}。");
                return baseConfig;
            }

            // 连接配置引用的传输插件已不存在（外部插件 DLL 被删除后重启）：
            // 回退默认连接并记录日志，宿主正常启动，不因缺失插件崩溃。
            if (_registry.GetPlugin(persisted.PluginId) is null)
            {
                _hostLogWriter.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] 连接配置引用的传输插件不存在（{persisted.PluginId}），已回退默认连接 {Describe(baseConfig)}。");
                return baseConfig;
            }

            // connection.json 优先级最高：存在即以它为当前连接（同插件缺失参数回退 baseConfig）
            if (string.Equals(persisted.PluginId, baseConfig.PluginId, StringComparison.OrdinalIgnoreCase))
            {
                foreach (var kv in baseConfig.Params)
                {
                    if (!persisted.Params.ContainsKey(kv.Key))
                    {
                        persisted.Params[kv.Key] = kv.Value;
                    }
                }
            }

            persisted.SyncLegacyFields();
            return persisted;
        }
        catch (Exception ex)
        {
            // 读取失败（IO / 权限等）回退默认连接：留痕原因与回退目标（决策 #108）
            WriteHostLog($"connection.json 读取失败（{ConfigFilePath}）：{ex.Message}，已回退默认连接 {Describe(baseConfig)}。");
            return baseConfig;
        }
    }

    /// <summary>回退留痕写宿主日志（host.log）：写入失败不影响回退决策。</summary>
    private void WriteHostLog(string message)
    {
        try
        {
            _hostLogWriter.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}");
            _hostLogWriter.Flush();
        }
        catch (Exception)
        {
            // 宿主日志不可用（降级 TextWriter.Null / 通道已停用）时留痕失败不阻断启动
        }
    }

    private void Persist(TransportConfig config)
    {
        try
        {
            var dir = Path.GetDirectoryName(ConfigFilePath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            File.WriteAllText(ConfigFilePath, config.ToJson());
        }
        catch (Exception ex)
        {
            // 持久化失败不阻断本次切换（下次启动回退 appsettings 默认）
            _hostLogWriter.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] 连接配置持久化失败（{ConfigFilePath}）：{ex.Message}");
        }
    }
    /// <summary>释放切换串行锁（宿主停机时由 DI 容器触发）。</summary>
    public void Dispose()
    {
        GC.SuppressFinalize(this);
        _applyLock.Dispose();
    }

}
