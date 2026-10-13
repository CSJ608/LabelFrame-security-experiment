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

/// <summary>服务器子页：地址输入、连通性测试与保存。</summary>
public sealed partial class MainActivity
{
    // 服务器子页
    private EditText _serverInput = null!;
    private TextView _serverTestText = null!;
    private TextView _serverSaveHint = null!;

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
}
