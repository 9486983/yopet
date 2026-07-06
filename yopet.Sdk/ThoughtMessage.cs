namespace yopet.Sdk;

/// <summary>气泡消息对象 —— 用于排队显示，避免被后续消息打断</summary>
public class ThoughtMessage
{
    /// <summary>气泡标题（如 "💬 Claude Code"）</summary>
    public string Title { get; set; } = "";

    /// <summary>气泡正文</summary>
    public string Text { get; set; } = "";

    /// <summary>显示时长（毫秒），默认 5000</summary>
    public int DurationMs { get; set; } = 5000;
}
