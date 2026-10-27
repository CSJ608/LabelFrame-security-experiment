using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.OS;
using Android.Runtime;
using Android.Text;
using Android.Views;
using Android.Widget;
using LabelFrame.AndroidHost.Errors;
using LabelFrame.AndroidHost.Updates;
using System.Globalization;
using System.Net.Http;
using System.Text.Json;

namespace LabelFrame.AndroidHost;

/// <summary>宿主配置页（唯一 UI）：主页运行状态 + 四个子页（服务器 / 打印机 / 插件管理 / 本机信息），
/// 保存即重启宿主服务生效；文案经资源 id 引用随系统语言，错误消息经 <see cref="LfErrorCatalog"/> 按码翻译。</summary>
[Activity(Label = "@string/app_name", MainLauncher = true, Exported = true, LaunchMode = LaunchMode.SingleTop,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.KeyboardHidden)]
public sealed partial class MainActivity : Activity
{

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(8) };

    // 插件包下载（服务器 → PDA）：包可达 64MB，WiFi 传输需要远超 8s 的预算
    private static readonly HttpClient HttpDownload = new() { Timeout = TimeSpan.FromMinutes(5) };

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private LinearLayout _root = null!;
    private LabelHostConfig _config = null!;

    private enum Screen
    {
        Home,
        Server,
        Printer,
        Device,
        Plugins,
    }

    private Screen _current = Screen.Home;
    private IDisposable? _editorLease;

    // 屏幕视图构建后缓存：往返导航不丢未保存的输入
    private View? _homeView;
    private View? _serverView;
    private View? _printerView;
    private View? _deviceView;
    private View? _pluginsView;

    /// <inheritdoc />
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        if (OperatingSystem.IsAndroidVersionAtLeast(33))
        {
            RequestPermissions([Android.Manifest.Permission.PostNotifications], 1);
        }

        // 打开配置页即确保后台服务在运行（幂等；重复 Start 不会重建已运行的服务）
        EnsureServiceStarted();

        _config = LabelHostConfig.Load(this);

        _root = new LinearLayout(this)
        {
            Orientation = Orientation.Vertical,
            LayoutParameters = new ViewGroup.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent),
        };
        _root.SetBackgroundColor(ColorBackground);
        SetContentView(_root);

        ShowScreen(Screen.Home);
    }

    /// <inheritdoc />
    protected override void OnResume()
    {
        base.OnResume();
        _autoRefresh = true;
        RefreshStatus();
        ScheduleAutoRefresh();
    }

    /// <inheritdoc />
    protected override void OnPause()
    {
        _autoRefresh = false;
        _updateInteraction.Pause();
        base.OnPause();
    }

    /// <inheritdoc />
    protected override void OnDestroy()
    {
        _editorLease?.Dispose();
        _editorLease = null;
        base.OnDestroy();
    }

    /// <summary>系统返回键：子页回主页，主页退出。</summary>
    // API 33 起 OnBackPressed 被标记过时（为预测性返回让路），但未启用 enableOnBackInvokedCallback
    // 时框架仍回调它；为避免引入 AndroidX 依赖按既有行为保留，屏蔽平台版本分析。
#pragma warning disable CA1422
    public override void OnBackPressed()
    {
        if (_current != Screen.Home)
        {
            ShowScreen(Screen.Home);
            return;
        }

        base.OnBackPressed();
    }
