using LabelFrame.Api;
using LabelFrame.Core.Documents;
using LabelFrame.Core.Encoding;
using LabelFrame.Core.Jobs;
using LabelFrame.Core.Templates;
using LabelFrame.WinHost.Tests.Samples;
using LabelFrame.Core.Validation;
using LabelFrame.Rendering;
using LabelFrame.WinHost.Api;
using LabelFrame.WinHost.Jobs;
using LabelFrame.WinHost.Transport;
using LabelFrame.WinHost.Tests.Transport;

namespace LabelFrame.WinHost.Tests.Jobs;

public class JobSubmissionServiceTests
{
    private static (JobSubmissionService Service, SqliteLabelJobStore Store, TemplateStore Templates) CreateService(
        TextWriter? hostLogWriter = null,
        TransportMode transportMode = TransportMode.Log)
    {
        var dbPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"lfhost-{Guid.NewGuid():N}.db");
        var store = new SqliteLabelJobStore(dbPath);
        store.InitializeAsync().GetAwaiter().GetResult();
        var queue = new LabelJobQueue(store);

        var templatesDb = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"lftpl-{Guid.NewGuid():N}.db");
        var templates = new TemplateStore(templatesDb);
        templates.InitializeAsync().GetAwaiter().GetResult();

        var (transportManager, registry) = TestTransportRegistry.CreateManagerWithRegistry(
            options: new HostOptions { Transport = transportMode });
        var service = new JobSubmissionService(
            queue,
            new ZplImageEncoder(),
            dpiProvider: () => 203,
            new SkiaLabelRenderer(),
            templates,
            transportManager,
            registry,
            TestTransportRegistry.CreateContext(),
            hostLogWriter ?? TextWriter.Null);
        return (service, store, templates);
    }

    private static SubmitJobRequest CreateRequest(string requestId, params IReadOnlyDictionary<string, string>[] labels) => new(
        requestId,
        new TemplateDto(LocationLabelSamples.Contract, LocationLabelSamples.Layout),
        labels.Select(d => new LabelDto(d)).ToList());

    [Fact]
    public async Task Valid_request_should_create_job_with_encoded_gf_image()
    {
        var (service, store, _) = CreateService();
        var request = CreateRequest("req-valid", new Dictionary<string, string>
        {
            ["zone"] = "A-01",
            ["locationCode"] = "A-01-02-03",
        });

        var result = await service.SubmitAsync(request);

        Assert.NotNull(result.Job);
        Assert.True(result.Created);
        Assert.Equal(LabelJobStatus.Pending, result.Job!.Status);
        var stored = await store.GetJobAsync(result.Job.Id);
        // 恒为整版位图（^GF），不再有元素级 ^BC
        Assert.Contains("^GF", stored!.Items[0].Zpl);
        Assert.DoesNotContain("^BC", stored.Items[0].Zpl);
    }

    [Fact]
    public async Task Direct_submit_with_callback_url_should_be_accepted_and_ignored()
    {
        // 直连模式：SubmitJobRequest 为三端共用契约，callbackUrl 字段接受但忽略——
        // 受理成功、作业照常创建进入队列，本地作业模型无回调概念（不产生出站回调）
        var (service, store, _) = CreateService();
        var request = CreateRequest("req-cb-direct", new Dictionary<string, string>
        {
            ["zone"] = "A-01",
            ["locationCode"] = "A-01-02-03",
        }) with { CallbackUrl = "http://127.0.0.1:9/hook" };

        var result = await service.SubmitAsync(request);

        Assert.NotNull(result.Job);
        Assert.True(result.Created);
        Assert.Null(result.ErrorCode);
        Assert.Equal(LabelJobStatus.Pending, result.Job!.Status);
        var stored = await store.GetJobAsync(result.Job.Id);
        Assert.Contains("^GF", stored!.Items[0].Zpl);
    }

    [Fact]
    public async Task Duplicate_request_id_should_return_existing_job()
    {
        var (service, store, _) = CreateService();
        var request = CreateRequest("req-duplicate", new Dictionary<string, string>
        {
            ["zone"] = "A-01",
            ["locationCode"] = "A-01-02-03",
        });

        var first = await service.SubmitAsync(request);
        var second = await service.SubmitAsync(request);

        Assert.True(first.Created);
        Assert.False(second.Created);
        Assert.Equal(first.Job!.Id, second.Job!.Id);
        Assert.Single(second.Job.Items);
    }

    [Fact]
    public async Task Missing_required_field_should_fail_with_problem_code_and_not_create_job()
    {
        var (service, store, _) = CreateService();
        var request = CreateRequest("req-invalid", new Dictionary<string, string>
        {
            ["zone"] = "A-01",
        });

        var result = await service.SubmitAsync(request);

        Assert.Null(result.Job);
        Assert.Equal(LabelProblemCodes.RequiredFieldMissing, result.ErrorCode);
        Assert.Equal("locationCode", result.FieldKey);
        Assert.Null(await store.GetJobByRequestIdAsync("req-invalid"));
    }

    [Fact]
    public async Task Chinese_label_should_encode_gf()
    {
        var (service, store, _) = CreateService();
        var request = CreateRequest("req-cn", new Dictionary<string, string>
        {
            ["zone"] = "中文区域",
            ["locationCode"] = "A-01-02-03",
        });

        var result = await service.SubmitAsync(request);

        Assert.NotNull(result.Job);
        var stored = await store.GetJobAsync(result.Job!.Id);
        Assert.Contains("^GF", stored!.Items[0].Zpl);
    }

    [Fact]
    public async Task Every_job_should_encode_whole_label_as_gf()
    {
        var (service, store, _) = CreateService();
        var request = CreateRequest("req-image", new Dictionary<string, string>
        {
            ["zone"] = "A-01",
            ["locationCode"] = "A-01-02-03",
        });

        var result = await service.SubmitAsync(request);

        Assert.NotNull(result.Job);
        var stored = await store.GetJobAsync(result.Job!.Id);
        var zpl = stored!.Items[0].Zpl;
        // 100mm x 60mm @203dpi => PW799 / LL480；整版 ^GF，无元素级 ^BC
        Assert.StartsWith("^XA", zpl);
        Assert.Contains("^PW799", zpl);
        Assert.Contains("^LL480", zpl);
        Assert.Contains("^FO0,0^GFA,", zpl);
        Assert.DoesNotContain("^BC", zpl);
    }

    [Fact]
    public async Task Empty_labels_should_fail_with_invalid_request()
    {
        var (service, _, _) = CreateService();
        var request = new SubmitJobRequest("req-empty", new TemplateDto(LocationLabelSamples.Contract, LocationLabelSamples.Layout), new List<LabelDto>());

        var result = await service.SubmitAsync(request);

        Assert.Null(result.Job);
        Assert.Equal(JobErrorCodes.InvalidRequest, result.ErrorCode);
    }

    [Fact]
    public async Task Missing_data_key_should_render_empty_and_create_job()
    {
        var (service, store, _) = CreateService();
        var layout = new LabelFrame.Core.Layout.LabelLayout
        {
            Name = "missing-key",
            ContractName = "location-label",
            ContractVersion = "1.0",
            WidthMm = 100,
            HeightMm = 60,
            Elements =
            [
                new LabelFrame.Core.Layout.LabelTextElement { SourceKey = "notInData", XMm = 5, YMm = 5, FontHeightMm = 8, FontWidthMm = 8 },
            ],
        };
        var request = new SubmitJobRequest("req-missing-key", new TemplateDto(LocationLabelSamples.Contract, layout), new List<LabelDto>
        {
            new(new Dictionary<string, string> { ["zone"] = "A-01", ["locationCode"] = "A-01-02-03" }),
        });

        var result = await service.SubmitAsync(request);

        // 图片渲染容错（TryGet），缺失的非必填字段渲染为空文本，作业正常创建（与预览一致）
        Assert.NotNull(result.Job);
        Assert.True(result.Created);
        var stored = await store.GetJobAsync(result.Job.Id);
        Assert.Contains("^GF", stored!.Items[0].Zpl);
    }

    // ──Log 模式作业数据留痕（模板名 + 数据集合）──

    private static SubmitJobRequest CreateNamedRequest(string requestId, params IReadOnlyDictionary<string, string>[] labels) => new(
        requestId,
        new TemplateDto(LocationLabelSamples.Contract, LocationLabelSamples.Layout, Name: "库位标签"),
        labels.Select(d => new LabelDto(d)).ToList());

    [Fact]
    public async Task Log_mode_submit_should_write_job_data_record()
    {
        var hostLog = new StringWriter();
        var (service, _, _) = CreateService(hostLog);
        var request = CreateNamedRequest("req-log-data", new Dictionary<string, string>
        {
            ["zone"] = "A-01",
            ["locationCode"] = "A-01-02-03",
        });

        var result = await service.SubmitAsync(request);

        Assert.NotNull(result.Job);
        var lines = hostLog.ToString().Split('\n').Where(l => l.Contains("作业数据")).ToList();
        var line = Assert.Single(lines);
        Assert.Contains($"作业 {result.Job!.Id}", line);
        Assert.Contains("模板 库位标签", line);
        Assert.Contains("共 1 张", line);
        Assert.Contains("locationCode=A-01-02-03", line);
        Assert.Contains("zone=A-01", line);
    }

    [Fact]
    public async Task Log_mode_batch_should_deduplicate_job_data_records()
    {
        var hostLog = new StringWriter();
        var (service, _, _) = CreateService(hostLog);
        var same = new Dictionary<string, string> { ["zone"] = "A-01", ["locationCode"] = "A-01-02-03" };
        var distinct = new Dictionary<string, string> { ["zone"] = "A-02", ["locationCode"] = "A-02-01-01" };
        var request = CreateNamedRequest("req-log-batch", same, same, same, distinct);

        var result = await service.SubmitAsync(request);

        Assert.NotNull(result.Job);
        var lines = hostLog.ToString().Split('\n').Where(l => l.Contains("作业数据")).ToList();
        Assert.Equal(2, lines.Count);
        Assert.Contains("共 4 张", lines[0]);
        Assert.Contains("数据组 1/2（重复 3 张）", lines[0]);
        Assert.Contains("zone=A-01", lines[0]);
        Assert.Contains("数据组 2/2", lines[1]);
        Assert.DoesNotContain("重复", lines[1]);
        Assert.Contains("zone=A-02", lines[1]);
    }

    [Fact]
    public async Task Log_mode_duplicate_request_should_not_write_data_record_again()
    {
        var hostLog = new StringWriter();
        var (service, _, _) = CreateService(hostLog);
        var request = CreateNamedRequest("req-log-idempotent", new Dictionary<string, string>
        {
            ["zone"] = "A-01",
            ["locationCode"] = "A-01-02-03",
        });

        await service.SubmitAsync(request);
        await service.SubmitAsync(request);

        var lines = hostLog.ToString().Split('\n').Where(l => l.Contains("作业数据")).ToList();
        Assert.Single(lines);
    }

    [Fact]
    public async Task Non_log_transport_should_not_write_job_data_record()
    {
        var hostLog = new StringWriter();
        var (service, store, _) = CreateService(hostLog, TransportMode.Tcp);
        var request = CreateNamedRequest("req-tcp-data", new Dictionary<string, string>
        {
            ["zone"] = "A-01",
            ["locationCode"] = "A-01-02-03",
        });

        var result = await service.SubmitAsync(request);

        // 真实传输路径零变化：作业照常创建，但不出现数据留痕
        Assert.NotNull(result.Job);
        Assert.True(result.Created);
        Assert.NotNull(await store.GetJobAsync(result.Job!.Id));
        Assert.DoesNotContain("作业数据", hostLog.ToString());
    }
}
