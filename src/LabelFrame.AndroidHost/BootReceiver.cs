using Android.Content;

namespace LabelFrame.AndroidHost;

/// <summary>开机自启：设备重启 / 应用更新后拉起前台打印服务。</summary>
[BroadcastReceiver(Enabled = true, Exported = true)]
[IntentFilter(new[] { Intent.ActionBootCompleted, Intent.ActionMyPackageReplaced })]
public sealed class BootReceiver : BroadcastReceiver
{
    /// <inheritdoc />
    public override void OnReceive(Context? context, Intent? intent)
    {
        if (context is null)
        {
            return;
        }

        HostLog.Info(HostLog.Tags.Host, $"收到自启广播（{intent?.Action ?? "未知"}），拉起打印服务");

        var service = new Intent(context, typeof(PrintHostService));
        if (OperatingSystem.IsAndroidVersionAtLeast(26))
        {
            context.StartForegroundService(service);
        }
        else
        {
            context.StartService(service);
        }
    }
}