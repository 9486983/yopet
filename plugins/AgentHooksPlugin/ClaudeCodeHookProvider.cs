using Lang.Avalonia;

namespace AgentHooksPlugin;

/// <summary>
/// Claude Code Hook Provider —— 监听 ~/.claude/pet-hooks.jsonl 日志文件，
/// 实时捕获 Claude Code 的会话、响应、命令执行等。
/// 使用 FileSystemWatcher 即时响应，无需轮询。
/// </summary>
public class ClaudeCodeHookProvider : JsonlHookProviderBase
{
    /// <summary>取当前语言的 Agent Hooks 词条</summary>
    private static string T(string key) =>
        I18nManager.Instance.GetResource($"Localization.AgentHooksPlugin.{key}");

    public override string Id => "claude";
    public override string Name => "Claude Code";
    public override string Emoji => "🦾";
    public override string Description => T("ClaudeDesc");
    public override string ConfigPrefix => "claude_";

    private static readonly string LogFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".claude", "pet-hooks.jsonl");

    protected override string LogFilePath => LogFile;
    protected override string CommandLabel => T("ShowBashLabel");
    protected override string CommandDescription => T("ShowBashDesc");
    protected override string CommandKey => "_show_bash";
}
