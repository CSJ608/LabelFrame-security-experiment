using System.Text.Json;

namespace LabelFrame.WinHost;

/// <summary>
/// 批次作业设置存取：用户级文件 %LOCALAPPDATA%\LabelFrame\print-settings.json（与 connection.json 同级）。
/// 原子写（先写临时文件再替换，与 HostConfigStore 同模式）；读取时 Normalize，
/// 缺失 / 损坏 / 越界统一回默认值（见 <see cref="PrintSettings.Normalize"/>）。
/// </summary>
public sealed class PrintSettingsStore
{
    private readonly string _path;
    private readonly object _gate = new();

    /// <summary>创建设置存储。</summary>
    /// <param name="path">print-settings.json 路径（测试可注入临时路径）。</param>
    public PrintSettingsStore(string path)
    {
        _path = path;
    }

    /// <summary>读取设置：缺失 / 损坏 / 越界统一 Normalize 回默认值，永不返回非法值。</summary>
    public PrintSettingsDto Load()
    {
        lock (_gate)
        {
            try
            {
                if (!File.Exists(_path))
                {
                    return PrintSettings.Defaults;
                }

                using var stream = File.OpenRead(_path);
                using var doc = JsonDocument.Parse(stream);
                var root = doc.RootElement;
                bool? batchEnabled = root.TryGetProperty("batchEnabled", out var enabled)
                    && enabled.ValueKind is JsonValueKind.True or JsonValueKind.False
                        ? enabled.GetBoolean()
                        : null;
                int? batchSize = root.TryGetProperty("batchSize", out var size) && size.TryGetInt32(out var sizeValue)
                    ? sizeValue
                    : null;
                int? batchIntervalMs = root.TryGetProperty("batchIntervalMs", out var interval) && interval.TryGetInt32(out var intervalValue)
                    ? intervalValue
                    : null;
                // dpi 序列化为 null（未设置）时 ValueKind 非 Number——TryGetInt32 会抛而非返 false，须先判 Kind
                int? dpi = root.TryGetProperty("dpi", out var dpiValue)
                    && dpiValue.ValueKind == JsonValueKind.Number && dpiValue.TryGetInt32(out var dpiInt)
                    ? dpiInt
                    : null;
                return PrintSettings.Normalize(batchEnabled, batchSize, batchIntervalMs, dpi);
            }
            catch
            {
                // 损坏兜底默认值，不影响启动 / 读取
                return PrintSettings.Defaults;
            }
        }
    }

    /// <summary>保存设置（先写临时文件再原子替换，避免写一半）。</summary>
    public void Save(PrintSettingsDto value)
    {
        lock (_gate)
        {
            var directory = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var temp = _path + ".tmp";
            File.WriteAllText(
                temp,
                JsonSerializer.Serialize(new
                {
                    batchEnabled = value.BatchEnabled,
                    batchSize = value.BatchSize,
                    batchIntervalMs = value.BatchIntervalMs,
                    dpi = value.Dpi,
                }),
                new System.Text.UTF8Encoding(false));
            File.Move(temp, _path, overwrite: true);
        }
    }
}
