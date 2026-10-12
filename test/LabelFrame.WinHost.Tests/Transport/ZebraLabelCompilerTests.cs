using LabelFrame.Core.Documents;
using LabelFrame.Core.Jobs;
using LabelFrame.Core.Layout;
using LabelFrame.Core.Transport.Plugins;
using LabelFrame.TransportPlugin.Zebra;

namespace LabelFrame.WinHost.Tests.Transport;

/// <summary>
/// Zebra 原生指令编译器单测（文本 + 条码与二维码 ；DESIGN）：
/// 能力与参数声明（能力位真 / printMode Select / 原生模式 QR 中文说明文案锚定）+
/// 文本编译正确性（整页自包含结构 / ^A 字体与字高 DPI 换算 /
/// 超长文本不换行不缩放直接输出 / 锚定与对齐起始坐标换算路径 / 中文 LF_ENC_002 显式拒绝）+
/// Code 128 条码编译（^BC 结构 / 模块宽与高度 DPI 换算 / displayValue 映射 / 中文 LF_ENC_001 既有语义零回归）+
/// QR 二维码编译（^BQ 结构 / ECC 与边距映射 / 放大倍数尺寸换算 / UTF-8 中文 / 混合模板整页自包含）。
/// 编译输出为确定性文本，不依赖真机（单测口径）；真机扫码效果验收归 （转待验收）。
/// </summary>
public class ZebraLabelCompilerTests
{
    private static readonly ZebraLabelCompiler Compiler = new();

    private static LabelCommandCompileOptions Options(int dpi = 203)
        => new(dpi, new TransportPluginContext(TextWriter.Null, Path.GetTempPath()));

    /// <summary>默认文本元素：绑定字段 code，(2,2)mm，字高 5mm，字体 0。</summary>
    private static LabelTextElement Text(
        double xMm = 2,
        double yMm = 2,
        double fontHeightMm = 5,
        string fontName = "0",
        double fontWidthMm = 0,
        bool bold = false,
        double widthMm = 0,
        double heightMm = 0,
        LabelTextAlign textAlign = LabelTextAlign.Left,
        LabelVerticalAlign verticalAlign = LabelVerticalAlign.Middle,
        string? regionId = null,
        LabelRegionAlign? regionHAlign = null)
        => new()
        {
            SourceKey = "code",
            XMm = xMm,
            YMm = yMm,
            FontHeightMm = fontHeightMm,
            FontName = fontName,
            FontWidthMm = fontWidthMm,
            Bold = bold,
            WidthMm = widthMm,
            HeightMm = heightMm,
            TextAlign = textAlign,
            VerticalAlign = verticalAlign,
            RegionId = regionId,
            RegionHAlign = regionHAlign,
        };

    /// <summary>默认条码元素：绑定字段 code，(2,10)mm，高 8mm，模块宽 2，displayValue 开。</summary>
    private static LabelBarcodeElement Barcode(
        double xMm = 2,
        double yMm = 10,
        double heightMm = 8,
        int moduleWidth = 2,
        bool displayValue = true,
        string sourceKey = "code")
        => new()
        {
            SourceKey = sourceKey,
            XMm = xMm,
            YMm = yMm,
            HeightMm = heightMm,
            ModuleWidth = moduleWidth,
            DisplayValue = displayValue,
        };

    /// <summary>默认二维码元素：绑定字段 code，(2,2)mm，边长 15mm，ECC M，边距 2 模块。</summary>
    private static LabelQrCodeElement Qr(
        double xMm = 2,
        double yMm = 2,
        double sizeMm = 15,
        LabelQrEcc ecc = LabelQrEcc.M,
        int margin = 2,
        string sourceKey = "code")
        => new()
        {
            SourceKey = sourceKey,
            XMm = xMm,
            YMm = yMm,
            SizeMm = sizeMm,
            QrEcc = ecc,
            QrMargin = margin,
        };

