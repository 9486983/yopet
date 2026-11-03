using System.Collections.Concurrent;
using Quartz;
using yopet.Sdk;

namespace yopet.Services;

/// <summary>
/// 全局统一调度器 —— 基于 Quartz.NET 的进程内单例，主程序与所有插件共用。
///
/// 使用 Quartz 的 RAMJobStore（内存存储），无需数据库，适合桌面单进程应用；
/// cron、固定间隔、一次性延时任务全部通过构造函数注入的同一个实例执行。
/// </summary>
public sealed class SchedulerService : ISchedulerService, IDisposable
{
    private const string JobGroup = "yopet";
    private const string CallbackKey = "yopet.callback";

    private readonly IPluginLogger? _logger;
    private readonly ConcurrentDictionary<string, JobState> _states = new();
    private readonly object _gate = new();
    private IScheduler? _scheduler;
    private StandaloneSchedulerFactory? _factory;

    public SchedulerService(IPluginLogger? logger = null)
    {
        _logger = logger?.For("Scheduler");
    }

    public void Start()
    {
        lock (_gate)
        {
            var scheduler = EnsureScheduler();
            scheduler.Start().AsTask().GetAwaiter().GetResult();
            _logger?.Info("全局定时任务调度器已启动 (Quartz.NET)");
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            if (_scheduler == null) return;
            try
            {
                _scheduler.Shutdown(false).AsTask().GetAwaiter().GetResult();
            }
            finally
            {
                _factory?.Dispose();
                _factory = null;
                _scheduler = null;
                _states.Clear();
            }
            _logger?.Info("全局定时任务调度器已停止");
        }
    }

    public void AddJob(string jobId, string cronExpression, Func<Task> callback, string? description = null)
    {
        var expression = ParseCron(cronExpression);

        lock (_gate)
        {
            var scheduler = EnsureScheduler();
            var key = new JobKey(jobId, JobGroup);
            var detail = BuildJobDetail(key, jobId, description, callback, oneShot: false);
            var trigger = TriggerBuilder.Create()
                .WithIdentity(TriggerKey(jobId))
                .WithDescription(description)
                .WithSchedule(CronScheduleBuilder.Create(expression))
                .StartNow()
                .Build();

            DeleteIfExists(scheduler, key);
            scheduler.ScheduleJob(detail, trigger).AsTask().GetAwaiter().GetResult();
            _states[jobId] = new JobState(description ?? "", false);
        }
    }

    public void AddIntervalJob(string jobId, TimeSpan interval, Func<Task> callback,
        string? description = null, bool runImmediately = true)
    {
        if (interval <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(interval), "间隔必须大于 0");

        lock (_gate)
        {
            var scheduler = EnsureScheduler();
            var key = new JobKey(jobId, JobGroup);
            var detail = BuildJobDetail(key, jobId, description, callback, oneShot: false);

            var startAt = runImmediately ? DateTimeOffset.Now : DateTimeOffset.Now + interval;
            var trigger = TriggerBuilder.Create()
                .WithIdentity(TriggerKey(jobId))
                .WithDescription(description)
                .ForJob(key)
                .WithSchedule(SimpleScheduleBuilder.Create()
                    .WithInterval(interval)
                    .RepeatForever())
                .StartAt(startAt)
                .Build();

            DeleteIfExists(scheduler, key);
            scheduler.ScheduleJob(detail, trigger).AsTask().GetAwaiter().GetResult();
            _states[jobId] = new JobState(description ?? "", false);
        }
    }

    public void AddDelayJob(string jobId, TimeSpan delay, Func<Task> callback, string? description = null)
    {
        lock (_gate)
        {
            var scheduler = EnsureScheduler();
            var key = new JobKey(jobId, JobGroup);
            var detail = BuildJobDetail(key, jobId, description, callback, oneShot: true);

            var startAt = delay > TimeSpan.Zero
                ? DateTimeOffset.Now + delay
                : DateTimeOffset.Now;
            var trigger = TriggerBuilder.Create()
                .WithIdentity(TriggerKey(jobId))
                .WithDescription(description)
                .ForJob(key)
                .WithSchedule(SimpleScheduleBuilder.Create().WithRepeatCount(0))
                .StartAt(startAt)
                .Build();

            DeleteIfExists(scheduler, key);
            scheduler.ScheduleJob(detail, trigger).AsTask().GetAwaiter().GetResult();
            _states[jobId] = new JobState(description ?? "", false);
        }
    }

