using System.Diagnostics;
using System.Text;
using Lang.Avalonia;
using yopet.Core.Models;
using yopet.Sdk;

namespace PythonScriptPlugin;

/// <summary>
/// Python 脚本管理器插件。
///
/// 功能：
///   1. 📜 右键 → "Python 脚本列表" 展示所有已挂载脚本
///   2. 🐍 拖入 .py 文件 → 自动复制到脚本库 → 自动弹出列表 → 自动执行
///   3. ▶  运行脚本 → 检测参数需求 → 有参数则弹出输入框 → 执行并显示输出
///   4. 🗑 删除 / 📝 编辑脚本
///   5. ⚡ 快速运行（右键菜单直接搜索脚本名）
///   6. ⚙️ 可配置 Python 路径、是否自动运行
/// </summary>
[Plugin("Python 脚本管理器", Version = "1.0.0",
    Description = "挂载、管理和运行 Python 脚本，支持拖入自动挂载与执行")]
public class PythonScriptPlugin : PluginBase
{
    /// <summary>取当前语言的插件词条</summary>
    private static string T(string key) => I18nManager.Instance.GetResource($"Localization.PythonScriptPlugin.{key}");

    public override string Name => T("Name");

    private string _scriptsDir = "";
    private string _logDir = "";
    private readonly object _logLock = new();
    private ListDialogConfig? _listConfig;
    private IPluginHost? _host;

    // ──────────────────────────────────────────────
    //  初始化
    // ──────────────────────────────────────────────

