namespace yopet.Sdk;

/// <summary>插件定时任务接口 —— 所有调度方法集中在此对象中</summary>
public interface IPluginScheduler
{
    /// <summary>注册一个 cron 定时任务</summary>
    /// <param name="jobId">任务唯一标识</param>
    /// <param name="cronExpression">cron 表达式（如 "0 0 * * *" 每小时）</param>
    /// <param name="callback">任务触发回调</param>
    /// <param name="description">任务描述（可选）</param>
    void Register(string jobId, string cronExpression, Func<Task> callback, string? description = null);

    /// <summary>取消一个定时任务</summary>
    void Unregister(string jobId);

    /// <summary>暂停一个定时任务（可恢复）</summary>
    void Pause(string jobId);

    /// <summary>恢复一个暂停的定时任务</summary>
    void Resume(string jobId);

    /// <summary>注册一个间隔定时任务（秒级精度）</summary>
    /// <param name="jobId">任务唯一标识</param>
    /// <param name="intervalSeconds">执行间隔（秒）</param>
    /// <param name="callback">任务触发回调</param>
    /// <param name="description">任务描述（可选）</param>
    void RegisterInterval(string jobId, int intervalSeconds, Func<Task> callback, string? description = null);

    /// <summary>获取所有已注册的定时任务状态</summary>
    List<(string JobId, string Description, bool IsRunning)> GetJobs();
}
