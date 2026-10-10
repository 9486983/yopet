using System.Text;
using System.Text.Json;
using Lang.Avalonia;
using yopet.Sdk;

namespace AgentHooksPlugin;

/// <summary>
/// Claude Code Hook Provider —— 监听 ~/.claude/pet-hooks.jsonl 日志文件，
/// 实时捕获 Claude Code 的会话、响应、命令执行等。
/// 使用 FileSystemWatcher 即时响应，无需轮询。
/// </summary>
public class ClaudeCodeHookProvider : HookProviderBase
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

    private FileSystemWatcher? _watcher;
    private CancellationTokenSource? _cleanupCts;
    private long _lastLogPosition;
    private int _bashEventCount;
    private const int BashEventThrottle = 5;

    private const string KeyEnabled = "_enabled";
    private const string KeyShowBash = "_show_bash";
    private const string KeyMaxLength = "_max_length";

    public override PluginConfigSection ConfigSection => new()
    {
        Key = "agent_claude",
        Title = $"Agent: {Name}",
        Emoji = Emoji,
        Fields = new()
        {
            new()
            {
                Key = $"{ConfigPrefix}{KeyEnabled}",
                Label = T("EnabledLabel"),
                Type = PluginConfigFieldType.Boolean,
                DefaultValue = "true",
                Description = string.Format(T("EnabledDesc"), Name),
            },
            new()
            {
                Key = $"{ConfigPrefix}{KeyShowBash}",
                Label = T("ShowBashLabel"),
                Type = PluginConfigFieldType.Boolean,
                DefaultValue = "false",
                Description = T("ShowBashDesc"),
            },
            new()
            {
                Key = $"{ConfigPrefix}{KeyMaxLength}",
                Label = T("MaxLengthLabel"),
                Type = PluginConfigFieldType.Number,
                DefaultValue = "150",
                MinValue = 30,
                MaxValue = 500,
                Description = T("MaxLengthDesc"),
            },
        },
    };

    public override void Start(IPluginHost host)
    {
        base.Start(host);
        if (_watcher != null) return;

        try
        {
            var logDir = Path.GetDirectoryName(LogFile);
            if (!string.IsNullOrEmpty(logDir) && !Directory.Exists(logDir))
                Directory.CreateDirectory(logDir);
            if (!File.Exists(LogFile))
                File.WriteAllText(LogFile, "", Encoding.UTF8);
            _lastLogPosition = new FileInfo(LogFile).Length;
        }
        catch { _lastLogPosition = 0; }

        try
        {
            var logDir = Path.GetDirectoryName(LogFile) ?? "";
            _watcher = new FileSystemWatcher(logDir, "pet-hooks.jsonl")
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
                IncludeSubdirectories = false,
                EnableRaisingEvents = true,
            };
            _watcher.Changed += (_, _) => OnLogChanged(host);

            // 后台定时清理日志文件
            _cleanupCts = new CancellationTokenSource();
            var ct = _cleanupCts.Token;
            _ = Task.Run(async () =>
            {
                while (!ct.IsCancellationRequested)
                {
                    try { await Task.Delay(TimeSpan.FromMinutes(30), ct); }
                    catch (TaskCanceledException) { break; }
                    TruncateLog();
                }
            }, ct);
        }
        catch { }
    }

    public override void Stop()
    {
        base.Stop();
        if (_watcher != null)
        {
            _watcher.EnableRaisingEvents = false;
            _watcher.Dispose();
            _watcher = null;
        }
        _cleanupCts?.Cancel();
        _cleanupCts?.Dispose();
        _cleanupCts = null;
    }

    private void OnLogChanged(IPluginHost host)
    {
        if (host.GetConfig($"{ConfigPrefix}{KeyEnabled}") == "false") return;

        try
        {
            var fi = new FileInfo(LogFile);
            if (!fi.Exists || fi.Length <= _lastLogPosition) return;

            using var fs = new FileStream(LogFile, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            fs.Seek(_lastLogPosition, SeekOrigin.Begin);
            using var reader = new StreamReader(fs, Encoding.UTF8);
            var newContent = reader.ReadToEnd();
            _lastLogPosition = fs.Length;
            if (string.IsNullOrEmpty(newContent)) return;

            _bashEventCount = 0;
            foreach (var line in newContent.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                ProcessLogLine(line.Trim('\r'), host);
        }
        catch { }
    }

    private void ProcessLogLine(string line, IPluginHost host)
    {
        var parts = line.Split('\t', 4);
        if (parts.Length < 2) return;

        var eventId = parts[1];
        var project = parts.Length > 2 ? parts[2] : "";
        var extra = parts.Length > 3 ? parts[3] : "";
        var maxLen = int.TryParse(host.GetConfig($"{ConfigPrefix}{KeyMaxLength}"), out var m) ? m : 150;

        switch (eventId)
        {
            case "session_start":
                DispatchEvent(host, eventId, Name,
                    string.IsNullOrEmpty(project) ? T("StartWork") : string.Format(T("StartWorkIn"), project), 3000);
                break;

            case "session_end":
                DispatchSessionEnd(host);
                break;

            case "stop":
                if (string.IsNullOrEmpty(extra)) break;
                DispatchEvent(host, eventId, Name,
                    extra.Length > maxLen ? extra[..maxLen] + "…" : extra, 6000);
                break;

            case "notification":
                DispatchEvent(host, eventId, Name, T("WaitingReply"), 3000);
                break;

            case "bash":
                if (host.GetConfig($"{ConfigPrefix}{KeyShowBash}") != "true") break;
                _bashEventCount++;
                if (_bashEventCount > BashEventThrottle || string.IsNullOrEmpty(extra)) break;
                DispatchEvent(host, eventId, Name,
                    extra.Length > 80 ? extra[..80] + "…" : extra, 4000);
                break;

            case "file":
                if (string.IsNullOrEmpty(extra)) break;
                DispatchEvent(host, eventId, Name,
                    extra.Length > 80 ? extra[..80] + "…" : extra, 3000);
                break;
        }
    }

    private void TruncateLog()
    {
        try
        {
            if (!File.Exists(LogFile)) return;
            var fi = new FileInfo(LogFile);
            if (fi.Length < 1024 * 1024) return; // 小于 1MB 不清理

            // 保留最后 100KB
            using var fs = new FileStream(LogFile, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            var keep = (int)Math.Min(fs.Length, 100 * 1024);
            fs.Seek(-keep, SeekOrigin.End);
            using var reader = new StreamReader(fs, Encoding.UTF8);
            var tail = reader.ReadToEnd();
            File.WriteAllText(LogFile, tail, Encoding.UTF8);
            _lastLogPosition = tail.Length;
        }
        catch { }
    }

    public override void Dispose()
    {
        Stop();
        GC.SuppressFinalize(this);
    }
}
