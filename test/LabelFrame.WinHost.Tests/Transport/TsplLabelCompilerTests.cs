using System.Globalization;
using LabelFrame.Core.Documents;
using LabelFrame.Core.Jobs;
using LabelFrame.Core.Layout;
using LabelFrame.Core.Transport.Plugins;
using LabelFrame.TransportPlugin.Tspl;

namespace LabelFrame.WinHost.Tests.Transport;

/// <summary>
/// TSPL 原生指令编译器单测（按文件内分节分组：页结构 / 文本 / 条码 / 二维码 / 线与边框）。
/// </summary>
public class TsplLabelCompilerTests
{
    private static readonly TsplLabelCompiler Compiler = new();

    private static LabelCommandCompileOptions Options(int dpi = 203)
        => new(dpi, new TransportPluginContext(TextWriter.Null, Path.GetTempPath()));

    private static LabelDocument DocWithCode(string code, params LabelElement[] elements)
        => new()
        {
            Layout = new LabelLayout { Name = "t", ContractName = "t", ContractVersion = "1.0", WidthMm = 40, HeightMm = 20, Elements = elements.Length > 0 ? elements : [Text()] },
            Data = new Dictionary<string, string> { ["code"] = code },
        };

    private static LabelDocument Doc(params LabelElement[] elements)
        => DocWithCode("A-1", elements);

    private static LabelTextElement Text(
        double xMm = 2, double yMm = 2, double fontHeightMm = 5, double fontWidthMm = 0, bool bold = false,
        double widthMm = 0, double heightMm = 0, LabelTextAlign textAlign = LabelTextAlign.Left,
        LabelVerticalAlign verticalAlign = LabelVerticalAlign.Middle, string? regionId = null,
        LabelRegionAlign? regionHAlign = null, string sourceKey = "code", double borderMm = 0)
        => new()
        {
            SourceKey = sourceKey,
            XMm = xMm, YMm = yMm, FontHeightMm = fontHeightMm, FontWidthMm = fontWidthMm,
            Bold = bold, WidthMm = widthMm, HeightMm = heightMm, TextAlign = textAlign,
            VerticalAlign = verticalAlign, RegionId = regionId, RegionHAlign = regionHAlign,
            BorderMm = borderMm,
        };

    private static LabelBarcodeElement Barcode(double xMm = 2, double yMm = 10, double heightMm = 8, int moduleWidth = 2, bool displayValue = true)
        => new() { SourceKey = "code", XMm = xMm, YMm = yMm, HeightMm = heightMm, ModuleWidth = moduleWidth, DisplayValue = displayValue };

    private static LabelQrCodeElement Qr(double xMm = 2, double yMm = 2, double sizeMm = 15, LabelQrEcc ecc = LabelQrEcc.M, int margin = 2)
        => new() { SourceKey = "code", XMm = xMm, YMm = yMm, SizeMm = sizeMm, QrEcc = ecc, QrMargin = margin };

    private static async Task<string> CompileOkAsync(LabelDocument document, int dpi = 203)
    {
        var result = await Compiler.CompileAsync(document, Options(dpi));
        Assert.Null(result.ErrorCode);
        return result.Command!;
    }

    // ──页结构与 DPI──

    [Fact]
    public async Task Compile_should_emit_self_contained_page_structure()
    {
        var command = await CompileOkAsync(Doc(Text()));

        Assert.StartsWith("SIZE 40 mm,20 mm\r\nDIRECTION 1\r\nCODEPAGE UTF-8\r\nCLS\r\n", command);
        Assert.EndsWith("PRINT 1\r\n", command);
    }

    [Fact]
    public async Task Compile_with_non_positive_dpi_should_fail()
    {
        var result = await Compiler.CompileAsync(Doc(Text()), Options(0));
        Assert.Equal(JobErrorCodes.CommandCompileFailed, result.ErrorCode);
        Assert.Contains("DPI", result.ErrorMessage);
    }

    [Fact]
    public async Task Compile_with_size_fractions_should_keep_precision()
    {
        var layout = new LabelLayout { Name = "t", ContractName = "t", ContractVersion = "1.0", WidthMm = 37.5, HeightMm = 12.25, Elements = [Text()] };
        var command = await CompileOkAsync(new LabelDocument { Layout = layout, Data = new Dictionary<string, string> { ["code"] = "A-1" } });
        Assert.StartsWith("SIZE 37.5 mm,12.25 mm\r\n", command);
    }