    private static LabelDocument Document(string text = "A-01", LabelTextElement? textElement = null, params LabelElement[] extraElements)
        => new()
        {
            Layout = new LabelLayout
            {
                Name = "t",
                ContractName = "c",
                ContractVersion = "1.0",
                WidthMm = 40,
                HeightMm = 20,
                Elements = textElement is null
                    ? [Text(), .. extraElements]
                    : [textElement, .. extraElements],
            },
            Data = new Dictionary<string, string> { ["code"] = text },
        };

    /// <summary>自定义数据与元素的文档（文本与条码 / 二维码用各自字段键区分数据源）。</summary>
    private static LabelDocument DocumentWithData(IReadOnlyDictionary<string, string> data, params LabelElement[] elements)
        => new()
        {
            Layout = new LabelLayout
            {
                Name = "t",
                ContractName = "c",
                ContractVersion = "1.0",
                WidthMm = 40,
                HeightMm = 20,
                Elements = elements,
            },
            Data = data,
        };

    private static LabelCommandCompileResult Compile(LabelDocument document, int dpi = 203)
        => Compiler.CompileAsync(document, Options(dpi)).GetAwaiter().GetResult();

    private static LabelCommandCompileResult CompilePlugin(LabelDocument document, int dpi = 203)
        => new ZebraTransportPlugin().CompileAsync(document, Options(dpi)).GetAwaiter().GetResult();

    // ── ：能力与参数声明 ──

    [Fact]
    public void Plugin_should_implement_compiler_and_registry_capability_should_be_true()
    {
        // 能力实现于插件对象：注册表装配期判定 plugin is ILabelCommandCompiler → 能力位真
        // GET /api/transport 的 DTO 为 descriptor 字段透传（WinHostEndpointsTests 已锚定映射），此处锚定源头
        var plugin = new ZebraTransportPlugin();
        Assert.IsAssignableFrom<ILabelCommandCompiler>(plugin);

        var registry = new TransportPluginRegistry();
        registry.Register(plugin);
        var descriptor = registry.GetPlugin(TransportPluginIdPolicy.ZebraPluginId);
        Assert.NotNull(descriptor);
        Assert.True(descriptor!.SupportsDocumentCompile);
        Assert.Equal(plugin, registry.GetCommandCompiler(TransportPluginIdPolicy.ZebraPluginId));
    }

    [Fact]
    public void Plugin_should_declare_print_mode_select_parameter()
    {
        // 决议 2：Select 参数 Key printMode、必填、默认 image、Options image / native
        var spec = new ZebraTransportPlugin().Parameters.Single(p => p.Key == TransportPrintMode.ParameterKey);
        Assert.Equal(TransportParameterType.Select, spec.Type);
        Assert.True(spec.Required);
        Assert.Equal(TransportPrintMode.Image, spec.DefaultValue);
        Assert.Equal(
            new[] { TransportPrintMode.Image, TransportPrintMode.Native },
            spec.Options!.Select(o => o.Value).ToArray());
    }

    [Fact]
    public void Plugin_description_and_print_mode_hint_should_note_native_qr_chinese_limit()
    {
        // （a 案仅说明文案）：原生指令模式下二维码（QR）数据不支持中文（仅 ASCII）——真机实证见 
        // （原生 ^CI28 下中文被打印机静默过滤），中文走文本框（图片模式 / 原生 LF_ENC_002 既有拒绝口径）；
        // GET /api/transport 的 Description / Hint 为 descriptor 字段透传（WinHostEndpointsTests 已锚定映射）
        // 此处锚定源头（插件声明 + 注册表 descriptor——API availablePlugins 的数据来源）。
        var plugin = new ZebraTransportPlugin();

        // 说明文（Description）：含该说明且限定「原生指令模式」，句读完整（文案风格）
        Assert.Contains("原生指令模式下二维码（QR）数据不支持中文（仅 ASCII）", plugin.Description);
        Assert.Contains("中文请用文本框（图片模式）", plugin.Description);
        Assert.EndsWith("。", plugin.Description);

        // 打印方式提示（printMode Hint）：首句限定原生指令模式，QR 中文说明随其后（同句境限定，不外溢图片模式）
        var spec = plugin.Parameters.Single(p => p.Key == TransportPrintMode.ParameterKey);
        Assert.NotNull(spec.Hint);
        Assert.StartsWith("原生指令模式", spec.Hint);
        Assert.Contains("二维码（QR）数据不支持中文（仅 ASCII）", spec.Hint);
        Assert.Contains("中文请用文本框（图片模式）", spec.Hint);

        // 注册表 descriptor（GET /api/transport.availablePlugins 透出来源）同文案锚定
        var registry = new TransportPluginRegistry();
        registry.Register(plugin);
        var descriptor = registry.GetPlugin(TransportPluginIdPolicy.ZebraPluginId);
        Assert.NotNull(descriptor);
        Assert.Contains("原生指令模式下二维码（QR）数据不支持中文（仅 ASCII）", descriptor!.Description);
        var descriptorSpec = descriptor.Parameters.Single(p => p.Key == TransportPrintMode.ParameterKey);
        Assert.Contains("二维码（QR）数据不支持中文（仅 ASCII）", descriptorSpec.Hint);
    }

