namespace yopet.Sdk;

/// <summary>
/// 插件级事件注册接口 —— 自动绑定当前插件名，并在插件卸载时自动反注册。
/// </summary>
public interface IPluginEvents
{
    /// <summary>注册当前插件对指定事件的处理程序；重复注册会覆盖旧处理程序。</summary>
    void Register<T>(string eventName, T handler) where T : Delegate;

    /// <summary>反注册当前插件对指定事件的处理程序。</summary>
    void Unregister(string eventName);

    /// <summary>反注册当前插件注册的全部事件处理程序。</summary>
    void UnregisterAll();
}
