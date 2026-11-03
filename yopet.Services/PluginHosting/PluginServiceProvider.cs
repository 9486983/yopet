using System.Collections.Concurrent;
using yopet.Core.Interfaces;
using yopet.Sdk;

namespace yopet.Services.PluginHosting;

/// <summary>
/// 插件级调度器作用域 —— 自动为 jobId 添加插件前缀，并在插件卸载时清理其全部任务，
/// 避免不同插件任务重名以及热重载后的任务泄漏。
/// </summary>
internal sealed class ScopedPluginScheduler : IPluginScheduler, IDisposable
{
    private readonly IPluginScheduler _inner;
    private readonly string _prefix;
    private readonly ConcurrentDictionary<string, byte> _registered = new(StringComparer.Ordinal);

    public ScopedPluginScheduler(IPluginScheduler inner, string pluginName)
    {
        _inner = inner;
        _prefix = $"{pluginName}:";
    }

    private string Map(string jobId)
    {
        var scopedId = _prefix + jobId;
        _registered[scopedId] = 0;
        return scopedId;
    }

    public void Register(string jobId, string cronExpression, Func<Task> callback, string? description = null)
        => _inner.Register(Map(jobId), cronExpression, callback, description);

    public void Unregister(string jobId)
    {
        var scopedId = _prefix + jobId;
        _registered.TryRemove(scopedId, out _);
        _inner.Unregister(scopedId);
    }

    public void Pause(string jobId) => _inner.Pause(_prefix + jobId);

    public void Resume(string jobId) => _inner.Resume(_prefix + jobId);

    public void RegisterInterval(string jobId, int intervalSeconds, Func<Task> callback,
        string? description = null, bool runImmediately = true)
        => _inner.RegisterInterval(Map(jobId), intervalSeconds, callback, description, runImmediately);

    public void RegisterInterval(string jobId, TimeSpan interval, Func<Task> callback,
        string? description = null, bool runImmediately = true)
        => _inner.RegisterInterval(Map(jobId), interval, callback, description, runImmediately);

    public void RegisterOnce(string jobId, TimeSpan delay, Func<Task> callback, string? description = null)
        => _inner.RegisterOnce(Map(jobId), delay, callback, description);

    public List<(string JobId, string Description, bool IsRunning)> GetJobs()
    {
        return _inner.GetJobs()
            .Where(job => job.JobId.StartsWith(_prefix, StringComparison.Ordinal))
            .Select(job => (job.JobId[_prefix.Length..], job.Description, job.IsRunning))
            .ToList();
    }

    public void Dispose()
    {
        foreach (var jobId in _registered.Keys)
            _inner.Unregister(jobId);
        _registered.Clear();
    }
}

/// <summary>
/// 单插件服务提供器 —— 在根容器之上覆盖该插件专属的上下文、日志、本地化器和调度器，
/// 供 <c>ActivatorUtilities</c> 构造插件实例时使用。
/// </summary>
internal sealed class PluginServiceProvider : IServiceProvider, IDisposable
{
    private readonly IServiceProvider _root;
    private readonly ScopedPluginScheduler _scheduler;
    private readonly PluginEventScope _events;
    private readonly PluginContext _context;

    public PluginServiceProvider(
        IServiceProvider root,
        IPluginLogger logger,
        ILocalizer localizer,
        IPluginScheduler scheduler,
        Type pluginType)
    {
        _root = root;
        _scheduler = new ScopedPluginScheduler(scheduler, pluginType.Name);

        var pluginName = pluginType.Name;
        var localizationPrefix = pluginType.Assembly.GetName().Name ?? pluginName;

        var host = root.GetService(typeof(IPluginHost)) as IPluginHost;
        var configService = root.GetService(typeof(IConfigService)) as IConfigService;
        IPluginConfig config = configService != null
            ? new PluginConfigAdapter(configService)
            : NullPluginConfig.Instance;
        IDispatcherService dispatcher = root.GetService(typeof(IDispatcherService)) as IDispatcherService
                                        ?? NullDispatcherService.Instance;

        _events = new PluginEventScope(host?.Events ?? new PluginEventPool(), pluginName);

        _context = new PluginContext(
            logger.For(pluginName),
            localizer.For($"Localization.{localizationPrefix}"),
            _scheduler,
            config,
            dispatcher,
            _events,
            host);
    }

    public object? GetService(Type serviceType)
    {
        if (serviceType == typeof(PluginContext)) return _context;
        if (serviceType == typeof(IPluginLogger)) return _context.Logger;
        if (serviceType == typeof(ILocalizer)) return _context.Localizer;
        if (serviceType == typeof(IPluginScheduler)) return _scheduler;
        if (serviceType == typeof(IPluginConfig)) return _context.Config;
        if (serviceType == typeof(IDispatcherService)) return _context.Dispatcher;
        if (serviceType == typeof(IPluginEvents)) return _context.Events;
        if (serviceType == typeof(IPluginHost)) return _context.Host;
        return _root.GetService(serviceType);
    }

    public void Dispose()
    {
        _scheduler.Dispose();
        _events.Dispose();
    }
}