    // ── ：整页自包含结构（^XA / ^XZ / ^PW / ^LL）与文本指令 ──

    [Fact]
    public void Compile_should_produce_self_contained_page_structure()
    {
        var result = Compile(Document());

        Assert.NotNull(result.Command);
        Assert.Null(result.ErrorCode);
        var lines = result.Command!.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("^XA", lines[0]);
        Assert.Equal("^XZ", lines[^1]);
        // 40mm / 20mm @203dpi → 320 / 160 点（毫米 → 点按 DPI 换算）
        Assert.Equal("^PW320", lines[1]);
        Assert.Equal("^LL160", lines[2]);
        // ^CI28 = UTF-8 国际编码（Latin-1 字符通道）
        Assert.Contains("^CI28", result.Command);
    }

    [Fact]
    public void Compile_should_scale_coordinates_and_font_height_by_dpi()
    {
        // 同一文档 300 dpi：2mm → 24 点、字高 4mm → 47 点（DPI 换算路径）
        var result = Compile(Document(text: "A-1", textElement: Text(fontHeightMm: 4)), dpi: 300);

        Assert.Contains("^FO24,24^A0N,47^FH^FDA-1^FS", result.Command);
        Assert.Contains("^PW472", result.Command);
        Assert.Contains("^LL236", result.Command);
    }

    [Fact]
    public void Compile_should_use_template_font_and_bold_variant_mapping()
    {
        // 模板保留的 ZPL 内置字体名原样输出（^ABN）；默认字体 0（缩放字体）
        Assert.Contains("^FO16,16^ABN,40,40^FH^FDA-01^FS", Compile(Document(textElement: Text(fontName: "B", fontWidthMm: 5))).Command);
        Assert.Contains("^FO16,16^A0N,40^FH^FDA-01^FS", Compile(Document()).Command);

        // 加粗近似（方案 A）：粗体字体变体映射 0 → 1
        Assert.Contains("^A1N,40", Compile(Document(textElement: Text(bold: true))).Command);
        // 无粗体变体映射的字体：加粗忽略（尽力项，效果以真机验收为准）
        Assert.Contains("^AAN,40", Compile(Document(textElement: Text(fontName: "A", bold: true))).Command);
    }

    [Fact]
    public void Compile_should_escape_caret_tilde_and_underscore_via_field_hex()
    {
        // ^FH 十六进制转义（官方口径）：^ / ~ / _ 不进入指令层
        var result = Compile(Document(text: "a^b~c_d"));

        Assert.Contains("^FH^FDa_5Eb_7Ec_5Fd^FS", result.Command);
    }

    [Fact]
    public void Compile_should_accept_latin1_characters()
    {
        var result = Compile(Document(text: "café"));

        Assert.Null(result.ErrorCode);
        Assert.Contains("café", result.Command);
    }

    [Fact]
    public void Compile_should_skip_empty_text_without_field_command()
    {
        var result = Compile(Document(text: string.Empty));

        Assert.Null(result.ErrorCode);
        Assert.DoesNotContain("^FD", result.Command);
    }

