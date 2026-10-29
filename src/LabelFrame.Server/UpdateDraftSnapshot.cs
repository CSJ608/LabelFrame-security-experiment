using System.Security.Cryptography;

namespace LabelFrame.Server;

/// <summary>为发布确认提供有界临时快照和内容一致性校验。</summary>
public static class UpdateDraftSnapshot
{
    public static async Task<T> InspectAsync<T>(string source, string extension, Func<string, Task<T>> inspect, CancellationToken ct)
    {
        var temporary = Path.Combine(Path.GetTempPath(), "LabelFrame-preview-" + Guid.NewGuid().ToString("N") + extension);
        try
        {
            await using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read))
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                const long limit = 64 * 1024 * 1024;
                if (input.Length is <= 0 or > limit) throw new InvalidDataException("安装包大小超出更新限制。");
                var buffer = new byte[81920];
                long total = 0;
                int count;
                while ((count = await input.ReadAsync(buffer, ct)) > 0)
                {
                    total += count;
                    if (total > limit) throw new InvalidDataException("安装包大小超出更新限制。");
                    await output.WriteAsync(buffer.AsMemory(0, count), ct);
                }
            }
            return await inspect(temporary);
        }
        finally { File.Delete(temporary); }
    }

    public static void VerifyExpected(string actual, string? expected)
    {
        if (expected is not null && !string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("安装包已变化，请重新确认后发布。");
    }

    public static async Task<string?> HashAsync(string? path, CancellationToken ct)
    {
        if (path is null) return null;
        try
        {
            await using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (input.Length is <= 0 or > 64 * 1024 * 1024) return null;
            return Convert.ToHexString(await SHA256.HashDataAsync(input, ct));
        }
        catch (IOException) { return null; }
    }
}
