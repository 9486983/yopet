using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using yopet.Core.Models;
using yopet.Sdk;

namespace ScriptManagerPlugin;

/// <summary>
/// 通用脚本管理器插件。
///
/// 功能：
///   1. 📜 右键 → "脚本列表" 展示所有已挂载脚本
///   2. 📥 拖入脚本文件（.py/.bat/.cmd/.ps1/.sh/.js/.vbs）→ 自动复制到脚本库 → 弹出列表 → 按配置自动执行
///   3. ▶  运行脚本 → 检测参数需求 → 有参数则弹出输入框 → 执行并显示输出
///   4. 🗑 删除 / 📝 编辑脚本
///   5. ⚡ 快速运行（右键菜单直接搜索脚本名）
///   6. 🖱️ 脚本列表内可将脚本绑定到宠物单击/双击事件
///   7. ⚙️ 可配置各解释器路径、工作目录、是否自动运行
/// </summary>
[Plugin("脚本管理器", Version = "2.0.0",
    Description = "挂载、管理和运行多种脚本（Python、批处理、PowerShell、Shell、Node.js、VBScript）")]
public class ScriptManagerPlugin : PluginBase
{
    private static readonly string[] SupportedExtensions =
        { ".py", ".bat", ".cmd", ".ps1", ".sh", ".js", ".vbs" };

    private const string SingleClickScriptKey = "event_single_click_script";
    private const string DoubleClickScriptKey = "event_double_click_script";

    public ScriptManagerPlugin(PluginContext context)
        : base(context)
    {
    }

    public override string Name => T("Name");

    private string _scriptsDir = "";
    private ListDialogConfig? _listConfig;
    private IPluginHost? _host;
    private int _eventScriptRunning;

    // ──────────────────────────────────────────────
    //  初始化
    // ──────────────────────────────────────────────

    public override async Task InitializeAsync(IPluginHost host)
    {
        await base.InitializeAsync(host);
        _host = host;

        // 脚本存储目录：插件 DLL 同级下的 scripts/
        var pluginDir = Path.GetDirectoryName(GetType().Assembly.Location)!;
        _scriptsDir = Path.Combine(pluginDir, "scripts");
        Directory.CreateDirectory(_scriptsDir);
        LogToFile("插件初始化完成");
        RestoreCronJobs(host);

        // ── 宠物单击 / 双击事件 ──
        Events.Register(EventNames.PetClicked, new Action(OnPetClicked));
        Events.Register(EventNames.PetDoubleClicked, new Action(OnPetDoubleClicked));

        // ── 径向菜单：拖入脚本文件自动挂载 ──
        host.RegisterAction(new PluginAction
        {
            Name = T("MountScript"),
            Emoji = "📥",
            Description = T("MountScriptDesc"),
            Target = ActionTarget.RadialMenu,
            AcceptType = ItemType.File,
            FileExtensions = SupportedExtensions,
            CanActivate = true,
            FileCallback = async (paths) => await OnScriptsDropped(paths, host),
        });

        // ── 右键菜单：折叠在「📜 脚本」分组下 ──
        host.RegisterAction(new PluginAction
        {
            Name = T("ScriptListName"),
            Emoji = "📜",
            Group = T("GroupScripts"),
            Description = T("ScriptListDesc"),
            Display = LocalizedDisplay.Of(
                name: () => T("ScriptListName"),
                group: () => T("GroupScripts"),
                description: () => T("ScriptListDesc")),
            Target = ActionTarget.ContextMenu,
            Callback = async () => await ShowScriptList(host),
        });

        host.RegisterAction(new PluginAction
        {
            Name = T("QuickRunName"),
            Emoji = "⚡",
            Group = T("GroupScripts"),
            Description = T("QuickRunDesc"),
            Display = LocalizedDisplay.Of(
                name: () => T("QuickRunName"),
                group: () => T("GroupScripts"),
                description: () => T("QuickRunDesc")),
            Target = ActionTarget.ContextMenu,
            Callback = async () => await QuickRun(host),
        });

        // ── 插件配置 ──
        host.RegisterConfig(new PluginConfigSection
        {
            Key = "scripts",
            Title = T("ScriptRunConfig"),
            Emoji = "⚙️",
            Groups = new()
            {
                new PluginConfigGroup
                {
                    Title = T("ConfigGroupRuntimeTitle"),
                    Emoji = "🖥️",
                    Description = T("ConfigGroupRuntimeDesc"),
                    FieldKeys =
                    {
                        "python_path", "powershell_path", "shell_path",
                        "node_path", "work_dir",
                    },
                },
                new PluginConfigGroup
                {
                    Title = T("ConfigGroupAutoTitle"),
                    Emoji = "▶️",
                    FieldKeys = { "auto_run", "run_interval" },
                },
                new PluginConfigGroup
                {
                    Title = T("ConfigGroupNotesTitle"),
                    Emoji = "📝",
                    FieldKeys = { "notes" },
                },
            },
            Fields = new()
            {
                new()
                {
                    Key = "python_path",
                    Label = T("PythonPathLabel"),
                    Type = PluginConfigFieldType.FilePath,
                    Placeholder = T("PythonPathPlaceholder"),
                    Description = T("PythonPathDesc"),
                },
                new()
                {
                    Key = "powershell_path",
                    Label = T("PowerShellPathLabel"),
                    Type = PluginConfigFieldType.FilePath,
                    Placeholder = T("PowerShellPathPlaceholder"),
                    Description = T("PowerShellPathDesc"),
                },
                new()
                {
                    Key = "shell_path",
                    Label = T("ShellPathLabel"),
                    Type = PluginConfigFieldType.FilePath,
                    Placeholder = T("ShellPathPlaceholder"),
                    Description = T("ShellPathDesc"),
                },
                new()
                {
                    Key = "node_path",
                    Label = T("NodePathLabel"),
                    Type = PluginConfigFieldType.FilePath,
                    Placeholder = T("NodePathPlaceholder"),
                    Description = T("NodePathDesc"),
                },
                new()
                {
                    Key = "work_dir",
                    Label = T("WorkDirLabel"),
                    Type = PluginConfigFieldType.FolderPath,
                    Placeholder = T("WorkDirPlaceholder"),
                    Description = T("WorkDirDesc"),
                },
                new()
                {
                    Key = "auto_run",
                    Label = T("AutoRunLabel"),
                    Type = PluginConfigFieldType.Boolean,
                    DefaultValue = "true",
                    Description = T("AutoRunDesc"),
                },
                new()
                {
                    Key = "run_interval",
                    Label = T("RunIntervalLabel"),
                    Type = PluginConfigFieldType.CronExpression,
                    Placeholder = "*/10 * * * *",
                    Description = T("RunIntervalDesc"),
                },
                new()
                {
                    Key = "notes",
                    Label = T("NotesLabel"),
                    Type = PluginConfigFieldType.TextArea,
                    TextAreaRows = 4,
                    Placeholder = T("NotesPlaceholder"),
                    Description = T("NotesDesc"),
                },
            },
        });

        await Task.CompletedTask;
    }

