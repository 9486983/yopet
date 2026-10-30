using System.Text.Json;
using System.Text.Json.Serialization;
using Lang.Avalonia;
using yopet.Core.Models;
using yopet.Sdk;

namespace ProgramSchedulerPlugin;

/// <summary>
/// 程序定时启停插件。
///
/// 让用户为若干外部程序各自配置「每天固定时间」的启停规则：
///   - 时段内运行：到启动时间拉起程序，到停止时间关闭程序；
///   - 仅启动一次：到启动时间拉起程序，不负责关闭。
///
/// 规则以 JSON 形式保存在插件配置中，通过右键菜单的列表弹窗管理；
/// 新增/编辑使用带标签的配置表单（含文件选择、下拉、开关），一次填完所有字段。
/// 停止程序时先优雅关闭（CloseMainWindow），超时再强制结束进程树。
/// </summary>
[Plugin("程序定时启停", Version = "1.0.0",
    Description = "按每天固定时间自动启动/关闭所配置的外部程序")]
public class ProgramSchedulerPlugin : PluginBase
{
    private const string RulesKey = "psp_rules";
    private const string EditPrefix = "psp_edit_";

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly List<ScheduleRule> _rules = new();
    private readonly List<string> _registeredJobs = new();
    private readonly ProgramLauncher _launcher = new();

    private IPluginHost? _host;
    private ListDialogConfig? _listConfig;
    private DateTime _lastApply = DateTime.MinValue;

    /// <summary>取当前语言的插件词条</summary>
    private static string T(string key) =>
        I18nManager.Instance.GetResource($"Localization.ProgramSchedulerPlugin.{key}");

    private void Log(string message) =>
        Logger.Info(message);

    private void LogWarn(string message) =>
        Logger.Warn(message);

    private void LogError(string message, Exception? ex = null) =>
        Logger.Error(message, ex);

    private void LogLauncher(string message, bool isError)
    {
        if (isError) LogError(message);
        else Log(message);
    }

    public override string Name => T("Name");

    public override string Description => T("Description");

    // ──────────────────────────────────────────────
    //  初始化 / 清理
    // ──────────────────────────────────────────────

    public override Task InitializeAsync(IPluginHost host)
    {
        _host = host;
        Log("插件初始化");

        LoadRules(host);
        ApplySchedules(host);
        RegisterEditForm(host);

        host.RegisterAction(new PluginAction
        {
            Name = T("Name"),
            Emoji = "🚀",
            Group = T("Group"),
            Description = T("Description"),
            Display = LocalizedDisplay.Of(
                name: () => T("Name"),
                group: () => T("Group"),
                description: () => T("Description")),
            Target = ActionTarget.ContextMenu,
            Callback = () => ShowRulesAsync(host),
        });

        host.ConfigValueChanged += OnConfigChanged;
        Log("程序定时启停插件已加载");
        return Task.CompletedTask;
    }

    public override Task CleanupAsync()
    {
        if (_host is { } host)
        {
            UnregisterSchedules(host);
            host.ConfigValueChanged -= OnConfigChanged;
        }
        return Task.CompletedTask;
    }

    // ──────────────────────────────────────────────
    //  规则持久化
    // ──────────────────────────────────────────────

    private void LoadRules(IPluginHost host)
    {
        _rules.Clear();
        var json = host.GetConfig(RulesKey);
        if (string.IsNullOrWhiteSpace(json)) return;

        try
        {
            var loaded = JsonSerializer.Deserialize<List<ScheduleRule>>(json, JsonOpts);
            if (loaded != null) _rules.AddRange(loaded);
        }
        catch (Exception ex)
        {
            Logger.Warn($"解析规则失败，已忽略: {ex.Message}");
        }
    }

    private void SaveRules(IPluginHost host)
    {
        try
        {
            host.SetConfig(RulesKey, JsonSerializer.Serialize(_rules, JsonOpts));
        }
        catch (Exception ex)
        {
            Logger.Error("保存规则失败", ex);
        }
    }

    // ──────────────────────────────────────────────
    //  定时任务编排
    // ──────────────────────────────────────────────