    // ──文本──

    [Fact]
    public async Task Compile_text_should_use_scalable_font_with_point_sizes()
    {
        // 字高 5mm → 5 × 72 / 25.4 ≈ 14.17 → 14 磅；(2,2)mm @203dpi → 16 点
        var command = await CompileOkAsync(Doc(Text()));
        Assert.Contains("TEXT 16,16,\"0\",0,14,14,\"A-1\"", command);
    }

    [Fact]
    public async Task Compile_text_with_width_should_scale_x_independently()
    {
        // 宽 3mm → 9 磅；高 5mm → 14 磅
        var command = await CompileOkAsync(Doc(Text(fontWidthMm: 3)));
        Assert.Contains("\"0\",0,9,14,\"A-1\"", command);
    }

    [Fact]
    public async Task Compile_text_bold_should_double_draw_with_one_dot_offset()
    {
        var command = await CompileOkAsync(Doc(Text(bold: true)));
        Assert.Contains("TEXT 16,16,\"0\",0,14,14,\"A-1\"", command);
        Assert.Contains("TEXT 17,16,\"0\",0,14,14,\"A-1\"", command);
    }

    [Fact]
    public async Task Compile_text_should_escape_embedded_quotes()
    {
        var command = await CompileOkAsync(DocWithCode("say \"hi\"", Text()));
        Assert.Contains("\"say [\"]hi[\"]\"", command);
    }

    [Fact]
    public async Task Compile_text_with_chinese_should_fail_with_field_key()
    {
        var result = await Compiler.CompileAsync(DocWithCode("中文", Text()), Options());

        Assert.Equal(JobErrorCodes.CommandCompileFailed, result.ErrorCode);
        Assert.Contains("汉字", result.ErrorMessage);
        Assert.Equal("code", result.FieldKey);
    }

    [Fact]
    public async Task Compile_text_with_latin1_should_pass()
    {
        var command = await CompileOkAsync(DocWithCode("Résumé", Text()));
        Assert.Contains("Résumé", command);
    }

    [Fact]
    public async Task Compile_text_missing_field_data_should_fail()
    {
        var result = await Compiler.CompileAsync(Doc(Text(sourceKey: "absent")), Options());
        Assert.Equal(JobErrorCodes.CommandCompileFailed, result.ErrorCode);
    }

    [Fact]
    public async Task Compile_text_empty_value_should_emit_nothing_but_page()
    {
        var command = await CompileOkAsync(DocWithCode(string.Empty, Text()));
        Assert.Equal("SIZE 40 mm,20 mm\r\nDIRECTION 1\r\nCODEPAGE UTF-8\r\nCLS\r\nPRINT 1\r\n", command);
    }

    [Fact]
    public async Task Compile_text_block_align_should_offset_by_measured_width()
    {
        // 块宽 20mm、右对齐：实测宽度 = 比例字宽 1.556 × 5mm ≈ 7.8mm → 偏移 ≈ 12.2mm ≈ 98 点
        static int TextX(string command)
            => int.Parse(command.Split("TEXT ")[1].Split(',')[0], CultureInfo.InvariantCulture);
        var left = await CompileOkAsync(Doc(Text(widthMm: 20, textAlign: LabelTextAlign.Left)));
        var right = await CompileOkAsync(Doc(Text(widthMm: 20, textAlign: LabelTextAlign.Right)));
        Assert.InRange(TextX(right) - TextX(left), 98, 102);
    }

    [Fact]
    public async Task Compile_text_region_anchored_should_position_in_region()
    {
        // 区域 (10,5) 30×10，元素默认居中锚定（RegionHAlign 未指定 = Center）
        var region = new LabelRegionElement { Id = "r1", XMm = 10, YMm = 5, WidthMm = 30, HeightMm = 10 };
        var layout = new LabelLayout { Name = "t", ContractName = "t", ContractVersion = "1.0", WidthMm = 40, HeightMm = 20, Elements = [region, Text(regionId: "r1")] };
        var command = await CompileOkAsync(new LabelDocument { Layout = layout, Data = new Dictionary<string, string> { ["code"] = "A-1" } });

        // 锚定宽度 = 实测 7.78mm（"A-1" 比例字宽 1.556 × 5mm，居中）：x = 10 + (30 − 7.78) × 0.5 ≈ 21.1mm ≈ 169 点；y = 7.5mm ≈ 60 点
        Assert.Contains("TEXT 169,60", command);
    }

