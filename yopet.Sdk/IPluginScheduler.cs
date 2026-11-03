namespace yopet.Sdk;

/// <summary>
/// 全局统一定时任务接口 —— 主程序与所有插件共用的唯一调度入口（底层为 Quartz.NET）。
/// 插件中禁止再自行创建 <c>Timer</c> / <c>PeriodicTimer</c> / <c>Task.Delay</c> 循环，
/// 所有定时、轮询、延时、清理任务都必须通过此接口注册。
/// </summary>
public interface IPluginScheduler
{
    /// <summary>注册一个 cron 定时任务（同 jobId 会覆盖旧任务）</summary>
    /// <param name="jobId">任务唯一标识</param>
    /// <param name="cronExpression">cron 表达式（支持 5 字段 Unix 与 6/7 字段 Quartz）</param>
    /// <param name="callback">任务触发回调</param>
    /// <param name="description">任务描述（可选）</param>
    void Register(string jobId, string cronExpression, Func<Task> callback, string? description = null);

    /// <summary>取消一个定时任务（cron / 间隔 / 一次性均可）</summary>
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
    /// <param name="runImmediately">是否在注册后立即执行一次（默认 true）</param>
    void RegisterInterval(string jobId, int intervalSeconds, Func<Task> callback,
        string? description = null, bool runImmediately = true);

    /// <summary>注册一个间隔定时任务（支持亚秒/分钟级间隔）</summary>
    /// <param name="jobId">任务唯一标识</param>
    /// <param name="interval">执行间隔</param>
    /// <param name="callback">任务触发回调</param>
    /// <param name="description">任务描述（可选）</param>
    /// <param name="runImmediately">是否在注册后立即执行一次（默认 true）</param>
    void RegisterInterval(string jobId, TimeSpan interval, Func<Task> callback,
        string? description = null, bool runImmediately = true);

    /// <summary>注册一个一次性延时任务（如防抖、超时恢复）</summary>
    /// <param name="jobId">任务唯一标识</param>
    /// <param name="delay">延时时间</param>
    /// <param name="callback">延时到达后的回调</param>
    /// <param name="description">任务描述（可选）</param>
    void RegisterOnce(string jobId, TimeSpan delay, Func<Task> callback, string? description = null);

    /// <summary>获取所有已注册的定时任务状态</summary>
    List<(string JobId, string Description, bool IsRunning)> GetJobs();
}