    // ── ：超长文本不换行不缩放直接输出（排版降级——溢出为预期常态）──

    [Fact]
    public void Compile_should_output_overlong_text_as_single_line_without_scaling()
    {
        var longText = new string('A', 60) + "-" + new string('B', 60); // 远超 40mm 版心
        var result = Compile(Document(text: longText));

        // 不换行：单个 ^FD 输出全部内容；不缩放：字高仍按模板 5mm（40 点）
        Assert.Null(result.ErrorCode);
        Assert.Contains($"^A0N,40^FH^FD{longText}^FS", result.Command);
        Assert.Equal(1, CountOccurrences(result.Command!, "^FD"));
    }

    // ── ：锚定 / 对齐的起始坐标换算路径（尽力近似：近似路径存在即可，不要求与图片模式等价）──

    [Fact]
    public void Compile_should_shift_origin_by_region_anchor_alignment_with_builtin_metrics()
    {
        // 区域 (10,20) 80×30mm；自动宽度文本 "ABC"（缩放字体 5mm，实测 (667+667+722)/1000×5 = 10.28mm）：
        // Start → x=10mm（80 点）；End → x=79.72mm（638 点）；y 垂直居中 → 32.5mm（260 点）
        LabelDocument Create(LabelRegionAlign? align)
        {
            var region = new LabelRegionElement { Id = "r1", XMm = 10, YMm = 20, WidthMm = 80, HeightMm = 30 };
            return Document(text: "ABC", textElement: Text(regionId: "r1", regionHAlign: align), region);
        }

        var start = Compile(Create(LabelRegionAlign.Start)).Command!;
        var end = Compile(Create(LabelRegionAlign.End)).Command!;

        Assert.Contains("^FO80,260", start); // Start：区域左缘（10mm = 80 点）
        Assert.Contains("^FO637,260", end); // End：按内置字体度量右移 79.72mm = 637 点（锚定换算路径可断言）
    }

    [Fact]
    public void Compile_should_approximate_block_text_align_and_vertical_align()
    {
        // 显式块宽 30mm、块高 10mm、字高 5mm，文本 ""（实测 （667+333+556）/1000×5 = 7.78mm）：
        // TextAlign Center → x = 2 + (30-7.78)×0.5 = 13.11mm（105 点）；VerticalAlign Bottom → y = 2 + (10-5)×1 = 7mm（56 点）
        var result = Compile(Document(
            text: "A-1",
            textElement: Text(widthMm: 30, heightMm: 10, textAlign: LabelTextAlign.Center, verticalAlign: LabelVerticalAlign.Bottom)));

        Assert.Contains("^FO105,56^A0N,40", result.Command);
    }

    [Fact]
    public void Region_element_should_compile_as_container_without_graphic_command()
    {
        // 区域 = 版式容器：仅参与锚定解析，自身不产出指令；边框为图片模式能力（裁剪）
        var region = new LabelRegionElement { Id = "r1", XMm = 1, YMm = 1, WidthMm = 10, HeightMm = 10, BorderMm = 0.2 };
        var result = Compile(Document(extraElements: region));

        Assert.Null(result.ErrorCode);
        Assert.DoesNotContain("^GB", result.Command);
        Assert.Equal(1, CountOccurrences(result.Command!, "^FD"));
    }

    // ── ：中文（无字库）→ LF_ENC_002 显式拒绝──

    [Fact]
    public void Chinese_text_should_be_rejected_with_lf_enc_002_and_field_key()
    {
        var result = Compile(Document(text: "库位A-01"));

        Assert.Null(result.Command);
        Assert.Equal(JobErrorCodes.CommandCompileFailed, result.ErrorCode);
        Assert.Contains("内置字体无法表示的字符", result.ErrorMessage);
        Assert.Contains("5E93", result.ErrorMessage); // 首个问题字符码点「库」U+5E93（可定位）
        Assert.Equal("code", result.FieldKey);
    }

