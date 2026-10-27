using LabelFrame.Updates;

namespace LabelFrame.AndroidHost.Updates;

/// <summary>合并当前下载与安装事务，避免历史终态掩盖新候选或检查失败。</summary>
public static class UpdateStatusProjection
{
    public static AndroidUpdateStatus Create(string deviceId, long version, string phase, string? reason,
        long? target, InstallTransaction.State? install, string? fault, bool needsRecovery)
    {
        var useInstall = install is not null && (needsRecovery || phase == "idle");
        return new(deviceId, version, fault is not null ? "repair" : useInstall ? install!.Phase : phase,
            useInstall ? install!.TargetVersion : target, fault ?? (useInstall ? install!.Reason : reason),
            useInstall ? install!.Id : null);
    }
}
