using yopet.Core.Interfaces;

namespace yopet.Sdk;

/// <summary>
/// 插件依赖注入上下文 —— 将插件最常用的日志、本地化、定时任务、配置、UI 调度与事件能力
/// 聚合为一个注入对象；后续新增公共能力时只需扩展本类，无需修改所有插件构造函数。
/// </summary>
public sealed class PluginContext
{
    public PluginContext(
        IPluginLogger logger,
        ILocalizer localizer,
        IPluginScheduler scheduler,
        IPluginConfig config,
        IDispatcherService dispatcher,
        IPluginEvents events,
        IPluginHost? host = null)
    {
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));
        Localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
        Scheduler = scheduler ?? throw new ArgumentNullException(nameof(scheduler));
        Config = config ?? throw new ArgumentNullException(nameof(config));
        Dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        Events = events ?? throw new ArgumentNullException(nameof(events));
        Host = host;
    }

    /// <summary>插件专属日志服务。</summary>
    public IPluginLogger Logger { get; }

    /// <summary>插件专属本地化服务（已按 Localization.&lt;程序集&gt; 前缀绑定）。</summary>
    public ILocalizer Localizer { get; }

    /// <summary>插件专属定时任务服务（jobId 已按插件隔离）。</summary>
    public IPluginScheduler Scheduler { get; }

    /// <summary>插件配置读写服务。</summary>
    public IPluginConfig Config { get; }

    /// <summary>UI 线程调度服务。</summary>
    public IDispatcherService Dispatcher { get; }

    /// <summary>插件级事件注册服务（自动绑定插件名并在卸载时反注册）。</summary>
    public IPluginEvents Events { get; }

    /// <summary>插件宿主；仅在宿主已注册时可用。</summary>
    public IPluginHost? Host { get; }
}
