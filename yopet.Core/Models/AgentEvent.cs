namespace yopet.Core.Models;

/// <summary>Agent 活动事件（由 AI 助手 hooks 写入）</summary>
public class AgentEvent
{
    public string Id { get; set; } = "";
    public DateTime Timestamp { get; set; }
    public string Assistant { get; set; } = "";       // claude-code, codex, deepseek, etc.
    public string Type { get; set; } = "";             // session_start, session_end, response, thought
    public string Content { get; set; } = "";           // 响应内容片段
}