    public void RemoveJob(string jobId)
    {
        lock (_gate)
        {
            _states.TryRemove(jobId, out _);
            if (_scheduler == null) return;
            DeleteIfExists(_scheduler, new JobKey(jobId, JobGroup));
        }
    }

    public void PauseJob(string jobId)
    {
        lock (_gate)
        {
            if (_scheduler == null) return;
            if (_states.TryGetValue(jobId, out var state))
                state.IsPaused = true;
            _scheduler.PauseJob(new JobKey(jobId, JobGroup)).AsTask().GetAwaiter().GetResult();
        }
    }

    public void ResumeJob(string jobId)
    {
        lock (_gate)
        {
            if (_scheduler == null) return;
            if (_states.TryGetValue(jobId, out var state))
                state.IsPaused = false;
            _scheduler.ResumeJob(new JobKey(jobId, JobGroup)).AsTask().GetAwaiter().GetResult();
        }
    }

    public List<(string JobId, string Description, bool IsRunning)> GetJobs()
    {
        return _states
            .Select(kv => (kv.Key, kv.Value.Description, !kv.Value.IsPaused))
            .ToList();
    }

    private IScheduler EnsureScheduler()
    {
        if (_scheduler != null) return _scheduler;

        _factory = QuartzSchedulerBuilder.Create(q => q
                .ConfigureScheduler(o => o.InstanceName = "yopet")
                .UseDefaultThreadPool(maxConcurrency: 10)
                .UseInMemoryStore())
            .Build();

        _scheduler = _factory.GetScheduler().AsTask().GetAwaiter().GetResult();
        return _scheduler;
    }

    private IJobDetail BuildJobDetail(JobKey key, string jobId, string? description,
        Func<Task> callback, bool oneShot)
    {
        var wrapped = WrapCallback(jobId, callback, oneShot);

        return JobBuilder.Create<DelegateJob>()
            .WithIdentity(key)
            .WithDescription(description)
            .UsingJobData(CallbackKey, wrapped)
            .Build();
    }

    private Func<Task> WrapCallback(string jobId, Func<Task> callback, bool oneShot)
    {
        return async () =>
        {
            try
            {
                await callback();
            }
            catch (Exception ex)
            {
                _logger?.Error($"定时任务执行失败: {jobId}", ex);
            }
            finally
            {
                if (oneShot)
                    _states.TryRemove(jobId, out _);
            }
        };
    }

    private static TriggerKey TriggerKey(string jobId) => new($"trigger:{jobId}", JobGroup);

    private static void DeleteIfExists(IScheduler scheduler, JobKey key)
    {
        scheduler.DeleteJob(key).AsTask().GetAwaiter().GetResult();
    }

    private static CronExpression ParseCron(string cronExpression)
    {
        var trimmed = cronExpression.Trim();
        var fields = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var expression = fields.Length == 5
            ? CronExpression.Parse(trimmed, CronFormat.Unix)
            : CronExpression.Parse(trimmed);

        return expression.WithTimeZone(TimeZoneInfo.Local);
    }

    public void Dispose() => Stop();

    private sealed class JobState
    {
        public JobState(string description, bool isPaused)
        {
            Description = description;
            IsPaused = isPaused;
        }

        public string Description { get; }
        public volatile bool IsPaused;
    }

    /// <summary>Quartz 作业适配器 —— 从 JobDataMap 取出委托并执行。</summary>
    [DisallowConcurrentExecution]
    internal sealed class DelegateJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken)
        {
            if (context.MergedJobDataMap.TryGetValue(CallbackKey, out var value) &&
                value is Func<Task> callback)
            {
                return new ValueTask(callback());
            }

            return ValueTask.CompletedTask;
        }
    }
}
