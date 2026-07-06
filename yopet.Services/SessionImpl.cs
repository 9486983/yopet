using System.Collections.Concurrent;
using yopet.Sdk;

namespace yopet.Services;

/// <summary>多步会话实现 —— 线程安全，支持 Complete/Cancel 生命周期</summary>
internal class SessionImpl : ISession
{
    private readonly object _lock = new();
    private string _status;
    private double _progress;

    public string Title { get; }

    public ConcurrentDictionary<string, object> Context { get; } = new();

    public string Status
    {
        get { lock (_lock) return _status; }
        set { lock (_lock) _status = value; }
    }

    public double Progress
    {
        get { lock (_lock) return _progress; }
        set { lock (_lock) _progress = value; }
    }

    public bool IsActive { get; private set; } = true;

    public event Action<ISession>? OnCompleted;
    public event Action<ISession>? OnCancelled;

    /// <summary>当会话结束时回调（由 PluginHostImpl 注入，用于反激活 + 清理）</summary>
    internal Action? OnEndRequested { get; set; }

    public SessionImpl(string title)
    {
        Title = title;
        _status = "";
        _progress = -1;
    }

    public void Complete()
    {
        if (!IsActive) return;
        IsActive = false;
        OnCompleted?.Invoke(this);
        OnEndRequested?.Invoke();
    }

    public void Cancel()
    {
        if (!IsActive) return;
        IsActive = false;
        OnCancelled?.Invoke(this);
        OnEndRequested?.Invoke();
    }
}