    // ──────────────────────────────────────────────
    //  拖入脚本文件 → 自动挂载 + 自动执行
    // ──────────────────────────────────────────────

    private async Task OnScriptsDropped(string[] paths, IPluginHost host)
    {
        var mounted = new List<string>();

        foreach (var path in paths)
        {
            if (!IsSupportedScript(path)) continue;
            var dest = Path.Combine(_scriptsDir, Path.GetFileName(path));
            File.Copy(path, dest, overwrite: true);
            mounted.Add(dest);
        }

        if (mounted.Count == 0)
        {
            host.ShowThought(T("UnsupportedTypeTitle"),
                string.Format(T("UnsupportedTypeContent"), string.Join(" / ", SupportedExtensions)));
            return;
        }

        host.ShowThought(T("MountCompleteTitle"), string.Format(T("MountCompleteContent"), mounted.Count));

        // 弹出脚本列表
        await ShowScriptList(host);

        // 如果配置了自动运行且只挂载了一个脚本，自动执行
        var autoRun = host.GetConfig("auto_run");
        if (autoRun != "false" && mounted.Count == 1)
        {
            var scriptName = Path.GetFileName(mounted[0]);
            await Task.Delay(600); // 让用户先看到挂载成功提示
            await RunScript(scriptName, host);
        }
    }

    // ──────────────────────────────────────────────
    //  脚本列表弹窗
    // ──────────────────────────────────────────────

