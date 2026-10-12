namespace LabelFrame.Core.IO;

/// <summary>普通文件名规范化（路径穿越防护）：只允许普通文件名——拒绝路径分隔符 / .. / 非法字符，不允许子目录。</summary>
public static class SafeFileName
{
    /// <summary>规范化文件名；非法返回 null。</summary>
    public static string? Normalize(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return null;
        }

        var name = fileName.Trim();
        if (name is "." or ".." || name.Contains('/') || name.Contains('\\'))
        {
            return null;
        }

        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            return null;
        }

        return name;
    }
}