    [Theory]
    [InlineData("中文")]
    [InlineData("カナ")]
    [InlineData("한글")]
    [InlineData("🙂")]
    public void Unsupported_scripts_should_be_rejected(string text)
    {
        var result = Compile(Document(text: text));

        Assert.Null(result.Command);
        Assert.Equal(JobErrorCodes.CommandCompileFailed, result.ErrorCode);
    }

    [Fact]
    public void Control_characters_should_be_rejected()
    {
        var result = Compile(Document(text: "A\r\nB"));

        Assert.Equal(JobErrorCodes.CommandCompileFailed, result.ErrorCode);
    }

    // ── ：Code 128 条码编译（^BC / ^BY 结构 / 模块宽与高度 DPI 换算 / displayValue 映射）──

    [Fact]
    public void Barcode_should_compile_code128_structure_with_by_and_bc()
    {
        // (2,10)mm @203dpi → ^FO16,80；高 8mm → 64 点；模块宽 2 透传 ^BY2,3；displayValue=true → interpretation Y
        var result = Compile(Document(textElement: Text(yMm: 1), extraElements: Barcode()));

        Assert.Null(result.ErrorCode);
        Assert.Contains("^FO16,80^BY2,3^BCN,64,Y,N,N^FH^FDA-01^FS", result.Command);
    }

    [Fact]
    public void Barcode_should_scale_position_and_height_by_dpi()
    {
        // 同一文档 300 dpi：(2,10)mm → 24 / 118 点，高 8mm → 94 点（毫米 → 点按 DPI 换算）
        var result = Compile(Document(textElement: Text(yMm: 1), extraElements: Barcode()), dpi: 300);

        Assert.Null(result.ErrorCode);
        Assert.Contains("^FO24,118^BY2,3^BCN,94,Y,N,N", result.Command);
    }

    [Fact]
    public void Barcode_display_value_false_should_turn_off_interpretation_line()
    {
        // displayValue=false → f 参数 N（仅条码，不绘制人眼可读行）；true 为默认 Y
        Assert.Contains("^BCN,64,N,N,N", Compile(Document(textElement: Text(yMm: 1), extraElements: Barcode(displayValue: false))).Command);
        Assert.Contains("^BCN,64,Y,N,N", Compile(Document(textElement: Text(yMm: 1), extraElements: Barcode())).Command);
    }

    [Fact]
    public void Barcode_module_width_should_pass_through_clamped_to_valid_range()
    {
        // 模板字段即 ZPL 模块宽度（点，非毫米量纲）直接透传；越界值夹取到官方 1..10 范围
        Assert.Contains("^BY5,3^BCN", Compile(Document(textElement: Text(yMm: 1), extraElements: Barcode(moduleWidth: 5))).Command);
        Assert.Contains("^BY10,3^BCN", Compile(Document(textElement: Text(yMm: 1), extraElements: Barcode(moduleWidth: 99))).Command);
        Assert.Contains("^BY1,3^BCN", Compile(Document(textElement: Text(yMm: 1), extraElements: Barcode(moduleWidth: 0))).Command);
    }

    [Fact]
    public void Barcode_special_characters_should_escape_via_field_hex_and_invocation_code()
    {
        // ^ / ~ / _ 走 ^FH 十六进制转义（指令控制字符不进指令层）；> 按官方调用码映射 >0（子集 B 调用前缀）
        var result = Compile(Document(text: "a^b~c_d>e", textElement: Text(yMm: 1), extraElements: Barcode()));

        Assert.Null(result.ErrorCode);
        Assert.Contains("^FH^FDa_5Eb_7Ec_5Fd>0e^FS", result.Command);
    }

    [Fact]
    public void Barcode_empty_or_whitespace_value_should_skip_element()
    {
        // 空白值跳过（与图片模式渲染口径一致：不绘制空条码）
        Assert.DoesNotContain("^BC", Compile(Document(text: string.Empty, textElement: Text(yMm: 1), extraElements: Barcode())).Command);
        Assert.DoesNotContain("^BC", Compile(Document(text: "   ", textElement: Text(yMm: 1), extraElements: Barcode())).Command);
    }

