using System.Text;

namespace LabelFrame.WinHost;

/// <summary>
/// host.log 按日轮转写入器：基准路径 host.log，实际写入 host-&lt;yyyyMMdd&gt;.log
/// （与 app-*.log / server-*.log 口径一致）；超期文件自动清理（默认保留 31 个，0 或负值 = 不清理，
/// 清理在启动与每日轮转时执行；历史单名 host.log 不迁移不删除）。
/// 失败防护：构造时目录 / 文件不可打开由调用方捕获回退（沿用 TextWriter.Null 降级）；
/// 运行中轮转开新文件失败继续写旧文件（下次写入再试），实际写入失败自我禁用（不抛出，
/// 控制台告警一次）——宿主日志失效不能影响启动与请求路径。
/// </summary>
public sealed class DailyRotatingFileWriter : TextWriter
{
    private readonly string _basePath;
    private readonly int _retainedFileCountLimit;
    private readonly object _gate = new();
    private StreamWriter? _writer;
    private DateOnly _currentDate;
    private bool _enabled = true;

    /// <summary>创建按日轮转写入器。</summary>
    /// <param name="basePath">基准路径（如 .../host.log；实际写入 host-yyyyMMdd.log）。</param>
    /// <param name="retainedFileCountLimit">按日文件保留上限（默认 31；0 或负值 = 不清理）。</param>
    public DailyRotatingFileWriter(string basePath, int retainedFileCountLimit = 31)
    {
        _basePath = basePath;
        _retainedFileCountLimit = retainedFileCountLimit;
        var today = Today();
        _writer = OpenWriter(today);
        _currentDate = today;
        CleanupOverRetained();
    }

    /// <summary>实际编码由内部 StreamWriter 决定（UTF-8 无 BOM，与既有 host.log 一致）。</summary>
    public override Encoding Encoding => Encoding.UTF8;

    public override void Write(char value) => WriteCore(w => w.Write(value));

    public override void Write(string? value) => WriteCore(w => w.Write(value));

    public override void WriteLine(string? value) => WriteCore(w => w.WriteLine(value));

    public override void WriteLine() => WriteCore(static w => w.WriteLine());

    public override void Flush()
    {
        lock (_gate)
        {
            try
            {
                _writer?.Flush();
            }
            catch (Exception)
            {
                // 刷盘失败不抛出（AutoFlush 已开启；此处仅为兼容显式 Flush 调用方）
            }
        }
    }

    protected override void Dispose(bool disposing)
    {
        lock (_gate)
        {
            _enabled = false;
            if (disposing)
            {
                try
                {
                    _writer?.Dispose();
                }
                catch (Exception)
                {
                    // 关闭失败即可，通道已停用
                }
            }

            _writer = null;
        }

        base.Dispose(disposing);
    }

    private static DateOnly Today() => DateOnly.FromDateTime(DateTime.Now);

    /// <summary>写入入口：永不抛出。轮转失败退回旧文件继续写；写入失败自我禁用并告警一次。</summary>
    private void WriteCore(Action<StreamWriter> write)
    {
        lock (_gate)
        {
            if (!_enabled)
            {
                return;
            }

            try
            {
                TryRotate();
                write(_writer!);
            }
            catch (Exception ex)
            {
                Disable($"写入宿主日志失败：{ex.Message}");
            }
        }
    }

    /// <summary>当日文件路径：<名>-yyyyMMdd<扩展名>（基准路径 host.log → host-20260911.log）。</summary>
    private string DatedPath(DateOnly date)
    {
        var directory = Path.GetDirectoryName(_basePath);
        var fileName = $"{Path.GetFileNameWithoutExtension(_basePath)}-{date:yyyyMMdd}{Path.GetExtension(_basePath)}";
        return string.IsNullOrEmpty(directory) ? fileName : Path.Combine(directory, fileName);
    }

    private StreamWriter OpenWriter(DateOnly date)
    {
        var path = DatedPath(date);
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        return new StreamWriter(path, append: true) { AutoFlush = true };
    }

    /// <summary>按日轮转：跨天时开新文件、关旧文件并清理超期。失败不抛出（退回旧文件，下次写入再试）。</summary>
    private void TryRotate()
    {
        var today = Today();
        if (today == _currentDate)
        {
            return;
        }

        var newWriter = OpenWriter(today);
        var oldWriter = _writer;
        _writer = newWriter;
        _currentDate = today;
        try
        {
            oldWriter?.Dispose();
        }
        catch (Exception)
        {
            // 旧文件关闭失败不影响新文件写入
        }

        CleanupOverRetained();
    }

    /// <summary>清理超期文件：匹配 <名>-yyyyMMdd<扩展名> 且日期可解析者，按文件名序（即时间序）删最旧，保留最近 limit 个（含当日）。</summary>
    private void CleanupOverRetained()
    {
        if (_retainedFileCountLimit <= 0)
        {
            return;
        }

        try
        {
            var directory = Path.GetDirectoryName(_basePath);
            if (string.IsNullOrEmpty(directory))
            {
                return;
            }

            var prefix = $"{Path.GetFileNameWithoutExtension(_basePath)}-";
            var extension = Path.GetExtension(_basePath);
            var files = Directory.GetFiles(directory, $"{prefix}*{extension}")
                .Where(file =>
                {
                    var datePart = Path.GetFileNameWithoutExtension(file)[prefix.Length..];
                    return datePart.Length == 8 && DateOnly.TryParseExact(datePart, "yyyyMMdd", out _);
                })
                .OrderBy(file => file, StringComparer.Ordinal)
                .ToList();
            var excess = files.Count - _retainedFileCountLimit;
            for (var i = 0; i < excess; i++)
            {
                File.Delete(files[i]);
            }
        }
        catch (Exception)
        {
            // 清理失败（文件被占用等）不影响写入，下次轮转再试
        }
    }

    /// <summary>运行中自我禁用：关闭文件、控制台告警一次（后续宿主日志静默丢弃）。</summary>
    private void Disable(string reason)
    {
        _enabled = false;
        try
        {
            _writer?.Dispose();
        }
        catch (Exception)
        {
            // 关闭失败即可，通道已禁用
        }

        _writer = null;
        try
        {
            Console.Error.WriteLine(
                $"[LabelFrame] 警告：宿主日志文件通道已停用（{_basePath}）：{reason}。后续宿主日志不再落盘，客户端继续运行；请检查磁盘状态与日志目录配置。");
        }
        catch (Exception)
        {
            // 无控制台（WinExe 无界面壳）时告警无处展示，忽略
        }
    }
}
