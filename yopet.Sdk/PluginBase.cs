using System.Reflection;

namespace yopet.Sdk;

/// <summary>插件基类 —— 提供默认实现，插件可继承此类简化开发</summary>
public abstract class PluginBase : IPlugin
{
    public abstract string Name { get; }
    public virtual string Version => "1.0.0";
    public virtual string Description => GetType().GetCustomAttribute<PluginAttribute>()?.Description ?? "";

    protected IPluginHost? Host { get; private set; }

    public virtual Task InitializeAsync(IPluginHost host)
    {
        Host = host;
        return Task.CompletedTask;
    }

    public virtual Task CleanupAsync() => Task.CompletedTask;
}
