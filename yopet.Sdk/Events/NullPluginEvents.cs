namespace yopet.Sdk;

/// <summary>无操作事件实现，用于未初始化或无宿主事件池的回退场景。</summary>
public sealed class NullPluginEvents : IPluginEvents
{
    public static readonly NullPluginEvents Instance = new();

    private NullPluginEvents()
    {
    }

    public void Register<T>(string eventName, T handler) where T : Delegate
    {
    }

    public void Unregister(string eventName)
    {
    }

    public void UnregisterAll()
    {
    }
}
