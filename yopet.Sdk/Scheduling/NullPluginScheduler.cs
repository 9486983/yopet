namespace yopet.Sdk;

/// <summary>空调度器 —— 用于未经过依赖注入初始化的插件兜底，避免空引用。</summary>
internal sealed class NullPluginScheduler : IPluginScheduler
{
    public static readonly NullPluginScheduler Instance = new();

    private NullPluginScheduler()
    {
    }

    public void Register(string jobId, string cronExpression, Func<Task> callback, string? description = null)
    {
    }

    public void Unregister(string jobId)
    {
    }

    public void Pause(string jobId)
    {
    }

    public void Resume(string jobId)
    {
    }

    public void RegisterInterval(string jobId, int intervalSeconds, Func<Task> callback,
        string? description = null, bool runImmediately = true)
    {
    }

    public void RegisterInterval(string jobId, TimeSpan interval, Func<Task> callback,
        string? description = null, bool runImmediately = true)
    {
    }

    public void RegisterOnce(string jobId, TimeSpan delay, Func<Task> callback, string? description = null)
    {
    }

    public List<(string JobId, string Description, bool IsRunning)> GetJobs() => new();
}