    // ── ：Code 128 中文值 → LF_ENC_001 编码拒绝（既有语义零回归，DESIGN §7）──

    [Fact]
    public void Barcode_chinese_value_should_be_rejected_with_lf_enc_001_and_field_key()
    {
        // Code 128 字符集仅可打印 ASCII：中文值与图片模式渲染层同一拒绝语义（LF_ENC_001，非文本元素的 LF_ENC_002）
        var result = Compile(DocumentWithData(
            new Dictionary<string, string> { ["code"] = "A-01", ["bc"] = "库位A-01" },
            Text(yMm: 1),
            Barcode(sourceKey: "bc")));

        Assert.Null(result.Command);
        Assert.Equal(JobErrorCodes.EncodeFailed, result.ErrorCode);
        Assert.Contains("Code 128", result.ErrorMessage);
        Assert.Contains("5E93", result.ErrorMessage); // 首个问题字符码点「库」U+5E93（可定位）
        Assert.Equal("bc", result.FieldKey);
    }

    [Theory]
    [InlineData("café")] // Latin-1 亦超出 Code 128 ASCII 字符集
    [InlineData("A\rB")] // 控制字符
    [InlineData("カナ")]
    [InlineData("🙂")]
    public void Barcode_values_outside_printable_ascii_should_be_rejected(string value)
    {
        var result = Compile(DocumentWithData(
            new Dictionary<string, string> { ["code"] = "A-01", ["bc"] = value },
            Text(yMm: 1),
            Barcode(sourceKey: "bc")));

        Assert.Null(result.Command);
        Assert.Equal(JobErrorCodes.EncodeFailed, result.ErrorCode);
    }

    // ── ：QR 二维码编译（^BQ 结构 / ECC 与边距映射 / 放大倍数按尺寸与 DPI 换算）──

    [Fact]
    public void Qrcode_should_compile_bq_structure_with_magnification_from_size()
    {
        // 15mm @203dpi = 120 点；"ABC123"（6 字节，ECC M ≤ V1 容量 14）→ 矩阵 21 模块 + 边距 2×2 → 总 25；
        // 放大倍数 = round(120/25) = 5；矩阵 105 点在框内居中 → 内缩 round((120-105)/2) = 8 → ^FO24,24（(2,2)mm=16 点）
        var result = Compile(Document(text: "ABC123", textElement: Text(yMm: 1), extraElements: Qr()));

        Assert.Null(result.ErrorCode);
        Assert.Contains("^FO24,24^BQN,2,5,M^FH^FDMA,ABC123^FS", result.Command);
    }

    [Fact]
    public void Qrcode_should_scale_magnification_by_dpi()
    {
        // 同一文档 300 dpi：15mm = 177 点 → 放大倍数 round(177/25) = 7；内缩 round((177-147)/2) = 15 → (2,2)mm=24 点 +15
        var result = Compile(Document(text: "ABC123", textElement: Text(yMm: 1), extraElements: Qr()), dpi: 300);

        Assert.Null(result.ErrorCode);
        Assert.Contains("^FO39,39^BQN,2,7,M", result.Command);
    }

    [Theory]
    [InlineData(LabelQrEcc.L)]
    [InlineData(LabelQrEcc.M)]
    [InlineData(LabelQrEcc.Q)]
    [InlineData(LabelQrEcc.H)]
    public void Qrcode_ecc_should_map_to_bq_parameter_and_fd_switch(LabelQrEcc ecc)
    {
        // qrEcc → ECC 档双处一致输出：^BQ d 参数 + ^FD 必填 ECC 开关（官方 QR 开关形态 ^FD<ECC>A,<数据>）
        var result = Compile(Document(text: "ABC123", textElement: Text(yMm: 1), extraElements: Qr(ecc: ecc)));

        Assert.Null(result.ErrorCode);
        Assert.Contains($"^BQN,2,5,{ecc}^FH^FD{ecc}A,ABC123^FS", result.Command);
    }

