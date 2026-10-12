using System.Text.Json;
using System.Text.Json.Serialization;
using LabelFrame.Api;
using LabelFrame.Core.Layout;

namespace LabelFrame.WinHost.Tests.Api;

public class SubmitRequestJsonTests
{
    [Fact]
    public void Submit_request_should_deserialize_with_web_defaults()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        options.Converters.Add(new LabelElementJsonConverter());
        DeserializeShouldWork(options);
    }

    [Fact]
    public void Submit_request_should_deserialize_with_http_json_options()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.PropertyNameCaseInsensitive = true;
        options.Converters.Add(new JsonStringEnumConverter());
        options.Converters.Add(new LabelElementJsonConverter());
        DeserializeShouldWork(options);
    }

    private static void DeserializeShouldWork(JsonSerializerOptions options)
    {

        const string json = """
            {
              "requestId": "smoke-1",
              "template": {
                "contract": {
                  "name": "location-label", "version": "1.0",
                  "fields": [
                    { "key": "locationCode", "displayName": "库位码", "isRequired": true, "type": "text" }
                  ]
                },
                "layout": {
                  "name": "location-label-100x60", "contractName": "location-label", "contractVersion": "1.0",
                  "widthMm": 100, "heightMm": 60,
                  "elements": [
                    { "type": "text", "sourceKey": "zone", "xMm": 5, "yMm": 4, "fontHeightMm": 5, "fontWidthMm": 5 },
                    { "type": "barcode", "sourceKey": "locationCode", "xMm": 5, "yMm": 26, "heightMm": 22, "moduleWidth": 2 }
                  ]
                }
              },
              "labels": [ { "data": { "zone": "A-01", "locationCode": "A-01-02-03" } } ],
              "callbackUrl": "https://cb.example.com/hook"
            }
            """;

        var request = JsonSerializer.Deserialize<SubmitJobRequest>(json, options);

        Assert.NotNull(request);
        Assert.Equal("smoke-1", request!.RequestId);
        Assert.NotNull(request.Template);
        Assert.NotNull(request.Template!.Layout);
        Assert.Equal(2, request.Template.Layout.Elements.Count);
        Assert.IsType<LabelTextElement>(request.Template.Layout.Elements[0]);
        Assert.IsType<LabelBarcodeElement>(request.Template.Layout.Elements[1]);
        Assert.Single(request.Labels!);

        // 终态回调字段：可空可选，直连模式反序列化接受（忽略语义在提交服务测试覆盖）
        Assert.Equal("https://cb.example.com/hook", request.CallbackUrl);
    }
}