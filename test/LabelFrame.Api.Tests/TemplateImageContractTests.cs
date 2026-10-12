using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using SkiaSharp;
using Xunit;

namespace LabelFrame.Api.Tests;

/// <summary>
/// 模板保存图片三态契约（· ，，DESIGN）：
/// 不带 images 字段＝保留存量；显式 "images": null＝保留（与同结果——record 可选参数反序列化合并为一态
/// 本用例锚定该合并语义）；空字典＝清空；非空字典＝全量覆盖（旧 key 消失）。
/// 另含：含图保存 → GET 回读 → 预览 PNG 元素盒有墨（黑白）；坏 base64 / 格式 / 上限 400。
/// </summary>
public class TemplateImageContractTests : SharedEndpointsTestBase
{
    private const string TemplateName = "图片三态模板";

    /// <summary>左半红右半白的 8×8 PNG（红色 luma≈76&lt;128 → 黑、白色 → 白：未单色化时红色像素即彩色，供黑白断言）。</summary>
    private static byte[] HalfRedHalfWhitePng()
    {
        using var bitmap = new SKBitmap(new SKImageInfo(8, 8, SKColorType.Bgra8888, SKAlphaType.Opaque));
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.White);
            canvas.DrawRect(0, 0, 4, 8, new SKPaint { Color = SKColors.Red });
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private static new string TemplateJson(string? imagesJson) => $$"""
        {
          "name": "{{TemplateName}}",
          "group": "测试",
          "contract": { "name": "img", "version": "1.0", "fields": [] },
          "layout": {
            "name": "l", "contractName": "img", "contractVersion": "1.0",
            "widthMm": 40, "heightMm": 20,
            "elements": [ { "type": "text", "literal": "X", "xMm": 30, "yMm": 8, "fontHeightMm": 3 },
                          { "type": "image", "sourceKey": "logo.png", "xMm": 2, "yMm": 5, "widthMm": 20, "heightMm": 10 } ]
          }{{(imagesJson is null ? "" : ",\n  \"images\": " + imagesJson)}}
        }
        """;

