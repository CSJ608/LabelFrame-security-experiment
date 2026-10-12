using LabelFrame.Bootstrapper.OfflineLayout;
using Xunit;

namespace LabelFrame.Bootstrapper.Tests;

/// <summary>引导布局生成参数解析测试（/）——双横线 --layout / --manifest 形态锚定。</summary>
public sealed class LayoutModeArgumentsTests
{
    [Fact]
    public void Empty_command_line_is_not_layout_mode()
    {
        var parsed = LayoutModeArguments.Parse([]);

        Assert.False(parsed.IsLayoutMode);
        Assert.Null(parsed.LayoutDirectory);
        Assert.Null(parsed.ManifestSource);
        Assert.Empty(parsed.Errors);
    }

    [Fact]
    public void Layout_switch_with_space_separated_value_enters_layout_mode()
    {
        var parsed = LayoutModeArguments.Parse(["--layout", @"D:\offline-layout"]);

        Assert.True(parsed.IsLayoutMode);
        Assert.Equal(@"D:\offline-layout", parsed.LayoutDirectory);
    }

    [Fact]
    public void Layout_switch_with_equals_form_enters_layout_mode()
    {
        var parsed = LayoutModeArguments.Parse([$"--layout=D:\\offline layout"]);

        Assert.True(parsed.IsLayoutMode);
        Assert.Equal(@"D:\offline layout", parsed.LayoutDirectory);
    }

    [Fact]
    public void Manifest_source_can_override_generation_manifest()
    {
        var parsed = LayoutModeArguments.Parse(["--layout", @"D:\dir", "--manifest", "http://127.0.0.1:8140/install-manifest.json"]);

        Assert.True(parsed.IsLayoutMode);
        Assert.Equal("http://127.0.0.1:8140/install-manifest.json", parsed.ManifestSource);
    }

    [Fact]
    public void Layout_without_value_records_error_and_stays_non_layout()
    {
        var parsed = LayoutModeArguments.Parse(["--layout"]);

        Assert.False(parsed.IsLayoutMode);
        Assert.NotEmpty(parsed.Errors);
    }

    [Fact]
    public void Switch_like_value_is_not_consumed_as_layout_directory()
    {
        // --layout 后跟其他开关：不是目录值（防把 -passive 之类吞成路径）
        var parsed = LayoutModeArguments.Parse(["--layout", "-passive"]);

        Assert.False(parsed.IsLayoutMode);
        Assert.NotEmpty(parsed.Errors);
    }

    [Fact]
    public void Unrelated_arguments_are_ignored()
    {
        // 引擎透传的其他参数（变量赋值等）不报错、不影响解析
        var parsed = LayoutModeArguments.Parse(["InstallPreset=standalone", "--layout", @"D:\dir", "-q"]);

        Assert.True(parsed.IsLayoutMode);
        Assert.Empty(parsed.Errors);
    }

    [Fact]
    public void Command_line_string_is_split_with_quote_awareness()
    {
        var tokens = LayoutModeArguments.SplitCommandLine("--layout \"D:\\my layout\" other");

        Assert.Equal(["--layout", @"D:\my layout", "other"], tokens);
    }

    [Fact]
    public void Parse_from_raw_command_line_string_supports_quoted_paths()
    {
        var parsed = LayoutModeArguments.Parse("--layout \"D:\\my layout\" --manifest C:\\test\\install-manifest.json");

        Assert.True(parsed.IsLayoutMode);
        Assert.Equal(@"D:\my layout", parsed.LayoutDirectory);
        Assert.Equal(@"C:\test\install-manifest.json", parsed.ManifestSource);
    }
}
