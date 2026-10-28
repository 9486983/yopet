using System.Text;
using Lang.Avalonia;
using yopet.Sdk;

namespace AgentHooksPlugin;

/// <summary>
/// 基于 JSONL 日志文件的 Hook Provider 基类。
/// 日志格式：timestamp \t event \t project \t extra。
/// 子类只需提供日志路径与展示信息，并可选择安装自己的写入端。
/// </summary>
public abstract class JsonlHookProviderBase : HookProviderBase
{
    /// <summary>取当前语言的 Agent Hooks 词条</summary>
    private static string T(string key) =>
        I18nManager.Instance.GetResource($"Localization.AgentHooksPlugin.{key}");

    private const string KeyEnabled = "_enabled";
    private const string KeyMaxLength = "_max_length";
    private const int CommandEventThrottle = 5;

    /// <summary>该 Provider 监听的 JSONL 日志文件路径</summary>
    protected abstract string LogFilePath { get; }

    protected virtual string EnabledDescription => string.Format(T("EnabledDesc"), Name);
    protected virtual string CommandLabel => T("ShowCmdLabel");
    protected virtual string CommandDescription => T("ShowCmdDesc");

    /// <summary>是否显示命令执行的配置键后缀</summary>
    protected virtual string CommandKey => "_show_commands";

    private string CommandConfigKey => $"{ConfigPrefix}{CommandKey}";

    public override PluginConfigSection ConfigSection => new()
    {
        Key = $"agent_{Id}",
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
                Description = EnabledDescription,
            },
            new()
            {
                Key = CommandConfigKey,
                Label = CommandLabel,
                Type = PluginConfigFieldType.Boolean,
                DefaultValue = "false",
                Description = CommandDescription,
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

    private FileSystemWatcher? _watcher;
    private CancellationTokenSource? _cleanupCts;
    private string? _logFile;
    private long _lastLogPosition;
    private int _commandEventCount;

    public override void Start(IPluginHost host)
    {
        base.Start(host);
        if (_watcher != null) return;

        var logFile = LogFilePath;
        _logFile = logFile;

        try
        {
            var logDir = Path.GetDirectoryName(logFile);
            if (!string.IsNullOrEmpty(logDir) && !Directory.Exists(logDir))
                Directory.CreateDirectory(logDir);
            if (!File.Exists(logFile))
                File.WriteAllText(logFile, "", Encoding.UTF8);
            _lastLogPosition = new FileInfo(logFile).Length;
        }
        catch { _lastLogPosition = 0; }

        try { OnStarted(host); }
        catch (Exception ex) { host.Log($"[{Id}] 启动失败: {ex.Message}"); }

        try
        {
            var logDir = Path.GetDirectoryName(logFile) ?? "";
            _watcher = new FileSystemWatcher(logDir, Path.GetFileName(logFile))
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
                IncludeSubdirectories = false,
                EnableRaisingEvents = true,
            };
            _watcher.Changed += (_, _) => OnLogChanged(host);

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
        _logFile = null;
    }

    /// <summary>日志文件与监听器就绪后调用，子类可在此安装写入端</summary>
    protected virtual void OnStarted(IPluginHost host) { }

    private void OnLogChanged(IPluginHost host)
    {
        if (host.GetConfig($"{ConfigPrefix}{KeyEnabled}") == "false") return;
        if (_logFile == null) return;

        try
        {
            var fi = new FileInfo(_logFile);
            if (!fi.Exists || fi.Length <= _lastLogPosition) return;

            using var fs = new FileStream(_logFile, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            fs.Seek(_lastLogPosition, SeekOrigin.Begin);
            using var reader = new StreamReader(fs, Encoding.UTF8);
            var newContent = reader.ReadToEnd();
            _lastLogPosition = fs.Length;
            if (string.IsNullOrEmpty(newContent)) return;

            _commandEventCount = 0;
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
                if (host.GetConfig(CommandConfigKey) != "true") break;
                _commandEventCount++;
                if (_commandEventCount > CommandEventThrottle || string.IsNullOrEmpty(extra)) break;
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
        if (_logFile == null) return;

        try
        {
            if (!File.Exists(_logFile)) return;
            var fi = new FileInfo(_logFile);
            if (fi.Length < 1024 * 1024) return;

            using var fs = new FileStream(_logFile, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            var keep = (int)Math.Min(fs.Length, 100 * 1024);
            fs.Seek(-keep, SeekOrigin.End);
            using var reader = new StreamReader(fs, Encoding.UTF8);
            var tail = reader.ReadToEnd();
            File.WriteAllText(_logFile, tail, Encoding.UTF8);
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