    private void ApplySchedules(IPluginHost host)
    {
        UnregisterSchedules(host);

        foreach (var rule in _rules.Where(r => r.Enabled))
        {
            if (string.IsNullOrEmpty(rule.StartCron)) continue;

            var startId = $"psp_{rule.Id}_start";
            host.Scheduler.Register(startId, rule.StartCron,
                () => OnStartAsync(rule), $"🚀 {rule.Name}");
            _registeredJobs.Add(startId);

            var hasStop = rule.Mode == ScheduleMode.Window && !string.IsNullOrEmpty(rule.StopCron);
            if (hasStop)
            {
                var stopId = $"psp_{rule.Id}_stop";
                host.Scheduler.Register(stopId, rule.StopCron,
                    () => OnStopAsync(rule), $"🛑 {rule.Name}");
                _registeredJobs.Add(stopId);
            }

            Log($"注册规则「{rule.Name}」mode={rule.Mode} start={rule.StartCron}" +
                (hasStop ? $" stop={rule.StopCron}" : "（无停止任务）"));
        }
    }

    private void UnregisterSchedules(IPluginHost host)
    {
        foreach (var jobId in _registeredJobs)
            host.Scheduler.Unregister(jobId);
        _registeredJobs.Clear();
    }

    private async Task OnStartAsync(ScheduleRule rule)
    {
        Log($"定时启动触发「{rule.Name}」");
        var (ok, message) = await _launcher.LaunchAsync(rule, LogLauncher);
        if (ok) Log($"定时启动成功「{rule.Name}」");
        else LogError($"定时启动失败「{rule.Name}」: {message}");
    }

    private async Task OnStopAsync(ScheduleRule rule)
    {
        Log($"定时停止触发「{rule.Name}」");
        var outcome = await _launcher.StopAsync(rule, LogLauncher);
        switch (outcome)
        {
            case StopOutcome.Stopped:
                Log($"定时停止成功「{rule.Name}」");
                break;
            case StopOutcome.NotRunning:
                LogWarn($"定时停止跳过「{rule.Name}」: 进程未运行");
                break;
            default:
                LogError($"定时停止失败「{rule.Name}」");
                break;
        }
    }

    // ──────────────────────────────────────────────
    //  编辑表单（新增 / 编辑共用）
    // ──────────────────────────────────────────────

    private void RegisterEditForm(IPluginHost host)
    {
        host.RegisterConfig(new PluginConfigSection
        {
            Key = "psp_edit",
            Title = T("FormTitle"),
            Emoji = "🚀",
            HideFromSettings = true,
            Fields = new()
            {
                new()
                {
                    Key = EditPrefix + "name",
                    Label = T("FldName"),
                    Type = PluginConfigFieldType.String,
                    Placeholder = T("FldNamePh"),
                    Description = T("FldNameDesc"),
                },
                new()
                {
                    Key = EditPrefix + "path",
                    Label = T("FldPath"),
                    Type = PluginConfigFieldType.FilePath,
                    Placeholder = T("FldPathPh"),
                    Description = T("FldPathDesc"),
                },
                new()
                {
                    Key = EditPrefix + "args",
                    Label = T("FldArgs"),
                    Type = PluginConfigFieldType.String,
                    Placeholder = T("FldArgsPh"),
                    Description = T("FldArgsDesc"),
                },
                new()
                {
                    Key = EditPrefix + "workdir",
                    Label = T("FldWorkDir"),
                    Type = PluginConfigFieldType.FolderPath,
                    Placeholder = T("FldWorkDirPh"),
                    Description = T("FldWorkDirDesc"),
                },
                new()
                {
                    Key = EditPrefix + "mode",
                    Label = T("FldMode"),
                    Type = PluginConfigFieldType.Dropdown,
                    DefaultValue = "window",
                    Options = new()
                    {
                        new() { Label = T("ModeWindow"), Value = "window" },
                        new() { Label = T("ModeOnce"), Value = "once" },
                    },
                    Description = T("FldModeDesc"),
                },
                new()
                {
                    Key = EditPrefix + "start",
                    Label = T("FldStart"),
                    Type = PluginConfigFieldType.String,
                    Placeholder = "17:00",
                    Description = T("FldStartDesc"),
                },
                new()
                {
                    Key = EditPrefix + "stop",
                    Label = T("FldStop"),
                    Type = PluginConfigFieldType.String,
                    Placeholder = "04:00",
                    Description = T("FldStopDesc"),
                },
                new()
                {
                    Key = EditPrefix + "enabled",
                    Label = T("FldEnabled"),
                    Type = PluginConfigFieldType.Boolean,
                    DefaultValue = "true",
                    Description = T("FldEnabledDesc"),
                },
            },
        }, Name);
    }

