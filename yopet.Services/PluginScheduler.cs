using yopet.Sdk;

namespace yopet.Services;

/// <summary>插件定时任务实现 —— 包装 CronSchedulerService</summary>
public class PluginScheduler : IPluginScheduler
{
    private readonly CronSchedulerService _inner;
    private readonly IPluginLogger _logger;

    public PluginScheduler(CronSchedulerService inner, IPluginLogger logger)
    {
        _inner = inner;
        _logger = logger;
    }

    public void Register(string jobId, string cronExpression, Func<Task> callback, string? description = null)
    {
        _inner.AddJob(jobId, cronExpression, callback, description);
        _logger.Info<PluginScheduler>($"定时任务已注册: {jobId} [{cronExpression}] {description}");
    }

    public void Unregister(string jobId)
    {
        _inner.RemoveJob(jobId);
        _logger.Info<PluginScheduler>($"定时任务已取消: {jobId}");
    }

    public void Pause(string jobId)
    {
        _inner.PauseJob(jobId);
        _logger.Info<PluginScheduler>($"定时任务已暂停: {jobId}");
    }

    public void Resume(string jobId)
    {
        _inner.ResumeJob(jobId);
        _logger.Info<PluginScheduler>($"定时任务已恢复: {jobId}");
    }

    public void RegisterInterval(string jobId, int intervalSeconds, Func<Task> callback, string? description = null)
    {
        _inner.AddIntervalJob(jobId, intervalSeconds, callback, description);
        _logger.Info<PluginScheduler>($"间隔任务已注册: {jobId} 每{intervalSeconds}秒");
    }

    public List<(string JobId, string Description, bool IsRunning)> GetJobs() =>
        _inner.GetJobs();
}
