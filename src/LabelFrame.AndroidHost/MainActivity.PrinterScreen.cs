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

/// <summary>打印机子页：品牌 / 连接方式选择、参数编辑、测试打印与保存。</summary>
public sealed partial class MainActivity
{
    // 打印机子页
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

    // ---------- 打印机子页 ----------

    private ScrollView BuildPrinterScreen()
    {
        var content = ScrollColumn();

        content.AddView(Header(L(Resource.String.printer_title)));
        content.AddView(Spacing(10));

        // 打印机品牌：Zebra 内置 + 已装外置插件动态扩展
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

        // 连接类型（Zebra 专属；插件品牌固定网口，隐藏整块）
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

        // 蓝牙参数：MAC 地址手输
        _bluetoothFields = new LinearLayout(this) { Orientation = Orientation.Vertical };
        _bluetoothFields.LayoutParameters = new ViewGroup.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent);
        _bluetoothFields.AddView(FieldLabel(L(Resource.String.printer_bt_label)));
        _macInput = Input(L(Resource.String.printer_bt_hint), _config.BluetoothMac, InputTypes.ClassText | InputTypes.TextVariationVisiblePassword);
        _bluetoothFields.AddView(_macInput);
        _bluetoothFields.AddView(Spacing(6));
        _bluetoothFields.AddView(Subtle(L(Resource.String.printer_bt_note)));
        content.AddView(_bluetoothFields);

        // USB 参数：自动发现锁定第一台（无可选设备给可行动错误）
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

    /// <summary>刷新品牌选项（已装外部插件动态扩展）。</summary>
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
                    // 配置引用的插件品牌当前不可用（已卸载 / 加载失败）：选项按 Zebra 呈现（与宿主路由回退一致）
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

    /// <summary>当前选中连接类型对应的存储值（插件品牌固定 tcp，配置结构零变更）。</summary>
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
                    // 项级无 params 通道：模板带参的码自然回退中文
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

    /// <summary>测试打印轮询用的作业视图（camelCase）。</summary>
    private sealed record JobStatusDto(string JobId, string Status, int TotalItems, int CompletedItems, IReadOnlyList<JobItemDto>? Items);

    private sealed record JobItemDto(int Index, string Status, string? ErrorCode, string? ErrorMessage);

    private static string Truncate(string text, int max = 120) => text.Length <= max ? text : text[..max] + "…";
}