    [Fact]
    public void Qrcode_margin_should_map_to_quiet_zone_via_magnification_and_inset()
    {
        // qrMargin = 静区模块数（与图片模式 ZXing Margin 语义一致）：
        // 边距 0 → 总 21 模块 → 放大倍数 6、矩阵 126 点超出 120 框不内缩（尽力近似）；边距 4 → 总 29 → 放大倍数 4、内缩 18
        var none = Compile(Document(text: "ABC123", textElement: Text(yMm: 1), extraElements: Qr(margin: 0))).Command;
        var wide = Compile(Document(text: "ABC123", textElement: Text(yMm: 1), extraElements: Qr(margin: 4))).Command;

        Assert.Contains("^FO16,16^BQN,2,6,M", none);
        Assert.Contains("^FO34,34^BQN,2,4,M", wide);
    }

    [Fact]
    public void Qrcode_longer_content_should_estimate_larger_version_and_shrink_magnification()
    {
        // 40 字节（ECC M）超 V2 容量 26 → 估算 V3（容量 42）→ 矩阵 29 模块 + 边距 2×2 → 总 33；
        // 15mm=120 点 → 放大倍数 round(120/33) = 4、内缩 round((120-116)/2) = 2（短内容放大倍数为 5——
        // 内容变长符号变大，放大倍数自适应缩小以近似模板边长）
        var result = Compile(Document(
            text: "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789ABCD",
            textElement: Text(yMm: 1),
            extraElements: Qr()));

        Assert.Null(result.ErrorCode);
        Assert.Contains("^FO18,18^BQN,2,4,M", result.Command);
    }

    [Fact]
    public void Qrcode_utf8_chinese_value_should_compile_with_charset_instruction()
    {
        // UTF-8 中文可编译（^CI28 字符集指令在场，整页头部输出）；"库位A-01" 10 字节 ≤ V1-M 容量 14 → 矩阵 21 模块
        var result = Compile(DocumentWithData(
            new Dictionary<string, string> { ["code"] = "A-01", ["qr"] = "库位A-01" },
            Text(yMm: 1),
            Qr(sourceKey: "qr")));

        Assert.Null(result.ErrorCode);
        Assert.Contains("^CI28", result.Command);
        Assert.Contains("^FDMA,库位A-01^FS", result.Command);
    }

    [Fact]
    public void Qrcode_empty_or_whitespace_value_should_skip_element()
    {
        // 空白值跳过（与图片模式渲染口径一致：不绘制空二维码）
        Assert.DoesNotContain("^BQ", Compile(Document(text: string.Empty, textElement: Text(yMm: 1), extraElements: Qr())).Command);
        Assert.DoesNotContain("^BQ", Compile(Document(text: "  ", textElement: Text(yMm: 1), extraElements: Qr())).Command);
    }

    // ── ：混合模板（文本 + 条码 + 二维码）整页指令完整自包含 ──

    [Fact]
    public void Mixed_template_should_compile_self_contained_page_with_all_element_types()
    {
        var result = Compile(Document("A-01", Text(), Barcode(yMm: 6), Qr(xMm: 27, yMm: 4, sizeMm: 12)));

        Assert.Null(result.ErrorCode);
        var lines = result.Command!.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("^XA", lines[0]);
        Assert.Equal("^XZ", lines[^1]);
        Assert.Contains("^PW320", result.Command); // 40mm @203dpi
        Assert.Contains("^LL160", result.Command); // 20mm @203dpi
        Assert.Contains("^CI28", result.Command); // 字符集指令在场
        Assert.Contains("^A0N,", result.Command); // 文本
        Assert.Contains("^BY2,3^BCN,", result.Command); // 条码
        Assert.Contains("^BQN,2,", result.Command); // 二维码
        Assert.Equal(3, CountOccurrences(result.Command, "^FD")); // 每元素恰好一个数据字段
    }

    // ── 范围外元素：显式失败（图片 / 线元素按裁剪拒绝，条码 / 二维码自起编译支持）──

    [Fact]
    public void Image_element_should_fail_explicitly()
    {
        var result = Compile(Document(extraElements: new LabelImageElement { SourceKey = "logo", YMm = 10, WidthMm = 8, HeightMm = 8 }));

        Assert.Equal(JobErrorCodes.CommandCompileFailed, result.ErrorCode);
        Assert.Contains("图片", result.ErrorMessage);
    }

