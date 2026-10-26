using Microsoft.Extensions.Configuration;

namespace LabelFrame.WinHost.Tests;

[CollectionDefinition("图片环境配置", DisableParallelization = true)]
public sealed class PrintImageOptionsTestGroup;

[Collection("图片环境配置")]
public sealed class PrintImageOptionsTests
{
    [Fact]
    public void Defaults_and_configuration_binding_should_preserve_opt_out()
    {
        var options = new HostOptions();
        Assert.True(options.SaveLogPrintImages);
        Assert.Equal(31, options.PrintImageRetentionDays);
        Assert.Equal(512, options.PrintImageMaxSizeMB);
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["WinHost:SaveLogPrintImages"] = "false",
            ["WinHost:PrintImageRetentionDays"] = "7",
            ["WinHost:PrintImageMaxSizeMB"] = "64",
        }).Build().GetSection("WinHost").Bind(options);
        Assert.False(options.SaveLogPrintImages);
        Assert.Equal(7, options.PrintImageRetentionDays);
        Assert.Equal(64, options.PrintImageMaxSizeMB);
    }

    [Theory]
    [InlineData("false", "0", "-1", false, 0, -1)]
    [InlineData("TRUE", "7", "64", true, 7, 64)]
    [InlineData("invalid", "invalid", "invalid", false, 2, 3)]
    public void Environment_should_override_valid_values_and_preserve_invalid_values(
        string save, string days, string size, bool expectedSave, int expectedDays, int expectedSize)
    {
        string[] keys = ["LABELFRAME_SAVE_LOG_PRINT_IMAGES", "LABELFRAME_PRINT_IMAGE_RETENTION_DAYS", "LABELFRAME_PRINT_IMAGE_MAX_SIZE_MB"];
        var previous = keys.Select(Environment.GetEnvironmentVariable).ToArray();
        try
        {
            Environment.SetEnvironmentVariable(keys[0], save);
            Environment.SetEnvironmentVariable(keys[1], days);
            Environment.SetEnvironmentVariable(keys[2], size);
            var options = new HostOptions { SaveLogPrintImages = false, PrintImageRetentionDays = 2, PrintImageMaxSizeMB = 3 };
            options.ApplyEnvironmentOverrides();
            Assert.Equal(expectedSave, options.SaveLogPrintImages);
            Assert.Equal(expectedDays, options.PrintImageRetentionDays);
            Assert.Equal(expectedSize, options.PrintImageMaxSizeMB);
        }
        finally
        {
            for (var i = 0; i < keys.Length; i++) Environment.SetEnvironmentVariable(keys[i], previous[i]);
        }
    }
}
