namespace LabelFrame.AndroidHost.Updates;

/// <summary>协调用户发起的更新与前台权限、系统确认衔接。</summary>
public sealed class UpdateInteraction
{
    public enum Action { None, Prepare, Confirm, PermissionDenied }
    private bool _active;
    private bool _permission;
    private bool _confirmation;
    public bool IsActive => _active;
    public static bool CanLeaveEditor(string? enteredName, string savedName) => string.Equals(enteredName?.Trim(), savedName, StringComparison.Ordinal);

    public void Begin() { _active = true; _permission = false; _confirmation = false; }
    public void WaitForPermission() => _permission = _active;
    public void WaitForConfirmation() => _confirmation = _active;
    public void Cancel() { _active = false; _permission = false; _confirmation = false; }
    public void Pause() { if (!_permission) Cancel(); }

    public Action Next(bool foreground, bool permissionGranted, bool confirmationAvailable, bool needsRecovery)
    {
        if (!_active || !foreground) return Action.None;
        if (_permission)
        {
            _permission = false;
            if (permissionGranted && !needsRecovery) return Action.Prepare;
            Cancel();
            return permissionGranted ? Action.None : Action.PermissionDenied;
        }
        if (_confirmation && confirmationAvailable)
        {
            Cancel();
            return Action.Confirm;
        }
        return Action.None;
    }
}
