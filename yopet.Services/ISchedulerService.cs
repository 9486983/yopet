namespace yopet.Services;

/// <summary>全局统一调度器接口 —— 主程序与插件共用同一个调度实例。</summary>
public interface ISchedulerService
{
    /// <summary>启动全局调度器（重复调用安全）。</summary>
    void Start();

    /// <summary>停止全局调度器并释放调度线程。</summary>
    void Stop();

    /// <summary>注册 cron 定时任务（同 id 会覆盖旧任务）。</summary>
    void AddJob(string jobId, string cronExpression, Func<Task> callback, string? description = null);

    /// <summary>注册固定间隔任务（同 id 会覆盖旧任务）。</summary>
    void AddIntervalJob(string jobId, TimeSpan interval, Func<Task> callback,
        string? description = null, bool runImmediately = true);

    /// <summary>注册一次性延时任务（同 id 会覆盖旧任务）。</summary>
    void AddDelayJob(string jobId, TimeSpan delay, Func<Task> callback, string? description = null);

    /// <summary>移除任务（cron / 间隔 / 一次性均可）。</summary>
    void RemoveJob(string jobId);

    /// <summary>暂停任务。</summary>
    void PauseJob(string jobId);

    /// <summary>恢复任务。</summary>
    void ResumeJob(string jobId);

    /// <summary>获取所有已注册任务的状态快照。</summary>
    List<(string JobId, string Description, bool IsRunning)> GetJobs();
}
