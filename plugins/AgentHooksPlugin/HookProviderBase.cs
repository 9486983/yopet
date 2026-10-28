using Lang.Avalonia;
using yopet.Sdk;

namespace AgentHooksPlugin;

/// <summary>
/// Agent Hook 提供者基类 —— 每种 AI 助手（Reasonix、Claude Code 等）
/// 继承此类实现自己的事件监听逻辑。新增 Provider 只需新建一个子类。
/// </summary>
public abstract class HookProviderBase : IDisposable
{
    /// <summary>取当前语言的 Agent Hooks 词条</summary>
    private static string T(string key) =>
        I18nManager.Instance.GetResource($"Localization.AgentHooksPlugin.{key}");

    /// <summary>唯一标识（如 "reasonix"、"claude"）</summary>
    public abstract string Id { get; }

    /// <summary>显示名称（如 "Reasonix Code"）</summary>
    public abstract string Name { get; }

    /// <summary>Emoji 图标</summary>
    public abstract string Emoji { get; }

    /// <summary>简短描述</summary>
    public abstract string Description { get; }

    /// <summary>当前状态（"运行中" / "已暂停" / "未启动"）</summary>
    public virtual string Status => _isRunning ? T("StatusRunning") : T("StatusPaused");

    /// <summary>该 Provider 的配置定义（注册到主程序配置页）</summary>
    public abstract PluginConfigSection ConfigSection { get; }

    /// <summary>配置键名前缀（如 "reasonix_"），用于隔离各 Provider 的配置</summary>
    public abstract string ConfigPrefix { get; }

    /// <summary>是否正在运行</summary>
    public bool IsRunning => _isRunning;
    protected bool _isRunning;

    /// <summary>
    /// 工作状态在无后续事件时的自动恢复时间（毫秒）。
    /// 0 表示关闭自动恢复，完全依赖 session_end / stop 等结束事件。
    /// </summary>
    protected virtual int WorkingIdleTimeoutMs => 0;

    private IPluginHost? _host;
    private readonly object _workingLock = new();
    private bool _isWorking;
    private CancellationTokenSource? _workingCts;

    /// <summary>
    /// 启动监听。
    /// 基类设置 _isRunning = true，子类实现具体的监听逻辑。
    /// </summary>
    public virtual void Start(IPluginHost host)
    {
        _host = host;
        _isRunning = true;
    }

    /// <summary>
    /// 停止监听。
    /// 基类设置 _isRunning = false，子类清理资源。
    /// </summary>
    public virtual void Stop()
    {
        _isRunning = false;
        StopWorking();
        _host = null;
    }

    /// <summary>处理一条事件（由子类在捕获到事件时调用，统一路由到宿主队列）</summary>
    protected void DispatchEvent(IPluginHost host, string type, string title, string text, int durationMs)
    {
        if (!_isRunning) return;

        switch (type)
        {
            case "session_start":
            case "working":
            case "bash":
            case "command":
            case "file":
            case "file_change":
                StartWorking();
                break;

            case "stop":
                StopWorking();
                break;
        }

        host.EnqueueThought(new ThoughtMessage
        {
            Title = title,
            Text = text,
            DurationMs = durationMs,
        });
    }

    /// <summary>处理 session_end 事件（停止动画，收工排队不冲掉前面的消息）</summary>
    protected void DispatchSessionEnd(IPluginHost host)
    {
        StopWorking();
        host.EnqueueThought(new ThoughtMessage
        {
            Title = Name,
            Text = T("WorkDone"),
            DurationMs = 3000,
        });
    }

    /// <summary>进入工作状态，播放工作动画</summary>
    protected void StartWorking()
    {
        var host = _host;
        if (!_isRunning || host == null) return;

        lock (_workingLock)
        {
            _isWorking = true;
            host.StartAnimation(PetAnimation.Running);

            _workingCts?.Cancel();
            _workingCts?.Dispose();
            _workingCts = null;

            if (WorkingIdleTimeoutMs <= 0) return;

            var cts = new CancellationTokenSource();
            _workingCts = cts;
            _ = Task.Run(async () =>
            {
                try { await Task.Delay(WorkingIdleTimeoutMs, cts.Token); }
                catch (TaskCanceledException) { return; }
                StopWorking();
            });
        }
    }

    /// <summary>退出工作状态，恢复待机动画</summary>
    protected void StopWorking()
    {
        var host = _host;
        CancellationTokenSource? cts;

        lock (_workingLock)
        {
            if (!_isWorking) return;
            _isWorking = false;
            cts = _workingCts;
            _workingCts = null;
        }

        cts?.Cancel();
        cts?.Dispose();
        host?.StopAnimation();
    }

    public virtual void Dispose() { Stop(); }
}
