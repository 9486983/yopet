namespace yopet.Sdk;

/// <summary>
/// 插件运行上下文 —— 把插件最常用的日志、本地化与定时任务服务聚合为一个注入对象。
/// 后续新增公共能力时只需扩展此类，无需修改所有插件的构造函数。
/// </summary>
public sealed class PluginContext
{
    public PluginContext(IPluginLogger logger, ILocalizer localizer, IPluginScheduler scheduler)
    {
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));
        Localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
        Scheduler = scheduler ?? throw new ArgumentNullException(nameof(scheduler));
    }

    /// <summary>插件专属日志器。</summary>
    public IPluginLogger Logger { get; }

    /// <summary>插件专属本地化器（已绑定 Localization.&lt;插件&gt; 前缀）。</summary>
    public ILocalizer Localizer { get; }

    /// <summary>插件专属定时任务调度器（jobId 已按插件隔离）。</summary>
    public IPluginScheduler Scheduler { get; }
}
