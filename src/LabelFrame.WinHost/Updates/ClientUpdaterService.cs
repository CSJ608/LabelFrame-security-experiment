using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using LabelFrame.Updates;

namespace LabelFrame.WinHost.Updates;

/// <summary>仅接受本机已安装客户端可信 MSI 的特权安装服务。</summary>
internal sealed class ClientUpdaterService(ILogger<ClientUpdaterService> logger) : BackgroundService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public static async Task RunAsync()
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { ContentRootPath = AppContext.BaseDirectory, Args = [] });
        builder.Services.AddWindowsService(options => options.ServiceName = ClientUpdateContract.ServiceName);
        builder.Services.AddHostedService<ClientUpdaterService>();
        await builder.Build().RunAsync();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        ProtectedUpdatePaths.CreateRoot();
        ProtectedUpdatePaths.Verify(ProtectedUpdatePaths.Executable);
        ProtectedUpdatePaths.Verify(ClientUpdateContract.CertificatePath);
        while (!stoppingToken.IsCancellationRequested)
        {
            using var pipe = CreatePipe();
            try
            {
                await pipe.WaitForConnectionAsync(stoppingToken);
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                timeout.CancelAfter(TimeSpan.FromMinutes(3));
                var response = await AcceptAsync(pipe, timeout.Token);
                await UpdatePipeProtocol.WriteAsync(pipe, response, timeout.Token);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                logger.LogWarning("客户端升级请求被拒绝：{Reason}", ex.Message);
                if (pipe.IsConnected)
                {
                    try { using var replyTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                        await UpdatePipeProtocol.WriteAsync(pipe, new UpdateInstallResponse(false, Reason: "安装服务拒绝此请求，请管理员查看更新服务日志。"), replyTimeout.Token); }
                    catch (Exception replyError) when (replyError is IOException or OperationCanceledException) { }
                }
            }
        }
    }

    private static NamedPipeServerStream CreatePipe()
    {
        var security = new PipeSecurity();
        security.SetAccessRuleProtection(true, false);
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.NetworkSid, null), PipeAccessRights.FullControl, AccessControlType.Deny));
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.AnonymousSid, null), PipeAccessRights.FullControl, AccessControlType.Deny));
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), PipeAccessRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null), PipeAccessRights.ReadWrite, AccessControlType.Allow));
        return NamedPipeServerStreamAcl.Create(ClientUpdateContract.PipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.FirstPipeInstance, 65536, 65536, security);
    }

    private static async Task<UpdateInstallResponse> AcceptAsync(NamedPipeServerStream pipe, CancellationToken ct)
    {
        if (!GetNamedPipeClientProcessId(pipe.SafePipeHandle.DangerousGetHandle(), out var pid)) throw new InvalidDataException("无法确认更新请求进程。");
        using var parent = Process.GetProcessById(checked((int)pid));
        if (!string.Equals(parent.MainModule?.FileName, ProtectedUpdatePaths.Executable, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("更新请求必须来自已安装的客户端。");
        var request = await UpdatePipeProtocol.ReadAsync<UpdateInstallRequest>(pipe, ct);
        if (request.SizeBytes is <= 0 or > ClientUpdateContract.MaxPackageBytes || request.Sha256.Length != 64
            || request.FileName.Length > 128 || parent.StartTime.ToUniversalTime().Ticks != request.ParentStartTicks)
            throw new InvalidDataException("更新请求身份或长度无效。");
        var activePath = Path.Combine(ProtectedUpdatePaths.Root, "active.json");
        if (File.Exists(activePath))
        {
            var active = JsonSerializer.Deserialize<string>(await File.ReadAllTextAsync(activePath, ct));
            if (!Guid.TryParseExact(active, "N", out _) || !File.Exists(Path.Combine(ProtectedUpdatePaths.Root, active, "result.json")))
                throw new InvalidDataException("已有安装事务未结束，请管理员确认后重试。");
        }
        var id = Guid.NewGuid().ToString("N");
        CleanupCompletedTransactions();
        var directory = Path.Combine(ProtectedUpdatePaths.Root, id);
        Directory.CreateDirectory(directory);
        var packagePath = Path.Combine(directory, "package.msi");
        var retained = false;
        var armed = false;
        var targetVersion = "";
        try
        {
        await using (var output = new FileStream(packagePath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            var buffer = new byte[65536];
            var remaining = request.SizeBytes;
            while (remaining > 0)
            {
                var count = await pipe.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, remaining)), ct);
                if (count == 0) throw new EndOfStreamException();
                await output.WriteAsync(buffer.AsMemory(0, count), ct);
                remaining -= count;
            }
        }
        var package = await new WindowsPackageInspector(ClientUpdateContract.CertificatePath).InspectAsync(packagePath, request.FileName, ct);
        targetVersion = package.Version;
        if (!string.Equals(package.Sha256, request.Sha256, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("请求包摘要不一致。");
        if (ClientUpdateContract.ParseVersion(package.Version) <= ClientUpdateContract.ParseVersion(InstalledVersion()))
            throw new InvalidDataException("拒绝同版安装或降级。");
        if (package.RuntimeMajor != 10) throw new InvalidDataException("运行时主版本变化需要管理员手工升级。");
        await File.WriteAllTextAsync(activePath + ".tmp", JsonSerializer.Serialize(id), ct);
        File.Move(activePath + ".tmp", activePath, true);
        retained = true;
        await UpdatePipeProtocol.WriteAsync(pipe, new UpdateInstallResponse(true, id), ct);
        var commit = await UpdatePipeProtocol.ReadAsync<UpdateCommitRequest>(pipe, ct);
        if (commit.TransactionId != id) throw new InvalidDataException("安装事务提交不匹配。");
        await UpdateSupervisor.PrepareInstallerAsync(id, (int)pid, request.ParentStartTicks, package.Version, ct);
        armed = true;
        return new(true, id);
        }
        catch
        {
            if (retained && !armed)
            {
                var resultPath = Path.Combine(directory, "result.json");
                if (!File.Exists(resultPath))
                {
                    await File.WriteAllTextAsync(resultPath + ".service.tmp",
                        JsonSerializer.Serialize(new UpdateInstallResult(1602, targetVersion, "安装准备或提交失败，未执行升级。"), Json), CancellationToken.None);
                    try { File.Move(resultPath + ".service.tmp", resultPath); }
                    catch (IOException) when (File.Exists(resultPath)) { }
                }
            }
            throw;
        }
        finally
        {
            if (!retained) { File.Delete(packagePath); Directory.Delete(directory); }
        }
    }

    private static void CleanupCompletedTransactions()
    {
        var completed = Directory.EnumerateDirectories(ProtectedUpdatePaths.Root)
            .Where(path => Guid.TryParseExact(Path.GetFileName(path), "N", out _) && File.Exists(Path.Combine(path, "result.json")))
            .OrderByDescending(Directory.GetCreationTimeUtc).Skip(4);
        foreach (var path in completed)
        {
            ProtectedUpdatePaths.Verify(path);
            foreach (var name in new[] { "package.msi", "install.log", "result.json", "installer.ready", "install.commit", "install.commit.tmp", "result.json.service.tmp", "result.json.runner.tmp" })
            {
                var file = Path.Combine(path, name);
                if (!File.Exists(file)) continue;
                ProtectedUpdatePaths.Verify(file);
                File.Delete(file);
            }
            if (!Directory.EnumerateFileSystemEntries(path).Any()) Directory.Delete(path);
        }
    }

    internal static string InstalledVersion()
    {
        var product = new char[39];
        Version? highest = null;
        for (uint index = 0; index < 128; index++)
        {
            var status = MsiEnumRelatedProducts("{" + ClientUpdateContract.UpgradeCode + "}", 0, index, product);
            if (status == 259) return highest?.ToString(3) ?? throw new InvalidDataException("未发现已安装的客户端产品。");
            if (status != 0) throw new InvalidDataException("无法枚举客户端已安装版本。");
            var version = new char[64];
            uint length = 63;
            if (MsiGetProductInfo(new string(product, 0, 38), "VersionString", version, ref length) != 0)
                throw new InvalidDataException("无法读取客户端已安装版本。");
            var current = ClientUpdateContract.ParseVersion(new string(version, 0, (int)length));
            if (highest is null || current > highest) highest = current;
        }
        throw new InvalidDataException("客户端安装产品记录过多，请管理员修复。");
    }

    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetNamedPipeClientProcessId(IntPtr pipe, out uint process);
    [DllImport("msi.dll", EntryPoint = "MsiEnumRelatedProductsW", CharSet = CharSet.Unicode)]
    private static extern uint MsiEnumRelatedProducts(string upgradeCode, uint reserved, uint index, [Out] char[] product);
    [DllImport("msi.dll", EntryPoint = "MsiGetProductInfoW", CharSet = CharSet.Unicode)]
    private static extern uint MsiGetProductInfo(string product, string property, [Out] char[] value, ref uint length);
}
