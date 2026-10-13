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

/// <summary>主页：运行状态卡 + 子页入口行 + 自动状态刷新。</summary>
public sealed partial class MainActivity
{
    // 主页动态区
    private LinearLayout _statusCard = null!;
    private TextView _statusSummary = null!;
    private TextView _statusServer = null!;
    private TextView _statusPrinter = null!;
    private TextView _serverEntrySummary = null!;
    private TextView _printerEntrySummary = null!;
    private TextView _deviceEntrySummary = null!;
    private TextView _pluginsEntrySummary = null!;

    private readonly Handler _refreshHandler = new(Looper.MainLooper!);
    private bool _autoRefresh;

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

    /// <summary>设置入口行：左侧标题 + 当前值摘要，右侧「›」；整行可点、高度 ≥64dp。</summary>
    private LinearLayout EntryRow(string title, Action onClick, out TextView summary)
    {
        // 文本列必须放在横向父容器里用宽度 0 + weight——竖向容器里宽度 0 的子视图会被压成 0 宽
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

        // ActivePrinterEndpoint 已是按连接类型生成的用户可读摘要
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
}