    // ──条码──

    [Fact]
    public async Task Compile_barcode_should_emit_code128_auto_form()
    {
        // (2,10)mm @203dpi → (16,80)；高 8mm → 64 点；narrow 2、wide 恒 1、human 居中 2
        var command = await CompileOkAsync(Doc(Barcode()));
        Assert.Contains("BARCODE 16,80,\"128\",64,2,0,2,1,\"A-1\"", command);
    }

    [Fact]
    public async Task Compile_barcode_without_display_value_should_turn_human_off()
    {
        var command = await CompileOkAsync(Doc(Barcode(displayValue: false)));
        Assert.Contains(",\"128\",64,0,0,2,1,", command);
    }

    [Fact]
    public async Task Compile_barcode_with_chinese_should_fail_with_encode_code()
    {
        var result = await Compiler.CompileAsync(DocWithCode("中文", Barcode()), Options());
        Assert.Equal(JobErrorCodes.EncodeFailed, result.ErrorCode);
        Assert.Equal("code", result.FieldKey);
    }

    [Fact]
    public async Task Compile_barcode_empty_value_should_skip()
    {
        var command = await CompileOkAsync(DocWithCode("  ", Barcode()));
        Assert.DoesNotContain("BARCODE", command);
    }

    // ──二维码──

    [Fact]
    public async Task Compile_qrcode_should_estimate_cell_width_and_center()
    {
        // "A-1" UTF-8 3 字节 + ECC M → V1（26 字节容量）；矩阵 21 + 2×2 静区 = 25 模块；
        // 15mm @203dpi = 120 点 → cell = 120/25 = 4.8 → 5（钳位内）；inset = (120 − 21×5)/2 = 7.5 → 8
        // (2,2)mm → 16,16 → 定位 24,24
        var command = await CompileOkAsync(Doc(Qr()));
        Assert.Contains("QRCODE 24,24,M,5,A,0,\"A-1\"", command);
    }

    [Fact]
    public async Task Compile_qrcode_cell_width_should_clamp_to_upper_bound()
    {
        // 超小数据 + 超大边长：25mm @203dpi = 200 点 ÷ 25 模块 = 8 → 直接 8；更大则钳 10
        var small = await CompileOkAsync(Doc(Qr(sizeMm: 25)));
        Assert.Contains(",M,8,A,0,", small);

        var huge = await CompileOkAsync(Doc(Qr(sizeMm: 60)));
        Assert.Contains(",M,10,A,0,", huge);
    }

    [Fact]
    public async Task Compile_qrcode_with_chinese_should_pass_through_utf8()
    {
        var command = await CompileOkAsync(DocWithCode("中文二维码", Qr()));
        Assert.Contains("中文二维码", command);
    }

    [Fact]
    public async Task Compile_qrcode_ecc_should_map()
    {
        var command = await CompileOkAsync(Doc(Qr(ecc: LabelQrEcc.H)));
        Assert.Contains("QRCODE ", command);
        Assert.Contains(",H,", command);
    }

    // ──线与边框──

    [Fact]
    public async Task Compile_horizontal_line_should_emit_bar()
    {
        // (2,18)→(30,18) 线宽 0.3mm：跨度 28mm → 224 点；厚 0.3mm → 2 点
        var line = new LabelLineElement { XMm = 2, YMm = 18, X2Mm = 30, Y2Mm = 18, ThicknessMm = 0.3 };
        var command = await CompileOkAsync(Doc(line));
        Assert.Contains("BAR 16,144,224,2", command);
    }

    [Fact]
    public async Task Compile_vertical_line_should_emit_bar()
    {
        var line = new LabelLineElement { XMm = 5, YMm = 2, X2Mm = 5, Y2Mm = 12, ThicknessMm = 0.5 };
        var command = await CompileOkAsync(Doc(line));
        Assert.Contains("BAR 40,16,4,80", command);
    }