#pragma warning restore CA1422

    private void EnsureServiceStarted()
    {
        var service = new Intent(this, typeof(PrintHostService));
        if (OperatingSystem.IsAndroidVersionAtLeast(26))
        {
            StartForegroundService(service);
        }
        else
        {
            StartService(service);
        }
    }

    // 本地化辅助（文案一律资源 id 引用）

    private string L(int resId) => GetString(resId)!;

    private string L(int resId, params object?[] args)
        => string.Format(CultureInfo.InvariantCulture, GetString(resId)!, args);

    /// <summary>错误消息按码翻译：en 态已知码本地翻译、未知码 / 参数不全回退后端中文 message（语义对齐）。</summary>
    private string ResolveError(string? code, IReadOnlyDictionary<string, string>? parameters, string backendMessage)
        => LfErrorCatalog.Resolve(code, parameters, backendMessage, HostLocale.IsEnglish(this));

    // ---------- 屏幕切换 ----------

    private void ShowScreen(Screen screen)
    {
        if (screen != Screen.Device) _updateInteraction.Cancel();
        if (screen is not (Screen.Home or Screen.Device) && _editorLease is null)
        {
            _editorLease = MaintenanceGate.Shared.TryEnterEditor();
            if (_editorLease is null)
            {
                Toast.MakeText(this, L(Resource.String.update_maintenance), ToastLength.Long)?.Show();
                return;
            }
        }
        _current = screen;
        var view = screen switch
        {
            Screen.Server => _serverView ??= BuildServerScreen(),
            Screen.Printer => _printerView ??= BuildPrinterScreen(),
            Screen.Device => _deviceView ??= BuildDeviceScreen(),
            Screen.Plugins => _pluginsView ??= BuildPluginsScreen(),
            _ => _homeView ??= BuildHomeScreen(),
        };
        _root.RemoveAllViews();
        _root.AddView(view);
        if (screen == Screen.Home)
        {
            SyncInputsFromConfig();
            _editorLease?.Dispose();
            _editorLease = null;
            RefreshStatus();
        }
        else if (screen == Screen.Plugins)
        {
            // 每次进入刷新两份列表（已装 / 服务器可选）
            RunAsync(RefreshPluginsScreenAsync);
        }
        if (screen == Screen.Device) RefreshUpdateStatus();
    }

    private void ShowServer() => ShowScreen(Screen.Server);

    private void ShowPrinter() => ShowScreen(Screen.Printer);

    private void ShowPlugins() => ShowScreen(Screen.Plugins);

    private void ShowDevice() => ShowScreen(Screen.Device);

    /// <summary>从错误响应 JSON 里取错误视图（码 + 可选 params + 中文消息；取不到返回 null）。</summary>
    private static ErrorViewDto? TryReadErrorView(string body)
    {
        try
        {
            return JsonSerializer.Deserialize<ErrorViewDto>(body, Json);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>错误响应体的展示文案：按码翻译（en 态已知码本地翻译、未知码 / 参数不全回退后端中文）；非 JSON 原样返回。</summary>
    private string DescribeErrorBody(string body) =>
        TryReadErrorView(body) is { } view ? ResolveError(view.Code, view.Params, view.Message ?? body) : body;

    /// <summary>错误响应视图（本地 HTTP ErrorView，宽松反序列化：code / message / params）。</summary>
    private sealed record ErrorViewDto(string? Code, string? Message, IReadOnlyDictionary<string, string>? Params);

    // ---------- 保存并重启 ----------

    /// <summary>保存配置并重启宿主服务使其生效（传输 / 路由实例在服务启动时创建）。</summary>
    private async Task ApplyAsync(
        string? serverUrl = null,
        string? printerBrand = null,
        string? connectionType = null,
        string? tcpHost = null,
        int? tcpPort = null,
        string? bluetoothMac = null,
        string? deviceName = null)
    {
        using var write = MaintenanceGate.Shared.TryEnterWrite()
            ?? throw new InvalidOperationException("应用升级维护中，暂不能保存配置。");
        Toast.MakeText(this, L(Resource.String.apply_saving_toast), ToastLength.Short)?.Show();
        _config.Persist(this, serverUrl, printerBrand: printerBrand, connectionType: connectionType, tcpHost: tcpHost, tcpPort: tcpPort, bluetoothMac: bluetoothMac, deviceName: deviceName);
        await RestartServiceAsync();
        Toast.MakeText(this, L(Resource.String.apply_saved_toast), ToastLength.Short)?.Show();
    }

    /// <summary>重启宿主服务并等待本地 HTTP 就绪（配置保存 / 插件安装 / 卸载后共用——重启生效）。</summary>
    private async Task RestartServiceAsync()
    {
        using var write = MaintenanceGate.Shared.TryEnterWrite()
            ?? throw new InvalidOperationException("应用升级维护中，暂不能重启服务。");
        StopService(new Intent(this, typeof(PrintHostService)));
        await Task.Delay(800);
        EnsureServiceStarted();

        // 等本地 HTTP 就绪后再刷新界面
        for (var i = 0; i < 20; i++)
        {
            try
            {
                using var probe = await Http.GetAsync($"http://127.0.0.1:{LabelHostConfig.LocalPort}/healthz");
                if (probe.IsSuccessStatusCode)
                {
                    break;
                }
            }
            catch
            {
                // 服务未就绪，继续等待
            }

            await Task.Delay(500);
        }

        _config = LabelHostConfig.Load(this);
        _installedPluginsCache = null;
        // 品牌选项随插件装配变化：打印机子页缓存须失效，重进时重建
        _printerView = null;
        SyncInputsFromConfig();
        RefreshStatus();
    }

    /// <summary>保存 / 重启后把输入框刷成正在使用的配置（子页惰性构建，未打开的跳过）。</summary>
    private void SyncInputsFromConfig()
    {
        if (_serverInput is not null)
        {
            _serverInput.Text = _config.ServerUrl;
        }

        if (_connectionTypeGroup is not null && _ipInput is not null && _portInput is not null && _macInput is not null)
        {
            if (_brandGroup is not null)
            {
                _brandGroup.Check(BrandRadioOf(_config.PrinterBrand));
            }

            _connectionTypeGroup.Check(SelectedConnectionRadio());
            _ipInput.Text = _config.TcpHost;
            _portInput.Text = _config.TcpPort.ToString(CultureInfo.InvariantCulture);
            _macInput.Text = _config.BluetoothMac;
            UpdateConnectionFields();
        }

        if (_deviceIdText is not null)
        {
            _deviceIdText.Text = _config.DeviceId;
        }

        if (_deviceNameInput is not null)
        {
            _deviceNameInput.Text = _config.DeviceName;
        }
    }

    // ---------- 执行辅助 ----------

    /// <summary>后台执行并回 UI 线程展示结果（异常兜底提示重试）。</summary>
    private async void RunAsync(Func<Task> work)
    {
        try
        {
            await work();
        }
        catch (Exception ex)
        {
            HostLog.Warn(HostLog.Tags.Ui, $"操作执行失败：{ex.Message}");
            RunOnUiThread(() => Toast.MakeText(this, L(Resource.String.run_error_toast), ToastLength.Short)?.Show());
        }
    }
}
