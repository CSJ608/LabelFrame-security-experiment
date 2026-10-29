namespace LabelFrame.Server.Security;

/// <summary>限制单管理员登录的全局尝试频率。</summary>
public sealed class AdminLoginLimiter(TimeProvider clock)
{
    private readonly object _gate = new();
    private DateTimeOffset _window;
    private int _attempts;

    public bool TryAcquire()
    {
        lock (_gate)
        {
            var now = clock.GetUtcNow();
            if (now >= _window.AddMinutes(1)) { _window = now; _attempts = 0; }
            if (_attempts >= 10) return false;
            _attempts++;
            return true;
        }
    }
}
