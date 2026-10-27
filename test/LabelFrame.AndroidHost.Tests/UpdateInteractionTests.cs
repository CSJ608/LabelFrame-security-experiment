using LabelFrame.AndroidHost.Updates;
using Xunit;

namespace LabelFrame.AndroidHost.Tests;

public sealed class UpdateInteractionTests
{
    [Theory]
    [InlineData("device", true)]
    [InlineData(" device ", true)]
    [InlineData("changed", false)]
    [InlineData(null, false)]
    public void UnsavedDeviceNameCannotReleaseEditor(string? entered, bool allowed)
        => Assert.Equal(allowed, UpdateInteraction.CanLeaveEditor(entered, "device"));

    [Fact]
    public void PermissionReturnPreparesOnce()
    {
        var flow = new UpdateInteraction();
        flow.Begin();
        flow.WaitForPermission();
        flow.Pause();
        Assert.Equal(UpdateInteraction.Action.None, flow.Next(false, true, false, false));
        Assert.Equal(UpdateInteraction.Action.Prepare, flow.Next(true, true, false, false));
        Assert.Equal(UpdateInteraction.Action.None, flow.Next(true, true, false, false));
    }

    [Fact]
    public void PermissionDenialDoesNotPrepare()
    {
        var flow = new UpdateInteraction();
        flow.Begin();
        flow.WaitForPermission();
        Assert.Equal(UpdateInteraction.Action.PermissionDenied, flow.Next(true, false, false, false));
        Assert.Equal(UpdateInteraction.Action.None, flow.Next(true, true, false, false));
    }

    [Fact]
    public void ForegroundUserRequestOpensConfirmationOnce()
    {
        var flow = new UpdateInteraction();
        flow.Begin();
        flow.WaitForConfirmation();
        Assert.Equal(UpdateInteraction.Action.None, flow.Next(true, true, false, true));
        Assert.Equal(UpdateInteraction.Action.Confirm, flow.Next(true, true, true, true));
        Assert.Equal(UpdateInteraction.Action.None, flow.Next(true, true, true, true));
    }

    [Fact]
    public void LeavingOrRecreatingDoesNotAutoOpenConfirmation()
    {
        var flow = new UpdateInteraction();
        flow.Begin();
        flow.Pause();
        flow.WaitForConfirmation();
        Assert.Equal(UpdateInteraction.Action.None, flow.Next(true, true, true, true));
        Assert.Equal(UpdateInteraction.Action.None, new UpdateInteraction().Next(true, true, true, true));
    }
}
