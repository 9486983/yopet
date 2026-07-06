using System.Text;

namespace yopet.Services;

/// <summary>
/// 轻量级滚动文件日志，零外部依赖。
/// 按日期分文件，自动清理旧日志（默认保留 30 天）。
/// </summary>
public class LoggerService : IDisposable
{
    private string _logDir;

    /// <summary>日志目录路径（可修改，自动创建）</summary>
    public string LogDir
    {
        get => _logDir;
        set { _logDir = value; Directory.CreateDirectory(value); }
    }
    private readonly int _maxDays;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private string? _currentDate;
    private StreamWriter? _writer;

    public LoggerService(string logDir, int maxDays = 30)
    {
        _logDir = logDir;
        _maxDays = maxDays;
        Directory.CreateDirectory(logDir);
    }

    private string TodayFile => Path.Combine(_logDir, $"{DateTime.Now:yyyy-MM-dd}.log");

    public async Task WriteAsync(string level, string source, string message, Exception? ex = null)
    {
        var now = DateTime.Now;
        var date = now.ToString("yyyy-MM-dd");

        await _lock.WaitAsync();
        try
        {
            // 跨天时切换文件
            if (_currentDate != date)
            {
                _writer?.Dispose();
                _writer = null;
                _currentDate = date;
                CleanupOldLogs();
            }

            _writer ??= new StreamWriter(TodayFile, append: true, Encoding.UTF8)
            {
                AutoFlush = true
            };

            var line = $"[{now:HH:mm:ss}] [{level}] [{source}] {message}";
            _writer.WriteLine(line);

            if (ex != null)
            {
                _writer.WriteLine($"  Exception: {ex.GetType().Name}: {ex.Message}");
                _writer.WriteLine($"  StackTrace: {ex.StackTrace}");
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>清理超过最大保留天数的日志文件</summary>
    private void CleanupOldLogs()
    {
        try
        {
            var cutoff = DateTime.Now.AddDays(-_maxDays);
            foreach (var f in Directory.GetFiles(_logDir, "*.log"))
            {
                if (File.GetLastWriteTime(f) < cutoff)
                    File.Delete(f);
            }
        }
        catch { /* 清理失败不影响主流程 */ }
    }

    public void Dispose()
    {
        _writer?.Dispose();
        _lock.Dispose();
    }
}
