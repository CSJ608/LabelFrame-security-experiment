using Microsoft.Extensions.Logging;

namespace LabelFrame.Server;

/// <summary>
/// 极简文件日志（增量见）：按日轮转 + 单文件大小上限轮转的 UTF-8 文本文件 + 超期清理
/// 供 Linux 部署把日志目录挂载到宿主机直接查看。`LABELFRAME_SERVER_LOG_FILE` 为基准路径，
/// 实际写入 <名>-&lt;yyyyMMdd&gt;.log（与客户端 app-*.log 口径一致）；单文件超过大小上限时切当日序号文件
/// （<名>-yyyyMMdd.1.log → .2.log …，跨天自然回到新日期基名），大小轮转同样触发按个数清理——
/// 文本层总占用上界 = 个数上限 × 单文件上限（默认 31 × 50MB ≈ 1.55GB）。
/// 启动防护：路径无效（目录不存在且不可创建 / 磁盘不可用）时构造**不抛异常**——
/// <see cref="FileChannelEnabled"/> 为 false，由调用方输出中文告警后跳过文件通道，宿主正常启动；
/// 运行中防护：轮转开新文件失败继续写旧文件（下次写入再试），实际写入失败自我禁用并告警一次，
/// <see cref="ILogger.Log{TState}"/> 永不抛出（不打断请求路径）。
/// </summary>
public sealed class FileLoggerProvider : ILoggerProvider
{
    /// <summary>单文件大小上限默认值（50MB）。</summary>
    public const long DefaultMaxFileSizeBytes = 50L * 1024 * 1024;

    private readonly string _basePath;
    private readonly int _retainedFileCountLimit;
    private readonly long _maxFileSizeBytes;
    private readonly object _gate = new();
    private TextWriter? _writer;
    private FileStream? _stream;
    private DateOnly _currentDate;
    private int _currentSequence;
    private bool _fileChannelEnabled;

    /// <summary>文件通道是否可用（构造成功为 true；运行中写入失败后自我禁用为 false）。</summary>
    public bool FileChannelEnabled
    {
        get { lock (_gate) return _fileChannelEnabled; }
    }

    /// <summary>文件通道不可用原因（构造失败时供调用方输出告警；通道可用为 null）。</summary>
    public string? InactiveReason { get; }

    /// <summary>创建文件日志提供器。</summary>
    /// <param name="path">日志基准路径（如 .../server.log；实际写入 server-yyyyMMdd.log）。</param>
    /// <param name="retainedFileCountLimit">文件个数保留上限（默认 31；0 或负值 = 不清理）。按日单文件时代即「保留天数」；
    /// 大小上限启用后单日可产生多个序号文件，实际语义为「文件个数上限」（既有部署无感）。</param>
    /// <param name="maxFileSizeBytes">单文件大小上限（字节；默认 50MB，；0 或负值 = 不限制大小轮转）。</param>
    public FileLoggerProvider(string path, int retainedFileCountLimit = 31, long maxFileSizeBytes = DefaultMaxFileSizeBytes)
    {
        _basePath = path;
        _retainedFileCountLimit = retainedFileCountLimit;
        _maxFileSizeBytes = maxFileSizeBytes;
        try
        {
            var today = Today();
            (_writer, _stream) = OpenFile(today, sequence: 0);
            _currentDate = today;
            _currentSequence = 0;
            _fileChannelEnabled = true;
            CleanupOverRetained();
        }
        catch (Exception ex)
        {
            // 启动防护：目录不存在且不可创建 / 磁盘不可用等——记录原因、不抛异常，
            // 由调用方告警后跳过文件通道（服务正常启动）。
            InactiveReason = ex.Message;
            _fileChannelEnabled = false;
            _writer = null;
        }
    }

    public ILogger CreateLogger(string categoryName) => new FileLogger(this, categoryName);

    public void Dispose()
    {
        lock (_gate)
        {
            _fileChannelEnabled = false;
            _writer?.Dispose();
            _writer = null;
            _stream = null;
        }
    }

    private static DateOnly Today() => DateOnly.FromDateTime(DateTime.Now);

    /// <summary>当日文件路径：sequence=0 为基名 <名>-yyyyMMdd<扩展名>（server-20260911.log）；
    /// &gt;0 追加序号段（server-20260911.2.log，大小轮转产生）。</summary>
    private string SequencePath(DateOnly date, int sequence)
    {
        var directory = Path.GetDirectoryName(_basePath);
        var core = $"{Path.GetFileNameWithoutExtension(_basePath)}-{date:yyyyMMdd}";
        if (sequence > 0)
        {
            core = $"{core}.{sequence}";
        }

        var fileName = $"{core}{Path.GetExtension(_basePath)}";
        return string.IsNullOrEmpty(directory) ? fileName : Path.Combine(directory, fileName);
    }