    public override async Task InitializeAsync(IPluginHost host)
    {
        _host = host;

        // 脚本存储目录：插件 DLL 同级下的 scripts/
        var pluginDir = Path.GetDirectoryName(GetType().Assembly.Location)!;
        _scriptsDir = Path.Combine(pluginDir, "scripts");
        Directory.CreateDirectory(_scriptsDir);
        _logDir = Path.Combine(pluginDir, "logs");
        Directory.CreateDirectory(_logDir);
        LogToFile("插件初始化完成");
        RestoreCronJobs(host);

        // ── 径向菜单：拖入 .py 文件自动挂载 ──
        host.RegisterAction(new PluginAction
        {
            Name = T("MountPythonScript"),
            Emoji = "🐍",
            Description = T("MountPythonScriptDesc"),
            Target = ActionTarget.RadialMenu,
            AcceptType = ItemType.File,
            FileExtensions = new[] { ".py" },
            CanActivate = true,
            FileCallback = async (paths) => await OnScriptsDropped(paths, host),
        });

        // ── 右键菜单：折叠在「🐍 Python 脚本」分组下 ──
        host.RegisterAction(new PluginAction
        {
            Name = T("ScriptListName"),
            Emoji = "📜",
            Group = T("GroupPythonScripts"),
            Description = T("ScriptListDesc"),
            Target = ActionTarget.ContextMenu,
            Callback = async () => await ShowScriptList(host),
        });

        host.RegisterAction(new PluginAction
        {
            Name = T("QuickRunName"),
            Emoji = "⚡",
            Group = T("GroupPythonScripts"),
            Description = T("QuickRunDesc"),
            Target = ActionTarget.ContextMenu,
            Callback = async () => await QuickRun(host),
        });

        host.RegisterAction(new PluginAction
        {
            Name = T("PluginConfigName"),
            Emoji = "⚙️",
            Group = T("GroupPythonScripts"),
            Description = T("PluginConfigDesc"),
            Target = ActionTarget.ContextMenu,
            Callback = () =>
            {
                host.ShowConfigDialog(T("PythonRunConfig"));
                return Task.CompletedTask;
            },
        });

        // ── 插件配置（演示新 SDK 字段类型） ──
        host.RegisterConfig(new PluginConfigSection
        {
            Title = T("PythonRunConfig"),
            Emoji = "⚙️",
            Groups = new()
            {
                new PluginConfigGroup
                {
                    Title = T("ConfigGroupRuntimeTitle"),
                    Emoji = "🖥️",
                    Description = T("ConfigGroupRuntimeDesc"),
                    FieldKeys = { "python_path", "work_dir" },
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
    //  拖入 .py 文件 → 自动挂载 + 自动执行
    // ──────────────────────────────────────────────

    private async Task OnScriptsDropped(string[] paths, IPluginHost host)
    {
        var mounted = new List<string>();

        foreach (var path in paths)
        {
            var dest = Path.Combine(_scriptsDir, Path.GetFileName(path));
            File.Copy(path, dest, overwrite: true);
            mounted.Add(dest);
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
            Emoji = "🐍",
            LayoutMode = ListDialogLayoutMode.Table,
            DataSource = () =>
            {
                var list = Directory.GetFiles(_scriptsDir, "*.py")
                    .Select(ParseScriptInfo)
                    .OrderBy(d => d.GetValueOrDefault("name", ""))
                    .Select(d =>
                    {
                        if (d.TryGetValue("name", out var name))
                            d["name"] = TruncateMiddle(name, 16);
                        return d;
                    })
                    .ToList();
                return Task.FromResult(list);
            },
            Columns = new()
            {
                new() { Key = "name", Header = T("ColumnScriptHeader"), Width = 110 },
                new() { Key = "desc", Header = T("ColumnDescHeader"), Width = double.NaN },
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
                                            host.Scheduler.Unregister(cronKey);
                                            host.SetConfig(cronKey, "");
                                        }
                                        var delDir = Path.Combine(_scriptsDir, "deleted");
                                        Directory.CreateDirectory(delDir);
                                        var now = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                                        var delName = Path.GetFileNameWithoutExtension(row["file"])
                                            + $"_{now}" + Path.GetExtension(row["file"]);
                                        var dst = Path.Combine(delDir, delName);
                                        if (File.Exists(src)) File.Move(src, dst);
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
        var scripts = Directory.GetFiles(_scriptsDir, "*.py");
        if (scripts.Length == 0)
        {
            host.ShowThought(T("NoScriptsTitle"), T("NoScriptsContent"));
            return;
        }

        var input = await host.ShowInputDialog(
            T("QuickRunDialogTitle"),
            T("QuickRunDialogMessage"));

        if (string.IsNullOrEmpty(input)) return;

        var matched = scripts
            .Select(p => Path.GetFileNameWithoutExtension(p))
            .Where(n => n.Contains(input, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (matched.Count == 0)
        {
            host.ShowThought(T("ScriptNotFoundTitle"), string.Format(T("ScriptNotFoundContent"), input));
            return;
        }

        if (matched.Count == 1)
        {
            await RunScript(matched[0] + ".py", host);
            return;
        }

        // 多个匹配：自动运行第一个
        host.ShowThought(T("MultipleMatchesTitle"),
            string.Format(T("MultipleMatchesContent"), matched.Count, matched[0]));
        await Task.Delay(400);
        await RunScript(matched[0] + ".py", host);
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

        try
        {
            // RunWithAnimation 自动：显示工作动画 + 进度环 + 可取消 + 结束后自动恢复待机
            await host.RunWithAnimation(PetAnimation.Running, async (token) =>
            {
                host.ShowThought(T("RunningTitle"), string.Format(T("RunningContent"), scriptName, args));

                var pythonPath = await ResolvePythonPathAsync(host);
                LogToFile($"解析 Python 路径: {pythonPath ?? "null (未找到)"}");
                if (pythonPath == null)
                {
                    LogToFile("错误: 未找到 Python 可执行文件");
                    host.ShowThought(T("PythonNotFoundTitle"),
                        T("PythonNotFoundContent"));
                    return;
                }

                var cmdLine = $"{pythonPath} \"{scriptPath}\" {args}";
                LogToFile($"完整命令: {cmdLine}");

                var psi = new ProcessStartInfo
                {
                    FileName = pythonPath,
                    Arguments = $"\"{scriptPath}\" {args}",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8,
                };
                psi.EnvironmentVariables["PYTHONIOENCODING"] = "utf-8";

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
            LogToFile($"退出码: {exitCode}");

            if (exitCode == 0)
            {
                var display = Truncate(output, 1500);
                LogToFile($"执行成功, 输出 {output.Length} 字符");
                host.ShowThought(string.Format(T("RunSuccessTitle"), exitCode),
                    string.Format(T("RunSuccessContent"), scriptName, display));
                host.ShowReaction("✅", PetAnimation.Jump);
            }
            else
            {
                var errMsg = Truncate(error.Length > 0 ? error : output, 1000);
                var stderrPreview = error.Length > 0 ? Truncate(error, 500) : output.Length > 0 ? Truncate(output, 500) : "(无输出)";
                LogToFile($"执行失败 退出码={exitCode}\n  stderr: {stderrPreview}");
                host.ShowThought(string.Format(T("RunFailedTitle"), exitCode),
                    string.Format(T("RunFailedContent"), scriptName, errMsg));
                host.ShowReaction("❌", PetAnimation.Failed);
            }
        }
        catch (OperationCanceledException)
        {
            LogToFile("执行被用户取消");
            host.ShowThought(T("CanceledTitle"), string.Format(T("CanceledContent"), scriptName));
            host.ShowReaction("⏹️", PetAnimation.Wave);
            // 如果是定时任务触发的，自动暂停
            var cronKey = "cron_" + scriptName;
            if (!string.IsNullOrEmpty(host.GetConfig(cronKey)))
            {
                host.Scheduler.Pause(cronKey);
                LogToFile($"定时任务已自动暂停: {cronKey}");
            }
        }
        catch (Exception ex)
        {
            LogToFile($"异常: {ex.GetType().Name}: {ex.Message}");
            host.ShowThought(T("RunErrorTitle"),
                string.Format(T("RunErrorContent"), scriptName, ex.Message));
            host.ShowReaction("❌", PetAnimation.Failed);
        }
    }

    // ──────────────────────────────────────────────
    //  参数检测
    // ──────────────────────────────────────────────

    /// <summary>
    /// 检测脚本是否需要参数，如需则弹出输入框。
    /// 返回参数串（文本）；null 表示用户取消。
    /// </summary>
    private static async Task<string?> DetectAndPromptArgs(
        string scriptPath, IPluginHost host)
    {
        try
        {
            var lines = await File.ReadAllLinesAsync(scriptPath);
            var scanLines = lines.Take(100).ToArray();

            // ① 显式声明 # args: / # args= → 预填为初始值，仍然弹出输入框供用户修改
            string? declaredArgs = null;
            foreach (var line in scanLines)
            {
                var trim = line.Trim();
                if (trim.StartsWith("# args:", StringComparison.OrdinalIgnoreCase))
                {
                    declaredArgs = trim["# args:".Length..].Trim();
                    break;
                }
                if (trim.StartsWith("# args=", StringComparison.OrdinalIgnoreCase))
                {
                    declaredArgs = trim["# args=".Length..].Trim();
                    break;
                }

                if (!trim.StartsWith("#") && !string.IsNullOrEmpty(trim))
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

            // ② 自动检测 —— 脚本中使用 argparse / sys.argv / input()
            var fullText = string.Join("\n", scanLines);
            var needsArgs =
                fullText.Contains("import argparse") ||
                fullText.Contains("from argparse") ||
                fullText.Contains("sys.argv") ||
                fullText.Contains("input(");

            if (needsArgs)
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

    // ──────────────────────────────────────────────
    //  脚本信息解析
    // ──────────────────────────────────────────────

    private static Dictionary<string, string> ParseScriptInfo(string filePath)
    {
        var fi = new FileInfo(filePath);
        var name = fi.Name;
        var desc = "";

        try
        {
            var lines = File.ReadLines(filePath).Take(30).ToArray();
            var inDocstring = false;
            var docQuote = "";

            foreach (var line in lines)
            {
                var trim = line.Trim();

                // # description: / # desc: 头部注释
                if (trim.StartsWith("# description:", StringComparison.OrdinalIgnoreCase))
                {
                    desc = trim["# description:".Length..].Trim();
                    continue;
                }
                if (trim.StartsWith("# desc:", StringComparison.OrdinalIgnoreCase))
                {
                    desc = trim["# desc:".Length..].Trim();
                    continue;
                }

                // 模块文档字符串 """...""" 或 '''...'''
                if (!inDocstring &&
                    (trim.StartsWith("\"\"\"") || trim.StartsWith("'''")))
                {
                    docQuote = trim[..3];
                    var rest = trim[3..].Trim();
                    var endIdx = rest.IndexOf(docQuote);
                    if (endIdx >= 0)
                    {
                        desc = rest[..endIdx].Trim();
                        break;
                    }
                    inDocstring = true;
                    desc = rest;
                    continue;
                }

                if (inDocstring)
                {
                    var endIdx = trim.IndexOf(docQuote);
                    if (endIdx >= 0)
                    {
                        desc = (desc + " " + trim[..endIdx].Trim()).Trim();
                        break;
                    }
                    desc = (desc + " " + trim).Trim();
                    continue;
                }

                // 遇到非注释/非空代码行即停止
                if (!trim.StartsWith("#") && !inDocstring && !string.IsNullOrEmpty(trim))
                    break;
            }
        }
        catch
        {
            // 忽略解析错误
        }

        if (string.IsNullOrEmpty(desc))
            desc = Path.GetFileNameWithoutExtension(name);

        var mtime = fi.LastWriteTime.ToString("yyyy-MM-dd HH:mm");

        return new()
        {
            ["name"] = name, // 显示用（会被 TruncateMiddle 截断）
            ["file"] = name,        // 操作用（原始文件名）
            ["desc"] = desc,
            ["mtime"] = mtime,
        };
    }

    // ──────────────────────────────────────────────
    //  工具方法
    // ──────────────────────────────────────────────

    /// <summary>
    /// 解析 Python 可执行文件路径。
    /// 优先使用配置值；其次尝试 py 启动器；最后找 python。
    /// 均不可用时返回 null。
    /// </summary>
    private static async Task<string?> ResolvePythonPathAsync(IPluginHost host)
    {
        // 1. 用户配置的路径
        var cfg = host.GetConfig("python_path");
        if (!string.IsNullOrWhiteSpace(cfg) && File.Exists(cfg))
            return cfg;

        // 2. 尝试 py 启动器（Windows Python Launcher）
        try
        {
            using var test = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "py",
                    Arguments = "-3 --version",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                }
            };
            test.Start();
            using var pyCts = new CancellationTokenSource(3000);
            await test.WaitForExitAsync(pyCts.Token);
            if (test.ExitCode == 0)
                return "py";
        }
        catch { /* py 不可用 */ }

        // 3. 尝试 python
        try
        {
            using var test = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "python",
                    Arguments = "--version",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                }
            };
            test.Start();
            using var pyCts = new CancellationTokenSource(3000);
            await test.WaitForExitAsync(pyCts.Token);
            if (test.ExitCode == 0)
                return "python";
        }
        catch { /* python 不可用 */ }

        // 4. 常见安装路径
        var candidates = new[]
        {
            @"C:\Python312\python.exe",
            @"C:\Python311\python.exe",
            @"C:\Python310\python.exe",
            @"C:\Python39\python.exe",
            @"C:\Python38\python.exe",
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Programs", "Python", "Python312", "python.exe"),
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Programs", "Python", "Python311", "python.exe"),
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Programs", "Python", "Python310", "python.exe"),
        };

        foreach (var p in candidates)
        {
            if (File.Exists(p))
                return p;
        }

        return null;
    }

    /// <summary>创建一个带自动暂停的定时任务回调</summary>
    private Func<Task> CreateCronCallback(string scriptName, string cronKey, IPluginHost host)
    {
        return async () =>
        {
            LogToFile($"定时触发: {scriptName}");
            var argsKey = "cron_args_" + scriptName;
            var savedArgs = host.GetConfig(argsKey) ?? "";
            await RunScriptWithArgs(scriptName, savedArgs, host);

            // 如果脚本执行过程中被取消，自动暂停定时任务
            // （RunScript 内部捕获 OperationCanceledException 后无法感知，这里通过检查状态判断）
        };
    }

    /// <summary>运行脚本（支持传入预置参数）</summary>
    private async Task RunScriptWithArgs(string scriptName, string args, IPluginHost host)
    {
        var scriptPath = Path.Combine(_scriptsDir, scriptName);
        if (!File.Exists(scriptPath))
        {
            host.Logger.Warn<PythonScriptPlugin>($"定时任务脚本不存在: {scriptName}");
            return;
        }
        await RunScript(scriptName, host);
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

        host.Scheduler.Unregister(cronKey);
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
            var head = await File.ReadAllTextAsync(scriptPath);
            needsArgs = head.Contains("import argparse") || head.Contains("sys.argv") || head.Contains("# args:");
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

            var m = System.Text.RegularExpressions.Regex.Match(trimmed, @"^(\d+)\s*([sm])$");
            if (m.Success)
            {
                var num = int.Parse(m.Groups[1].Value);
                var seconds = m.Groups[2].Value == "m" ? num * 60 : num;
                host.Scheduler.RegisterInterval(cronKey, seconds, callback, $"⏰ {scriptName}");
                host.ShowThought(T("CronSetTitle"),
                    string.Format(T("CronIntervalSetContent"), scriptName,
                        string.Format(m.Groups[2].Value == "m" ? T("MinutesFormat") : T("SecondsFormat"), num)) +
                    (cronArgs.Length > 0 ? string.Format(T("ArgsSuffix"), cronArgs) : ""));
            }
            else
            {
                host.Scheduler.Register(cronKey, trimmed, callback, $"⏰ {scriptName}");
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
            LogToFile($"定时设置失败: {input} - {ex.Message}");
        }
    }

    /// <summary>启动时恢复所有已保存的定时任务</summary>
    private void RestoreCronJobs(IPluginHost host)
    {
        try
        {
            foreach (var f in Directory.GetFiles(_scriptsDir, "*.py"))
            {
                var name = Path.GetFileName(f);
                var cronKey = "cron_" + name;
                var cronExpr = host.GetConfig(cronKey);
                if (string.IsNullOrEmpty(cronExpr)) continue;

                // 恢复时判断是 cron 还是间隔
                var m = System.Text.RegularExpressions.Regex.Match(cronExpr.Trim(), @"^(\d+)\s*([sm])$");
                if (m.Success)
                {
                    var num = int.Parse(m.Groups[1].Value);
                    var seconds = m.Groups[2].Value == "m" ? num * 60 : num;
                    host.Scheduler.RegisterInterval(cronKey, seconds,
                        CreateCronCallback(name, cronKey, host), $"⏰ {name}");
                }
                else
                {
                    host.Scheduler.Register(cronKey, cronExpr,
                        CreateCronCallback(name, cronKey, host), $"⏰ {name}");
                }

                LogToFile($"恢复定时任务: {name} [{cronExpr}]");
            }
        }
        catch (Exception ex)
        {
            LogToFile($"恢复定时任务失败: {ex.Message}");
        }
    }

    /// <summary>写入日志文件（{logDir}/yyy-MM-dd.log）</summary>
    private void LogToFile(string message)
    {
        try
        {
            var date = DateTime.Now;
            var logPath = Path.Combine(_logDir, $"{date:yyyy-MM-dd}.log");
            var line = $"[{date:HH:mm:ss}] {message}";
            lock (_logLock)
            {
                File.AppendAllText(logPath, line + Environment.NewLine);
            }
        }
        catch { /* 日志写入失败不影响主流程 */ }
    }

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
    private static string Truncate(string text, int maxLen)
    {
        if (text.Length <= maxLen) return text;
        return text[..maxLen] +
            string.Format(T("TruncateSuffix"), text.Length, maxLen);
    }
}
