using System.Reflection;

namespace yopet.Sdk;

/// <summary>插件基类 —— 提供默认实现，插件可继承此类简化开发。</summary>
public abstract class PluginBase : IPlugin, IPluginLoggerAware, IPluginLocalizerAware
{
    public abstract string Name { get; }
    public virtual string Version => "1.0.0";
    public virtual string Description => GetType().GetCustomAttribute<PluginAttribute>()?.Description ?? "";

    protected IPluginHost? Host { get; private set; }

    private IPluginLogger _logger = NullPluginLogger.Instance;
    private ILocalizer _localizer = NullLocalizer.Instance;

    /// <summary>当前插件专属日志器，日志来源自动使用插件类型名。</summary>
    protected IPluginLogger Logger => _logger;

    /// <summary>当前插件专属本地化器，key 前缀自动绑定到插件程序集名。</summary>
    protected ILocalizer Localizer => _localizer;

    /// <summary>本地化词条前缀（默认使用插件程序集名）。</summary>
    protected virtual string LocalizationPrefix => GetType().Assembly.GetName().Name ?? GetType().Name;

    /// <summary>读取当前语言的插件词条（自动添加 "Localization.&lt;前缀&gt;."）。</summary>
    protected string T(string key) => _localizer.Get(key);

    void IPluginLoggerAware.SetLogger(IPluginLogger logger) => _logger = logger;

    void IPluginLocalizerAware.SetLocalizer(ILocalizer localizer) =>
        _localizer = localizer.For($"Localization.{LocalizationPrefix}");

    public virtual Task InitializeAsync(IPluginHost host)
    {
        Host = host;
        _logger = host.Logger.For(GetType().Name);
        _localizer = host.Localizer.For($"Localization.{LocalizationPrefix}");
        return Task.CompletedTask;
    }

    public virtual Task CleanupAsync() => Task.CompletedTask;
}
