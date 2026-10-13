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

/// <summary>本机信息子页：设备标识、名称与版本信息。</summary>
public sealed partial class MainActivity
{
    // 本机信息子页
    private TextView _deviceIdText = null!;
    private EditText _deviceNameInput = null!;
    private TextView _deviceSaveHint = null!;

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
}
