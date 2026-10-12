using Android.App;
using Android.Runtime;

namespace LabelFrame.AndroidHost;

/// <summary>应用入口：进程创建第一时间注册全局崩溃捕获（早于任何 Activity / Service / Receiver）。</summary>
[Application]
public sealed class HostApplication : Application
{
    /// <summary>系统重建应用对象时经 JNI 调用的构造器（.NET Android 约定，必须保留）。</summary>
    public HostApplication(IntPtr handle, JniHandleOwnership transfer)
        : base(handle, transfer)
    {
    }

    /// <inheritdoc />
    public override void OnCreate()
    {
        CrashGuard.Register();
        base.OnCreate();
    }
}
