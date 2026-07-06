using Avalonia.Threading;
using yopet.Core.Interfaces;

namespace yopet.Services;

/// <summary>Avalonia 调度器实现</summary>
public class AvaloniaDispatcherService : IDispatcherService
{
    public void Post(Action action) => Dispatcher.UIThread.Post(action);
}
