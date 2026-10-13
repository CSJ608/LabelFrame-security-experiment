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

/// <summary>插件管理子页：已装列表、服务器包浏览、安装与卸载。</summary>
public sealed partial class MainActivity
{
    // 插件管理子页
    private LinearLayout _installedList = null!;
    private TextView _installedSummary = null!;
    private LinearLayout _serverPackagesList = null!;
    private TextView _pluginStatusText = null!;
    private List<InstalledPluginDto>? _installedPluginsCache;

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

    /// <summary>卸载确认——销毁类操作必须先经原生 AlertDialog 确认（标题 + 后果说明 + 确认按钮红字），
    /// 确认后才执行卸载（删插件目录并重启打印服务）。</summary>
    private void ConfirmUninstallPlugin(string pluginId, string pluginName)
    {
        var builder = new AlertDialog.Builder(this);
        builder.SetTitle(L(Resource.String.plugins_uninstall_dialog_title));
        builder.SetMessage(L(Resource.String.plugins_uninstall_dialog_message, pluginName));
        builder.SetNegativeButton(L(Resource.String.dialog_cancel), (_, _) => { });
        builder.SetPositiveButton(L(Resource.String.plugins_uninstall), (_, _) => RunAsync(() => UninstallPluginAsync(pluginId)));
        // 绑定注解将 Create() 标为可空（Java 契约实际不返回 null），按 0 警口径显式断言非空
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

            // 配置还指向被卸载品牌时回退 Zebra（与宿主路由回退一致，同步配置避免界面悬空）
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
}
