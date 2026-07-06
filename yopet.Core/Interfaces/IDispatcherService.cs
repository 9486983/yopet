namespace yopet.Core.Interfaces;

/// <summary>UI 线程调度服务 —— 抽象 Avalonia Dispatcher</summary>
public interface IDispatcherService
{
    void Post(Action action);
}
