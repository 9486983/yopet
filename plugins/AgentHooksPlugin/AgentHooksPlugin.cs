using yopet.Sdk;

namespace AgentHooksPlugin;

/// <summary>
/// Agent Hooks 监测插件 —— 统一管理所有 AI 助手（Reasonix、Claude Code 等）的 Hooks 监测。
/// 每种助手由独立的 HookProvider 实现，可在设置列表中查看和编辑配置。
/// </summary>
[Plugin("Agent 钩子", Version = "1.0.0",
    Description = "统一管理所有 AI 助手的 Hooks 监测：响应内容、命令执行、文件更改等")]
public class AgentHooksPlugin : PluginBase
{
    private readonly List<HookProviderBase> _providers = new();
    private IPluginHost? _host;

    public override string Name => "Agent 钩子";

    public override async Task InitializeAsync(IPluginHost host)
    {
        _host = host;
        await base.InitializeAsync(host);

        // ── 注册所有 Provider ──
        RegisterProvider(new ReasonixHookProvider());
        RegisterProvider(new ClaudeCodeHookProvider());

        // ── 右键菜单 ──
        host.RegisterAction(new PluginAction
        {
            Name = "Agent 设置",
            Emoji = "👾",
            Description = "查看和管理所有 AI 助手的 Hooks 监测",
            Group = "👾 Agent 钩子",
            Target = ActionTarget.ContextMenu,
            Callback = ShowAgentList,
        });

        host.Log($"Agent 钩子插件已加载 ({_providers.Count} 个 Provider)");
    }

    /// <summary>注册一个 Provider，自动配置并启动</summary>
    private void RegisterProvider(HookProviderBase provider)
    {
        _providers.Add(provider);

        // 注册配置定义
        if (_host != null)
        {
            _host.RegisterConfig(provider.ConfigSection, Name);

            // 启动该 Provider
            if (_host.GetConfig($"{provider.ConfigPrefix}_enabled") != "false")
                provider.Start(_host);

            // 监听该 Provider 的配置启用/关闭
            _host.ConfigValueChanged += (_, key) =>
            {
                if (key == $"{provider.ConfigPrefix}_enabled")
                {
                    if (_host.GetConfig(key) != "false")
                        provider.Start(_host);
                    else
                        provider.Stop();
                }
            };
        }
    }

    private ListDialogConfig? _listConfig;

    /// <summary>显示所有 Agent 的列表弹窗</summary>
    private async Task ShowAgentList()
    {
        if (_host == null) return;

        _listConfig = new ListDialogConfig
        {
            Title = "Agent 钩子",
            Emoji = "👾",
            LayoutMode = ListDialogLayoutMode.Table,
            DataSource = BuildAgentList,
            Columns = new()
            {
                new()
                {
                    Key = "label",
                    Header = "Agent",
                    Width = double.NaN,
                },
                new()
                {
                    Key = "status",
                    Header = "状态",
                    Width = 100,
                },
                new()
                {
                    Key = "_actions",
                    Header = "",
                    Width = 130,
                    Type = ListColumnType.Action,
                    RowActions = new()
                    {
                        new()
                        {
                            Emoji = "@toggle_emoji",
                            Label = "@toggle_label",
                            Tooltip = "启动或暂停此 Agent 的监测",
                            Callback = row =>
                            {
                                if (row.TryGetValue("id", out var id))
                                {
                                    var provider = _providers.FirstOrDefault(p => p.Id == id);
                                    if (provider != null)
                                    {
                                        ToggleProvider(provider);
                                        _listConfig?.NotifyDataChanged();
                                    }
                                }
                                return Task.CompletedTask;
                            },
                        },
                        new()
                        {
                            Emoji = "⚙️",
                            Label = "设置",
                            Tooltip = "编辑此 Agent 的详细配置",
                            Callback = row =>
                            {
                                if (row.TryGetValue("id", out var id))
                                {
                                    var provider = _providers.FirstOrDefault(p => p.Id == id);
                                    if (provider != null)
                                        _host?.ShowConfigDialog($"Agent: {provider.Name}");
                                }
                                return Task.CompletedTask;
                            },
                        },
                    },
                },
            },
        };

        await _host.ShowListDialog(_listConfig);
        _host?.StopAnimation();
    }

    /// <summary>构建 Agent 列表数据（DataSource 回调，每次刷新重新获取状态）</summary>
    private Task<List<Dictionary<string, string>>> BuildAgentList()
    {
        var items = _providers.Select(p =>
        {
            var running = p.IsRunning;
            return new Dictionary<string, string>
            {
                ["id"] = p.Id,
                ["label"] = $"{p.Emoji} {p.Name}",
                ["status"] = running ? "🟢 运行中" : "🔴 已暂停",
                ["toggle_emoji"] = running ? "⏹" : "▶️",
                ["toggle_label"] = running ? "暂停" : "启动",
            };
        }).ToList();

        return Task.FromResult(items);
    }

    /// <summary>切换单个 Provider 的启停状态</summary>
    private void ToggleProvider(HookProviderBase provider)
    {
        if (_host == null) return;

        if (provider.IsRunning)
        {
            provider.Stop();
            _host.SetConfig($"{provider.ConfigPrefix}_enabled", "false");
            _host.ShowThought($"⏹️ {provider.Name}", "监测已暂停");
        }
        else
        {
            _host.SetConfig($"{provider.ConfigPrefix}_enabled", "true");
            provider.Start(_host);
            _host.ShowThought($"▶️ {provider.Name}", "监测已启动");
        }
    }

    public override Task CleanupAsync()
    {
        foreach (var p in _providers)
            p.Dispose();
        _providers.Clear();
        return base.CleanupAsync();
    }
}
