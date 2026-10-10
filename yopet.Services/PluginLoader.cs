using yopet.Sdk;
using yopet.Services.PluginHosting;

namespace yopet.Services;

/// <summary>
/// 插件加载器 —— 对外门面，委托 <see cref="PluginLifecycleManager"/> 管理插件生命周期。
/// 提供加载/初始化/清理，以及热重载（ReloadAllAsync）入口。
/// </summary>
public class PluginLoader
{
    private readonly PluginLifecycleManager _manager;
    private string _directory = "";

    public PluginLoader()
    {
        _manager = new PluginLifecycleManager(
            msg => System.Diagnostics.Debug.WriteLine($"[Plugin] {msg}"));
    }

    /// <summary>已加载的插件列表（只读快照）</summary>
    public IReadOnlyList<IPlugin> Plugins => _manager.Plugins;

    /// <summary>从指定目录加载所有插件</summary>
    public void LoadFromDirectory(string directory)
    {
        _directory = directory;
        _manager.LoadFromDirectory(directory);
    }

    /// <summary>初始化所有插件</summary>
    public Task InitializeAllAsync(IPluginHost host) => _manager.InitializeAllAsync(host);

    /// <summary>清理所有插件（应用退出时调用）</summary>
    public Task CleanupAllAsync() => _manager.CleanupAllAsync();

    /// <summary>热重载：清理并卸载全部插件后，从原目录重新加载并初始化</summary>
    public Task ReloadAllAsync(IPluginHost host) => _manager.ReloadAllAsync(host, _directory);
}