    /// <summary>打开编辑表单；rule 为 null 表示新增。</summary>
    private Task OpenEditorAsync(IPluginHost host, ScheduleRule? rule)
    {
        host.SetConfig(EditPrefix + "id", rule?.Id ?? "");
        host.SetConfig(EditPrefix + "name", rule?.Name ?? "");
        host.SetConfig(EditPrefix + "path", rule?.Path ?? "");
        host.SetConfig(EditPrefix + "args", rule?.Arguments ?? "");
        host.SetConfig(EditPrefix + "workdir", rule?.WorkingDir ?? "");
        host.SetConfig(EditPrefix + "mode",
            rule == null ? "window" : rule.Mode == ScheduleMode.Window ? "window" : "once");
        host.SetConfig(EditPrefix + "start",
            string.IsNullOrWhiteSpace(rule?.StartTime) ? "09:00" : rule!.StartTime);
        host.SetConfig(EditPrefix + "stop",
            string.IsNullOrWhiteSpace(rule?.StopTime) ? "18:00" : rule!.StopTime);
        host.SetConfig(EditPrefix + "enabled", (rule?.Enabled ?? true) ? "true" : "false");

        host.ShowConfigDialog("psp_edit");
        return Task.CompletedTask;
    }

    private void OnConfigChanged(object? sender, string key)
    {
        if (_host == null || !key.StartsWith(EditPrefix, StringComparison.Ordinal)) return;

        // 一次保存会连续触发多个 key，合并为一次处理
        if ((DateTime.Now - _lastApply).TotalMilliseconds < 200) return;
        _lastApply = DateTime.Now;

        ApplyEditorSave(_host);
    }

    private void ApplyEditorSave(IPluginHost host)
    {
        var path = (host.GetConfig(EditPrefix + "path") ?? "").Trim();
        if (string.IsNullOrWhiteSpace(path))
        {
            host.ShowThought(T("EmptyPathTitle"), T("EmptyPathMsg"));
            return;
        }

        var start = ScheduleRule.NormalizeTime(host.GetConfig(EditPrefix + "start"));
        if (start == null)
        {
            host.ShowThought(T("InvalidTimeTitle"),
                string.Format(T("InvalidTimeMsg"), host.GetConfig(EditPrefix + "start") ?? ""));
            return;
        }

        var mode = (host.GetConfig(EditPrefix + "mode") ?? "window") == "once"
            ? ScheduleMode.Once
            : ScheduleMode.Window;

        var stop = "";
        if (mode == ScheduleMode.Window)
        {
            stop = ScheduleRule.NormalizeTime(host.GetConfig(EditPrefix + "stop")) ?? "";
            if (string.IsNullOrEmpty(stop))
            {
                host.ShowThought(T("InvalidTimeTitle"),
                    string.Format(T("InvalidTimeMsg"), host.GetConfig(EditPrefix + "stop") ?? ""));
                return;
            }
        }

        var id = host.GetConfig(EditPrefix + "id") ?? "";
        var name = (host.GetConfig(EditPrefix + "name") ?? "").Trim();
        if (string.IsNullOrWhiteSpace(name)) name = T("UnnamedRule");

        var rule = new ScheduleRule
        {
            Id = string.IsNullOrEmpty(id) ? Guid.NewGuid().ToString("N") : id,
            Name = name,
            Path = path,
            Arguments = (host.GetConfig(EditPrefix + "args") ?? "").Trim(),
            WorkingDir = (host.GetConfig(EditPrefix + "workdir") ?? "").Trim(),
            Mode = mode,
            StartTime = start,
            StopTime = stop,
            Enabled = (host.GetConfig(EditPrefix + "enabled") ?? "true") == "true",
        };

        // 回写 id，避免同一次保存的后续事件重复新增
        host.SetConfig(EditPrefix + "id", rule.Id);

        var index = _rules.FindIndex(r => r.Id == rule.Id);
        if (index >= 0) _rules[index] = rule; else _rules.Add(rule);

        Persist(host);
        host.ShowThought(T("SavedTitle"), string.Format(T("SavedMsg"), rule.Name));
    }

