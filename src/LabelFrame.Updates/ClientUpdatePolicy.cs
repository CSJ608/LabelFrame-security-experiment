namespace LabelFrame.Updates;

/// <summary>静默安装的维护窗口与界面空闲判定。</summary>
public static class ClientUpdatePolicy
{
    public static string? PostponeReason(ClientUpdateSettings settings, TimeOnly localTime, bool visible,
        DateTimeOffset lastInteraction, DateTimeOffset now)
    {
        if (settings.Mode != "maintenance" || !ClientUpdateContract.IsInWindow(localTime, settings.WindowStart, settings.WindowEnd))
            return "等待设定的维护时间。";
        if (visible || now - lastInteraction < TimeSpan.FromMinutes(10)) return "界面仍在使用，等待隐藏界面并空闲 10 分钟。";
        return null;
    }
}
