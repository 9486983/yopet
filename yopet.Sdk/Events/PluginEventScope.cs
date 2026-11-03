namespace yopet.Sdk;

/// <summary>
/// 按插件名隔离的事件注册作用域，插件卸载时可通过 <see cref="Dispose"/> 一次性反注册。
/// </summary>
public sealed class PluginEventScope : IPluginEvents, IDisposable
{
    private readonly PluginEventPool _pool;
    private readonly string _pluginName;

    public PluginEventScope(PluginEventPool pool, string pluginName)
    {
        _pool = pool ?? throw new ArgumentNullException(nameof(pool));
        _pluginName = string.IsNullOrWhiteSpace(pluginName)
            ? throw new ArgumentException("插件名不能为空", nameof(pluginName))
            : pluginName;
    }

    public void Register<T>(string eventName, T handler) where T : Delegate
        => _pool.Register(_pluginName, eventName, handler);

    public void Unregister(string eventName)
        => _pool.Unregister(_pluginName, eventName);

    public void UnregisterAll()
        => _pool.UnregisterAll(_pluginName);

    public void Dispose() => UnregisterAll();
}