    private async Task ShowScriptList(IPluginHost host)
    {
        _listConfig = new ListDialogConfig
        {
            Title = T("Name"),
            Emoji = "📜",
            LayoutMode = ListDialogLayoutMode.Table,
            DataSource = () =>
            {
                var single = host.GetConfig(SingleClickScriptKey) ?? "";
                var doubleClick = host.GetConfig(DoubleClickScriptKey) ?? "";

                var list = EnumerateScriptFiles()
                    .Select(ParseScriptInfo)
                    .Select(d =>
                    {
                        var fileName = d["file"];
                        var bindings = new List<string>();
                        if (string.Equals(fileName, single, StringComparison.OrdinalIgnoreCase))
                            bindings.Add(T("TriggerSingleClick"));
                        if (string.Equals(fileName, doubleClick, StringComparison.OrdinalIgnoreCase))
                            bindings.Add(T("TriggerDoubleClick"));

                        d["binding"] = bindings.Count > 0
                            ? string.Join(" + ", bindings)
                            : "—";
                        return d;
                    })
                    .OrderBy(d => d["name"], StringComparer.OrdinalIgnoreCase)
                    .Select(d =>
                    {
                        d["name"] = TruncateMiddle(d["name"], 16);
                        return d;
                    })
                    .ToList();
                return Task.FromResult(list);
            },
            Columns = new()
            {
                new() { Key = "name", Header = T("ColumnScriptHeader"), Width = 110 },
                new() { Key = "desc", Header = T("ColumnDescHeader"), Width = double.NaN },
                new() { Key = "binding", Header = T("ColumnBindingHeader"), Width = 90 },
                new() { Key = "mtime", Header = T("ColumnTimeHeader"), Width = 85 },
                new()
                {
                    Key = "actions",
                    Header = T("ColumnActionHeader"),
                    Type = ListColumnType.Action,
                    Width = 100,
                    RowActions = new()
                    {
                        new()
                        {
                            Label = T("ColumnActionHeader"),
                            Emoji = "⚙️",
                            Type = ListRowActionType.Dropdown,
                            Tooltip = T("RowActionTooltip"),
                            Children = new()
                            {
                                new()
                                {
                                    Label = T("RowRunLabel"),
                                    Emoji = "▶",
                                    Tooltip = T("RowRunTooltip"),
                                    Callback = async (row) =>
                                    {
                                        await RunScript(row["file"], host);
                                        _listConfig?.NotifyDataChanged();
                                    },
                                },
                                new()
                                {
                                    Label = T("RowEditLabel"),
                                    Emoji = "📝",
                                    Tooltip = T("RowEditTooltip"),
                                    Callback = (row) =>
                                    {
                                        var path = Path.Combine(_scriptsDir, row["file"]);
                                        if (File.Exists(path))
                                            Process.Start("notepad.exe", path);
                                        return Task.CompletedTask;
                                    },
                                },
                                new()
                                {
                                    Label = T("RowCronLabel"),
                                    Emoji = "⏰",
                                    Tooltip = T("RowCronTooltip"),
                                    Callback = (row) =>
                                    {
                                        _ = ConfigureCronAsync(row["file"], host);
                                        return Task.CompletedTask;
                                    },
                                },
                                new()
                                {
                                    Label = T("BindSingleClick"),
                                    Emoji = "🖱️",
                                    Tooltip = T("BindSingleClickTooltip"),
                                    Callback = (row) =>
                                    {
                                        BindEventScript(row["file"], single: true, host);
                                        _listConfig?.NotifyDataChanged();
                                        return Task.CompletedTask;
                                    },
                                },
                                new()
                                {
                                    Label = T("BindDoubleClick"),
                                    Emoji = "🖱️",
                                    Tooltip = T("BindDoubleClickTooltip"),
                                    Callback = (row) =>
                                    {
                                        BindEventScript(row["file"], single: false, host);
                                        _listConfig?.NotifyDataChanged();
                                        return Task.CompletedTask;
                                    },
                                },
                                new()
                                {
                                    Label = T("UnbindEvent"),
                                    Emoji = "🚫",
                                    Tooltip = T("UnbindEventTooltip"),
                                    Callback = (row) =>
                                    {
                                        UnbindEventScript(row["file"], host);
                                        _listConfig?.NotifyDataChanged();
                                        return Task.CompletedTask;
                                    },
                                },
                                new()
                                {
                                    Label = T("RowDeleteLabel"),
                                    Emoji = "🗑",
                                    Tooltip = T("RowDeleteTooltip"),
                                    Callback = (row) =>
                                    {
                                        var src = Path.Combine(_scriptsDir, row["file"]);
                                        // 删除时取消定时任务
                                        var cronKey = "cron_" + row["file"];
                                        var existing = host.GetConfig(cronKey);
                                        if (!string.IsNullOrEmpty(existing))
                                        {
                                            Scheduler.Unregister(cronKey);
                                            host.SetConfig(cronKey, "");
                                        }
                                        var delDir = Path.Combine(_scriptsDir, "deleted");
                                        Directory.CreateDirectory(delDir);
                                        var now = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                                        var delName = Path.GetFileNameWithoutExtension(row["file"])
                                            + $"_{now}" + Path.GetExtension(row["file"]);
                                        var dst = Path.Combine(delDir, delName);
                                        if (File.Exists(src)) File.Move(src, dst);
                                        ClearEventBindingFor(row["file"], host);
                                        _listConfig?.NotifyDataChanged();
                                        host.ShowReaction("🗑");
                                        return Task.CompletedTask;
                                    },
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
                    Label = T("ToolbarScriptsFolder"),
                    Emoji = "📂",
                    Callback = () =>
                    {
                        Process.Start("explorer.exe", _scriptsDir);
                        return Task.CompletedTask;
                    },
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

        await host.ShowListDialog(_listConfig);
    }

    // ──────────────────────────────────────────────
    //  快速运行（右键菜单）
    // ──────────────────────────────────────────────

    private async Task QuickRun(IPluginHost host)
    {
        var scripts = EnumerateScriptFiles().ToList();
        if (scripts.Count == 0)
        {
            host.ShowThought(T("NoScriptsTitle"), T("NoScriptsContent"));
            return;
        }

        var input = await host.ShowInputDialog(
            T("QuickRunDialogTitle"),
            T("QuickRunDialogMessage"));

        if (string.IsNullOrEmpty(input)) return;

        var matched = scripts
            .Where(p =>
                Path.GetFileName(p).Contains(input, StringComparison.OrdinalIgnoreCase) ||
                Path.GetFileNameWithoutExtension(p).Contains(input, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (matched.Count == 0)
        {
            host.ShowThought(T("ScriptNotFoundTitle"), string.Format(T("ScriptNotFoundContent"), input));
            return;
        }

        if (matched.Count == 1)
        {
            await RunScript(Path.GetFileName(matched[0]), host);
            return;
        }

        // 多个匹配：自动运行第一个
        var firstName = Path.GetFileName(matched[0]);
        host.ShowThought(T("MultipleMatchesTitle"),
            string.Format(T("MultipleMatchesContent"), matched.Count, firstName));
        await Task.Delay(400);
        await RunScript(firstName, host);
    }

    // ──────────────────────────────────────────────
    //  宠物点击事件绑定
    // ──────────────────────────────────────────────

    private void OnPetClicked() => TriggerBoundScript(SingleClickScriptKey);

    private void OnPetDoubleClicked() => TriggerBoundScript(DoubleClickScriptKey);

    private void TriggerBoundScript(string configKey)
    {
        if (_host == null) return;

        var scriptName = _host.GetConfig(configKey);
        if (string.IsNullOrWhiteSpace(scriptName)) return;

        if (!File.Exists(Path.Combine(_scriptsDir, scriptName)))
        {
            LogWarn($"点击事件绑定的脚本不存在: {scriptName}");
            return;
        }

        // 避免连续点击导致多个脚本并发运行
        if (Interlocked.Exchange(ref _eventScriptRunning, 1) == 1)
        {
            LogWarn("上一次点击事件脚本仍在运行，本次触发已忽略");
            return;
        }

        // 按脚本需要弹出参数输入框（与手动运行一致）
        _ = RunEventScript(scriptName, _host);
    }

    private async Task RunEventScript(string scriptName, IPluginHost host)
    {
        try
        {
            LogToFile($"宠物点击事件触发: {scriptName}");
            await RunScript(scriptName, host);
        }
        catch (Exception ex)
        {
            LogError($"宠物点击事件执行失败: {scriptName}", ex);
        }
        finally
        {
            Interlocked.Exchange(ref _eventScriptRunning, 0);
        }
    }

    private void BindEventScript(string fileName, bool single, IPluginHost host)
    {
        host.SetConfig(single ? SingleClickScriptKey : DoubleClickScriptKey, fileName);
        LogToFile($"绑定{(single ? "单击" : "双击")}事件: {fileName}");
        host.ShowThought(T("EventBoundTitle"),
            string.Format(single ? T("EventBoundSingleContent") : T("EventBoundDoubleContent"), fileName));
        host.ShowReaction("🖱️");
    }

    private void UnbindEventScript(string fileName, IPluginHost host)
    {
        var cleared = false;

        if (string.Equals(host.GetConfig(SingleClickScriptKey), fileName, StringComparison.OrdinalIgnoreCase))
        {
            host.SetConfig(SingleClickScriptKey, "");
            cleared = true;
        }
        if (string.Equals(host.GetConfig(DoubleClickScriptKey), fileName, StringComparison.OrdinalIgnoreCase))
        {
            host.SetConfig(DoubleClickScriptKey, "");
            cleared = true;
        }

        LogToFile($"解除事件绑定: {fileName} (cleared={cleared})");
        if (cleared)
            host.ShowThought(T("EventUnboundTitle"), string.Format(T("EventUnboundContent"), fileName));
        else
            host.ShowThought(T("EventNotBoundTitle"), string.Format(T("EventNotBoundContent"), fileName));
    }

    private static void ClearEventBindingFor(string fileName, IPluginHost host)
    {
        if (string.Equals(host.GetConfig(SingleClickScriptKey), fileName, StringComparison.OrdinalIgnoreCase))
            host.SetConfig(SingleClickScriptKey, "");
        if (string.Equals(host.GetConfig(DoubleClickScriptKey), fileName, StringComparison.OrdinalIgnoreCase))
            host.SetConfig(DoubleClickScriptKey, "");
    }

    // ──────────────────────────────────────────────
    //  执行脚本
    // ──────────────────────────────────────────────

    private async Task RunScript(string scriptName, IPluginHost host, string? presetArgs = null)
    {
        var scriptPath = Path.Combine(_scriptsDir, scriptName);
        if (!File.Exists(scriptPath))
        {
            host.ShowThought(T("ScriptMissingTitle"), string.Format(T("ScriptMissingContent"), scriptName));
            return;
        }

        // 检测参数需求
        string? args;
        if (presetArgs != null)
        {
            args = presetArgs; // 定时任务使用预置参数，不弹输入框
        }
        else
        {
            args = await DetectAndPromptArgs(scriptPath, host);
            if (args == null) return; // 用户取消
        }

        LogToFile($"开始执行: {scriptName} 参数: \"{args}\"");

        var exitCode = -1;
        var output = "";
        var error = "";
        var launchFailed = false;

        try
        {
            // RunWithAnimation 自动：显示工作动画 + 进度环 + 可取消 + 结束后自动恢复待机
            await host.RunWithAnimation(PetAnimation.Running, async (token) =>
            {
                host.ShowThought(T("RunningTitle"), string.Format(T("RunningContent"), scriptName, args));

                var plan = await CreateLaunchPlanAsync(scriptPath, args, host);
                if (plan.StartInfo == null)
                {
                    launchFailed = true;
                    LogError($"无法创建运行计划: {plan.MissingTitle}");
                    host.ShowThought(plan.MissingTitle ?? T("RunErrorTitle"),
                        plan.MissingContent ?? "");
                    return;
                }

                var psi = plan.StartInfo;
                LogToFile($"完整命令: {psi.FileName} {psi.Arguments}");

                using var process = new Process { StartInfo = psi };
                var outputBuilder = new StringBuilder();
                var errorBuilder = new StringBuilder();

                process.OutputDataReceived += (_, e) =>
                {
                    if (e.Data != null)
                        outputBuilder.AppendLine(e.Data);
                };
                process.ErrorDataReceived += (_, e) =>
                {
                    if (e.Data != null)
                        errorBuilder.AppendLine(e.Data);
                };

                token.Register(() =>
                {
                    try { if (!process.HasExited) process.Kill(); } catch { }
                });

                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                await process.WaitForExitAsync(token);

                output = outputBuilder.ToString().TrimEnd();
                error = errorBuilder.ToString().TrimEnd();
                exitCode = process.ExitCode;
            });

            // 执行完毕，RunWithAnimation 已自动停止动画

            if (launchFailed) return;

            if (exitCode == 0)
            {
                var display = Truncate(output, 1500);
                LogToFile($"执行成功 退出码={exitCode}，输出 {output.Length} 字符");
                host.ShowThought(string.Format(T("RunSuccessTitle"), exitCode),
                    string.Format(T("RunSuccessContent"), scriptName, display));
                host.ShowReaction("✅", PetAnimation.Jump);
            }
            else
            {
                var errMsg = Truncate(error.Length > 0 ? error : output, 1000);
                var stderrPreview = error.Length > 0 ? Truncate(error, 500) : output.Length > 0 ? Truncate(output, 500) : "(无输出)";
                LogError($"执行失败 退出码={exitCode}\n  stderr: {stderrPreview}");
                host.ShowThought(string.Format(T("RunFailedTitle"), exitCode),
                    string.Format(T("RunFailedContent"), scriptName, errMsg));
                host.ShowReaction("❌", PetAnimation.Failed);
            }
        }
        catch (OperationCanceledException)
        {
            LogWarn("执行被用户取消");
            host.ShowThought(T("CanceledTitle"), string.Format(T("CanceledContent"), scriptName));
            host.ShowReaction("⏹️", PetAnimation.Wave);
            // 如果是定时任务触发的，自动暂停
            var cronKey = "cron_" + scriptName;
            if (!string.IsNullOrEmpty(host.GetConfig(cronKey)))
            {
                Scheduler.Pause(cronKey);
                LogWarn($"定时任务已自动暂停: {cronKey}");
            }
        }
        catch (Exception ex)
        {
            LogError($"执行异常: {ex.GetType().Name}", ex);
            host.ShowThought(T("RunErrorTitle"),
                string.Format(T("RunErrorContent"), scriptName, ex.Message));
            host.ShowReaction("❌", PetAnimation.Failed);
        }
    }

    // ──────────────────────────────────────────────
    //  运行计划（按扩展名选择解释器）
    // ──────────────────────────────────────────────

    private sealed record LaunchPlan(
        ProcessStartInfo? StartInfo,
        string? MissingTitle = null,
        string? MissingContent = null);

    private async Task<LaunchPlan> CreateLaunchPlanAsync(
        string scriptPath, string args, IPluginHost host)
    {
        var ext = Path.GetExtension(scriptPath).ToLowerInvariant();
        var psi = new ProcessStartInfo
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        var workDir = host.GetConfig("work_dir");
        psi.WorkingDirectory = !string.IsNullOrWhiteSpace(workDir) && Directory.Exists(workDir)
            ? workDir
            : Path.GetDirectoryName(scriptPath)!;

        switch (ext)
        {
            case ".py":
            {
                var exe = await ResolvePythonAsync(host.GetConfig("python_path"));
                if (exe == null)
                    return new(null, T("PythonNotFoundTitle"), T("PythonNotFoundContent"));
                psi.FileName = exe;
                psi.Arguments = Quote(scriptPath) + " " + args;
                psi.EnvironmentVariables["PYTHONIOENCODING"] = "utf-8";
                SetUtf8(psi);
                break;
            }
            case ".ps1":
            {
                var exe = await ResolvePowerShellAsync(host.GetConfig("powershell_path"));
                if (exe == null)
                    return new(null, T("PowerShellNotFoundTitle"), T("PowerShellNotFoundContent"));
                psi.FileName = exe;
                psi.Arguments =
                    $"-NoLogo -NoProfile -ExecutionPolicy Bypass -File {Quote(scriptPath)} {args}";
                if (IsPwsh(exe)) SetUtf8(psi);
                break;
            }
            case ".bat":
            case ".cmd":
            {
                var comspec = Environment.GetEnvironmentVariable("ComSpec");
                psi.FileName = string.IsNullOrWhiteSpace(comspec) ? "cmd.exe" : comspec;
                psi.Arguments = $"/d /s /c \"{Quote(scriptPath)} {args}\"";
                break;
            }
            case ".sh":
            {
                var exe = await ResolveBashAsync(host.GetConfig("shell_path"));
                if (exe == null)
                    return new(null, T("BashNotFoundTitle"), T("BashNotFoundContent"));
                psi.FileName = exe;
                psi.Arguments = Quote(scriptPath.Replace('\\', '/')) + " " + args;
                SetUtf8(psi);
                break;
            }
            case ".js":
            {
                var exe = await ResolveNodeAsync(host.GetConfig("node_path"));
                if (exe == null)
                    return new(null, T("NodeNotFoundTitle"), T("NodeNotFoundContent"));
                psi.FileName = exe;
                psi.Arguments = Quote(scriptPath) + " " + args;
                SetUtf8(psi);
                break;
            }
            case ".vbs":
            {
                var cscript = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.System),
                    "cscript.exe");
                psi.FileName = cscript;
                psi.Arguments = $"//nologo {Quote(scriptPath)} {args}";
                break;
            }
            default:
                return new(null, T("UnsupportedTypeTitle"),
                    string.Format(T("UnsupportedTypeContent"),
                        string.Join(" / ", SupportedExtensions)));
        }

        return new(psi);
    }

    private static void SetUtf8(ProcessStartInfo psi)
    {
        psi.StandardOutputEncoding = Encoding.UTF8;
        psi.StandardErrorEncoding = Encoding.UTF8;
    }

    private static string Quote(string value) => $"\"{value}\"";

    private static bool IsPwsh(string exe) =>
        string.Equals(Path.GetFileNameWithoutExtension(exe), "pwsh",
            StringComparison.OrdinalIgnoreCase);

    // ──────────────────────────────────────────────
    //  参数检测
    // ──────────────────────────────────────────────

    /// <summary>
    /// 检测脚本是否需要参数，如需则弹出输入框。
    /// 返回参数串（文本）；null 表示用户取消。
    /// </summary>
    private async Task<string?> DetectAndPromptArgs(
        string scriptPath, IPluginHost host)
    {
        var ext = Path.GetExtension(scriptPath).ToLowerInvariant();
        try
        {
            var lines = await File.ReadAllLinesAsync(scriptPath);
            var scanLines = lines.Take(120).ToArray();

            // ① 显式声明 # args: / rem args: / :: args= / // args: → 预填为初始值，仍然弹出输入框供用户修改
            string? declaredArgs = null;
            foreach (var line in scanLines)
            {
                var comment = GetCommentText(line, ext);
                if (comment != null)
                {
                    if (TryGetHeaderValue(comment, "args", out var value))
                    {
                        declaredArgs = value;
                        break;
                    }
                    continue;
                }

                if (string.IsNullOrWhiteSpace(line) || IsPrologueLine(line.Trim()))
                    continue;

                break;
            }
            if (declaredArgs != null)
            {
                var edited = await host.ShowInputDialog(
                    T("InputArgsTitle"),
                    T("InputArgsEditMessage"),
                    declaredArgs);
                return edited; // null=取消, 空或实际输入=使用
            }

            // ② 自动检测 —— 按脚本类型识别常见参数入口
            var fullText = string.Join("\n", scanLines);
            if (DetectNeedsArgs(ext, fullText))
            {
                var argStr = await host.ShowInputDialog(
                    T("InputArgsTitle"),
                    T("InputArgsDetectedMessage"),
                    "");
                return argStr; // null=取消, ""=空参数
            }
        }
        catch
        {
            // 读取出错，按无参数处理
        }

        return ""; // 无需参数
    }

    private static bool DetectNeedsArgs(string ext, string text) => ext switch
    {
        ".py" => text.Contains("import argparse", StringComparison.Ordinal) ||
                 text.Contains("from argparse", StringComparison.Ordinal) ||
                 text.Contains("sys.argv", StringComparison.Ordinal) ||
                 text.Contains("input(", StringComparison.Ordinal),
        ".ps1" => text.Contains("param(", StringComparison.OrdinalIgnoreCase) ||
                  text.Contains("$args", StringComparison.OrdinalIgnoreCase),
        ".bat" or ".cmd" => text.Contains("%1", StringComparison.Ordinal) ||
                            text.Contains("%*", StringComparison.Ordinal),
        ".sh" => Regex.IsMatch(text, @"\$\{?[1-9@*]"),
        ".js" => text.Contains("process.argv", StringComparison.Ordinal),
        ".vbs" => text.Contains("WScript.Arguments", StringComparison.OrdinalIgnoreCase),
        _ => false,
    };

    // ──────────────────────────────────────────────
    //  脚本信息解析
    // ──────────────────────────────────────────────

    private static Dictionary<string, string> ParseScriptInfo(string filePath)
    {
        var fi = new FileInfo(filePath);
        var name = fi.Name;
        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        var desc = ExtractDescription(filePath, ext);

        if (string.IsNullOrEmpty(desc))
            desc = Path.GetFileNameWithoutExtension(name);

        var mtime = fi.LastWriteTime.ToString("yyyy-MM-dd HH:mm");

        return new()
        {
            ["name"] = name,        // 显示用（会被 TruncateMiddle 截断）
            ["file"] = name,        // 操作用（原始文件名）
            ["desc"] = desc,
            ["mtime"] = mtime,
        };
    }

    /// <summary>
    /// 从脚本头部提取描述：
    /// 1) 任意注释风格中的 desc:/description: 声明；
    /// 2) Python 模块文档字符串（docstring）。
    /// </summary>
    private static string ExtractDescription(string filePath, string ext)
    {
        try
        {
            var lines = File.ReadLines(filePath).Take(40).ToArray();

            foreach (var line in lines)
            {
                var comment = GetCommentText(line, ext);
                if (comment != null)
                {
                    if (TryGetHeaderValue(comment, "description", out var text) ||
                        TryGetHeaderValue(comment, "desc", out text))
                        return text;
                    continue;
                }

                if (string.IsNullOrWhiteSpace(line) || IsPrologueLine(line.Trim()))
                    continue;

                break;
            }

            if (ext == ".py")
            {
                var doc = ExtractPythonDocstring(lines);
                if (!string.IsNullOrWhiteSpace(doc))
                    return doc;
            }
        }
        catch
        {
            // 忽略解析错误
        }

        return "";
    }

    private static string ExtractPythonDocstring(string[] lines)
    {
        var inDocstring = false;
        var docQuote = "";
        var desc = "";

        foreach (var line in lines)
        {
            var trim = line.Trim();

            if (!inDocstring && (trim.StartsWith("\"\"\"") || trim.StartsWith("'''")))
            {
                docQuote = trim[..3];
                var rest = trim[3..].Trim();
                var endIdx = rest.IndexOf(docQuote, StringComparison.Ordinal);
                if (endIdx >= 0)
                    return rest[..endIdx].Trim();
                inDocstring = true;
                desc = rest;
                continue;
            }

            if (inDocstring)
            {
                var endIdx = trim.IndexOf(docQuote, StringComparison.Ordinal);
                if (endIdx >= 0)
                    return (desc + " " + trim[..endIdx].Trim()).Trim();
                desc = (desc + " " + trim).Trim();
                continue;
            }

            // 注释、空行之外的代码行出现即停止
            if (!trim.StartsWith("#") && !string.IsNullOrEmpty(trim))
                break;
        }

        return "";
    }

    /// <summary>
    /// 将一行内容解析为注释文本（不含注释符号）。
    /// 支持 #、//、::、REM、VBScript 的 '。
    /// </summary>
    private static string? GetCommentText(string line, string ext)
    {
        var trim = line.Trim();
        if (trim.Length == 0) return null;

        // 批处理常用 @rem / @echo 前缀
        if (trim.StartsWith("@", StringComparison.Ordinal))
            trim = trim[1..].TrimStart();

        if (trim.StartsWith("::", StringComparison.Ordinal))
            return trim[2..];
        if (trim.StartsWith("#", StringComparison.Ordinal))
            return trim[1..];
        if (trim.StartsWith("//", StringComparison.Ordinal))
            return trim[2..];
        if (trim.StartsWith("REM", StringComparison.OrdinalIgnoreCase) &&
            (trim.Length == 3 || char.IsWhiteSpace(trim[3])))
            return trim[3..];
        if (ext == ".vbs" && trim.StartsWith("'", StringComparison.Ordinal))
            return trim[1..];

        return null;
    }

    /// <summary>脚本头部常见的前置语句（不计入"代码开始"判断）。</summary>
    private static bool IsPrologueLine(string trim) =>
        trim.Equals("@echo off", StringComparison.OrdinalIgnoreCase) ||
        trim.Equals("echo off", StringComparison.OrdinalIgnoreCase) ||
        trim.Equals("setlocal", StringComparison.OrdinalIgnoreCase) ||
        trim.StartsWith("setlocal ", StringComparison.OrdinalIgnoreCase) ||
        trim.StartsWith("chcp ", StringComparison.OrdinalIgnoreCase) ||
        trim.Equals("option explicit", StringComparison.OrdinalIgnoreCase) ||
        trim.StartsWith("#requires", StringComparison.OrdinalIgnoreCase) ||
        trim.Equals("[cmdletbinding()]", StringComparison.OrdinalIgnoreCase);

    /// <summary>解析 "key: value" / "key=value" 形式的头部声明。</summary>
    private static bool TryGetHeaderValue(string comment, string key, out string value)
    {
        value = "";
        var trim = comment.Trim();
        if (!trim.StartsWith(key, StringComparison.OrdinalIgnoreCase))
            return false;

        var rest = trim[key.Length..];
        if (rest.StartsWith(":", StringComparison.Ordinal) ||
            rest.StartsWith("=", StringComparison.Ordinal))
        {
            value = rest[1..].Trim();
            return true;
        }

        return false;
    }

    // ──────────────────────────────────────────────
    //  解释器解析
    // ──────────────────────────────────────────────

    private static async Task<string?> ResolvePythonAsync(string? configured)
    {
        if (IsUsableExecutable(configured)) return configured;

        if (await CanRunAsync("py", "-3 --version")) return "py";
        if (await CanRunAsync("python", "--version")) return "python";

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var candidates = new[]
        {
            @"C:\Python312\python.exe",
            @"C:\Python311\python.exe",
            @"C:\Python310\python.exe",
            @"C:\Python39\python.exe",
            @"C:\Python38\python.exe",
            Path.Combine(localAppData, "Programs", "Python", "Python312", "python.exe"),
            Path.Combine(localAppData, "Programs", "Python", "Python311", "python.exe"),
            Path.Combine(localAppData, "Programs", "Python", "Python310", "python.exe"),
        };

        return candidates.FirstOrDefault(File.Exists);
    }

    private static async Task<string?> ResolvePowerShellAsync(string? configured)
    {
        if (IsUsableExecutable(configured)) return configured;

        if (await CanRunAsync("pwsh", "-NoProfile -Command exit")) return "pwsh";
        if (await CanRunAsync("powershell", "-NoProfile -Command exit")) return "powershell";

        var legacy = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            "WindowsPowerShell", "v1.0", "powershell.exe");
        return File.Exists(legacy) ? legacy : null;
    }

    private static async Task<string?> ResolveBashAsync(string? configured)
    {
        if (IsUsableExecutable(configured)) return configured;

        if (await CanRunAsync("bash", "--version")) return "bash";

        var candidates = new[]
        {
            @"C:\Program Files\Git\bin\bash.exe",
            @"C:\Program Files\Git\usr\bin\bash.exe",
            @"C:\Program Files (x86)\Git\bin\bash.exe",
        };

        return candidates.FirstOrDefault(File.Exists);
    }

    private static async Task<string?> ResolveNodeAsync(string? configured)
    {
        if (IsUsableExecutable(configured)) return configured;

        if (await CanRunAsync("node", "--version")) return "node";

        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var candidates = new[]
        {
            Path.Combine(programFiles, "nodejs", "node.exe"),
            Path.Combine(localAppData, "Programs", "nodejs", "node.exe"),
        };

        return candidates.FirstOrDefault(File.Exists);
    }

    private static bool IsUsableExecutable(string? path) =>
        !string.IsNullOrWhiteSpace(path) && File.Exists(path);

    private static async Task<bool> CanRunAsync(string fileName, string arguments)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            });
            if (process == null) return false;

            using var cts = new CancellationTokenSource(3000);
            await process.WaitForExitAsync(cts.Token);
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    // ──────────────────────────────────────────────
    //  脚本枚举
    // ──────────────────────────────────────────────

    private IEnumerable<string> EnumerateScriptFiles() =>
        Directory.Exists(_scriptsDir)
            ? Directory.EnumerateFiles(_scriptsDir)
                .Where(IsSupportedScript)
                .OrderBy(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase)
            : Enumerable.Empty<string>();

    private static bool IsSupportedScript(string path) =>
        SupportedExtensions.Contains(
            Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    // ──────────────────────────────────────────────
    //  定时任务
    // ──────────────────────────────────────────────

    /// <summary>创建一个带自动暂停的定时任务回调</summary>
    private Func<Task> CreateCronCallback(string scriptName, string cronKey, IPluginHost host)
    {
        return async () =>
        {
            LogToFile($"定时触发: {scriptName}");
            var argsKey = "cron_args_" + scriptName;
            var savedArgs = host.GetConfig(argsKey) ?? "";
            await RunScript(scriptName, host, savedArgs);

            // 如果脚本执行过程中被取消，RunScript 内部会负责暂停对应的定时任务。
        };
    }

    /// <summary>配置脚本的定时任务（支持 cron 和间隔两种模式）</summary>
    private async Task ConfigureCronAsync(string scriptName, IPluginHost host)
    {
        var cronKey = "cron_" + scriptName;
        var argsKey = "cron_args_" + scriptName;
        var currentCron = host.GetConfig(cronKey) ?? "";
        var currentArgs = host.GetConfig(argsKey) ?? "";

        // 第一步：cron/间隔表达式
        var input = await host.ShowInputDialog(
            T("SetCronTitle"),
            T("SetCronMessage"),
            currentCron);
        if (input == null) return;

        Scheduler.Unregister(cronKey);
        host.SetConfig(cronKey, "");

        if (string.IsNullOrWhiteSpace(input))
        {
            host.ShowThought(T("CronCanceledTitle"), string.Format(T("CronCanceledContent"), scriptName));
            LogToFile($"取消定时: {scriptName}");
            return;
        }

        // 第二步：检测脚本是否需要参数，需要则弹出参数输入
        var scriptPath = Path.Combine(_scriptsDir, scriptName);
        var needsArgs = false;
        try
        {
            var ext = Path.GetExtension(scriptName).ToLowerInvariant();
            var head = await File.ReadAllTextAsync(scriptPath);
            needsArgs = DetectNeedsArgs(ext, head) || head.Contains("# args:") ||
                        head.Contains(":: args:") || head.Contains("rem args:");
        }
        catch { }

        var cronArgs = currentArgs;
        if (needsArgs)
        {
            var argsInput = await host.ShowInputDialog(
                T("CronArgsTitle"),
                T("CronArgsMessage"),
                currentArgs);
            if (argsInput == null) return; // 用户取消
            cronArgs = argsInput;
        }

        try
        {
            var trimmed = input.Trim();
            var callback = CreateCronCallback(scriptName, cronKey, host);

            var m = Regex.Match(trimmed, @"^(\d+)\s*([sm])$");
            if (m.Success)
            {
                var num = int.Parse(m.Groups[1].Value);
                var seconds = m.Groups[2].Value == "m" ? num * 60 : num;
                Scheduler.RegisterInterval(cronKey, seconds, callback, $"⏰ {scriptName}");
                host.ShowThought(T("CronSetTitle"),
                    string.Format(T("CronIntervalSetContent"), scriptName,
                        string.Format(m.Groups[2].Value == "m" ? T("MinutesFormat") : T("SecondsFormat"), num)) +
                    (cronArgs.Length > 0 ? string.Format(T("ArgsSuffix"), cronArgs) : ""));
            }
            else
            {
                Scheduler.Register(cronKey, trimmed, callback, $"⏰ {scriptName}");
                host.ShowThought(T("CronSetTitle"),
                    string.Format(T("CronSetContent"), scriptName, trimmed) +
                    (cronArgs.Length > 0 ? string.Format(T("ArgsSuffix"), cronArgs) : ""));
            }

            host.SetConfig(cronKey, input);
            host.SetConfig(argsKey, cronArgs);
            LogToFile($"设置定时: {scriptName} [{trimmed}] args={cronArgs}");
        }
        catch (Exception ex)
        {
            host.ShowThought(T("CronFormatErrorTitle"),
                string.Format(T("CronFormatErrorContent"), input, ex.Message));
            LogError($"定时设置失败: {input}", ex);
        }
    }

    /// <summary>启动时恢复所有已保存的定时任务</summary>
    private void RestoreCronJobs(IPluginHost host)
    {
        try
        {
            foreach (var file in EnumerateScriptFiles())
            {
                var name = Path.GetFileName(file);
                var cronKey = "cron_" + name;
                var cronExpr = host.GetConfig(cronKey);
                if (string.IsNullOrEmpty(cronExpr)) continue;

                // 恢复时判断是 cron 还是间隔
                var m = Regex.Match(cronExpr.Trim(), @"^(\d+)\s*([sm])$");
                if (m.Success)
                {
                    var num = int.Parse(m.Groups[1].Value);
                    var seconds = m.Groups[2].Value == "m" ? num * 60 : num;
                    Scheduler.RegisterInterval(cronKey, seconds,
                        CreateCronCallback(name, cronKey, host), $"⏰ {name}");
                }
                else
                {
                    Scheduler.Register(cronKey, cronExpr,
                        CreateCronCallback(name, cronKey, host), $"⏰ {name}");
                }

                LogToFile($"恢复定时任务: {name} [{cronExpr}]");
            }
        }
        catch (Exception ex)
        {
            LogError("恢复定时任务失败", ex);
        }
    }

    // ──────────────────────────────────────────────
    //  工具方法
    // ──────────────────────────────────────────────

    private void LogToFile(string message) =>
        Logger.Info(message);

    private void LogWarn(string message) =>
        Logger.Warn(message);

    private void LogError(string message, Exception? ex = null) =>
        Logger.Error(message, ex);

    /// <summary>中间截断文件名：保留开头 + 扩展名，中间 ...</summary>
    private static string TruncateMiddle(string name, int maxLen)
    {
        if (name.Length <= maxLen) return name;
        var ext = Path.GetExtension(name);
        var body = Path.GetFileNameWithoutExtension(name);
        var keepFront = Math.Max(6, maxLen - ext.Length - 5); // 留空间给 ... + 尾部
        var keepTail = Math.Max(3, maxLen - keepFront - 3);
        if (keepFront + keepTail + 3 >= body.Length)
            return name; // 截断后不比原来短就不截
        return body[..keepFront] + "..." + body[^keepTail..] + ext;
    }

    /// <summary>截断文本（添加省略提示）</summary>
    private string Truncate(string text, int maxLen)
    {
        if (text.Length <= maxLen) return text;
        return text[..maxLen] +
            string.Format(T("TruncateSuffix"), text.Length, maxLen);
    }
}
