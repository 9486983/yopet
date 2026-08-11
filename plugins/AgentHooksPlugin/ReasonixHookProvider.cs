using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Lang.Avalonia;
using yopet.Sdk;

namespace AgentHooksPlugin;

/// <summary>
/// Reasonix Code Hook Provider —— 监听 ~/.petdex/events/ 事件目录，
/// 实时捕获 Reasonix 的响应、命令、文件操作等。
/// 使用 FileSystemWatcher 即时响应，无需轮询。
/// 首次启动时自动在 ~/.reasonix/ 下安装 hooks 基础设施（包装脚本 + settings.json）。
/// </summary>
public class ReasonixHookProvider : HookProviderBase
{
    /// <summary>取当前语言的 Agent Hooks 词条</summary>
    private static string T(string key) =>
        I18nManager.Instance.GetResource($"Localization.AgentHooksPlugin.{key}");

    public override string Id => "reasonix";
    public override string Name => "Reasonix Code";
    public override string Emoji => "🧠";
    public override string Description => T("ReasonixDesc");
    public override string ConfigPrefix => "reasonix_";

    private static readonly string EventsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".petdex", "events");

    private static readonly string ReasonixDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".reasonix");

    private static readonly string ReasonixHooksDir = Path.Combine(ReasonixDir, "hooks");

    private static readonly string ReasonixHookScript = Path.Combine(
        ReasonixHooksDir, "reasonix-event.ps1");

    private static readonly string ReasonixSettings = Path.Combine(
        ReasonixDir, "settings.json");

    private FileSystemWatcher? _watcher;
    private CancellationTokenSource? _cleanupCts;
    private string _lastEventId = "";
    private readonly HashSet<string> _processingFiles = new(StringComparer.OrdinalIgnoreCase);

    private const string KeyEnabled = "_enabled";
    private const string KeyShowCommands = "_show_commands";
    private const string KeyMaxLength = "_max_length";

    public override PluginConfigSection ConfigSection => new()
    {
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
                Key = $"{ConfigPrefix}{KeyShowCommands}",
                Label = T("ShowCmdLabel"),
                Type = PluginConfigFieldType.Boolean,
                DefaultValue = "false",
                Description = T("ShowCmdDesc"),
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

        try { Directory.CreateDirectory(EventsDir); }
        catch { return; }

        // ── 自动安装 Reasonix hooks 基础设施（包装脚本 + settings.json） ──
        EnsureReasonixHooksInstalled(host);

        // 扫描启动前已有的事件
        ScanExistingEvents();

        try
        {
            _watcher = new FileSystemWatcher(EventsDir, "*.json")
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite,
                IncludeSubdirectories = false,
                EnableRaisingEvents = true,
            };
            _watcher.Created += (_, e) => OnNewEvent(e.FullPath, host);
            _watcher.Changed += (_, e) => OnNewEvent(e.FullPath, host);

            // 后台定时清理旧文件
            _cleanupCts = new CancellationTokenSource();
            var ct = _cleanupCts.Token;
            _ = Task.Run(async () =>
            {
                while (!ct.IsCancellationRequested)
                {
                    try { await Task.Delay(TimeSpan.FromMinutes(5), ct); }
                    catch (TaskCanceledException) { break; }
                    CleanupOldFiles();
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
        _lastEventId = "";
    }

    private void ScanExistingEvents()
    {
        try
        {
            var files = Directory.GetFiles(EventsDir, "*.json").OrderBy(f => f).ToList();
            if (files.Count == 0) return;
            var lastFile = files[^1];
            var content = File.ReadAllText(lastFile);
            using var doc = JsonDocument.Parse(content);
            if (doc.RootElement.TryGetProperty("id", out var idEl))
                _lastEventId = idEl.GetString() ?? "";
        }
        catch { }
    }

    private void OnNewEvent(string fullPath, IPluginHost host)
    {
        if (host.GetConfig($"{ConfigPrefix}{KeyEnabled}") == "false") return;

        lock (_processingFiles)
        {
            if (!_processingFiles.Add(fullPath)) return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(200);
                string? content = null;
                for (var i = 0; i < 3; i++)
                {
                    try { content = File.ReadAllText(fullPath); break; }
                    catch (IOException) { await Task.Delay(100 * (i + 1)); }
                }
                if (content == null) return;

                using var doc = JsonDocument.Parse(content);
                var root = doc.RootElement;

                var id = root.TryGetProperty("id", out var idEl) ? idEl.GetString() ?? "" : "";
                var type = root.TryGetProperty("type", out var tyEl) ? tyEl.GetString() ?? "" : "";
                var text = root.TryGetProperty("content", out var coEl) ? coEl.GetString() ?? "" : "";

                if (string.IsNullOrEmpty(id) || id == _lastEventId) return;
                _lastEventId = id;

                var maxLen = int.TryParse(host.GetConfig($"{ConfigPrefix}{KeyMaxLength}"), out var m) ? m : 150;

                switch (type)
                {
                    case "response":
                        if (string.IsNullOrEmpty(text)) break;
                        DispatchEvent(host, type, Name,
                            text.Length > maxLen ? text[..maxLen] + "…" : text, 6000);
                        break;

                    case "session_start":
                        DispatchEvent(host, type, Name, T("StartWork"), 3000);
                        break;

                    case "session_end":
                        DispatchSessionEnd(host);
                        break;

                    case "file_change":
                        if (string.IsNullOrEmpty(text)) break;
                        DispatchEvent(host, type, Name,
                            text.Length > 80 ? text[..80] + "…" : text, 4000);
                        break;

                    case "command":
                        if (string.IsNullOrEmpty(text)) break;
                        if (host.GetConfig($"{ConfigPrefix}{KeyShowCommands}") != "true") break;
                        DispatchEvent(host, type, Name,
                            text.Length > 80 ? text[..80] + "…" : text, 4000);
                        break;
                }
            }
            catch { }
            finally
            {
                lock (_processingFiles)
                    _processingFiles.Remove(fullPath);
            }
        });
    }

    private static void CleanupOldFiles()
    {
        try
        {
            if (!Directory.Exists(EventsDir)) return;
            var cutoff = DateTime.UtcNow.AddMinutes(-5);
            foreach (var file in Directory.GetFiles(EventsDir, "*.json"))
            {
                try
                {
                    if (File.GetLastWriteTimeUtc(file) < cutoff)
                        File.Delete(file);
                }
                catch { }
            }
        }
        catch { }
    }

    public override void Dispose()
    {
        Stop();
        GC.SuppressFinalize(this);
    }

    // ===================================================================
    //  Reasonix Hooks 基础设施自动安装
    //  确保首次启动时 ~/.reasonix/hooks/reasonix-event.ps1 存在，
    //  且 ~/.reasonix/settings.json 中包含 hooks 配置。
    // ===================================================================

    /// <summary>包装脚本文件名（嵌入到 ~/.reasonix/hooks/ 下）</summary>
    private const string HookScriptFileName = "reasonix-event.ps1";

    /// <summary>
    /// 嵌入的 PowerShell 包装脚本内容。
    /// 接收 Reasonix Desktop 通过 stdin 传入的 JSON payload，
    /// 映射事件类型、提取内容，以小写键 JSON 写入 ~/.petdex/events/。
    /// </summary>
    private const string HookScriptTemplate = @"# Reasonix Event Hook Wrapper
# Called by Reasonix Desktop hooks mechanism, receives JSON on stdin.
# Writes lowercase-key events to ~/.petdex/events/ for ReasonixHookProvider.

param()

$ErrorActionPreference = ""Stop""

$eventsDir = [System.IO.Path]::Combine($env:USERPROFILE, "".petdex"", ""events"")
if (-not (Test-Path $eventsDir)) { [System.IO.Directory]::CreateDirectory($eventsDir) | Out-Null }

# Read stdin
$stdin = """"
try { if ([Console]::IsInputRedirected) { $stdin = [Console]::In.ReadToEnd() } } catch {}
if ([string]::IsNullOrWhiteSpace($stdin)) { exit 0 }

try { $payload = $stdin | ConvertFrom-Json } catch { exit 0 }
if (-not $payload) { exit 0 }

$reasonixEvent = $payload.event
$cwd = $payload.cwd

# ---- Event mapping ----
$type = """"
$content = """"
$skip = $false

switch ($reasonixEvent) {
    ""SessionStart"" {
        $type = ""session_start""
        $content = ""{{StartWork}}""
    }
    ""SessionEnd"" {
        $type = ""session_end""
        $content = ""{{WorkDone}}""
    }
    ""Stop"" {
        $type = ""response""
        $content = $payload.lastAssistantText
    }
    ""PostLLMCall"" {
        $type = ""response""
        $content = $payload.reasoning
    }
    ""PostToolUse"" {
        $toolName = $payload.toolName
        $toolArgs = $payload.toolArgs
        if ($toolName -eq ""bash"" -or $toolName -eq ""run_command"") {
            $type = ""command""
            if ($toolArgs) { $content = $toolArgs.command }
        }
        elseif ($toolName -eq ""write_file"" -or $toolName -eq ""edit_file"" -or `
                $toolName -eq ""multi_edit"" -or $toolName -eq ""move_file"" -or `
                $toolName -eq ""delete_file"" -or $toolName -eq ""copy_file"") {
            $type = ""file_change""
            if ($toolArgs) {
                if ($toolArgs.file_path) { $content = $toolArgs.file_path }
                elseif ($toolArgs.path) { $content = $toolArgs.path }
            }
        }
        else { $skip = $true }
    }
    ""Notification"" {
        $type = ""notification""
        $content = $payload.message
    }
    ""SubagentStop"" {
        $type = ""response""
        $content = $payload.lastAssistantText
    }
    default { $skip = $true }
}

if ($skip -or [string]::IsNullOrEmpty($type)) { exit 0 }
if ([string]::IsNullOrEmpty($content)) { exit 0 }

# Trim long content
if ($content.Length -gt 500) { $content = $content.Substring(0, 500) + ""..."" }

# ---- Write event file (lowercase keys to match provider) ----
$event = @{
    id        = [guid]::NewGuid().ToString()
    timestamp = [DateTime]::UtcNow.ToString(""o"")
    assistant = ""reasonix""
    type      = $type
    content   = $content
}

$filename = ""$(Get-Date -Format 'yyyyMMddHHmmssfff')_$(Get-Random -Max 9999).json""
$filepath = [System.IO.Path]::Combine($eventsDir, $filename)
$json = $event | ConvertTo-Json -Compress
[System.IO.File]::WriteAllText($filepath, $json)
";

    private static readonly string HookScriptContent = HookScriptTemplate
        .Replace("{{StartWork}}", T("StartWork"))
        .Replace("{{WorkDone}}", T("WorkDone"));

    /// <summary>
    /// 确保 Reasonix hooks 基础设施已就绪：
    /// 1. 创建 ~/.reasonix/hooks/ 目录
    /// 2. 写入 reasonix-event.ps1 包装脚本
    /// 3. 更新 ~/.reasonix/settings.json 注册 hooks
    /// </summary>
    private void EnsureReasonixHooksInstalled(IPluginHost host)
    {
        try
        {
            // ── 1. 创建 hooks 目录 ──
            Directory.CreateDirectory(ReasonixHooksDir);

            // ── 2. 写入包装脚本（如不存在或版本过旧则更新） ──
            WriteHookScript();

            // ── 3. 注册 hooks 到 settings.json ──
            RegisterReasonixHooks();
        }
        catch (Exception ex)
        {
            host.Log($"[ReasonixHook] 安装 hooks 基础设施失败: {ex.Message}");
        }
    }

    /// <summary>写入包装脚本到 ~/.reasonix/hooks/reasonix-event.ps1</summary>
    private static void WriteHookScript()
    {
        // 如果文件已存在且内容一致则跳过
        if (File.Exists(ReasonixHookScript))
        {
            var existing = File.ReadAllText(ReasonixHookScript, Encoding.UTF8);
            if (existing == HookScriptContent) return;
        }

        File.WriteAllText(ReasonixHookScript, HookScriptContent, Encoding.UTF8);
    }

    /// <summary>在 ~/.reasonix/settings.json 中注册 hooks 配置</summary>
    private static void RegisterReasonixHooks()
    {
        var scriptPath = ReasonixHookScript;

        // 构建每个事件 hook 条目
        var hookCmd = $"powershell.exe -NoProfile -ExecutionPolicy Bypass -File \"{scriptPath}\"";

        // 只注册 PostToolUse —— Reasonix CLI 仅在此事件上触发 hooks
        // （SessionStart/SessionEnd/Stop/PostLLMCall 等为 Desktop 专属，CLI 不触发）
        // 脚本内部自行按 toolName 过滤感兴趣的 tools
        var hooksObject = new JsonObject
        {
            ["PostToolUse"] = new JsonArray
            {
                new JsonObject { ["command"] = hookCmd },
            },
        };

        // ── 读取/创建 settings.json ──
        JsonObject root;
        if (File.Exists(ReasonixSettings))
        {
            var text = File.ReadAllText(ReasonixSettings, Encoding.UTF8);
            try { root = JsonNode.Parse(text)?.AsObject() ?? new JsonObject(); }
            catch { root = new JsonObject(); }

            // 如果已存在 hooks 配置，不覆盖（尊重用户已有配置）
            if (root.ContainsKey("hooks"))
                return;
        }
        else
        {
            root = new JsonObject();
        }

        root["hooks"] = hooksObject;

        // 使用 UnsafeRelaxedJsonEscaping 避免 \" 被序列化为 \u0022
        // 必须用 UTF8 without BOM，否则 Reasonix 无法解析
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };
        File.WriteAllText(ReasonixSettings, root.ToJsonString(options), new UTF8Encoding(false));
    }
}