    private static async Task<JsonElement> SaveAndGetAsync(HttpClient client, string? imagesJson)
    {
        var save = await client.PostAsync("/api/templates", new StringContent(TemplateJson(imagesJson), Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.OK, save.StatusCode);
        return await client.GetFromJsonAsync<JsonElement>($"/api/templates/{Uri.EscapeDataString(TemplateName)}");
    }

    [Fact]
    public async Task Absent_images_field_should_keep_existing_images()
    {
        var client = await CreateClientAsync();
        var first = await SaveAndGetAsync(client, $$"""{ "logo.png": "{{Convert.ToBase64String(HalfRedHalfWhitePng())}}" }""");
        Assert.Single(first.GetProperty("images").EnumerateObject());

        // 不带 images 字段保存新版式 → 存量图保留（旧口径为清空，本用例即修复锚点）
        var second = await SaveAndGetAsync(client, null);
        Assert.True(second.GetProperty("images").TryGetProperty("logo.png", out _), "不带 images 字段应保留存量图片");
    }

    [Fact]
    public async Task Explicit_null_images_should_keep_existing_images_same_as_absent()
    {
        var client = await CreateClientAsync();
        await SaveAndGetAsync(client, $$"""{ "logo.png": "{{Convert.ToBase64String(HalfRedHalfWhitePng())}}" }""");

        // 显式 "images": null 与缺省不可区分（record 可选参数反序列化合并一态）→ 同为保留
        var second = await SaveAndGetAsync(client, "null");
        Assert.True(second.GetProperty("images").TryGetProperty("logo.png", out _), "显式 images:null 应与缺省同为保留存量");
    }

    [Fact]
    public async Task Empty_dict_images_should_clear_all_images()
    {
        var client = await CreateClientAsync();
        await SaveAndGetAsync(client, $$"""{ "logo.png": "{{Convert.ToBase64String(HalfRedHalfWhitePng())}}" }""");

        var second = await SaveAndGetAsync(client, "{}");
        Assert.Empty(second.GetProperty("images").EnumerateObject());
    }

    [Fact]
    public async Task NonEmpty_dict_images_should_overwrite_and_drop_old_keys()
    {
        var client = await CreateClientAsync();
        await SaveAndGetAsync(client, $$"""{ "old.png": "{{Convert.ToBase64String(HalfRedHalfWhitePng())}}" }""");

        var second = await SaveAndGetAsync(client, $$"""{ "new.png": "{{Convert.ToBase64String(HalfRedHalfWhitePng())}}" }""");
        var images = second.GetProperty("images");
        Assert.False(images.TryGetProperty("old.png", out _), "非空字典为全量覆盖：旧 key 应消失");
        Assert.True(images.TryGetProperty("new.png", out _));
    }

    [Fact]
    public async Task Saved_image_should_roundtrip_and_preview_with_monochrome_ink()
    {
        var client = await CreateClientAsync();
        var png = HalfRedHalfWhitePng();
        var detail = await SaveAndGetAsync(client, $$"""{ "logo.png": "{{Convert.ToBase64String(png)}}" }""");

        // 回读 base64 与保存值一致
        Assert.Equal(png, Convert.FromBase64String(detail.GetProperty("images").GetProperty("logo.png").GetString()!));

        // 预览：元素盒（2~22mm × 5~15mm @203dpi ≈ x16~175, y40~119）左半红（→黑）有墨、右半白无墨
        var preview = await client.PostAsync($"/api/templates/{Uri.EscapeDataString(TemplateName)}/preview", new StringContent("{}", Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
        var previewBytes = await preview.Content.ReadAsByteArrayAsync();
        using var decoded = SKBitmap.Decode(previewBytes);
        Assert.NotNull(decoded);
        var black = 0;
        for (var y = 45; y < 115; y++)
        {
            for (var x = 20; x < 88; x++)
            {
                var c = decoded.GetPixel(x, y);
                if (c.Red < 128 && c.Green < 128 && c.Blue < 128) black++;
            }
        }

        Assert.True(black > 500, $"元素盒左半（红→黑）应有墨迹：black={black}");
    }

    [Fact]
    public async Task Invalid_base64_or_bad_format_or_over_limit_should_return_400()
    {
        var client = await CreateClientAsync();

        // 坏 base64
        var bad = await client.PostAsync("/api/templates", new StringContent(TemplateJson("""{ "logo.png": "!!!not-base64!!!" }"""), Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        var error = await ReadErrorAsync(bad);
        Assert.Equal("LF_TPL_002", error.GetProperty("code").GetString());
        Assert.Contains("base64", error.GetProperty("message").GetString());

        // 合法 base64 但非图片字节（格式探嗅失败，含键名）
        var notImage = await client.PostAsync("/api/templates", new StringContent(TemplateJson($$"""{ "logo.png": "{{Convert.ToBase64String("not an image"u8.ToArray())}}" }"""), Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, notImage.StatusCode);
        var formatError = await ReadErrorAsync(notImage);
        Assert.Contains("logo.png", formatError.GetProperty("message").GetString());

        // 单图超 2MB（大字节数组合法 PNG 头 + 超限体量——先过探嗅需真图；改为 2MB 纯白 PNG 不现实，
        // 用格式合法但超限的路径：以 1×1 白 PNG 前缀 + 补零到 2MB+（探嗅只看前 4 字节）
        var tiny = HalfRedHalfWhitePng();
        var oversized = new byte[2 * 1024 * 1024 + 16];
        tiny.AsSpan(0, 4).CopyTo(oversized);
        var tooLarge = await client.PostAsync("/api/templates", new StringContent(TemplateJson($$"""{ "big.png": "{{Convert.ToBase64String(oversized)}}" }"""), Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, tooLarge.StatusCode);
        Assert.Contains("2MB", (await ReadErrorAsync(tooLarge)).GetProperty("message").GetString());

        // 张数超 10
        var dict = new Dictionary<string, string>();
        for (var i = 0; i <= 10; i++) dict[$"i{i}.png"] = Convert.ToBase64String(tiny);
        var many = await client.PostAsync("/api/templates", new StringContent(TemplateJson(JsonSerializer.Serialize(dict)), Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, many.StatusCode);
        Assert.Contains("10", (await ReadErrorAsync(many)).GetProperty("message").GetString());
    }

    [Fact]
    public async Task Preview_with_missing_image_resource_should_return_400_with_key_name()
    {
        var client = await CreateClientAsync();
        // 不带 images 保存（缺省＝保留，但模板不存在 → 空集）：版式引用 logo.png 而库中无图
        var save = await client.PostAsync("/api/templates", new StringContent(TemplateJson(null), Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.OK, save.StatusCode);

        var preview = await client.PostAsync($"/api/templates/{Uri.EscapeDataString(TemplateName)}/preview", new StringContent("{}", Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, preview.StatusCode);
        var error = await ReadErrorAsync(preview);
        Assert.Contains("logo.png", error.GetProperty("message").GetString());
        Assert.Contains("缺失", error.GetProperty("message").GetString());
    }
}
