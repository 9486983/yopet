using yopet.Sdk;

namespace AgentHooksPlugin;

/// <summary>
/// Agent Hook 提供者基类 —— 每种 AI 助手（Reasonix、Claude Code 等）
/// 继承此类实现自己的事件监听逻辑。新增 Provider 只需新建一个子类。
/// </summary>
public abstract class HookProviderBase : IDisposable
{
    /// <summary>唯一标识（如 "reasonix"、"claude"）</summary>
    public abstract string Id { get; }

    /// <summary>显示名称（如 "Reasonix Code"）</summary>
    public abstract string Name { get; }

    /// <summary>Emoji 图标</summary>
    public abstract string Emoji { get; }

    /// <summary>简短描述</summary>
    public abstract string Description { get; }

    /// <summary>当前状态（"运行中" / "已暂停" / "未启动"）</summary>
    public virtual string Status => _isRunning ? "运行中" : "已暂停";

    /// <summary>该 Provider 的配置定义（注册到主程序配置页）</summary>
    public abstract PluginConfigSection ConfigSection { get; }

    /// <summary>配置键名前缀（如 "reasonix_"），用于隔离各 Provider 的配置</summary>
    public abstract string ConfigPrefix { get; }

    /// <summary>是否正在运行</summary>
    public bool IsRunning => _isRunning;
    protected bool _isRunning;

    /// <summary>
    /// 启动监听。
    /// 基类设置 _isRunning = true，子类实现具体的监听逻辑。
    /// </summary>
    public virtual void Start(IPluginHost host)
    {
        _isRunning = true;
    }

    /// <summary>
    /// 停止监听。
    /// 基类设置 _isRunning = false，子类清理资源。
    /// </summary>
    public virtual void Stop()
    {
        _isRunning = false;
    }

    /// <summary>处理一条事件（由子类在捕获到事件时调用，统一路由到宿主队列）</summary>
    protected void DispatchEvent(IPluginHost host, string type, string title, string text, int durationMs)
    {
        if (!_isRunning) return;
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
        host.StopAnimation();
        host.EnqueueThought(new ThoughtMessage
        {
            Title = Name,
            Text = "收工 ✨",
            DurationMs = 3000,
        });
    }

    public virtual void Dispose() { Stop(); }
}
