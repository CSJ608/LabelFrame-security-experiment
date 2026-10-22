using System.Text.Json;
using System.Text.Json.Serialization;
using LabelFrame.Core.Layout;

namespace LabelFrame.Core.Tests.Json;

public class LabelElementJsonConverterTests
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters =
        {
            new JsonStringEnumConverter(),
            new LabelElementJsonConverter(),
        },
    };

    [Theory]
    [InlineData("""{"type":"text","xMm":5,"yMm":4,"sourceKey":"zone","fontHeightMm":5,"fontWidthMm":5}""")]
    [InlineData("""{"type":"barcode","xMm":5,"yMm":26,"sourceKey":"code","heightMm":22,"moduleWidth":2}""")]
    [InlineData("""{"type":"qrcode","xMm":1,"yMm":1,"sourceKey":"qr","sizeMm":20}""")]
    [InlineData("""{"type":"image","xMm":80,"yMm":4,"sourceKey":"logo","widthMm":10,"heightMm":10}""")]
    [InlineData("""{"type":"line","xMm":0,"yMm":0,"x2Mm":10,"y2Mm":10,"thicknessMm":0.5}""")]
    [InlineData("""{"type":"region","xMm":5,"yMm":5,"id":"top","widthMm":60,"heightMm":30,"borderMm":0.3}""")]
    [InlineData("""{"type":"text","xMm":0,"yMm":0,"sourceKey":"k","fontHeightMm":5,"fontWidthMm":5,"widthMm":40,"textAlign":"Center","paddingMm":1,"borderMm":0.3,"regionId":"top","regionHAlign":"Center","regionVAlign":"End"}""")]
    public void Read_should_parse_each_element_type(string json)
    {
        var element = JsonSerializer.Deserialize<LabelElement>(json, Options);

        Assert.NotNull(element);
        Assert.Equal(element.Type.ToString().ToLowerInvariant(), GetTypeName(element));
    }

    [Fact]
    public void Round_trip_should_preserve_properties()
    {
        var element = new LabelBarcodeElement { SourceKey = "locationCode", XMm = 5, YMm = 26, HeightMm = 22, ModuleWidth = 2 };

        var json = JsonSerializer.Serialize<LabelElement>(element, Options);
        var roundTrip = JsonSerializer.Deserialize<LabelElement>(json, Options);

        var barcode = Assert.IsType<LabelBarcodeElement>(roundTrip);
        Assert.Equal("locationCode", barcode.SourceKey);
        Assert.Equal(5, barcode.XMm);
        Assert.Equal(26, barcode.YMm);
        Assert.Equal(22, barcode.HeightMm);
        Assert.Equal(2, barcode.ModuleWidth);
        Assert.Contains("\"type\":\"barcode\"", json);
    }

    [Fact]
    public void Region_and_style_should_round_trip()
    {
        var element = new LabelTextElement
        {
            SourceKey = "k",
            XMm = 0,
            YMm = 0,
            FontHeightMm = 5,
            FontWidthMm = 5,
            WidthMm = 40,
            TextAlign = LabelTextAlign.Center,
            PaddingMm = 1,
            BorderMm = 0.3,
            RegionId = "top",
            RegionHAlign = LabelRegionAlign.Center,
            RegionVAlign = LabelRegionAlign.End,
        };

        var json = JsonSerializer.Serialize<LabelElement>(element, Options);
        var roundTrip = JsonSerializer.Deserialize<LabelElement>(json, Options);

        var text = Assert.IsType<LabelTextElement>(roundTrip);
        Assert.Equal(40, text.WidthMm);
        Assert.Equal(LabelTextAlign.Center, text.TextAlign);
        Assert.Equal(1, text.PaddingMm);
        Assert.Equal(0.3, text.BorderMm);
        Assert.Equal("top", text.RegionId);
        Assert.Equal(LabelRegionAlign.End, text.RegionVAlign);
    }

    [Fact]
    public void Literal_should_round_trip()
    {
        var element = new LabelTextElement { SourceKey = string.Empty, Literal = "库位标签", XMm = 1, YMm = 2, FontHeightMm = 5, FontWidthMm = 5 };

        var json = JsonSerializer.Serialize<LabelElement>(element, Options);
        var roundTrip = JsonSerializer.Deserialize<LabelElement>(json, Options);

        var text = Assert.IsType<LabelTextElement>(roundTrip);
        Assert.Equal("库位标签", text.Literal);
        Assert.Contains("literal", json);
    }

    [Fact]
    public void Preview_value_should_round_trip_for_field_mode_elements()
    {
        var element = new LabelTextElement { SourceKey = "locationCode", PreviewValue = "A-01-02-03", XMm = 5, YMm = 5, FontHeightMm = 5, FontWidthMm = 5 };

        var json = JsonSerializer.Serialize<LabelElement>(element, Options);
        var roundTrip = JsonSerializer.Deserialize<LabelElement>(json, Options);

        var text = Assert.IsType<LabelTextElement>(roundTrip);
        Assert.Equal("A-01-02-03", text.PreviewValue);
        Assert.Contains("\"previewValue\":\"A-01-02-03\"", json);
    }

    [Fact]
    public void Missing_preview_value_should_be_null_and_literal_mode_should_not_emit_it()
    {
        const string fieldJson = """{"type":"text","xMm":5,"yMm":5,"sourceKey":"k","fontHeightMm":5,"fontWidthMm":5}""";
        var field = Assert.IsType<LabelTextElement>(JsonSerializer.Deserialize<LabelElement>(fieldJson, Options));
        Assert.Null(field.PreviewValue);

        var literal = new LabelTextElement { Literal = "x", XMm = 0, YMm = 0, FontHeightMm = 5, FontWidthMm = 5 };
        var literalJson = JsonSerializer.Serialize<LabelElement>(literal, Options);
        Assert.DoesNotContain("previewValue", literalJson);
    }
    [Fact]
    public void Text_height_and_vertical_align_should_round_trip()
    {
        var element = new LabelTextElement { SourceKey = "k", XMm = 5, YMm = 10, FontHeightMm = 3, FontWidthMm = 3, WidthMm = 25, HeightMm = 15, VerticalAlign = LabelVerticalAlign.Top };

        var json = JsonSerializer.Serialize<LabelElement>(element, Options);
        var roundTrip = JsonSerializer.Deserialize<LabelElement>(json, Options);

        var text = Assert.IsType<LabelTextElement>(roundTrip);
        Assert.Equal(15, text.HeightMm);
        Assert.Equal(LabelVerticalAlign.Top, text.VerticalAlign);
        Assert.Contains("\"heightMm\":15", json);
        Assert.Contains("\"verticalAlign\":\"Top\"", json);
    }

    [Fact]
    public void New_text_fields_should_round_trip_and_write_non_defaults()
    {
        var element = new LabelTextElement
        {
            SourceKey = "k",
            XMm = 5,
            YMm = 6,
            FontHeightMm = 3,
            FontWidthMm = 3,
            WidthMm = 25,
            PaddingHMm = 2,
            PaddingVMm = 1,
            VerticalAlign = LabelVerticalAlign.Top,
            FontFamily = "SimSun",
            Wrap = true,
            LineHeight = 1.5,
            FitMode = LabelFitMode.Overflow,
        };

        var json = JsonSerializer.Serialize<LabelElement>(element, Options);
        var roundTrip = Assert.IsType<LabelTextElement>(JsonSerializer.Deserialize<LabelElement>(json, Options));

        Assert.Equal(2, roundTrip.PaddingHMm);
        Assert.Equal(1, roundTrip.PaddingVMm);
        Assert.Equal(LabelVerticalAlign.Top, roundTrip.VerticalAlign);
        Assert.Equal("SimSun", roundTrip.FontFamily);
        Assert.True(roundTrip.Wrap);
        Assert.Equal(1.5, roundTrip.LineHeight);
        Assert.Equal(LabelFitMode.Overflow, roundTrip.FitMode);
        Assert.Contains("\"paddingH\":2", json);
        Assert.Contains("\"paddingV\":1", json);
        Assert.Contains("\"verticalAlign\":\"Top\"", json);
        Assert.Contains("\"fontFamily\":\"SimSun\"", json);
        Assert.Contains("\"wrap\":true", json);
        Assert.Contains("\"lineHeight\":1.5", json);
        Assert.Contains("\"fitMode\":\"overflow\"", json);
    }

    [Fact]
    public void Text_defaults_should_be_omitted_on_write_and_restored_on_read()
    {
        var element = new LabelTextElement
        {
            SourceKey = "k",
            XMm = 0,
            YMm = 0,
            FontHeightMm = 5,
            FontWidthMm = 5,
            // 全部保持默认：VerticalAlign=Middle、FontFamily=Microsoft YaHei、Wrap=false、LineHeight=1.2、FitMode=Shrink
        };

        var json = JsonSerializer.Serialize<LabelElement>(element, Options);
        Assert.DoesNotContain("verticalAlign", json);
        Assert.DoesNotContain("fontFamily", json);
        Assert.DoesNotContain("\"wrap\"", json);
        Assert.DoesNotContain("lineHeight", json);
        Assert.DoesNotContain("fitMode", json);

        const string oldJson = """{"type":"text","xMm":0,"yMm":0,"sourceKey":"k","fontHeightMm":5,"fontWidthMm":5}""";
        var old = Assert.IsType<LabelTextElement>(JsonSerializer.Deserialize<LabelElement>(oldJson, Options));
        Assert.Equal(LabelVerticalAlign.Middle, old.VerticalAlign);
        Assert.Equal(LabelTextElement.DefaultFontFamily, old.FontFamily);
        Assert.False(old.Wrap);
        Assert.Equal(1.2, old.LineHeight);
        Assert.Equal(LabelFitMode.Shrink, old.FitMode);
    }

    [Fact]
    public void Padding_mm_fallback_should_apply_when_h_v_missing()
    {
        const string oldJson = """{"type":"text","xMm":0,"yMm":0,"sourceKey":"k","fontHeightMm":5,"fontWidthMm":5,"paddingMm":1}""";
        var old = Assert.IsType<LabelTextElement>(JsonSerializer.Deserialize<LabelElement>(oldJson, Options));

        Assert.Null(old.PaddingHMm);
        Assert.Null(old.PaddingVMm);
        Assert.Equal(1, old.EffectivePaddingHMm);
        Assert.Equal(1, old.EffectivePaddingVMm);

        var json = JsonSerializer.Serialize<LabelElement>(old, Options);
        Assert.Contains("\"paddingMm\":1", json);
        Assert.DoesNotContain("paddingH", json);
        Assert.DoesNotContain("paddingV", json);
    }

    [Fact]
    public void Asymmetric_padding_should_round_trip_without_padding_mm()
    {
        var element = new LabelTextElement { SourceKey = "k", XMm = 0, YMm = 0, FontHeightMm = 5, FontWidthMm = 5, PaddingHMm = 2, PaddingVMm = 1, PaddingMm = 2 };

        var json = JsonSerializer.Serialize<LabelElement>(element, Options);
        var roundTrip = Assert.IsType<LabelTextElement>(JsonSerializer.Deserialize<LabelElement>(json, Options));

        Assert.Equal(2, roundTrip.PaddingHMm);
        Assert.Equal(1, roundTrip.PaddingVMm);
        Assert.Contains("\"paddingH\":2", json);
        Assert.Contains("\"paddingV\":1", json);
        // 双侧不等时兼容单值必须省略——旧读端按单值兜底会伪造其中一侧
        Assert.DoesNotContain("paddingMm", json);
        Assert.Equal(0, roundTrip.PaddingMm);
    }

    [Fact]
    public void Symmetric_padding_should_write_padding_mm_as_effective_value()
    {
        var element = new LabelTextElement { SourceKey = "k", XMm = 0, YMm = 0, FontHeightMm = 5, FontWidthMm = 5, PaddingHMm = 1.5, PaddingVMm = 1.5 };

        var json = JsonSerializer.Serialize<LabelElement>(element, Options);
        Assert.Contains("\"paddingMm\":1.5", json);

        var roundTrip = Assert.IsType<LabelTextElement>(JsonSerializer.Deserialize<LabelElement>(json, Options));
        Assert.Equal(1.5, roundTrip.PaddingMm);
        Assert.Equal(1.5, roundTrip.EffectivePaddingHMm);
    }

    [Fact]
    public void Explicit_zero_padding_should_round_trip_without_fallback()
    {
        var element = new LabelTextElement { SourceKey = "k", XMm = 0, YMm = 0, FontHeightMm = 5, FontWidthMm = 5, PaddingMm = 1.5, PaddingHMm = 1, PaddingVMm = 0 };

        var json = JsonSerializer.Serialize<LabelElement>(element, Options);
        var roundTrip = Assert.IsType<LabelTextElement>(JsonSerializer.Deserialize<LabelElement>(json, Options));

        // 显式 0 在场即写出并读回，不得回退单值 paddingMm
        Assert.Contains("\"paddingH\":1", json);
        Assert.Contains("\"paddingV\":0", json);
        Assert.DoesNotContain("paddingMm", json);
        Assert.Equal(1, roundTrip.PaddingHMm);
        Assert.Equal(0, roundTrip.PaddingVMm);
        Assert.Equal(1, roundTrip.EffectivePaddingHMm);
        Assert.Equal(0, roundTrip.EffectivePaddingVMm);
    }

    [Fact]
    public void Legacy_padding_mm_only_falls_back_and_missing_defaults_to_zero()
    {
        const string legacyJson = """{"type":"text","xMm":0,"yMm":0,"sourceKey":"k","fontHeightMm":5,"fontWidthMm":5,"paddingMm":1.5}""";
        var legacy = Assert.IsType<LabelTextElement>(JsonSerializer.Deserialize<LabelElement>(legacyJson, Options));
        Assert.Null(legacy.PaddingHMm);
        Assert.Null(legacy.PaddingVMm);
        Assert.Equal(1.5, legacy.EffectivePaddingHMm);
        Assert.Equal(1.5, legacy.EffectivePaddingVMm);

        const string bareJson = """{"type":"text","xMm":0,"yMm":0,"sourceKey":"k","fontHeightMm":5,"fontWidthMm":5}""";
        var bare = Assert.IsType<LabelTextElement>(JsonSerializer.Deserialize<LabelElement>(bareJson, Options));
        Assert.Equal(0, bare.EffectivePaddingHMm);
        Assert.Equal(0, bare.EffectivePaddingVMm);
    }

    [Fact]
    public void Qr_code_ecc_and_margin_should_round_trip_and_omit_defaults()
    {
        var element = new LabelQrCodeElement { SourceKey = "qr", XMm = 1, YMm = 1, SizeMm = 20, QrEcc = LabelQrEcc.H, QrMargin = 4 };

        var json = JsonSerializer.Serialize<LabelElement>(element, Options);
        var roundTrip = Assert.IsType<LabelQrCodeElement>(JsonSerializer.Deserialize<LabelElement>(json, Options));

        Assert.Equal(LabelQrEcc.H, roundTrip.QrEcc);
        Assert.Equal(4, roundTrip.QrMargin);
        Assert.Contains("\"qrEcc\":\"H\"", json);
        Assert.Contains("\"qrMargin\":4", json);

        var defaults = new LabelQrCodeElement { SourceKey = "qr", XMm = 1, YMm = 1, SizeMm = 20 };
        var defaultJson = JsonSerializer.Serialize<LabelElement>(defaults, Options);
        Assert.DoesNotContain("qrEcc", defaultJson);
        Assert.DoesNotContain("qrMargin", defaultJson);
        var defaultRoundTrip = Assert.IsType<LabelQrCodeElement>(JsonSerializer.Deserialize<LabelElement>(defaultJson, Options));
        Assert.Equal(LabelQrEcc.M, defaultRoundTrip.QrEcc);
        Assert.Equal(2, defaultRoundTrip.QrMargin);
    }

    [Fact]
    public void Barcode_display_value_false_should_write_and_round_trip()
    {
        var element = new LabelBarcodeElement { SourceKey = "code", XMm = 1, YMm = 1, HeightMm = 20, ModuleWidth = 2, DisplayValue = false };

        var json = JsonSerializer.Serialize<LabelElement>(element, Options);
        var roundTrip = Assert.IsType<LabelBarcodeElement>(JsonSerializer.Deserialize<LabelElement>(json, Options));

        Assert.False(roundTrip.DisplayValue);
        Assert.Contains("\"displayValue\":false", json);

        var defaults = new LabelBarcodeElement { SourceKey = "code", XMm = 1, YMm = 1, HeightMm = 20, ModuleWidth = 2 };
        var defaultJson = JsonSerializer.Serialize<LabelElement>(defaults, Options);
        Assert.DoesNotContain("displayValue", defaultJson);
        var defaultRoundTrip = Assert.IsType<LabelBarcodeElement>(JsonSerializer.Deserialize<LabelElement>(defaultJson, Options));
        Assert.True(defaultRoundTrip.DisplayValue);
    }

    [Fact]
    public void Unknown_type_should_throw_json_exception()
    {
        const string json = """{"type":"hexagon","xMm":0,"yMm":0}""";

        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<LabelElement>(json, Options));
    }

    [Fact]
    public void Text_bold_should_write_and_round_trip_and_omit_default()
    {
        var bold = new LabelTextElement { SourceKey = "k", XMm = 0, YMm = 0, FontHeightMm = 5, FontWidthMm = 5, Bold = true };

        var json = JsonSerializer.Serialize<LabelElement>(bold, Options);
        var roundTrip = Assert.IsType<LabelTextElement>(JsonSerializer.Deserialize<LabelElement>(json, Options));

        Assert.True(roundTrip.Bold);
        Assert.Contains("\"bold\":true", json);

        var regular = new LabelTextElement { SourceKey = "k", XMm = 0, YMm = 0, FontHeightMm = 5, FontWidthMm = 5 };
        var regularJson = JsonSerializer.Serialize<LabelElement>(regular, Options);
        Assert.DoesNotContain("bold", regularJson);

        const string oldJson = """{"type":"text","xMm":0,"yMm":0,"sourceKey":"k","fontHeightMm":5,"fontWidthMm":5}""";
        var old = Assert.IsType<LabelTextElement>(JsonSerializer.Deserialize<LabelElement>(oldJson, Options));
        Assert.False(old.Bold);
    }

    private static string GetTypeName(LabelElement element) => element switch
    {
        LabelTextElement => "text",
        LabelBarcodeElement => "barcode",
        LabelQrCodeElement => "qrcode",
        LabelImageElement => "image",
        LabelLineElement => "line",
        LabelRegionElement => "region",
        _ => throw new InvalidOperationException(),
    };
}