    // ──────────────────────────────────────────────
    //  列表弹窗
    // ──────────────────────────────────────────────

    private async Task ShowRulesAsync(IPluginHost host)
    {
        _listConfig ??= BuildListConfig(host);
        await host.ShowListDialog(_listConfig);
    }

    private ListDialogConfig BuildListConfig(IPluginHost host)
    {
        return new ListDialogConfig
        {
            Title = T("ListTitle"),
            Emoji = "🚀",
            LayoutMode = ListDialogLayoutMode.Table,
            DataSource = () => Task.FromResult(BuildRows()),
            Columns = new()
            {
                new() { Key = "name", Header = T("ColName"), Width = 110 },
                new() { Key = "program", Header = T("ColProgram"), Width = double.NaN },
                new() { Key = "time", Header = T("ColTime"), Width = 110 },
                new() { Key = "mode", Header = T("ColMode"), Width = 90 },
                new() { Key = "status", Header = T("ColStatus"), Width = 80 },
                new()
                {
                    Key = "actions",
                    Header = T("ColAction"),
                    Type = ListColumnType.Action,
                    Width = 100,
                    RowActions = new()
                    {
                        new()
                        {
                            Label = T("ColAction"),
                            Emoji = "⚙️",
                            Type = ListRowActionType.Dropdown,
                            Tooltip = T("RowActionTooltip"),
                            Children = new()
                            {
                                new()
                                {
                                    Label = T("RowEdit"),
                                    Emoji = "📝",
                                    Callback = row => EditRuleAsync(host, row["id"]),
                                },
                                new()
                                {
                                    Label = T("RowToggle"),
                                    Emoji = "🔁",
                                    Callback = row => ToggleRuleAsync(host, row["id"]),
                                },
                                new()
                                {
                                    Label = T("RowStartNow"),
                                    Emoji = "▶",
                                    Callback = row => StartNowAsync(host, row["id"]),
                                },
                                new()
                                {
                                    Label = T("RowStopNow"),
                                    Emoji = "⏹",
                                    Callback = row => StopNowAsync(host, row["id"]),
                                },
                                new()
                                {
                                    Label = T("RowDelete"),
                                    Emoji = "🗑",
                                    Callback = row => DeleteRuleAsync(host, row["id"]),
                                },
                            },
                        },
                    },
                },
            },
            ToolbarActions = new()
            {
                new()
                {
                    Label = T("ToolbarAdd"),
                    Emoji = "➕",
                    Callback = () => NewRuleAsync(host),
                },
                new()
                {
                    Label = T("ToolbarRefresh"),
                    Emoji = "🔄",
                    Callback = () =>
                    {
                        _listConfig?.NotifyDataChanged();
                        host.ShowReaction("🔄");
                        return Task.CompletedTask;
                    },
                },
            },
        };
    }

    /// <summary>列表数据源。空规则时返回一行占位，避免宿主弹窗因空数据自动关闭。</summary>
    private List<Dictionary<string, string>> BuildRows()
    {
        if (_rules.Count == 0)
        {
            return new()
            {
                new()
                {
                    ["id"] = "",
                    ["name"] = T("EmptyHint"),
                    ["program"] = "",
                    ["time"] = "",
                    ["mode"] = "",
                    ["status"] = "",
                },
            };
        }
        return _rules.Select(ToRow).ToList();
    }

    private Dictionary<string, string> ToRow(ScheduleRule rule)
    {
        var name = string.IsNullOrWhiteSpace(rule.Name) ? T("UnnamedRule") : rule.Name;
        var program = string.IsNullOrWhiteSpace(rule.Path)
            ? ""
            : TruncateMiddle(Path.GetFileName(rule.Path), 28);
        var time = rule.Mode == ScheduleMode.Window
            ? string.Format(T("TimeRangeFormat"), rule.StartTime, rule.StopTime)
            : string.Format(T("TimeOnceFormat"), rule.StartTime);
        var status = !rule.Enabled
            ? T("StatusDisabled")
            : _launcher.IsRunning(rule.Id) ? T("StatusRunning") : T("StatusEnabled");

        return new()
        {
            ["id"] = rule.Id,
            ["name"] = name,
            ["program"] = program,
            ["time"] = time,
            ["mode"] = rule.Mode == ScheduleMode.Window ? T("ModeWindow") : T("ModeOnce"),
            ["status"] = status,
        };
    }