    [Fact]
    public async Task Compile_diagonal_line_should_fail()
    {
        var line = new LabelLineElement { XMm = 2, YMm = 2, X2Mm = 10, Y2Mm = 12, ThicknessMm = 0.3 };
        var result = await Compiler.CompileAsync(Doc(line), Options());
        Assert.Equal(JobErrorCodes.CommandCompileFailed, result.ErrorCode);
        Assert.Contains("斜线", result.ErrorMessage);
    }

    [Fact]
    public async Task Compile_degenerate_line_should_fail()
    {
        var line = new LabelLineElement { XMm = 2, YMm = 2, X2Mm = 2, Y2Mm = 2, ThicknessMm = 0.3 };
        var result = await Compiler.CompileAsync(Doc(line), Options());
        Assert.Equal(JobErrorCodes.CommandCompileFailed, result.ErrorCode);
    }

    [Fact]
    public async Task Compile_region_with_border_should_emit_box_and_content_elements_pass_through()
    {
        // 区域 (10,5) 30×10 边框 0.25mm → (80,40)-(320,120) 厚 2 点
        var region = new LabelRegionElement { Id = "r1", XMm = 10, YMm = 5, WidthMm = 30, HeightMm = 10, BorderMm = 0.25 };
        var layout = new LabelLayout { Name = "t", ContractName = "t", ContractVersion = "1.0", WidthMm = 40, HeightMm = 20, Elements = [region, Text(regionId: "r1")] };
        var command = await CompileOkAsync(new LabelDocument { Layout = layout, Data = new Dictionary<string, string> { ["code"] = "A-1" } });

        Assert.Contains("BOX 80,40,320,120,2", command);
        Assert.Contains("TEXT ", command);
    }

    [Fact]
    public async Task Compile_text_with_border_should_emit_box_with_element_frame()
    {
        // 文本 (2,2) 块 20×6 边框 0.2mm → (16,16)-(176,64) 厚 2 点
        var command = await CompileOkAsync(Doc(Text(widthMm: 20, heightMm: 6, borderMm: 0.2)));
        Assert.Contains("BOX 16,16,176,64,2", command);
    }

    [Fact]
    public async Task Compile_auto_width_text_with_border_should_skip_box()
    {
        var command = await CompileOkAsync(Doc(Text()));
        Assert.DoesNotContain("BOX", command);
    }

    // ──图片与未知元素──

    [Fact]
    public async Task Compile_image_without_source_key_should_skip()
    {
        var command = await CompileOkAsync(Doc(new LabelImageElement { SourceKey = "" }));
        Assert.DoesNotContain("BITMAP", command);
    }

    [Fact]
    public async Task Compile_image_with_source_key_should_fail()
    {
        var result = await Compiler.CompileAsync(Doc(new LabelImageElement { SourceKey = "logo" }), Options());
        Assert.Equal(JobErrorCodes.CommandCompileFailed, result.ErrorCode);
        Assert.Contains("切回图片", result.ErrorMessage);
    }

    // ──插件声明──

    [Fact]
    public void Plugin_should_declare_official_id_and_dual_capabilities()
    {
        var plugin = new TsplTransportPlugin();
        Assert.Equal(TransportPluginIdPolicy.TsplPluginId, plugin.Id);
        Assert.IsAssignableFrom<ILabelCommandCompiler>(plugin);
        Assert.IsAssignableFrom<ILabelImageEncoder>(plugin);
    }

    [Fact]
    public void Plugin_should_declare_parameters_with_visibility_and_i18n_keys()
    {
        var plugin = new TsplTransportPlugin();
        var specs = plugin.Parameters;

        Assert.Equal("kind", specs[0].Key);
        Assert.Equal("host", specs[1].Key);
        Assert.Equal("Tcp", string.Join(",", specs[1].VisibleWhenValues ?? Array.Empty<string>()));
        Assert.Equal("port", specs[2].Key);
        Assert.Equal("usbName", specs[3].Key);
        Assert.Equal("Usb", string.Join(",", specs[3].VisibleWhenValues ?? Array.Empty<string>()));
        Assert.Equal("printMode", specs[4].Key);
        Assert.All(specs, spec => Assert.False(string.IsNullOrWhiteSpace(spec.LabelKey)));
        var usbOption = Assert.Single(specs[0].Options ?? Array.Empty<TransportParameterOption>(), o => o.Value == "Usb");
        Assert.False(string.IsNullOrWhiteSpace(usbOption.LabelKey));
    }
}
