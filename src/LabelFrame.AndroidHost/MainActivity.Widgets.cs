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

/// <summary>代码布局的通用控件与视觉常量（无资源依赖）。</summary>
public sealed partial class MainActivity
{
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

    /// <summary>销毁类操作按钮（卸载）：红底白字实心 danger 视觉，高度 ≥48dp。</summary>
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
}
