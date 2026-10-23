using System.Buffers.Binary;
using System.Text.Json;

namespace LabelFrame.Updates;

/// <summary>有界的本机升级控制帧。</summary>
public static class UpdatePipeProtocol
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public static async Task WriteAsync<T>(Stream stream, T value, CancellationToken ct)
    {
        var data = JsonSerializer.SerializeToUtf8Bytes(value, Json);
        if (data.Length > 16384) throw new InvalidDataException("更新控制帧过大。");
        var length = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(length, data.Length);
        await stream.WriteAsync(length, ct);
        await stream.WriteAsync(data, ct);
        await stream.FlushAsync(ct);
    }
    public static async Task<T> ReadAsync<T>(Stream stream, CancellationToken ct)
    {
        var length = new byte[4];
        await stream.ReadExactlyAsync(length, ct);
        var size = BinaryPrimitives.ReadInt32LittleEndian(length);
        if (size is <= 0 or > 16384) throw new InvalidDataException("更新控制帧长度无效。");
        var data = new byte[size];
        await stream.ReadExactlyAsync(data, ct);
        return JsonSerializer.Deserialize<T>(data, Json) ?? throw new InvalidDataException("更新控制帧无效。");
    }
}

/// <summary>客户端提交的安装包长度与身份摘要。</summary>
public sealed record UpdateInstallRequest(string FileName, string Sha256, long SizeBytes, long ParentStartTicks);
/// <summary>受限安装服务创建的事务结果。</summary>
public sealed record UpdateInstallResponse(bool Accepted, string? TransactionId = null, string? Reason = null);
/// <summary>客户端在恢复记录与原用户等待器就绪后提交安装意图。</summary>
public sealed record UpdateCommitRequest(string TransactionId);
/// <summary>安装监督进程保存的最终退出状态。</summary>
public sealed record UpdateInstallResult(int ExitCode, string TargetVersion, string? Reason = null);
