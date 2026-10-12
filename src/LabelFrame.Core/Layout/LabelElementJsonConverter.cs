using System.Text.Json;
using System.Text.Json.Serialization;

namespace LabelFrame.Core.Layout;

/// <summary>
/// 版式元素 JSON 转换器：按 "type" 判别子类型后委托具体类型默认反序列化（不依赖 JsonPolymorphic，
/// ASP.NET Core HttpJsonOptions 等环境稳定）；写方向「非默认才写」省略风格，与前端 convert.ts 一致。
/// </summary>
public sealed class LabelElementJsonConverter : JsonConverter<LabelElement>
{
    /// <inheritdoc />
    public override LabelElement Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var root = document.RootElement;
        var typeName = root.TryGetProperty("type", out var typeProperty)
            ? typeProperty.GetString()
            : null;
        var json = root.GetRawText();

        return typeName switch
        {
            "text" => JsonSerializer.Deserialize<LabelTextElement>(json, options)
                ?? throw new JsonException("版式元素 text 解析结果为空。"),
            "barcode" => JsonSerializer.Deserialize<LabelBarcodeElement>(json, options)
                ?? throw new JsonException("版式元素 barcode 解析结果为空。"),
            "qrcode" => JsonSerializer.Deserialize<LabelQrCodeElement>(json, options)
                ?? throw new JsonException("版式元素 qrcode 解析结果为空。"),
            "image" => JsonSerializer.Deserialize<LabelImageElement>(json, options)
                ?? throw new JsonException("版式元素 image 解析结果为空。"),
            "line" => JsonSerializer.Deserialize<LabelLineElement>(json, options)
                ?? throw new JsonException("版式元素 line 解析结果为空。"),
            "region" => JsonSerializer.Deserialize<LabelRegionElement>(json, options)
                ?? throw new JsonException("版式元素 region 解析结果为空。"),
            _ => throw new JsonException($"未知版式元素类型：{typeName ?? "(空)"}。"),
        };
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, LabelElement value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString("type", value switch
        {
            LabelTextElement => "text",
            LabelBarcodeElement => "barcode",
            LabelQrCodeElement => "qrcode",
            LabelImageElement => "image",
            LabelLineElement => "line",
            LabelRegionElement => "region",
            _ => throw new JsonException($"未知版式元素类型：{value.GetType().Name}。"),
        });

        writer.WriteNumber("xMm", value.XMm);
        writer.WriteNumber("yMm", value.YMm);
        if (value.PaddingMm > 0)
        {
            writer.WriteNumber("paddingMm", value.PaddingMm);
        }

        if (value.PaddingHMm > 0)
        {
            writer.WriteNumber("paddingH", value.PaddingHMm);
        }

        if (value.PaddingVMm > 0)
        {
            writer.WriteNumber("paddingV", value.PaddingVMm);
        }

        if (value.BorderMm > 0)
        {
            writer.WriteNumber("borderMm", value.BorderMm);
        }

        if (value.RegionId is not null)
        {
            writer.WriteString("regionId", value.RegionId);
        }

        if (value.RegionHAlign is not null)
        {
            writer.WriteString("regionHAlign", value.RegionHAlign.Value.ToString());
        }

        if (value.RegionVAlign is not null)
        {
            writer.WriteString("regionVAlign", value.RegionVAlign.Value.ToString());
        }

        switch (value)
        {
            case LabelTextElement text:
                writer.WriteString("sourceKey", text.SourceKey);
                if (text.Literal is not null)
                {
                    writer.WriteString("literal", text.Literal);
                }
                if (text.PreviewValue is not null)
                {
                    writer.WriteString("previewValue", text.PreviewValue);
                }

                writer.WriteString("fontName", text.FontName);
                writer.WriteNumber("fontHeightMm", text.FontHeightMm);
                writer.WriteNumber("fontWidthMm", text.FontWidthMm);
                if (text.WidthMm > 0)
                {
                    writer.WriteNumber("widthMm", text.WidthMm);
                }

                if (text.HeightMm > 0)
                {
                    writer.WriteNumber("heightMm", text.HeightMm);
                }

                if (text.TextAlign != LabelTextAlign.Left)
                {
                    writer.WriteString("textAlign", text.TextAlign.ToString());
                }

                if (text.VerticalAlign != LabelVerticalAlign.Middle)
                {
                    writer.WriteString("verticalAlign", text.VerticalAlign.ToString());
                }

                if (!string.Equals(text.FontFamily, LabelTextElement.DefaultFontFamily, StringComparison.Ordinal))
                {
                    writer.WriteString("fontFamily", text.FontFamily);
                }

                if (text.Wrap)
                {
                    writer.WriteBoolean("wrap", true);
                }

                if (text.LineHeight != 1.2)
                {
                    writer.WriteNumber("lineHeight", text.LineHeight);
                }

                if (text.FitMode != LabelFitMode.Shrink)
                {
                    // 前端契约枚举值为小写 shrink / overflow，不得直写 ToString()
                    writer.WriteString("fitMode", text.FitMode == LabelFitMode.Overflow ? "overflow" : "shrink");
                }

                if (text.Bold)
                {
                    writer.WriteBoolean("bold", true);
                }

                break;
            case LabelBarcodeElement barcode:
                writer.WriteString("sourceKey", barcode.SourceKey);
                if (barcode.Literal is not null)
                {
                    writer.WriteString("literal", barcode.Literal);
                }
                if (barcode.PreviewValue is not null)
                {
                    writer.WriteString("previewValue", barcode.PreviewValue);
                }

                writer.WriteNumber("heightMm", barcode.HeightMm);
                writer.WriteNumber("moduleWidth", barcode.ModuleWidth);
                if (!barcode.DisplayValue)
                {
                    writer.WriteBoolean("displayValue", false);
                }

                break;
            case LabelQrCodeElement qrCode:
                writer.WriteString("sourceKey", qrCode.SourceKey);
                if (qrCode.Literal is not null)
                {
                    writer.WriteString("literal", qrCode.Literal);
                }
                if (qrCode.PreviewValue is not null)
                {
                    writer.WriteString("previewValue", qrCode.PreviewValue);
                }

                writer.WriteNumber("sizeMm", qrCode.SizeMm);
                if (qrCode.QrEcc != LabelQrEcc.M)
                {
                    writer.WriteString("qrEcc", qrCode.QrEcc.ToString());
                }

                if (qrCode.QrMargin != 2)
                {
                    writer.WriteNumber("qrMargin", qrCode.QrMargin);
                }

                break;
            case LabelImageElement image:
                writer.WriteString("sourceKey", image.SourceKey);
                writer.WriteNumber("widthMm", image.WidthMm);
                writer.WriteNumber("heightMm", image.HeightMm);
                break;
            case LabelLineElement line:
                writer.WriteNumber("x2Mm", line.X2Mm);
                writer.WriteNumber("y2Mm", line.Y2Mm);
                writer.WriteNumber("thicknessMm", line.ThicknessMm);
                break;
            case LabelRegionElement region:
                writer.WriteString("id", region.Id);
                writer.WriteNumber("widthMm", region.WidthMm);
                writer.WriteNumber("heightMm", region.HeightMm);
                break;
        }

        writer.WriteEndObject();
    }
}