    /// <summary>打开写入器（追加模式，共享读）。同时返回底层 FileStream 供大小检测取 Position——
    /// TextWriter.Synchronized 包裹后取不到流位置，须单独保留引用。</summary>
    private static (TextWriter Writer, FileStream Stream) OpenFile(string path)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
        var writer = TextWriter.Synchronized(new StreamWriter(stream)
        {
            AutoFlush = true,
        });
        return (writer, stream);
    }

    private (TextWriter Writer, FileStream Stream) OpenFile(DateOnly date, int sequence)
        => OpenFile(SequencePath(date, sequence));

    /// <summary>日志行写入入口：永不抛出。轮转失败退回旧文件继续写；写入失败自我禁用并告警一次。</summary>
    private void WriteLine(string line)
    {
        lock (_gate)
        {
            if (!_fileChannelEnabled)
            {
                return;
            }

            try
            {
                TryRotate();
                _writer!.WriteLine(line);
            }
            catch (Exception ex)
            {
                DisableFileChannel($"写入日志文件失败：{ex.Message}");
            }
        }
    }

    /// <summary>按日 + 按大小轮转：跨天回新日期基名（序号归零），单文件超上限切当日下一个序号文件；
    /// 任一轮转开新文件失败不抛出（退回旧文件继续写，下次写入再试—— 防护语义， 保持）。
    /// 大小轮转分支同样触发清理（单日多次轮转后文件总数不超上限）。</summary>
    private void TryRotate()
    {
        var today = Today();
        var sizeExceeded = _maxFileSizeBytes > 0 && _stream is not null && _stream.Position >= _maxFileSizeBytes;
        if (today == _currentDate && !sizeExceeded)
        {
            return;
        }

        var sequence = today == _currentDate ? _currentSequence + 1 : 0;
        TextWriter newWriter;
        FileStream newStream;
        try
        {
            (newWriter, newStream) = OpenFile(today, sequence);
        }
        catch (Exception)
        {
            // 开新文件失败（目录被占位 / 磁盘满等）：退回旧文件继续写，下次写入再试——不自我禁用
            return;
        }

        var oldWriter = _writer;
        _writer = newWriter;
        _stream = newStream;
        _currentDate = today;
        _currentSequence = sequence;
        try
        {
            oldWriter?.Dispose();
        }
        catch (IOException)
        {
            // 旧文件关闭失败不影响新文件写入
        }

        CleanupOverRetained();
    }

    /// <summary>清理超期文件：匹配 <名>-yyyyMMdd[.N]<扩展名>（日期部分前 8 字符可解析 yyyyMMdd）者，
    /// 按（日期, 序号〔基名=0〕）元组序删最旧，保留最近 limit 个（含写入中文件）。
    /// 旧实现按文件名 Ordinal 串序排序存在两处错序：`.10` 排在 `.2` 之前、基名文件（最旧）排在全部序号文件之后，
    /// 极端压力下会选中写入中文件去删（Windows 打开句柄 File.Delete 抛 IOException 中止整轮清理）—— 修正。</summary>
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
            var candidates = new List<(DateOnly Date, int Sequence, string Path)>();
            foreach (var file in Directory.GetFiles(directory, $"{prefix}*{extension}"))
            {
                var nameWithoutExtension = Path.GetFileNameWithoutExtension(file);
                if (nameWithoutExtension.Length < prefix.Length + 8)
                {
                    continue;
                }

                var dateAndSequence = nameWithoutExtension[prefix.Length..];
                if (!DateOnly.TryParseExact(dateAndSequence[..8], "yyyyMMdd", out var date))
                {
                    continue;
                }

                var sequence = 0;
                var sequencePart = dateAndSequence[8..];
                if (sequencePart.Length > 0
                    && (sequencePart[0] != '.' || !int.TryParse(sequencePart[1..], out sequence)))
                {
                    // 非序号形态（历史「备注」等非日期文件）不参与清理
                    continue;
                }

                candidates.Add((date, sequence, file));
            }

            var excess = candidates.Count - _retainedFileCountLimit;
            if (excess <= 0)
            {
                return;
            }

            foreach (var candidate in candidates.OrderBy(c => c.Date).ThenBy(c => c.Sequence).Take(excess))
            {
                try
                {
                    File.Delete(candidate.Path);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // 单个文件被占用 / 权限不足（Windows 只读属性、ACL 拒绝）跳过，继续删其余
                    // （写入中文件按元组序总在队尾不会被选中）—— 留观项①
                }
            }
        }
        catch (Exception)
        {
            // 清理失败（枚举目录失败等）不影响写入，下次轮转再试
        }
    }

    /// <summary>运行中自我禁用：关闭文件、控制台告警一次（后续日志仅控制台 / 其他通道）。</summary>
    private void DisableFileChannel(string reason)
    {
        _fileChannelEnabled = false;
        try
        {
            _writer?.Dispose();
        }
        catch (IOException)
        {
            // 关闭失败即可，通道已禁用
        }

        _writer = null;
        _stream = null;
        try
        {
            Console.Error.WriteLine(
                $"[LabelFrame] 警告：日志文件通道已停用（{_basePath}）：{reason}。后续日志仅输出到控制台，服务继续运行；请检查 LABELFRAME_SERVER_LOG_FILE 配置与磁盘状态。");
        }
        catch (Exception)
        {
            // 无控制台（Windows 服务）时告警无处展示，忽略
        }
    }

    private sealed class FileLogger(FileLoggerProvider provider, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{logLevel.ToString().ToUpperInvariant()}] [{category}] {formatter(state, exception)}";
            if (exception is not null)
            {
                line += Environment.NewLine + exception;
            }

            provider.WriteLine(line);
        }
    }
}
