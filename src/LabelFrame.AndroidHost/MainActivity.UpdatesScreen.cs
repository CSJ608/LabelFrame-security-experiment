using Android.App;
using Android.Content;
using Android.Provider;
using Android.Views;
using Android.Widget;
using LabelFrame.AndroidHost.Updates;

namespace LabelFrame.AndroidHost;

/// <summary>在本机信息中展示版本状态并衔接用户确认更新。</summary>
public sealed partial class MainActivity
{
    private TextView? _updateState;
    private Button? _installUpdate;
    private bool _preparingUpdate;
    private string? _updateActionError;
    private readonly UpdateInteraction _updateInteraction = new();

    private void AddUpdateSection(LinearLayout content)
    {
        content.AddView(Spacing(16));
        _updateState = TextView("", 16, ColorText, true);
        content.AddView(_updateState);
        content.AddView(Spacing(12));
        _installUpdate = DangerButton(L(Resource.String.update_install), StartUpdate, ViewGroup.LayoutParams.MatchParent);
        content.AddView(_installUpdate);
    }

    private void RefreshUpdateStatus()
    {
        if (_updateState is null || _current != Screen.Device) return;
        var download = AndroidUpdateMonitor.Current;
        var transaction = AndroidUpdateInstaller.Journal;
        var state = transaction.Current;
        var phase = UpdateStatusProjection.Create("ui", 0, download.Phase, download.Reason,
            download.Release?.Package.VersionCode, state, transaction.Fault, transaction.NeedsRecovery).Phase;
        _updateState.Text = phase switch
        {
            "ready" => L(Resource.String.update_available, download.Release?.Package.VersionName),
            "downloading" or "checking" => L(Resource.String.update_downloading),
            "preparing" or "submitted" => L(Resource.String.update_submitted),
            "confirmation" => L(Resource.String.update_confirmation),
            "recovering" => L(Resource.String.update_recovering),
            "repair" or "unknown" or "rejected" => L(Resource.String.update_repair),
            "unsupported" => L(Resource.String.update_unsupported),
            "unconfigured" => L(Resource.String.update_unconfigured),
            "failed" or "blocked" => L(Resource.String.update_failed),
            _ => L(Resource.String.update_idle)
        };
        if (_updateActionError is not null) _updateState.Text = _updateActionError;
        if (_installUpdate is not null)
        {
            var canUpdate = !transaction.NeedsRecovery && download.Release is not null;
            var canConfirm = state?.Phase == "confirmation" && AndroidUpdateInstaller.HasConfirmation;
            _installUpdate.Visibility = canUpdate || canConfirm ? ViewStates.Visible : ViewStates.Gone;
            _installUpdate.Enabled = !_preparingUpdate && (canConfirm || download.Phase == "ready" && canUpdate);
        }
        ContinueUpdateInteraction();
        RefreshDeviceEditing();
    }

    private bool IsUpdateForeground() => _autoRefresh && _current == Screen.Device && !IsFinishing && !IsDestroyed;

    private void StartUpdate()
    {
        _updateActionError = null;
        if (AndroidUpdateInstaller.Journal.Current?.Phase == "confirmation")
        {
            if (!AndroidUpdateInstaller.ShowConfirmation(this)) _updateActionError = L(Resource.String.update_repair);
            return;
        }
        if (!UpdateInteraction.CanLeaveEditor(_deviceNameInput.Text, _config.DeviceName))
        {
            _updateActionError = L(Resource.String.update_save_device_first);
            RefreshUpdateStatus();
            return;
        }
        ConfirmUpdate();
    }

    private void ContinueUpdateInteraction()
    {
        var action = _updateInteraction.Next(IsUpdateForeground(),
            OperatingSystem.IsAndroidVersionAtLeast(26) && PackageManager!.CanRequestPackageInstalls(),
            AndroidUpdateInstaller.HasConfirmation, AndroidUpdateInstaller.Journal.NeedsRecovery);
        if (action == UpdateInteraction.Action.Prepare) RunAsync(PrepareUpdateAsync);
        if (action == UpdateInteraction.Action.Confirm) AndroidUpdateInstaller.ShowConfirmation(this);
        if (action == UpdateInteraction.Action.PermissionDenied)
            _updateActionError = L(Resource.String.update_permission_needed);
    }

    private void BeginUpdateInteraction()
    {
        if (!IsUpdateForeground() || !UpdateInteraction.CanLeaveEditor(_deviceNameInput.Text, _config.DeviceName)) return;
        _updateInteraction.Begin();
        _deviceNameInput.Enabled = false;
        _deviceSaveButton.Enabled = false;
        _editorLease?.Dispose();
        _editorLease = null;
        if (OperatingSystem.IsAndroidVersionAtLeast(26) && !PackageManager!.CanRequestPackageInstalls())
        {
            _updateInteraction.WaitForPermission();
            StartActivity(new Intent(Settings.ActionManageUnknownAppSources, Android.Net.Uri.Parse("package:" + PackageName)));
        }
        else RunAsync(PrepareUpdateAsync);
    }

    private void RefreshDeviceEditing()
    {
        var editable = !_preparingUpdate && !_updateInteraction.IsActive && !MaintenanceGate.Shared.IsFrozen;
        if (editable) _editorLease ??= MaintenanceGate.Shared.TryEnterEditor();
        _deviceNameInput.Enabled = editable && _editorLease is not null;
        _deviceSaveButton.Enabled = _deviceNameInput.Enabled;
    }

    private void ConfirmUpdate()
    {
        if (_preparingUpdate || AndroidUpdateMonitor.Current.Release is not { } release) return;
        var dialog = new AlertDialog.Builder(this)!
            .SetTitle(L(Resource.String.update_install))!
            .SetMessage(L(Resource.String.update_confirm_impact, HostInfo.GetVersion(this), release.Package.VersionName))!
            .SetNegativeButton(L(Resource.String.update_not_now), (_, _) => { })!
            .SetPositiveButton(L(Resource.String.update_install), (_, _) => BeginUpdateInteraction())!.Create()!;
        dialog.Show();
        var confirm = dialog.GetButton((int)DialogButtonType.Positive)!;
        confirm.Enabled = false;
        _refreshHandler.PostDelayed(() => { if (dialog.IsShowing) confirm.Enabled = true; }, 3000);
    }

    private async Task PrepareUpdateAsync()
    {
        if (_preparingUpdate || !IsUpdateForeground()) return;
        _preparingUpdate = true;
        _updateActionError = null;
        RefreshUpdateStatus();
        try
        {
            var installer = AndroidUpdateInstaller.Current ?? throw new InvalidOperationException(L(Resource.String.update_service_not_ready));
            await installer.PrepareAsync(IsUpdateForeground, CancellationToken.None);
            _updateInteraction.WaitForConfirmation();
        }
        catch (Exception error)
        {
            HostLog.Warn(HostLog.Tags.Ui, $"更新准备失败：{error.Message}");
            _updateActionError = L(Resource.String.update_action_blocked);
            _updateInteraction.Cancel();
        }
        finally { _preparingUpdate = false; RefreshUpdateStatus(); }
    }
}
