using LabelFrame.Bootstrapper.Prerequisites;
using Xunit;

namespace LabelFrame.Bootstrapper.Tests;

/// <summary>运行时前置探测（DESIGN 检测口径）：.NET 文件版本探测（≥ 10.0.0 latestMajor 前滚）+ WebView2 注册表双查。</summary>
public sealed class RuntimeProbeTests
{
    [Fact]
    public void No_desktop_runtime_dirs_should_report_not_installed()
    {
        var probe = new RuntimeProbe(enumerateDesktopRuntimeVersions: () => []);
        var result = probe.Probe();

        Assert.False(result.DesktopRuntimeInstalled);
        Assert.Null(result.DesktopRuntimeVersion);
    }

    [Fact]
    public void Desktop_runtime_10x_dir_should_satisfy_minimum_with_latest_major_rollback_forward()
    {
        var probe = new RuntimeProbe(enumerateDesktopRuntimeVersions: () => ["6.0.36", "9.0.7", "10.0.1"]);
        var result = probe.Probe();

        Assert.True(result.DesktopRuntimeInstalled);
        Assert.Equal("10.0.1", result.DesktopRuntimeVersion);
    }

    [Fact]
    public void Desktop_runtime_below_minimum_should_report_version_but_not_installed()
    {
        var probe = new RuntimeProbe(enumerateDesktopRuntimeVersions: () => ["6.0.36", "9.0.7"]);
        var result = probe.Probe();

        Assert.False(result.DesktopRuntimeInstalled);
        Assert.Equal("9.0.7", result.DesktopRuntimeVersion);
    }

    [Fact]
    public void Non_version_dirs_and_v_prefix_should_be_tolerated()
    {
        var probe = new RuntimeProbe(enumerateDesktopRuntimeVersions: () => ["tmp", "v10.0.2", "not-a-version"]);
        var result = probe.Probe();

        Assert.True(result.DesktopRuntimeInstalled);
        Assert.Equal("v10.0.2", result.DesktopRuntimeVersion); // 目录名原样上报，解析仅用于比较
    }

    [Theory]
    [InlineData("10.0.0", true)]
    [InlineData("10.0.12", true)]
    [InlineData("11.0.0", true)]
    [InlineData("9.9.9", false)]
    public void Desktop_runtime_version_floor_should_match_netcorecheck_latest_major(string version, bool expected)
    {
        var probe = new RuntimeProbe(enumerateDesktopRuntimeVersions: () => [version]);

        Assert.Equal(expected, probe.Probe().DesktopRuntimeInstalled);
    }

    [Fact]
    public void AspNetCore_runtime_dirs_are_probed_independently_of_desktop()
    {
        // AspNetCore 与 Desktop 互不包含——只装 Desktop 的机器 AspNetCore 应报未装（缺陷根因）
        var probe = new RuntimeProbe(
            enumerateDesktopRuntimeVersions: () => ["10.0.12"],
            enumerateAspNetCoreRuntimeVersions: () => []);
        var result = probe.Probe();

        Assert.True(result.DesktopRuntimeInstalled);
        Assert.False(result.AspNetCoreRuntimeInstalled);
        Assert.Null(result.AspNetCoreRuntimeVersion);
    }

    [Fact]
    public void AspNetCore_runtime_dir_should_satisfy_minimum_with_latest_major()
    {
        var probe = new RuntimeProbe(
            enumerateDesktopRuntimeVersions: () => [],
            enumerateAspNetCoreRuntimeVersions: () => ["6.0.36", "10.0.12"]);
        var result = probe.Probe();

        Assert.True(result.AspNetCoreRuntimeInstalled);
        Assert.Equal("10.0.12", result.AspNetCoreRuntimeVersion);
    }

    [Theory]
    [InlineData("10.0.0", true)]
    [InlineData("11.0.0", true)]
    [InlineData("9.9.9", false)]
    public void AspNetCore_runtime_version_floor_matches_netcorecheck_latest_major(string version, bool expected)
    {
        var probe = new RuntimeProbe(enumerateAspNetCoreRuntimeVersions: () => [version]);

        Assert.Equal(expected, probe.Probe().AspNetCoreRuntimeInstalled);
    }

    [Fact]
    public void WebView2_per_machine_registry_should_report_installed()
    {
        var probe = new RuntimeProbe(
            enumerateDesktopRuntimeVersions: () => [],
            readRegistryValue: name => name == RuntimeProbe.WebView2PerMachineValue ? "152.0.4191.66" : null);

        Assert.True(probe.Probe().WebView2Installed);
    }

    [Fact]
    public void WebView2_per_user_registry_should_alone_report_installed()
    {
        var probe = new RuntimeProbe(
            enumerateDesktopRuntimeVersions: () => [],
            readRegistryValue: name => name == RuntimeProbe.WebView2PerUserValue ? "1.0.2903.40" : null);

        Assert.True(probe.Probe().WebView2Installed);
    }

    [Fact]
    public void WebView2_missing_or_blank_registry_should_report_not_installed()
    {
        var missing = new RuntimeProbe(enumerateDesktopRuntimeVersions: () => [], readRegistryValue: _ => null);
        var blank = new RuntimeProbe(enumerateDesktopRuntimeVersions: () => [], readRegistryValue: _ => "  ");

        Assert.False(missing.Probe().WebView2Installed);
        Assert.False(blank.Probe().WebView2Installed);
    }

    [Fact]
    public void Parse_version_should_tolerate_v_prefix_and_reject_garbage()
    {
        Assert.Equal(new Version(10, 0, 1), RuntimeProbe.ParseVersion("v10.0.1"));
        Assert.Equal(new Version(10, 0), RuntimeProbe.ParseVersion("10.0"));
        Assert.Null(RuntimeProbe.ParseVersion("not-a-version"));
        Assert.Null(RuntimeProbe.ParseVersion(""));
    }
}
