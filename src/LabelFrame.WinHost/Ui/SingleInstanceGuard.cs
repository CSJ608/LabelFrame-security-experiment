namespace LabelFrame.WinHost.Ui;

/// <summary>
/// 单实例守卫：进程级互斥锁 + 跨进程「激活」事件。第二实例拿不到锁时发激活事件
/// （已有实例收到后显示并前置窗口）随即退出，不再走到 HTTP 监听（避免端口占用启动失败）。
/// </summary>
public sealed class SingleInstanceGuard : IDisposable
{
    /// <summary>互斥锁名（Global 命名空间：端口 53960 机器级唯一，实例也应机器级唯一）。</summary>
    public const string MutexName = @"Global\LabelFrame.WinHost.Instance";

    /// <summary>激活事件名：第二实例 Set，已有实例收到后显示界面。</summary>
    public const string ActivateEventName = @"Global\LabelFrame.WinHost.Activate";

    private readonly Mutex _mutex;

    private SingleInstanceGuard(Mutex mutex, EventWaitHandle activateEvent)
    {
        _mutex = mutex;
        ActivateEvent = activateEvent;
    }

    /// <summary>激活事件（已有实例持有；用于注册「被再次启动」回调）。</summary>
    public EventWaitHandle ActivateEvent { get; }

    /// <summary>
    /// 尝试成为唯一实例。返回 null 表示已有实例在运行（应调 <see cref="SignalActivate"/> 后退出）。
    /// </summary>
    public static SingleInstanceGuard? TryAcquire(string mutexName = MutexName, string activateEventName = ActivateEventName)
    {
        var mutex = new Mutex(initiallyOwned: false, mutexName);
        if (!mutex.WaitOne(TimeSpan.Zero))
        {
            mutex.Dispose();
            return null;
        }

        var activateEvent = new EventWaitHandle(initialState: false, EventResetMode.AutoReset, activateEventName);
        return new SingleInstanceGuard(mutex, activateEvent);
    }

    /// <summary>第二实例：通知已有实例显示界面（打开既有命名事件后 Set；不存在时创建即释放，无副作用）。</summary>
    public static void SignalActivate(string activateEventName = ActivateEventName)
    {
        using var activateEvent = new EventWaitHandle(initialState: false, EventResetMode.AutoReset, activateEventName);
        activateEvent.Set();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        try
        {
            _mutex.ReleaseMutex();
        }
        catch (ApplicationException)
        {
            // 非持有线程释放（退出路径兜底），忽略
        }

        _mutex.Dispose();
        ActivateEvent.Dispose();
    }
}
