using System.Reflection;

namespace yopet.Sdk;

/// <summary>插件基类 —— 提供默认实现，插件可继承此类简化开发。</summary>
public abstract class PluginBase : IPlugin, IPluginLoggerAware
{
    public abstract string Name { get; }
    public virtual string Version => "1.0.0";
    public virtual string Description => GetType().GetCustomAttribute<PluginAttribute>()?.Description ?? "";

    protected IPluginHost? Host { get; private set; }

    private IPluginLogger _logger = NullPluginLogger.Instance;

    /// <summary>当前插件专属日志器，日志来源自动使用插件类型名。</summary>
    protected IPluginLogger Logger => _logger;

    void IPluginLoggerAware.SetLogger(IPluginLogger logger) => _logger = logger;

    public virtual Task InitializeAsync(IPluginHost host)
    {
        Host = host;
        _logger = host.Logger.For(GetType().Name);
        return Task.CompletedTask;
    }

    public virtual Task CleanupAsync() => Task.CompletedTask;
}