    // ──────────────────────────────────────────────
    //  增 / 改 / 删 / 启停
    // ──────────────────────────────────────────────

    private Task NewRuleAsync(IPluginHost host) => OpenEditorAsync(host, null);

    private Task EditRuleAsync(IPluginHost host, string id) =>
        OpenEditorAsync(host, _rules.FirstOrDefault(r => r.Id == id));

    private Task ToggleRuleAsync(IPluginHost host, string id)
    {
        var rule = _rules.FirstOrDefault(r => r.Id == id);
        if (rule == null) return Task.CompletedTask;

        rule.Enabled = !rule.Enabled;
        Persist(host);
        return Task.CompletedTask;
    }

    private async Task DeleteRuleAsync(IPluginHost host, string id)
    {
        var rule = _rules.FirstOrDefault(r => r.Id == id);
        if (rule == null) return;

        var confirmed = await host.ShowConfirmDialog(
            T("ConfirmDeleteTitle"), string.Format(T("ConfirmDeleteMsg"), rule.Name));
        if (!confirmed) return;

        _rules.Remove(rule);
        Persist(host);
        host.ShowThought(T("DeletedTitle"), string.Format(T("DeletedMsg"), rule.Name));
    }

    private async Task StartNowAsync(IPluginHost host, string id)
    {
        var rule = _rules.FirstOrDefault(r => r.Id == id);
        if (rule == null) return;

        Log($"手动启动「{rule.Name}」");
        var (ok, message) = await _launcher.LaunchAsync(rule, LogLauncher);
        _listConfig?.NotifyDataChanged();
        if (ok)
            host.ShowThought(T("StartedTitle"), string.Format(T("StartedMsg"), rule.Name));
        else
        {
            LogError($"手动启动失败「{rule.Name}」: {message}");
            host.ShowThought(T("StartFailedTitle"),
                string.Format(T("StartFailedMsg"), rule.Name, message));
        }
    }

    private async Task StopNowAsync(IPluginHost host, string id)
    {
        var rule = _rules.FirstOrDefault(r => r.Id == id);
        if (rule == null) return;

        Log($"手动停止「{rule.Name}」");
        var outcome = await _launcher.StopAsync(rule, LogLauncher);
        _listConfig?.NotifyDataChanged();
        switch (outcome)
        {
            case StopOutcome.Stopped:
                Log($"手动停止成功「{rule.Name}」");
                host.ShowThought(T("StoppedTitle"), string.Format(T("StoppedMsg"), rule.Name));
                break;
            case StopOutcome.NotRunning:
                LogWarn($"手动停止跳过「{rule.Name}」: 进程未运行");
                host.ShowThought(T("NotRunningTitle"), string.Format(T("NotRunningMsg"), rule.Name));
                break;
            default:
                LogError($"手动停止失败「{rule.Name}」");
                host.ShowThought(T("StopFailedTitle"), string.Format(T("StopFailedMsg"), rule.Name));
                break;
        }
    }

    private void Persist(IPluginHost host)
    {
        SaveRules(host);
        ApplySchedules(host);
        _listConfig?.NotifyDataChanged();
    }

    // ──────────────────────────────────────────────
    //  工具方法
    // ──────────────────────────────────────────────

    /// <summary>中间截断：保留开头 + 扩展名，中间以 ... 代替</summary>
    private static string TruncateMiddle(string name, int maxLen)
    {
        if (name.Length <= maxLen) return name;
        var ext = Path.GetExtension(name);
        var body = Path.GetFileNameWithoutExtension(name);
        var keepFront = Math.Max(6, maxLen - ext.Length - 5);
        var keepTail = Math.Max(3, maxLen - keepFront - 3);
        if (keepFront + keepTail + 3 >= body.Length) return name;
        return body[..keepFront] + "..." + body[^keepTail..] + ext;
    }
}
