using yopet.Core.Models;

namespace yopet.Core.Interfaces;

/// <summary>AI 助手事件监控（通过 ~/.petdex/events/ 事件文件）</summary>
public interface IActivityMonitor
{
    /// <summary>获取自上次调用以来的新事件</summary>
    AgentEvent[] GetNewEvents();

    /// <summary>启动监控</summary>
    void Start();

    /// <summary>停止监控</summary>
    void Stop();
}
