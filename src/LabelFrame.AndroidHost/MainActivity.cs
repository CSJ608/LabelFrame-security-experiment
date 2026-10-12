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
using System.Globalization;
using System.Net.Http;
using System.Text.Json;

namespace LabelFrame.AndroidHost;

/// <summary>
/// 宿主配置页（唯一 UI）：主页展示运行状态与设置入口，「连接服务器 / 连接打印机 / 插件管理 / 本机信息」
/// 子页各自编辑并保存（保存即重启宿主服务生效）；设备号由系统唯一码自动生成只读展示。
/// 文案面向不懂技术的仓库用户：只说「这里填什么 / 点按钮会发生什么 / 状态意味着什么与下一步」。
/// 全部 UI 文案经 <c>Resources/values</c>（缺省中文）+
/// <c>values-en</c> 资源 id 引用，语言跟随系统；错误消息经 <see cref="LfErrorCatalog"/> 按码翻译。
/// </summary>
[Activity(Label = "@string/app_name", MainLauncher = true, Exported = true, LaunchMode = LaunchMode.SingleTop,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.KeyboardHidden)]
public sealed class MainActivity : Activity
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(8) };

    // 插件包下载（服务器 → PDA）：包可达 64MB，WiFi 传输需要远超 8s 的预算
    private static readonly HttpClient HttpDownload = new() { Timeout = TimeSpan.FromMinutes(5) };

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    // 语义色：绿 = 正常，红 = 异常，橙 = 提醒，蓝 = 主操作；不做装饰用色
    private static readonly Color ColorBackground = Color.Argb(0xFF, 0xF2, 0xF4, 0xF7);
    private static readonly Color ColorText = Color.Argb(0xFF, 0x1F, 0x23, 0x29);
    private static readonly Color ColorTextSecondary = Color.Argb(0xFF, 0x6B, 0x70, 0x75);
    private static readonly Color ColorOk = Color.Argb(0xFF, 0x1B, 0x7E, 0x3A);
    private static readonly Color ColorOkBg = Color.Argb(0xFF, 0xE9, 0xF6, 0xEC);
    private static readonly Color ColorErr = Color.Argb(0xFF, 0xC5, 0x22, 0x1F);
    private static readonly Color ColorErrBg = Color.Argb(0xFF, 0xFD, 0xEC, 0xEA);
    private static readonly Color ColorWarn = Color.Argb(0xFF, 0xB2, 0x5E, 0x00);
    private static readonly Color ColorWarnBg = Color.Argb(0xFF, 0xFF, 0xF3, 0xE0);
    private static readonly Color ColorMutedBg = Color.Argb(0xFF, 0xF1, 0xF3, 0xF5);
    private static readonly Color ColorPrimary = Color.Argb(0xFF, 0x1D, 0x6F, 0xE0);

    private const float RadiusCardDp = 12;
    private const float RadiusButtonDp = 10;

    private LinearLayout _root = null!;
    private LabelHostConfig _config = null!;

    // 主页动态区
    private LinearLayout _statusCard = null!;
    private TextView _statusSummary = null!;
    private TextView _statusServer = null!;
    private TextView _statusPrinter = null!;
    private TextView _serverEntrySummary = null!;
    private TextView _printerEntrySummary = null!;
    private TextView _deviceEntrySummary = null!;
    private TextView _pluginsEntrySummary = null!;

    // 服务器子页
    private EditText _serverInput = null!;
    private TextView _serverTestText = null!;
    private TextView _serverSaveHint = null!;

    // 打印机子页（/ ：品牌选项由内置 Zebra + 已装外置插件动态扩展）
    private RadioGroup _brandGroup = null!;
    private readonly Dictionary<int, string> _brandIds = new(); // 单选项 Id → 品牌（zebra 或插件 id）
    private RadioGroup _connectionTypeGroup = null!;
    private LinearLayout _connectionTypeCard = null!;
    private LinearLayout _tcpFields = null!;
    private EditText _ipInput = null!;
    private EditText _portInput = null!;
    private LinearLayout _bluetoothFields = null!;
    private EditText _macInput = null!;
    private LinearLayout _usbFields = null!;
    private TextView _printTestText = null!;
    private TextView _printerSaveHint = null!;

    // 插件管理子页
    private LinearLayout _installedList = null!;
    private TextView _installedSummary = null!;
    private LinearLayout _serverPackagesList = null!;
    private TextView _pluginStatusText = null!;
    private List<InstalledPluginDto>? _installedPluginsCache;

    // 本机信息子页
    private TextView _deviceIdText = null!;
    private EditText _deviceNameInput = null!;
    private TextView _deviceSaveHint = null!;

    private readonly Handler _refreshHandler = new(Looper.MainLooper!);
    private bool _autoRefresh;

    private enum Screen
    {
        Home,
        Server,
        Printer,
        Device,
        Plugins,
    }

    private Screen _current = Screen.Home;

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
        base.OnPause();
    }

    /// <summary>系统返回键：子页回主页，主页退出。</summary>
    // API 33 起 OnBackPressed 被标记过时（为预测性返回让路），但应用未启用 enableOnBackInvokedCallback
    // 时框架仍回调它；为避免引入 AndroidX 依赖，按既有行为保留并屏蔽平台版本分析。
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
            RefreshStatus();
        }
        else if (screen == Screen.Plugins)
        {
            // 每次进入刷新两份列表（已装 / 服务器可选）
            RunAsync(RefreshPluginsScreenAsync);
        }
    }

    // ---------- 主页 ----------

    private ScrollView BuildHomeScreen()
    {
        var content = ScrollColumn();

        content.AddView(PageTitle(L(Resource.String.app_name)));
        content.AddView(Subtle(L(Resource.String.home_subtitle)));
        content.AddView(Spacing(12));

        _statusCard = Card(ColorMutedBg);
        _statusCard.SetPadding(Dp(14), Dp(12), Dp(14), Dp(10));
        _statusSummary = TextView(string.Empty, 16, ColorText, bold: true);
        _statusServer = TextView(string.Empty, 13, ColorText);
        _statusPrinter = TextView(string.Empty, 13, ColorText);
        var autoNote = TextView(L(Resource.String.home_status_auto_refresh), 11, ColorTextSecondary);
        autoNote.SetPadding(0, Dp(6), 0, 0);
        _statusCard.AddView(_statusSummary);
        _statusCard.AddView(Spacing(2));
        _statusCard.AddView(_statusServer);
        _statusCard.AddView(Spacing(2));
        _statusCard.AddView(_statusPrinter);
        _statusCard.AddView(autoNote);
        content.AddView(_statusCard);
        content.AddView(Spacing(14));

        content.AddView(EntryRow(L(Resource.String.entry_server), ShowServer, out _serverEntrySummary));
        content.AddView(Spacing(10));
        content.AddView(EntryRow(L(Resource.String.entry_printer), ShowPrinter, out _printerEntrySummary));
        content.AddView(Spacing(10));
        content.AddView(EntryRow(L(Resource.String.entry_plugins), ShowPlugins, out _pluginsEntrySummary));
        content.AddView(Spacing(10));
        content.AddView(EntryRow(L(Resource.String.entry_device), ShowDevice, out _deviceEntrySummary));

        return WrapScroll(content);
    }

    private void ShowServer() => ShowScreen(Screen.Server);

    private void ShowPrinter() => ShowScreen(Screen.Printer);

    private void ShowPlugins() => ShowScreen(Screen.Plugins);

    private void ShowDevice() => ShowScreen(Screen.Device);

    /// <summary>设置入口行：左侧标题 + 当前值摘要，右侧「›」；整行可点、高度 ≥64dp。</summary>
    private LinearLayout EntryRow(string title, Action onClick, out TextView summary)
    {
        // 行内横向排列：文本列（宽度 0 + weight）+ 箭头。父容器必须是横向，
        // 竖向容器里宽度 0 的子视图会被压成 0 宽（文字逐字换行、卡片又高又空）。
        var row = Card(Color.White);
        row.Orientation = Orientation.Horizontal;
        row.SetPadding(Dp(16), Dp(12), Dp(12), Dp(12));
        row.SetMinimumHeight(Dp(64));

        var textColumn = new LinearLayout(this) { Orientation = Orientation.Vertical };
        textColumn.LayoutParameters = new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f)
        {
            Gravity = GravityFlags.CenterVertical,
        };
        var titleView = TextView(title, 15, ColorText, bold: true);
        titleView.LayoutParameters = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent);
        summary = TextView(string.Empty, 12.5f, ColorTextSecondary);
        summary.SetPadding(0, Dp(4), 0, 0);
        textColumn.AddView(titleView);
        textColumn.AddView(summary);
        row.AddView(textColumn);

        var chevron = TextView("›", 22, ColorTextSecondary);
        chevron.SetPadding(Dp(8), 0, 0, 0);
        chevron.LayoutParameters = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent)
        {
            Gravity = GravityFlags.CenterVertical,
        };
        row.AddView(chevron);

        row.Click += (_, _) => onClick();
        return row;
    }

    // ---------- 服务器子页 ----------

    private ScrollView BuildServerScreen()
    {
        var content = ScrollColumn();

        content.AddView(Header(L(Resource.String.server_title)));
        content.AddView(Subtle(L(Resource.String.server_hint)));
        content.AddView(Spacing(10));

        content.AddView(FieldLabel(L(Resource.String.server_field_label)));
        _serverInput = Input(L(Resource.String.server_input_hint), _config.ServerUrl, InputTypes.ClassText | InputTypes.TextVariationUri);
        content.AddView(_serverInput);
        content.AddView(Spacing(10));

        var testRow = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        testRow.LayoutParameters = new ViewGroup.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent);
        var testButton = ActionButton(L(Resource.String.server_test_button), () => RunAsync(TestServerAsync));
        _serverTestText = TextView(string.Empty, 13, ColorTextSecondary);
        _serverTestText.SetPadding(Dp(10), 0, 0, 0);
        _serverTestText.LayoutParameters = new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f)
        {
            Gravity = GravityFlags.CenterVertical,
        };
        testRow.AddView(testButton);
        testRow.AddView(_serverTestText);
        content.AddView(testRow);

        content.AddView(Spacing(20));
        _serverSaveHint = SaveHint();
        content.AddView(_serverSaveHint);
        content.AddView(SaveButton(() => RunAsync(SaveServerAsync)));

        return WrapScroll(content);
    }

    /// <summary>测试连接：探测服务端 healthz（用的是输入框里的地址，不用先保存）。</summary>
    private async Task TestServerAsync()
    {
        var url = (_serverInput.Text?.Trim() ?? string.Empty).TrimEnd('/');
        if (url.Length == 0)
        {
            SetResult(_serverTestText, L(Resource.String.server_test_empty), ColorTextSecondary);
            return;
        }

        if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            SetResult(_serverTestText, L(Resource.String.server_test_bad_scheme), ColorErr);
            return;
        }

        SetResult(_serverTestText, L(Resource.String.server_test_testing), ColorTextSecondary);
        try
        {
            using var response = await Http.GetAsync($"{url}/healthz");
            if (response.IsSuccessStatusCode)
            {
                SetResult(_serverTestText, L(Resource.String.server_test_ok), ColorOk);
            }
            else
            {
                SetResult(_serverTestText, L(Resource.String.server_test_http_error, (int)response.StatusCode), ColorErr);
            }
        }
        catch (Exception ex)
        {
            HostLog.Warn(HostLog.Tags.Ui, $"测试服务器连接失败（{url}）：{ex.Message}");
            SetResult(_serverTestText, L(Resource.String.error_server_unreachable), ColorErr);
        }
    }

    private async Task SaveServerAsync()
    {
        var url = (_serverInput.Text?.Trim() ?? string.Empty).TrimEnd('/');
        if (url.Length > 0
            && !url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            && !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            ShowSaveHint(_serverSaveHint, L(Resource.String.server_save_bad_scheme));
            return;
        }

        await ApplyAsync(serverUrl: url);
    }

    // ---------- 打印机子页 ----------

    private ScrollView BuildPrinterScreen()
    {
        var content = ScrollColumn();

        content.AddView(Header(L(Resource.String.printer_title)));
        content.AddView(Spacing(10));

        // 打印机品牌（/ / 预埋兑现）：Zebra 内置 + 已装外置插件动态扩展
        content.AddView(FieldLabel(L(Resource.String.printer_brand_label)));
        _brandGroup = new RadioGroup(this);
        _brandGroup.LayoutParameters = new ViewGroup.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent);
        _brandIds.Clear();
        var zebraRadio = ConnectionRadio(L(Resource.String.printer_brand_zebra), L(Resource.String.printer_brand_zebra_hint));
        zebraRadio.Id = 0x2001;
        _brandIds[0x2001] = LabelHostConfig.DefaultPrinterBrand;
        _brandGroup.AddView(zebraRadio);
        _brandGroup.Check(BrandRadioOf(_config.PrinterBrand));
        _brandGroup.CheckedChange += (_, _) => UpdateConnectionFields();
        content.AddView(_brandGroup);
        content.AddView(Spacing(6));
        content.AddView(Subtle(L(Resource.String.printer_brand_plugin_hint)));
        content.AddView(Spacing(10));
        RunAsync(RefreshBrandOptionsAsync);

        // 连接类型（网口默认且一级路径；蓝牙 / USB 为增量类型）——Zebra 专属，插件品牌隐藏整块
        _connectionTypeCard = new LinearLayout(this) { Orientation = Orientation.Vertical };
        _connectionTypeCard.LayoutParameters = new ViewGroup.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent);
        _connectionTypeCard.AddView(FieldLabel(L(Resource.String.conn_type_label)));
        _connectionTypeGroup = new RadioGroup(this);
        _connectionTypeGroup.LayoutParameters = new ViewGroup.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent);
        var tcpRadio = ConnectionRadio(L(Resource.String.conn_tcp), L(Resource.String.conn_tcp_hint));
        tcpRadio.Id = 0x1001;
        var bluetoothRadio = ConnectionRadio(L(Resource.String.conn_bluetooth), L(Resource.String.conn_bluetooth_hint));
        bluetoothRadio.Id = 0x1002;
        var usbRadio = ConnectionRadio(L(Resource.String.conn_usb), L(Resource.String.conn_usb_hint));
        usbRadio.Id = 0x1003;
        _connectionTypeGroup.AddView(tcpRadio);
        _connectionTypeGroup.AddView(bluetoothRadio);
        _connectionTypeGroup.AddView(usbRadio);
        _connectionTypeGroup.Check(SelectedConnectionRadio());
        _connectionTypeGroup.CheckedChange += (_, _) => UpdateConnectionFields();
        _connectionTypeCard.AddView(_connectionTypeGroup);
        content.AddView(_connectionTypeCard);
        content.AddView(Spacing(10));

        // 网口参数：IP + 端口
        _tcpFields = new LinearLayout(this) { Orientation = Orientation.Vertical };
        var fieldRow = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        fieldRow.LayoutParameters = new ViewGroup.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent);
        var ipColumn = new LinearLayout(this) { Orientation = Orientation.Vertical };
        ipColumn.LayoutParameters = new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 2.5f)
        {
            MarginEnd = Dp(10),
        };
        ipColumn.AddView(FieldLabel(L(Resource.String.printer_ip_label)));
        _ipInput = Input(L(Resource.String.printer_ip_hint), _config.TcpHost, InputTypes.ClassText);
        ipColumn.AddView(_ipInput);
        var portColumn = new LinearLayout(this) { Orientation = Orientation.Vertical };
        portColumn.LayoutParameters = new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f);
        portColumn.AddView(FieldLabel(L(Resource.String.printer_port_label)));
        _portInput = Input(L(Resource.String.printer_port_hint), _config.TcpPort.ToString(CultureInfo.InvariantCulture), InputTypes.ClassNumber);
        portColumn.AddView(_portInput);
        fieldRow.AddView(ipColumn);
        fieldRow.AddView(portColumn);
        _tcpFields.AddView(fieldRow);
        _tcpFields.AddView(Spacing(6));
        _tcpFields.AddView(Subtle(L(Resource.String.printer_tcp_note)));
        _tcpFields.LayoutParameters = new ViewGroup.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent);
        content.AddView(_tcpFields);

        // 蓝牙参数：MAC 地址手输（预授权：首版最简）
        _bluetoothFields = new LinearLayout(this) { Orientation = Orientation.Vertical };
        _bluetoothFields.LayoutParameters = new ViewGroup.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent);
        _bluetoothFields.AddView(FieldLabel(L(Resource.String.printer_bt_label)));
        _macInput = Input(L(Resource.String.printer_bt_hint), _config.BluetoothMac, InputTypes.ClassText | InputTypes.TextVariationVisiblePassword);
        _bluetoothFields.AddView(_macInput);
        _bluetoothFields.AddView(Spacing(6));
        _bluetoothFields.AddView(Subtle(L(Resource.String.printer_bt_note)));
        content.AddView(_bluetoothFields);

        // USB 参数：自动发现锁定第一台（无可选设备给可行动错误，预授权）
        _usbFields = new LinearLayout(this) { Orientation = Orientation.Vertical };
        _usbFields.LayoutParameters = new ViewGroup.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent);
        var usbNote = TextView(L(Resource.String.printer_usb_note), 12.5f, ColorTextSecondary);
        _usbFields.AddView(usbNote);
        content.AddView(_usbFields);
        UpdateConnectionFields();
        content.AddView(Spacing(10));

        var testRow = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        testRow.LayoutParameters = new ViewGroup.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent);
        var testButton = ActionButton(L(Resource.String.printer_test_button), () => RunAsync(TestPrintAsync));
        _printTestText = TextView(string.Empty, 13, ColorTextSecondary);
        _printTestText.SetPadding(Dp(10), 0, 0, 0);
        _printTestText.LayoutParameters = new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f)
        {
            Gravity = GravityFlags.CenterVertical,
        };
        testRow.AddView(testButton);
        testRow.AddView(_printTestText);
        content.AddView(testRow);

        content.AddView(Spacing(20));
        _printerSaveHint = SaveHint();
        content.AddView(_printerSaveHint);
        content.AddView(SaveButton(() => RunAsync(SavePrinterAsync)));

        return WrapScroll(content);
    }

    /// <summary>当前选中的品牌（内置 zebra 或外置插件 id；空选回退 zebra）。</summary>
    private string SelectedBrand()
        => _brandGroup is not null && _brandIds.TryGetValue(_brandGroup.CheckedRadioButtonId, out var brand) ? brand : LabelHostConfig.DefaultPrinterBrand;

    /// <summary>品牌对应的单选项 Id（未安装的品牌 / 未知值回退 Zebra）。</summary>
    private int BrandRadioOf(string brand)
        => string.Equals(brand.Trim(), LabelHostConfig.DefaultPrinterBrand, StringComparison.OrdinalIgnoreCase) || brand.Trim().Length == 0
            ? 0x2001
            : _brandIds.FirstOrDefault(kv => string.Equals(kv.Value, brand.Trim(), StringComparison.OrdinalIgnoreCase)).Key is var id and not 0
                ? id
                : 0x2001;

    /// <summary>刷新品牌选项（已装外部插件动态扩展，「品牌选项卡由已装插件扩展」兑现）。</summary>
    private async Task RefreshBrandOptionsAsync()
    {
        var installed = await FetchInstalledPluginsAsync();
        if (installed is null || _brandGroup is null)
        {
            return;
        }

        RunOnUiThread(() =>
        {
            try
            {
                var selected = SelectedBrand();
                for (var i = _brandGroup.ChildCount - 1; i >= 1; i--)
                {
                    _brandGroup.RemoveViewAt(i);
                }

                foreach (var stale in _brandIds.Where(kv => kv.Key != 0x2001).Select(kv => kv.Key).ToList())
                {
                    _brandIds.Remove(stale);
                }
                var index = 0;
                foreach (var plugin in installed.Where(p => p.Loaded && p.Source == "package"))
                {
                    var id = 0x2100 + index++;
                    var radio = ConnectionRadio(plugin.Name, L(Resource.String.printer_plugin_brand_radio, plugin.PluginId));
                    radio.Id = id;
                    _brandIds[id] = plugin.PluginId;
                    _brandGroup.AddView(radio);
                }

                _brandGroup.Check(BrandRadioOf(selected));
                if (!string.Equals(selected, LabelHostConfig.DefaultPrinterBrand, StringComparison.OrdinalIgnoreCase)
                    && BrandRadioOf(selected) == 0x2001)
                {
                    // 配置引用的插件品牌当前不可用（已卸载 / 加载失败）：状态栏摘要会显示回退，这里按 Zebra 呈现
                    HostLog.Warn(HostLog.Tags.Ui, $"配置的打印机品牌 {selected} 插件未装配，品牌选项回退 Zebra。");
                }

                UpdateConnectionFields();
            }
            catch (Exception ex)
            {
                HostLog.Warn(HostLog.Tags.Ui, $"刷新品牌选项失败：{ex.Message}");
            }
        });
    }

    /// <summary>连接方式单选项：圆角卡片行 + 标题说明，触达 ≥48dp。</summary>
    private RadioButton ConnectionRadio(string title, string hint)
    {
        var button = new RadioButton(this) { Text = title, TextSize = 15 };
        button.SetTextColor(ColorText);
        button.SetPadding(Dp(8), Dp(10), Dp(8), Dp(10));
        button.SetMinimumHeight(Dp(48));
        return button;
    }

    /// <summary>当前选中连接类型对应的存储值（插件品牌固定 tcp—— 配置结构零变更）。</summary>
    private string SelectedConnectionType()
        => IsPluginBrandSelected()
            ? LabelFrame.AndroidHost.Transport.ZebraSdkTransport.ConnectionTypeTcp
            : _connectionTypeGroup.CheckedRadioButtonId switch
            {
                0x1002 => LabelFrame.AndroidHost.Transport.ZebraSdkTransport.ConnectionTypeBluetooth,
                0x1003 => LabelFrame.AndroidHost.Transport.ZebraSdkTransport.ConnectionTypeUsb,
                _ => LabelFrame.AndroidHost.Transport.ZebraSdkTransport.ConnectionTypeTcp,
            };

    /// <summary>当前是否选中了外置插件品牌（非内置 Zebra）。</summary>
    private bool IsPluginBrandSelected()
        => !string.Equals(SelectedBrand(), LabelHostConfig.DefaultPrinterBrand, StringComparison.OrdinalIgnoreCase);

    /// <summary>当前配置连接类型对应的单选项 Id。</summary>
    private int SelectedConnectionRadio() => LabelFrame.AndroidHost.Transport.ZebraSdkTransport.NormalizeConnectionType(_config.ConnectionType) switch
    {
        LabelFrame.AndroidHost.Transport.ZebraSdkTransport.ConnectionTypeBluetooth => 0x1002,
        LabelFrame.AndroidHost.Transport.ZebraSdkTransport.ConnectionTypeUsb => 0x1003,
        _ => 0x1001,
    };

    /// <summary>按选中的品牌 / 连接方式切换参数区（插件品牌只显示 IP + 端口）。</summary>
    private void UpdateConnectionFields()
    {
        var pluginBrand = _brandGroup is not null && IsPluginBrandSelected();
        if (_connectionTypeCard is not null)
        {
            _connectionTypeCard.Visibility = pluginBrand ? ViewStates.Gone : ViewStates.Visible;
        }

        var type = SelectedConnectionType();
        _tcpFields.Visibility = type == LabelFrame.AndroidHost.Transport.ZebraSdkTransport.ConnectionTypeTcp ? ViewStates.Visible : ViewStates.Gone;
        _bluetoothFields.Visibility = !pluginBrand && type == LabelFrame.AndroidHost.Transport.ZebraSdkTransport.ConnectionTypeBluetooth ? ViewStates.Visible : ViewStates.Gone;
        _usbFields.Visibility = !pluginBrand && type == LabelFrame.AndroidHost.Transport.ZebraSdkTransport.ConnectionTypeUsb ? ViewStates.Visible : ViewStates.Gone;
    }

    /// <summary>测试打印：经本地 HTTP 提交内置测试标签，轮询终态。用的是已保存的地址——输入未保存时先提示保存。</summary>
    private async Task TestPrintAsync()
    {
        if (PrinterInputDirty())
        {
            SetResult(_printTestText, L(Resource.String.printer_test_dirty), ColorWarn);
            return;
        }

        SetResult(_printTestText, L(Resource.String.printer_test_sending), ColorTextSecondary);
        try
        {
            using var response = await Http.PostAsync($"http://127.0.0.1:{LabelHostConfig.LocalPort}/api/host/test-print", content: null);
            var body = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                // 错误码翻译：已知码本地翻译、未知码回退后端中文 message
                var detail = Truncate(DescribeErrorBody(body));
                SetResult(_printTestText, L(Resource.String.printer_test_http_error, detail), ColorErr);
                return;
            }

            var job = JsonSerializer.Deserialize<JobStatusDto>(body, Json);
            if (job is null)
            {
                SetResult(_printTestText, L(Resource.String.printer_test_parse_error), ColorErr);
                return;
            }

            for (var i = 0; i < 60; i++)
            {
                await Task.Delay(1000);
                using var poll = await Http.GetAsync($"http://127.0.0.1:{LabelHostConfig.LocalPort}/api/jobs/{job.JobId}");
                var pollBody = await poll.Content.ReadAsStringAsync();
                var current = JsonSerializer.Deserialize<JobStatusDto>(pollBody, Json);
                if (current is null)
                {
                    continue;
                }

                if (current.Status is "Completed")
                {
                    SetResult(_printTestText, L(Resource.String.printer_test_ok, current.CompletedItems, current.TotalItems), ColorOk);
                    return;
                }

                if (current.Status is "Failed" or "Cancelled")
                {
                    var failedItem = current.Items?.FirstOrDefault(i2 => i2.ErrorMessage is not null);
                    // 失败项按码翻译（项级无 params：模板带参的码自然回退中文，语义对齐）
                    var reason = ResolveError(failedItem?.ErrorCode, null, failedItem?.ErrorMessage ?? current.Status);
                    SetResult(_printTestText, PrintFailText(reason), ColorErr);
                    return;
                }

                SetResult(_printTestText, L(Resource.String.printer_test_printing), ColorTextSecondary);
            }

            SetResult(_printTestText, L(Resource.String.printer_test_timeout), ColorWarn);
        }
        catch (Exception ex)
        {
            HostLog.Warn(HostLog.Tags.Ui, $"测试打印失败：{ex.Message}");
            SetResult(_printTestText, L(Resource.String.printer_test_no_service), ColorErr);
        }
    }

    /// <summary>打印失败的可行动提示；原始原因作为第二行小字附后，供管理员远程排障。</summary>
    private string PrintFailText(string reason) =>
        L(Resource.String.printer_test_fail, reason);

    /// <summary>输入的打印机配置与已保存（正在使用）的是否不一致（品牌 + 按连接类型比较对应参数）。</summary>
    private bool PrinterInputDirty()
    {
        if (!string.Equals(SelectedBrand(), _config.PrinterBrand.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (IsPluginBrandSelected())
        {
            var pluginIp = _ipInput.Text?.Trim() ?? string.Empty;
            var pluginPort = int.TryParse(_portInput.Text?.Trim(), out var parsedPort) ? parsedPort : -1;
            return pluginIp != _config.TcpHost || pluginPort != _config.TcpPort;
        }

        if (SelectedConnectionType() != LabelFrame.AndroidHost.Transport.ZebraSdkTransport.NormalizeConnectionType(_config.ConnectionType))
        {
            return true;
        }

        if (SelectedConnectionType() == LabelFrame.AndroidHost.Transport.ZebraSdkTransport.ConnectionTypeBluetooth)
        {
            return (_macInput.Text?.Trim() ?? string.Empty) != _config.BluetoothMac;
        }

        var ip = _ipInput.Text?.Trim() ?? string.Empty;
        var port = int.TryParse(_portInput.Text?.Trim(), out var parsed) ? parsed : -1;
        return ip != _config.TcpHost || port != _config.TcpPort;
    }

    /// <summary>蓝牙地址格式校验：12 位十六进制，冒号分隔（AA:BB:CC:DD:EE:FF）或连写（AABBCCDDEEFF）均可。</summary>
    private static bool IsValidBluetoothMac(string value) =>
        value.Length is 12 or 17 && value.Replace(":", string.Empty).Length == 12
        && value.Replace(":", string.Empty).All(char.IsAsciiHexDigit);

    private async Task SavePrinterAsync()
    {
        var brand = SelectedBrand();
        var type = SelectedConnectionType();

        // IP / 端口校验（插件品牌固定网口；Zebra 网口同样必填）
        if (IsPluginBrandSelected() || type == LabelFrame.AndroidHost.Transport.ZebraSdkTransport.ConnectionTypeTcp)
        {
            var ip = _ipInput.Text?.Trim() ?? string.Empty;
            if (ip.Length == 0)
            {
                ShowSaveHint(_printerSaveHint, L(Resource.String.printer_save_no_ip));
                return;
            }

            if (!int.TryParse(_portInput.Text?.Trim(), out var port) || port is < 1 or > 65535)
            {
                ShowSaveHint(_printerSaveHint, L(Resource.String.printer_save_bad_port));
                return;
            }

            await ApplyAsync(printerBrand: brand, connectionType: type, tcpHost: ip, tcpPort: port);
            return;
        }

        string? mac = null;
        if (type == LabelFrame.AndroidHost.Transport.ZebraSdkTransport.ConnectionTypeBluetooth)
        {
            mac = _macInput.Text?.Trim() ?? string.Empty;
            if (!IsValidBluetoothMac(mac))
            {
                ShowSaveHint(_printerSaveHint, L(Resource.String.printer_save_bad_mac));
                return;
            }

            RequestBluetoothPermissionIfNeeded();
        }

        await ApplyAsync(printerBrand: brand, connectionType: type, bluetoothMac: mac);
    }

    /// <summary>Android 12+ 蓝牙连接需要「附近的设备」运行时权限：选蓝牙保存时顺带请求（拒绝不阻塞保存，打印 / 测试时会再提示）。</summary>
    private void RequestBluetoothPermissionIfNeeded()
    {
        if (OperatingSystem.IsAndroidVersionAtLeast(31)
            && CheckSelfPermission(Android.Manifest.Permission.BluetoothConnect) != Android.Content.PM.Permission.Granted)
        {
            RequestPermissions([Android.Manifest.Permission.BluetoothConnect], 2);
        }
    }

    // 插件管理子页（已装列表 + 浏览服务端安装 + 卸载，重启生效）

    private ScrollView BuildPluginsScreen()
    {
        var content = ScrollColumn();

        content.AddView(Header(L(Resource.String.plugins_title)));
        content.AddView(Spacing(4));
        content.AddView(Subtle(L(Resource.String.plugins_hint)));
        content.AddView(Spacing(10));

        content.AddView(FieldLabel(L(Resource.String.plugins_installed_label)));
        _installedSummary = TextView(L(Resource.String.plugins_loading), 13, ColorTextSecondary);
        content.AddView(_installedSummary);
        content.AddView(Spacing(6));
        _installedList = new LinearLayout(this) { Orientation = Orientation.Vertical };
        _installedList.LayoutParameters = new ViewGroup.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent);
        content.AddView(_installedList);

        content.AddView(Spacing(16));
        content.AddView(FieldLabel(L(Resource.String.plugins_server_label)));
        _pluginStatusText = TextView(string.Empty, 13, ColorTextSecondary);
        content.AddView(_pluginStatusText);
        content.AddView(Spacing(6));
        _serverPackagesList = new LinearLayout(this) { Orientation = Orientation.Vertical };
        _serverPackagesList.LayoutParameters = new ViewGroup.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent);
        content.AddView(_serverPackagesList);

        return WrapScroll(content);
    }

    /// <summary>刷新插件子页两份列表：本机已装（本地 HTTP）+ 服务器可选（plugin-packages）。</summary>
    private async Task RefreshPluginsScreenAsync()
    {
        var installed = await FetchInstalledPluginsAsync();
        if (installed is not null)
        {
            _installedPluginsCache = installed;
            RunOnUiThread(() => RenderInstalledPlugins(installed));
        }

        await RefreshServerPackagesAsync();
        RefreshStatus();
    }

    /// <summary>渲染已装列表卡片（package 来源可卸载；加载失败给原因，不阻断）。</summary>
    private void RenderInstalledPlugins(IReadOnlyList<InstalledPluginDto> installed)
    {
        if (_installedList is null)
        {
            return;
        }

        _installedList.RemoveAllViews();
        var packages = installed.Where(p => p.Source == "package").ToList();
        _installedSummary.Text = packages.Count == 0
            ? L(Resource.String.plugins_installed_empty)
            : L(Resource.String.plugins_installed_count, packages.Count);

        foreach (var plugin in packages)
        {
            var pluginId = plugin.PluginId;
            var statusText = plugin.Loaded
                ? L(Resource.String.plugins_item_loaded)
                : L(Resource.String.plugins_item_not_loaded, plugin.LoadError ?? L(Resource.String.plugins_item_not_loaded_fallback));
            var card = Card(plugin.Loaded ? Color.White : ColorWarnBg);
            card.SetPadding(Dp(14), Dp(12), Dp(14), Dp(10));

            var title = TextView(L(Resource.String.common_name_version, plugin.Name, plugin.Version), 15, ColorText, bold: true);
            var idLine = TextView(pluginId, 12, ColorTextSecondary);
            var status = TextView(statusText, 12.5f, plugin.Loaded ? ColorOk : ColorWarn);
            status.SetPadding(0, Dp(4), 0, 0);
            card.AddView(title);
            card.AddView(idLine);
            card.AddView(status);

            var buttonRow = new LinearLayout(this) { Orientation = Orientation.Horizontal };
            buttonRow.SetPadding(0, Dp(8), 0, 0);
            // 卸载 = 销毁类操作——红底白字 danger 视觉，点击先弹原生确认对话框
            var uninstallButton = DangerButton(L(Resource.String.plugins_uninstall), () => ConfirmUninstallPlugin(pluginId, plugin.Name), Dp(88));
            buttonRow.AddView(uninstallButton);
            card.AddView(buttonRow);

            _installedList.AddView(card);
            _installedList.AddView(Spacing(8));
        }
    }

    /// <summary>拉取服务器插件包列表并渲染（服务器地址未配置给可行动提示）。</summary>
    private async Task RefreshServerPackagesAsync()
    {
        if (_serverPackagesList is null)
        {
            return;
        }

        var serverUrl = _config.ServerUrl.TrimEnd('/');
        if (serverUrl.Length == 0)
        {
            RunOnUiThread(() =>
            {
                _serverPackagesList.RemoveAllViews();
                _pluginStatusText.Text = L(Resource.String.plugins_server_no_url);
                _pluginStatusText.SetTextColor(ColorWarn);
            });
            return;
        }

        RunOnUiThread(() => _pluginStatusText.Text = L(Resource.String.plugins_server_loading));
        try
        {
            using var response = await Http.GetAsync($"{serverUrl}/api/plugin-packages");
            if (!response.IsSuccessStatusCode)
            {
                RunOnUiThread(() =>
                {
                    _serverPackagesList.RemoveAllViews();
                    _pluginStatusText.Text = L(Resource.String.plugins_server_http_error, (int)response.StatusCode);
                    _pluginStatusText.SetTextColor(ColorErr);
                });
                return;
            }

            var body = await response.Content.ReadAsStringAsync();
            var packages = JsonSerializer.Deserialize<List<ServerPluginPackageDto>>(body, Json) ?? [];
            RunOnUiThread(() => RenderServerPackages(serverUrl, packages));
        }
        catch (Exception ex)
        {
            HostLog.Warn(HostLog.Tags.Ui, $"读取服务器插件列表失败（{serverUrl}）：{ex.Message}");
            RunOnUiThread(() =>
            {
                _serverPackagesList.RemoveAllViews();
                _pluginStatusText.Text = L(Resource.String.error_server_unreachable);
                _pluginStatusText.SetTextColor(ColorErr);
            });
        }
    }

    /// <summary>渲染服务器插件包卡片（invalid 条目跳过；平台标记透出——PDA 只能装 android 包）。</summary>
    private void RenderServerPackages(string serverUrl, IReadOnlyList<ServerPluginPackageDto> packages)
    {
        _serverPackagesList.RemoveAllViews();
        var valid = packages.Where(p => p.Valid).ToList();
        _pluginStatusText.Text = valid.Count == 0
            ? L(Resource.String.plugins_server_empty)
            : L(Resource.String.plugins_server_count, valid.Count);
        _pluginStatusText.SetTextColor(ColorTextSecondary);

        foreach (var package in valid)
        {
            var captured = package;
            var platforms = captured.Platforms is { Count: > 0 } list ? string.Join(L(Resource.String.common_list_separator), list) : "windows";
            var isAndroid = captured.Platforms?.Contains("android", StringComparer.OrdinalIgnoreCase) == true;
            var card = Card(Color.White);
            card.SetPadding(Dp(14), Dp(12), Dp(14), Dp(10));

            var title = TextView(L(Resource.String.common_name_version, captured.Name, captured.Version), 15, ColorText, bold: true);
            var idLine = TextView(L(Resource.String.plugins_server_platforms, captured.PluginId, platforms), 12, ColorTextSecondary);
            card.AddView(title);
            card.AddView(idLine);
            if (!string.IsNullOrWhiteSpace(captured.Description))
            {
                card.AddView(TextView(captured.Description, 12.5f, ColorTextSecondary));
            }

            var buttonRow = new LinearLayout(this) { Orientation = Orientation.Horizontal };
            buttonRow.SetPadding(0, Dp(8), 0, 0);
            var installButton = ActionButton(L(Resource.String.plugins_install), () => RunAsync(() => InstallPluginAsync(serverUrl, captured)), Dp(88));
            if (!isAndroid)
            {
                installButton.Enabled = false;
                installButton.Text = L(Resource.String.plugins_windows_only);
            }

            buttonRow.AddView(installButton);
            card.AddView(buttonRow);

            _serverPackagesList.AddView(card);
            _serverPackagesList.AddView(Spacing(8));
        }
    }

    /// <summary>从服务器下载插件包并经本地 HTTP 安装（三层校验含平台门）→ 重启打印服务生效。</summary>
    private async Task InstallPluginAsync(string serverUrl, ServerPluginPackageDto package)
    {
        RunOnUiThread(() =>
        {
            _pluginStatusText.Text = L(Resource.String.plugins_downloading, package.Name);
            _pluginStatusText.SetTextColor(ColorTextSecondary);
        });

        byte[] bytes;
        try
        {
            bytes = await HttpDownload.GetByteArrayAsync($"{serverUrl}{package.Url}");
        }
        catch (Exception ex)
        {
            HostLog.Warn(HostLog.Tags.Ui, $"下载插件包失败（{package.FileName}）：{ex.Message}");
            RunOnUiThread(() => SetResult(_pluginStatusText, L(Resource.String.plugins_download_error), ColorErr));
            return;
        }

        RunOnUiThread(() => _pluginStatusText.Text = L(Resource.String.plugins_installing));
        try
        {
            using var response = await Http.PostAsync(
                $"http://127.0.0.1:{LabelHostConfig.LocalPort}/api/plugins/install",
                new ByteArrayContent(bytes));
            var body = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                var error = TryReadErrorView(body) is { } view
                    ? ResolveError(view.Code, view.Params, view.Message ?? string.Empty)
                    : L(Resource.String.plugins_install_http_fallback, (int)response.StatusCode);
                RunOnUiThread(() => SetResult(_pluginStatusText, L(Resource.String.error_prefix, error), ColorErr));
                return;
            }

            // 安装成功：重启打印服务生效（装配新插件 + 品牌选项扩展）
            await RestartServiceAsync();
            RunOnUiThread(() =>
            {
                Toast.MakeText(this, L(Resource.String.plugins_installed_toast, package.Name), ToastLength.Long)?.Show();
                SetResult(_pluginStatusText, L(Resource.String.plugins_installed_ok, package.Name), ColorOk);
            });
            await RefreshPluginsScreenAsync();
        }
        catch (Exception ex)
        {
            HostLog.Warn(HostLog.Tags.Ui, $"安装插件失败（{package.FileName}）：{ex.Message}");
            RunOnUiThread(() => SetResult(_pluginStatusText, L(Resource.String.plugins_install_no_service), ColorErr));
        }
    }

    /// <summary>
    /// 卸载确认（/ ，：销毁类操作必须先确认）——原生 AlertDialog，danger 文案与视觉：
    /// 标题 + 后果说明（含不可恢复语义与自动重启提示）+ 确认按钮红字；确认后才执行卸载（删插件目录并重启打印服务）。
    /// 文案双语化，仅换文案来源——danger 视觉（确认红字 / 取消次要色）与确认语义不变。
    /// </summary>
    private void ConfirmUninstallPlugin(string pluginId, string pluginName)
    {
        var builder = new AlertDialog.Builder(this);
        builder.SetTitle(L(Resource.String.plugins_uninstall_dialog_title));
        builder.SetMessage(L(Resource.String.plugins_uninstall_dialog_message, pluginName));
        builder.SetNegativeButton(L(Resource.String.dialog_cancel), (_, _) => { });
        builder.SetPositiveButton(L(Resource.String.plugins_uninstall), (_, _) => RunAsync(() => UninstallPluginAsync(pluginId)));
        // 绑定注解将 Create() 标为可空（Java 契约实际不返回 null），按仓库 0 警口径显式断言非空（与 Typeface.Monospace! 同款）
        var dialog = builder.Create()!;
        dialog.Show();
        // danger 视觉：确认（卸载）按钮红字警示；取消为常规次要色
        dialog.GetButton((int)DialogButtonType.Positive)?.SetTextColor(ColorErr);
        dialog.GetButton((int)DialogButtonType.Negative)?.SetTextColor(ColorTextSecondary);
    }

    /// <summary>卸载已装插件（本地 HTTP）→ 重启打印服务生效；卸载后如配置还指向该品牌则回退 Zebra。</summary>
    private async Task UninstallPluginAsync(string pluginId)
    {
        try
        {
            using var response = await Http.PostAsync(
                $"http://127.0.0.1:{LabelHostConfig.LocalPort}/api/plugins/uninstall",
                new StringContent(JsonSerializer.Serialize(new { pluginId }, Json), System.Text.Encoding.UTF8, "application/json"));
            var body = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                var error = TryReadErrorView(body) is { } view
                    ? ResolveError(view.Code, view.Params, view.Message ?? string.Empty)
                    : L(Resource.String.plugins_uninstall_http_fallback, (int)response.StatusCode);
                RunOnUiThread(() => Toast.MakeText(this, L(Resource.String.error_prefix, error), ToastLength.Long)?.Show());
                return;
            }

            // 配置还指向被卸载品牌时回退 Zebra（宿主路由同样回退，这里同步配置避免界面悬空）
            if (string.Equals(_config.PrinterBrand.Trim(), pluginId, StringComparison.OrdinalIgnoreCase))
            {
                _config.Persist(this, printerBrand: LabelHostConfig.DefaultPrinterBrand);
            }

            await RestartServiceAsync();
            RunOnUiThread(() => Toast.MakeText(this, L(Resource.String.plugins_uninstalled_toast, pluginId), ToastLength.Long)?.Show());
            await RefreshPluginsScreenAsync();
        }
        catch (Exception ex)
        {
            HostLog.Warn(HostLog.Tags.Ui, $"卸载插件失败（{pluginId}）：{ex.Message}");
            RunOnUiThread(() => Toast.MakeText(this, L(Resource.String.plugins_uninstall_error_toast), ToastLength.Long)?.Show());
        }
    }

    /// <summary>读取本机已装插件列表（本地 HTTP；服务未就绪返回 null）。</summary>
    private static async Task<List<InstalledPluginDto>?> FetchInstalledPluginsAsync()
    {
        try
        {
            using var response = await Http.GetAsync($"http://127.0.0.1:{LabelHostConfig.LocalPort}/api/plugins/installed");
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var body = await response.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<List<InstalledPluginDto>>(body, Json);
        }
        catch (Exception ex)
        {
            HostLog.Warn(HostLog.Tags.Ui, $"读取已装插件列表失败：{ex.Message}");
            return null;
        }
    }

    /// <summary>从错误响应 JSON 里取错误视图（码 + 可选 params + 中文消息；取不到返回 null）。
    /// 形状对齐本地 HTTP / Server 的 ErrorView（加 params，PDA 侧接入）。</summary>
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

    /// <summary>本机已装插件视图（GET /api/plugins/installed 响应形状，PascalCase）。</summary>
    private sealed record InstalledPluginDto(
        string PluginId,
        string Name,
        string Version,
        string? Description,
        bool Loaded,
        string? LoadError,
        string? PackageDir,
        string Source);

    /// <summary>服务器插件包视图（GET /api/plugin-packages 列表项，camelCase）。</summary>
    private sealed record ServerPluginPackageDto(
        string FileName,
        string? PluginId,
        string? Name,
        string? Version,
        string? Description,
        string Url,
        bool Valid,
        IReadOnlyList<string>? Platforms);

    /// <summary>错误响应视图（本地 HTTP ErrorView，宽松反序列化：code / message / params）。</summary>
    private sealed record ErrorViewDto(string? Code, string? Message, IReadOnlyDictionary<string, string>? Params);

    // ---------- 本机信息子页 ----------

    private ScrollView BuildDeviceScreen()
    {
        var content = ScrollColumn();

        content.AddView(Header(L(Resource.String.device_title)));
        content.AddView(Spacing(10));

        content.AddView(FieldLabel(L(Resource.String.device_id_label)));
        var idRow = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        idRow.LayoutParameters = new ViewGroup.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent);
        _deviceIdText = TextView(_config.DeviceId, 14, ColorText);
        _deviceIdText.SetTypeface(Android.Graphics.Typeface.Monospace!, Android.Graphics.TypefaceStyle.Normal);
        _deviceIdText.LayoutParameters = new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f)
        {
            Gravity = GravityFlags.CenterVertical,
        };
        var copyButton = ActionButton(L(Resource.String.device_copy), CopyDeviceId, Dp(72));
        idRow.AddView(_deviceIdText);
        idRow.AddView(copyButton);
        content.AddView(idRow);
        content.AddView(Spacing(14));

        content.AddView(FieldLabel(L(Resource.String.device_name_label)));
        _deviceNameInput = Input(L(Resource.String.device_name_hint), _config.DeviceName, InputTypes.ClassText);
        content.AddView(_deviceNameInput);
        content.AddView(Spacing(6));
        content.AddView(Subtle(L(Resource.String.device_name_note)));
        content.AddView(Spacing(20));

        _deviceSaveHint = SaveHint();
        content.AddView(_deviceSaveHint);
        content.AddView(SaveButton(() => RunAsync(SaveDeviceAsync)));
        content.AddView(Spacing(16));

        content.AddView(FieldLabel(L(Resource.String.device_version_label)));
        content.AddView(TextView(HostInfo.GetVersion(this), 14, ColorText));
        content.AddView(Spacing(4));
        content.AddView(Subtle(L(Resource.String.device_version_note)));

        return WrapScroll(content);
    }

    private void CopyDeviceId()
    {
        var clipboard = GetSystemService(ClipboardService)?.JavaCast<Android.Content.ClipboardManager>();
        clipboard?.PrimaryClip = ClipData.NewPlainText(L(Resource.String.device_clipboard_label), _config.DeviceId);
        Toast.MakeText(this, L(Resource.String.device_copied_toast), ToastLength.Short)?.Show();
    }

    private async Task SaveDeviceAsync() =>
        await ApplyAsync(deviceName: _deviceNameInput.Text?.Trim());

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
        Toast.MakeText(this, L(Resource.String.apply_saving_toast), ToastLength.Short)?.Show();
        _config.Persist(this, serverUrl, printerBrand: printerBrand, connectionType: connectionType, tcpHost: tcpHost, tcpPort: tcpPort, bluetoothMac: bluetoothMac, deviceName: deviceName);
        await RestartServiceAsync();
        Toast.MakeText(this, L(Resource.String.apply_saved_toast), ToastLength.Short)?.Show();
    }

    /// <summary>重启宿主服务并等待本地 HTTP 就绪（配置保存 / 插件安装 / 卸载后共用——重启生效）。</summary>
    private async Task RestartServiceAsync()
    {
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
        // 品牌选项随插件装配变化：打印机子页缓存失效，重进时重建（品牌选项卡由已装插件扩展）
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

    // ---------- 状态刷新 ----------

    private void ScheduleAutoRefresh()
    {
        _refreshHandler.PostDelayed(() =>
        {
            if (!_autoRefresh)
            {
                return;
            }

            RefreshStatus();
            ScheduleAutoRefresh();
        }, 5000);
    }

    private void RefreshStatus()
    {
        if (_statusSummary is null || _serverEntrySummary is null)
        {
            return;
        }

        var view = ComputeStatus();

        _statusCard.Background = RoundDrawable(view.SummaryBg);
        _statusSummary.SetTextColor(view.SummaryColor);
        _statusSummary.Text = view.Summary;
        _statusServer.Text = view.ServerLine;
        _statusPrinter.Text = view.PrinterLine;
        _serverEntrySummary.Text = view.ServerEntry;
        _printerEntrySummary.Text = view.PrinterEntry;
        _pluginsEntrySummary.Text = _installedPluginsCache is null
            ? L(Resource.String.home_plugins_entry_hint)
            : L(Resource.String.home_plugins_entry_count, _installedPluginsCache.Count);
        _deviceEntrySummary.Text = _config.DeviceName;
    }

    private sealed record StatusView(
        Color SummaryColor, Color SummaryBg, string Summary,
        string ServerLine, string PrinterLine, string ServerEntry, string PrinterEntry);

    private StatusView ComputeStatus()
    {
        var s = HostStatus.Current;
        static string Local(DateTime? utc) => utc is null ? string.Empty : utc.Value.ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture);

        var serverConfigured = s.ActiveServerUrl.Length > 0;
        var serverError = serverConfigured && s.LastServerError is not null;
        var serverOk = serverConfigured && !serverError && s.LastServerContactUtc is not null;

        string serverLine, serverEntry;
        if (!serverConfigured)
        {
            serverLine = L(Resource.String.status_server_unset);
            serverEntry = L(Resource.String.status_server_entry_unset);
        }
        else if (serverError)
        {
            serverLine = L(Resource.String.status_server_error);
            serverEntry = L(Resource.String.status_server_entry_error, UrlHost(s.ActiveServerUrl));
        }
        else if (serverOk)
        {
            serverLine = L(Resource.String.status_server_ok, Local(s.LastServerContactUtc));
            serverEntry = L(Resource.String.status_server_entry_ok, UrlHost(s.ActiveServerUrl));
        }
        else
        {
            serverLine = L(Resource.String.status_server_connecting);
            serverEntry = L(Resource.String.status_server_entry_connecting, UrlHost(s.ActiveServerUrl));
        }

        // ActivePrinterEndpoint 已是按连接类型生成的用户可读摘要（网口 IP / 蓝牙地址 / USB 数据线）
        var printerDisplay = s.ActivePrinterEndpoint;
        var printerError = s.LastPrintError is not null;
        string printerLine, printerEntry;
        if (printerError)
        {
            printerLine = L(Resource.String.status_printer_error);
            printerEntry = L(Resource.String.status_printer_entry_error, printerDisplay);
        }
        else if (s.LastPrintUtc is not null)
        {
            printerLine = L(Resource.String.status_printer_ok, Local(s.LastPrintUtc), printerDisplay);
            printerEntry = L(Resource.String.status_printer_entry_ok, printerDisplay);
        }
        else
        {
            printerLine = L(Resource.String.status_printer_never, printerDisplay);
            printerEntry = L(Resource.String.status_printer_entry_never, printerDisplay);
        }

        Color summaryColor, summaryBg;
        string summary;
        if (!s.ServiceRunning)
        {
            summaryColor = ColorTextSecondary;
            summaryBg = ColorMutedBg;
            summary = L(Resource.String.status_summary_service_stopped);
        }
        else if (serverError && printerError)
        {
            summaryColor = ColorErr;
            summaryBg = ColorErrBg;
            summary = L(Resource.String.status_summary_both_error);
        }
        else if (serverError)
        {
            summaryColor = ColorErr;
            summaryBg = ColorErrBg;
            summary = L(Resource.String.status_summary_server_error);
        }
        else if (printerError)
        {
            summaryColor = ColorErr;
            summaryBg = ColorErrBg;
            summary = L(Resource.String.status_summary_printer_error);
        }
        else
        {
            summaryColor = ColorOk;
            summaryBg = ColorOkBg;
            summary = L(Resource.String.status_summary_ok);
        }

        return new StatusView(summaryColor, summaryBg, summary, serverLine, printerLine, serverEntry, printerEntry);
    }

    /// <summary>取 URL 里的主机部分（只给用户看，去掉协议与端口）。</summary>
    private static string UrlHost(string url)
    {
        var noScheme = url.Replace("http://", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("https://", string.Empty, StringComparison.OrdinalIgnoreCase);
        var colon = noScheme.IndexOf(':');
        return colon > 0 ? noScheme[..colon] : noScheme;
    }

    // ---------- 通用控件（代码布局，无资源依赖） ----------

    private int Dp(float value) => (int)(value * Resources!.DisplayMetrics!.Density);

    private LinearLayout ScrollColumn()
    {
        var column = new LinearLayout(this)
        {
            Orientation = Orientation.Vertical,
            LayoutParameters = new ViewGroup.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent),
        };
        column.SetPadding(Dp(16), Dp(16), Dp(16), Dp(16));
        return column;
    }

    private ScrollView WrapScroll(LinearLayout content)
    {
        var scroll = new ScrollView(this)
        {
            LayoutParameters = new ViewGroup.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent),
        };
        scroll.AddView(content);
        return scroll;
    }

    private TextView PageTitle(string text)
    {
        var view = TextView(text, 20, ColorText, bold: true);
        view.SetPadding(0, 0, 0, Dp(2));
        return view;
    }

    /// <summary>子页页头：返回箭头 + 标题；返回箭头触达高度 ≥48dp。</summary>
    private LinearLayout Header(string text)
    {
        var row = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        row.LayoutParameters = new ViewGroup.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent);
        var back = TextView("‹", 26, ColorPrimary);
        back.SetPadding(Dp(4), 0, Dp(8), 0);
        back.SetMinimumHeight(Dp(48));
        back.Gravity = GravityFlags.Center;
        back.Click += (_, _) => ShowScreen(Screen.Home);
        var title = TextView(text, 17, ColorText, bold: true);
        title.LayoutParameters = new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f)
        {
            Gravity = GravityFlags.CenterVertical,
        };
        row.AddView(back);
        row.AddView(title);
        return row;
    }

    private TextView Subtle(string text) => TextView(text, 12.5f, ColorTextSecondary);

    private TextView FieldLabel(string text)
    {
        var view = TextView(text, 13, ColorText, bold: true);
        view.SetPadding(0, 0, 0, Dp(6));
        return view;
    }

    private TextView TextView(string text, float sp, Color color, bool bold = false)
    {
        var view = new TextView(this) { Text = text, TextSize = sp };
        view.SetTextColor(color);
        if (bold)
        {
            view.SetTypeface(Android.Graphics.Typeface.DefaultBold!, Android.Graphics.TypefaceStyle.Bold);
        }

        return view;
    }

    private EditText Input(string hint, string value, InputTypes type) => new(this)
    {
        Hint = hint,
        Text = value,
        InputType = type,
    };

    /// <summary>卡片容器：圆角彩色底。</summary>
    private LinearLayout Card(Color background)
    {
        var card = new LinearLayout(this)
        {
            Orientation = Orientation.Vertical,
            LayoutParameters = new ViewGroup.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent),
        };
        card.Background = RoundDrawable(background);
        return card;
    }

    private static GradientDrawable RoundDrawable(Color color, float radiusDp = RadiusCardDp)
    {
        var drawable = new GradientDrawable();
        drawable.SetShape(ShapeType.Rectangle);
        drawable.SetCornerRadius(radiusDp);
        drawable.SetColor(color);
        return drawable;
    }

    /// <summary>次级操作按钮（测试连接 / 复制）：透明底 + 蓝字蓝边，高度 ≥48dp。</summary>
    private Button ActionButton(string text, Action onClick, int? width = null)
    {
        var button = new Button(this) { Text = text };
        button.SetAllCaps(false);
        button.SetTextColor(ColorPrimary);
        button.Background = OutlineDrawable();
        button.SetMinimumHeight(Dp(48));
        button.LayoutParameters = new LinearLayout.LayoutParams(width ?? ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent);
        button.Click += (_, _) => onClick();
        return button;
    }

    /// <summary>销毁类操作按钮（卸载，/）：红底白字实心 danger 视觉，高度 ≥48dp。</summary>
    private Button DangerButton(string text, Action onClick, int? width = null)
    {
        var button = new Button(this) { Text = text };
        button.SetAllCaps(false);
        button.SetTextColor(Color.White);
        button.Background = RoundDrawable(ColorErr, RadiusButtonDp);
        button.SetMinimumHeight(Dp(48));
        button.LayoutParameters = new LinearLayout.LayoutParams(width ?? ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent);
        button.Click += (_, _) => onClick();
        return button;
    }

    private GradientDrawable OutlineDrawable()
    {
        var drawable = RoundDrawable(Color.Argb(0x00, 0, 0, 0));
        drawable.SetStroke(Dp(1), ColorPrimary);
        drawable.SetCornerRadius(RadiusButtonDp);
        return drawable;
    }

    /// <summary>主操作按钮（保存并重启服务）：通栏、蓝底白字、高度 ≥52dp。</summary>
    private Button SaveButton(Action onClick)
    {
        var button = new Button(this) { Text = L(Resource.String.save_button) };
        button.SetAllCaps(false);
        button.SetTextColor(Color.White);
        button.SetTypeface(Android.Graphics.Typeface.DefaultBold!, Android.Graphics.TypefaceStyle.Bold);
        button.SetTextSize(Android.Util.ComplexUnitType.Sp, 16);
        button.Background = RoundDrawable(ColorPrimary, RadiusButtonDp);
        button.SetMinimumHeight(Dp(52));
        button.LayoutParameters = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent);
        button.Click += (_, _) => onClick();
        return button;
    }

    /// <summary>保存校验提示：默认隐藏，出错时红字显示在保存按钮上方。</summary>
    private TextView SaveHint()
    {
        var view = TextView(string.Empty, 12.5f, ColorErr);
        view.Visibility = ViewStates.Gone;
        view.SetPadding(0, 0, 0, Dp(8));
        return view;
    }

    private static void ShowSaveHint(TextView hint, string message)
    {
        hint.Text = message;
        hint.Visibility = ViewStates.Visible;
    }

    private static void SetResult(TextView view, string message, Color color)
    {
        view.Text = message;
        view.SetTextColor(color);
    }

    private View Spacing(float dp) => new View(this)
    {
        LayoutParameters = new ViewGroup.LayoutParams(ViewGroup.LayoutParams.MatchParent, Dp(dp)),
    };

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

    /// <summary>测试打印轮询用的作业视图（camelCase）。</summary>
    private sealed record JobStatusDto(string JobId, string Status, int TotalItems, int CompletedItems, IReadOnlyList<JobItemDto>? Items);

    private sealed record JobItemDto(int Index, string Status, string? ErrorCode, string? ErrorMessage);

    private static string Truncate(string text, int max = 120) => text.Length <= max ? text : text[..max] + "…";
}
