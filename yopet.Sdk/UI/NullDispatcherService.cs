using yopet.Core.Interfaces;

namespace yopet.Sdk;

/// <summary>无操作 UI 调度实现，用于未使用依赖注入的旧插件回退。</summary>
public sealed class NullDispatcherService : IDispatcherService
{
    public static readonly NullDispatcherService Instance = new();

    private NullDispatcherService()
    {
    }

    public void Post(Action action)
    {
    }
}
