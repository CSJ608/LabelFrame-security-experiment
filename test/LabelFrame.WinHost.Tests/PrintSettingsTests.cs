using LabelFrame.WinHost;

namespace LabelFrame.WinHost.Tests;

public class PrintSettingsTests
{
    [Fact]
    public void Defaults_should_be_disabled_batch_10_interval_500()
    {
        Assert.Equal(new PrintSettingsDto(false, 10, 500), PrintSettings.Defaults);
    }

    [Fact]
    public void Normalize_should_keep_valid_values()
    {
        var normalized = PrintSettings.Normalize(true, 20, 300);
        Assert.Equal(new PrintSettingsDto(true, 20, 300), normalized);
    }

    [Fact]
    public void Normalize_batch_size_below_one_should_return_default_10()
    {
        Assert.Equal(10, PrintSettings.Normalize(true, 0, 500).BatchSize);
        Assert.Equal(10, PrintSettings.Normalize(true, -3, 500).BatchSize);
    }

    [Fact]
    public void Normalize_negative_interval_should_return_default_500()
    {
        Assert.Equal(500, PrintSettings.Normalize(true, 10, -1).BatchIntervalMs);
        Assert.Equal(500, PrintSettings.Normalize(true, 10, -100).BatchIntervalMs);
    }

    [Fact]
    public void Normalize_missing_or_non_bool_enabled_should_return_false()
    {
        Assert.False(PrintSettings.Normalize(null, 10, 500).BatchEnabled);
        Assert.False(PrintSettings.Normalize(false, 10, 500).BatchEnabled);
    }

    [Fact]
    public void Validate_should_accept_valid_values()
    {
        Assert.Null(PrintSettings.Validate(1, 0));
        Assert.Null(PrintSettings.Validate(10, 500));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-100)]
    public void Validate_should_reject_batch_size_below_one(int batchSize)
    {
        Assert.NotNull(PrintSettings.Validate(batchSize, 500));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(-50)]
    public void Validate_should_reject_negative_interval(int intervalMs)
    {
        Assert.NotNull(PrintSettings.Validate(10, intervalMs));
    }

    [Fact]
    public void Update_then_snapshot_should_round_trip()
    {
        var settings = new PrintSettings();
        settings.Update(new PrintSettingsDto(true, 7, 250));

        Assert.Equal(new PrintSettingsDto(true, 7, 250), settings.Snapshot());
    }

    [Fact]
    public void New_instance_should_start_with_defaults()
    {
        Assert.Equal(PrintSettings.Defaults, new PrintSettings().Snapshot());
    }

    // ──打印 DPI（203/300 白名单、未设置继承宿主配置）──

    [Theory]
    [InlineData(203)]
    [InlineData(300)]
    [InlineData(600)]
    public void Normalize_should_keep_whitelisted_dpi(int dpi)
    {
        Assert.Equal(dpi, PrintSettings.Normalize(false, 10, 500, dpi).Dpi);
    }

    [Theory]
    [InlineData(400)]
    [InlineData(0)]
    [InlineData(null)]
    public void Normalize_should_reject_non_whitelisted_dpi_to_null(int? dpi)
    {
        Assert.Null(PrintSettings.Normalize(false, 10, 500, dpi).Dpi);
    }

    [Fact]
    public void Validate_should_accept_whitelist_and_reject_others()
    {
        Assert.Null(PrintSettings.Validate(10, 500, 203));
        Assert.Null(PrintSettings.Validate(10, 500, 300));
        Assert.Null(PrintSettings.Validate(10, 500, 600));
        Assert.Null(PrintSettings.Validate(10, 500, null));
        Assert.NotNull(PrintSettings.Validate(10, 500, 400));
    }

    [Fact]
    public void EffectiveDpi_should_fallback_when_unset_and_override_when_set()
    {
        var settings = new PrintSettings();
        Assert.Equal(300, settings.EffectiveDpi(300)); // 未设置 → 宿主配置兜底
        settings.Update(new PrintSettingsDto(false, 10, 500, 203));
        Assert.Equal(203, settings.EffectiveDpi(300));
        Assert.Equal(203, settings.EffectiveSnapshot(300).Dpi);
    }

    [Fact]
    public void Update_with_non_whitelisted_dpi_should_normalize_to_null()
    {
        var settings = new PrintSettings();
        settings.Update(new PrintSettingsDto(false, 10, 500, 400));
        Assert.Null(settings.Snapshot().Dpi);
    }
}