    [Fact]
    public void Image_element_with_empty_source_key_should_compile_by_skipping()
    {
        // SourceKey 空＝未指定的空占位——静默跳过不出指令（不再无条件 Fail）
        // 其余元素照常编译；key 非空的有图元素仍显式失败（上一用例锚定）
        var result = Compile(Document("A-01", extraElements: new LabelImageElement { SourceKey = "", YMm = 10, WidthMm = 8, HeightMm = 8 }));

        Assert.Null(result.ErrorCode);
        Assert.Null(result.ErrorMessage);
        // 文本元素照常编译（空占位元素被跳过，不产生指令也不阻断整页）
        Assert.Contains("^A0N,", result.Command);
    }

    [Fact]
    public void Line_element_should_fail_explicitly()
    {
        var result = Compile(Document(extraElements: new LabelLineElement { XMm = 1, YMm = 18, X2Mm = 39, Y2Mm = 18, ThicknessMm = 0.3 }));

        Assert.Equal(JobErrorCodes.CommandCompileFailed, result.ErrorCode);
        Assert.Contains("线", result.ErrorMessage);
    }

    // ── 输入口径：DPI 非法显式失败；插件对象与编译器同结果（能力委托）──

    [Fact]
    public void Invalid_dpi_should_fail_explicitly()
    {
        var result = Compile(Document(), dpi: 0);

        Assert.Equal(JobErrorCodes.CommandCompileFailed, result.ErrorCode);
        Assert.Contains("DPI", result.ErrorMessage);
    }

    [Fact]
    public void Plugin_compile_should_delegate_to_compiler()
    {
        Assert.Equal(Compile(Document()).Command, CompilePlugin(Document()).Command);
        Assert.Equal(Compile(Document(text: "库位")).ErrorCode, CompilePlugin(Document(text: "库位")).ErrorCode);
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        var count = 0;
        for (var i = haystack.IndexOf(needle, StringComparison.Ordinal); i >= 0; i = haystack.IndexOf(needle, i + 1, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }
}

/// <summary>
/// Zebra 内置字体度量（ITextWidthMeasurer 编译侧实现）单测：锚定近似换算的确定性口径。
/// </summary>
public class ZebraTextWidthMeasurerTests
{
    [Fact]
    public void Scalable_font_width_should_be_proportional_to_height()
    {
        var measurer = new ZebraTextWidthMeasurer(203);
        var element = new LabelTextElement { SourceKey = "c", FontHeightMm = 5 };

        // "ii"：222/1000 × 2 × 5mm = 2.22mm（Helvetica 同度量近似）；字高翻倍宽度翻倍（比例度量与 DPI 无关）
        Assert.Equal(2.22, measurer.MeasureWidthMm(element, "ii"), precision: 3);
        Assert.Equal(4.44, measurer.MeasureWidthMm(new LabelTextElement { SourceKey = "c", FontHeightMm = 10 }, "ii"), precision: 3);
    }

    [Fact]
    public void Bitmap_font_width_should_scale_by_design_cell()
    {
        var measurer = new ZebraTextWidthMeasurer(203);
        var element = new LabelTextElement { SourceKey = "c", FontHeightMm = 5, FontName = "A" };

        // 字体 A 设计 9×5 点 @203dpi：5mm = 40 点 → 倍率 40/9；两字符宽（点）= 2×5×(40/9)，换算毫米
        var expected = 2 * 5 * (40.0 / 9) * 25.4 / 203;
        Assert.Equal(expected, measurer.MeasureWidthMm(element, "AB"), precision: 6);
    }

    [Fact]
    public void Empty_value_should_measure_zero()
    {
        var measurer = new ZebraTextWidthMeasurer(203);
        Assert.Equal(0, measurer.MeasureWidthMm(new LabelTextElement { SourceKey = "c", FontHeightMm = 5 }, string.Empty));
    }
}
