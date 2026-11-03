using System.Reflection;

namespace yopet.Sdk;

/// <summary>插件基类 —— 提供默认实现，插件可继承此类简化开发。</summary>
public abstract class PluginBase : IPlugin, IPluginLoggerAware, IPluginLocalizerAware
{
    public abstract string Name { get; }
    public virtual string Version => "1.0.0";
    public virtual string Description => GetType().GetCustomAttribute<PluginAttribute>()?.Description ?? "";

    protected IPluginHost? Host { get; private set; }

    private IPluginLogger? _logger;
    private ILocalizer? _rootLocalizer;
    private ILocalizer? _localizer;
    private IPluginScheduler? _scheduler;

    /// <summary>保留无参构造，兼容未使用依赖注入的第三方插件。</summary>
    protected PluginBase()
    {
    }

    /// <summary>
    /// 推荐构造函数 —— 由宿主 DI 容器注入插件上下文，聚合日志、本地化与全局调度器。
    /// 继承插件只需声明 <c>public XxxPlugin(PluginContext context) : base(context)</c> 即可。
    /// </summary>
    protected PluginBase(PluginContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        _logger = context.Logger;
        _localizer = context.Localizer;
        _rootLocalizer = context.Localizer;
        _scheduler = context.Scheduler;
    }

    /// <summary>当前插件专属日志器，日志来源自动使用插件类型名。</summary>
    protected IPluginLogger Logger => _logger ?? NullPluginLogger.Instance;

    /// <summary>当前插件专属本地化器，key 前缀自动绑定到插件程序集名。</summary>
    protected ILocalizer Localizer
    {
        get
        {
            if (_localizer != null) return _localizer;
            _localizer = (_rootLocalizer ?? NullLocalizer.Instance)
                .For($"Localization.{LocalizationPrefix}");
            return _localizer;
        }
    }

    /// <summary>全局统一定时任务调度器（已按插件自动隔离 jobId）。</summary>
    protected IPluginScheduler Scheduler => _scheduler ?? NullPluginScheduler.Instance;

    /// <summary>本地化词条前缀（默认使用插件程序集名）。</summary>
    protected virtual string LocalizationPrefix => GetType().Assembly.GetName().Name ?? GetType().Name;

    /// <summary>读取当前语言的插件词条（自动添加 "Localization.&lt;前缀&gt;."）。</summary>
    protected string T(string key) => Localizer.Get(key);

    void IPluginLoggerAware.SetLogger(IPluginLogger logger) => _logger ??= logger;

    void IPluginLocalizerAware.SetLocalizer(ILocalizer localizer)
    {
        _rootLocalizer ??= localizer;
        _localizer ??= localizer.For($"Localization.{LocalizationPrefix}");
    }

    public virtual Task InitializeAsync(IPluginHost host)
    {
        Host = host;
        _logger ??= host.Logger.For(GetType().Name);
        _rootLocalizer ??= host.Localizer;
        _scheduler ??= host.Scheduler;
        return Task.CompletedTask;
    }

    public virtual Task CleanupAsync() => Task.CompletedTask;
}
