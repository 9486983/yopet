using System.Collections.Concurrent;

namespace yopet.Services;

/// <summary>
/// 轻量级 cron 定时任务调度器，零外部依赖。
/// 支持标准 5 字段 cron 表达式：分 时 日 月 周
/// </summary>
public class CronSchedulerService : IDisposable
{
    private readonly ConcurrentDictionary<string, CronJob> _jobs = new();
    private readonly ConcurrentDictionary<string, IntervalJob> _intervalJobs = new();
    private Timer? _tickTimer;
    private bool _running;

    public void Start()
    {
        if (_running) return;
        _running = true;
        // 每 30 秒检查一次
        _tickTimer = new Timer(_ => Tick(), null, 0, 30_000);
    }

    public void Stop()
    {
        _running = false;
        _tickTimer?.Dispose();
        _tickTimer = null;
    }

    public void AddJob(string jobId, string cronExpression, Func<Task> callback, string? description = null)
    {
        var parsed = CronParser.Parse(cronExpression);
        _jobs[jobId] = new CronJob
        {
            Expression = cronExpression,
            Parsed = parsed,
            Callback = callback,
            Description = description ?? "",
            IsPaused = false,
        };
    }

    public void AddIntervalJob(string jobId, int intervalSeconds, Func<Task> callback, string? description = null)
    {
        // 先取消旧的
        RemoveIntervalJob(jobId);
        var cts = new CancellationTokenSource();
        var job = new IntervalJob
        {
            IntervalMs = intervalSeconds * 1000,
            Callback = callback,
            Description = description ?? "",
            Cts = cts,
        };
        // 启动独立定时器
        job.Timer = new Timer(async _ =>
        {
            try { await callback(); }
            catch { }
        }, null, 0, intervalSeconds * 1000);
        _intervalJobs[jobId] = job;
    }

    public void RemoveIntervalJob(string jobId)
    {
        if (_intervalJobs.TryRemove(jobId, out var job))
        {
            job.Cts.Cancel();
            job.Timer?.Dispose();
        }
    }

    public void RemoveJob(string jobId)
    {
        _jobs.TryRemove(jobId, out _);
        RemoveIntervalJob(jobId);
    }

    public void PauseJob(string jobId)
    {
        if (_jobs.TryGetValue(jobId, out var job))
            job.IsPaused = true;
        if (_intervalJobs.TryGetValue(jobId, out var ij))
            ij.Timer?.Change(Timeout.Infinite, Timeout.Infinite);
    }

    public void ResumeJob(string jobId)
    {
        if (_jobs.TryGetValue(jobId, out var job))
            job.IsPaused = false;
        if (_intervalJobs.TryGetValue(jobId, out var ij))
            ij.Timer?.Change(0, ij.IntervalMs);
    }

    public List<(string JobId, string Description, bool IsRunning)> GetJobs()
    {
        var result = _jobs.Select(kv => (
            kv.Key,
            kv.Value.Description,
            !kv.Value.IsPaused
        )).ToList();
        foreach (var kv in _intervalJobs)
        {
            result.Add((kv.Key, kv.Value.Description, true));
        }
        return result;
    }

    private void Tick()
    {
        var now = DateTime.Now;
        foreach (var (id, job) in _jobs)
        {
            if (job.IsPaused) continue;
            if (job.LastRun != null && job.LastRun.Value.Date == now.Date)
            {
                // 同一天同一分钟已运行过则跳过（防止重复触发）
                if (job.LastRun.Value.Hour == now.Hour && job.LastRun.Value.Minute == now.Minute)
                    continue;
            }
            if (CronParser.Matches(job.Parsed, now))
            {
                job.LastRun = now;
                _ = Task.Run(async () =>
                {
                    try { await job.Callback(); }
                    catch { /* 单次任务异常不影响调度器 */ }
                });
            }
        }
    }

    public void Dispose()
    {
        Stop();
        _jobs.Clear();
    }

    private class IntervalJob
    {
        public int IntervalMs { get; set; }
        public Func<Task> Callback { get; set; } = () => Task.CompletedTask;
        public string Description { get; set; } = "";
        public Timer? Timer { get; set; }
        public CancellationTokenSource Cts { get; set; } = new();
    }

    private class CronJob
    {
        public string Expression { get; set; } = "";
        public CronFields Parsed { get; set; }
        public Func<Task> Callback { get; set; } = () => Task.CompletedTask;
        public string Description { get; set; } = "";
        public bool IsPaused { get; set; }
        public DateTime? LastRun { get; set; }
    }
}

/// <summary>解析后的 cron 字段</summary>
internal struct CronFields
{
    public HashSet<int> Minutes;
    public HashSet<int> Hours;
    public HashSet<int> Days;
    public HashSet<int> Months;
    public HashSet<int> Weekdays;
}

/// <summary>简易 cron 解析器（标准 5 字段）</summary>
internal static class CronParser
{
    /// <summary>解析 cron 表达式，返回字段集</summary>
    public static CronFields Parse(string cron)
    {
        var parts = cron.Trim()
            .Replace("?", "")      // 兼容用户输入的 ?（Quartz 格式遗留）
            .Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 5)
            throw new FormatException($"cron 表达式需要 5 个字段，实际 {parts.Length} 个: {cron}");

        return new CronFields
        {
            Minutes = ParseField(parts[0], 0, 59),
            Hours = ParseField(parts[1], 0, 23),
            Days = ParseField(parts[2], 1, 31),
            Months = ParseField(parts[3], 1, 12),
            Weekdays = ParseField(parts[4], 0, 6),
        };
    }

    /// <summary>判断当前时间是否匹配 cron 表达式</summary>
    public static bool Matches(CronFields f, DateTime t)
    {
        return f.Minutes.Contains(t.Minute)
            && f.Hours.Contains(t.Hour)
            && f.Days.Contains(t.Day)
            && f.Months.Contains(t.Month)
            && f.Weekdays.Contains((int)t.DayOfWeek);
    }

    private static HashSet<int> ParseField(string field, int min, int max)
    {
        var result = new HashSet<int>();

        foreach (var part in field.Split(','))
        {
            var trim = part.Trim();
            if (trim == "*")
            {
                for (var i = min; i <= max; i++) result.Add(i);
                continue;
            }

            var step = 1;
            var rangePart = trim;
            var stepIdx = trim.IndexOf('/');
            if (stepIdx >= 0)
            {
                step = int.Parse(trim[(stepIdx + 1)..]);
                rangePart = trim[..stepIdx];
            }

            int rangeStart, rangeEnd;
            if (rangePart == "*")
            {
                rangeStart = min;
                rangeEnd = max;
            }
            else if (rangePart.Contains('-'))
            {
                var dash = rangePart.IndexOf('-');
                rangeStart = int.Parse(rangePart[..dash]);
                rangeEnd = int.Parse(rangePart[(dash + 1)..]);
            }
            else
            {
                rangeStart = int.Parse(rangePart);
                // N/step → 从 N 到 max 每 step 步（如 0/10 → 0,10,20,30,40,50）
                rangeEnd = step > 1 ? max : rangeStart;
            }

            for (var i = rangeStart; i <= rangeEnd; i += step)
            {
                if (i >= min && i <= max)
                    result.Add(i);
            }
        }

        return result;
    }
}
