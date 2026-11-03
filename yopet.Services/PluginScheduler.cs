using yopet.Sdk;

namespace yopet.Services;

/// <summary>插件定时任务实现 —— 包装全局唯一的 Quartz.NET 调度器。</summary>
public class PluginScheduler : IPluginScheduler
{
    private readonly ISchedulerService _inner;
    private readonly IPluginLogger _logger;

    public PluginScheduler(ISchedulerService inner, IPluginLogger logger)
    {
        _inner = inner;
        _logger = logger.For("PluginScheduler");
    }

    public void Register(string jobId, string cronExpression, Func<Task> callback, string? description = null)
    {
        _inner.AddJob(jobId, cronExpression, callback, description);
        _logger.Info($"定时任务已注册: {jobId} [{cronExpression}] {description}");
    }

    public void Unregister(string jobId)
    {
        _inner.RemoveJob(jobId);
        _logger.Info($"定时任务已取消: {jobId}");
    }

    public void Pause(string jobId)
    {
        _inner.PauseJob(jobId);
        _logger.Info($"定时任务已暂停: {jobId}");
    }

    public void Resume(string jobId)
    {
        _inner.ResumeJob(jobId);
        _logger.Info($"定时任务已恢复: {jobId}");
    }

    public void RegisterInterval(string jobId, int intervalSeconds, Func<Task> callback,
        string? description = null, bool runImmediately = true)
    {
        RegisterInterval(jobId, TimeSpan.FromSeconds(intervalSeconds), callback, description, runImmediately);
    }

    public void RegisterInterval(string jobId, TimeSpan interval, Func<Task> callback,
        string? description = null, bool runImmediately = true)
    {
        _inner.AddIntervalJob(jobId, interval, callback, description, runImmediately);
        _logger.Info($"间隔任务已注册: {jobId} 每{interval} {description}");
    }

    public void RegisterOnce(string jobId, TimeSpan delay, Func<Task> callback, string? description = null)
    {
        _inner.AddDelayJob(jobId, delay, callback, description);
        _logger.Info($"一次性任务已注册: {jobId} 延迟{delay} {description}");
    }

    public List<(string JobId, string Description, bool IsRunning)> GetJobs() =>
        _inner.GetJobs();
}
