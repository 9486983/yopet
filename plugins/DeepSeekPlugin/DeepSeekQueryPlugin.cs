using yopet.Sdk;
using System.Text.Json;
using yopet.Core.Models;

namespace DeepSeekPlugin;

/// <summary>
/// DeepSeek 余额查询 &amp; 缓存测试插件
/// 配置在设置页中管理（API Key、定时查询间隔）
/// </summary>
[Plugin("DeepSeek 查询", Version = "2.0.0", Description = "查询 DeepSeek API 余额、用量和缓存命中率，支持定时自动查询")]
public class DeepSeekQueryPlugin : PluginBase
{
    public DeepSeekQueryPlugin(PluginContext context)
        : base(context)
    {
    }

    private const string KeyApiKey = "deepseek_api_key";
    private const string KeyInterval = "deepseek_interval";
    private const string KeyAutoQuery = "deepseek_auto_query";
    private const string KeySummaryUrl = "deepseek_summary_url";
    private const string AutoQueryJobId = "deepseek_auto_query";
    private const string BalanceUrl = "https://api.deepseek.com/user/balance";
    private const string ChatUrl = "https://api.deepseek.com/v1/chat/completions";

    private IPluginHost? _host;

    public override string Name => T("Name");
    public override string Description => T("Description");

    public override async Task InitializeAsync(IPluginHost host)
    {
        _host = host;
        await base.InitializeAsync(host);

        // ── 注册配置定义 ──
        host.RegisterConfig(new PluginConfigSection
        {
            Key = "deepseek",
            Title = "DeepSeek API",
            Emoji = "🔑",
            Fields = new()
            {
                new()
                {
                    Key = KeyApiKey,
                    Label = "API Key",
                    Type = PluginConfigFieldType.Password,
                    IsRequired = true,
                    Placeholder = "sk-...",
                    Description = T("ApiKeyDesc"),
                },
                new()
                {
                    Key = KeyAutoQuery,
                    Label = T("AutoQueryLabel"),
                    Type = PluginConfigFieldType.Boolean,
                    DefaultValue = "false",
                    Description = T("AutoQueryDesc"),
                },
                new()
                {
                    Key = KeyInterval,
                    Label = T("IntervalLabel"),
                    Type = PluginConfigFieldType.Number,
                    DefaultValue = "30",
                    MinValue = 1,
                    MaxValue = 180,
                    Description = T("IntervalDesc"),
                },
                new()
                {
                    Key = KeySummaryUrl,
                    Label = T("UsageApiLabel"),
                    Type = PluginConfigFieldType.String,
                    DefaultValue = "https://platform.deepseek.com/api/v0/users/get_user_summary",
                    Description = T("UsageApiDesc"),
                },
            },
        }, Name);

        // ── 监听配置变更 ──
        host.ConfigValueChanged += OnConfigChanged;

        // ── 注册动作 ──

        host.RegisterAction(new PluginAction
        {
            Name = T("QueryBalance"),
            Emoji = "💰",
            Description = T("QueryBalanceDesc"),
            Group = T("Group"),
            Display = LocalizedDisplay.Of(
                name: () => T("QueryBalance"),
                group: () => T("Group"),
                description: () => T("QueryBalanceDesc")),
            Target = ActionTarget.ContextMenu,
            Callback = async () => await QueryBalance(),
        });

        // ── 启动定时查询（如果已启用） ──
        RestartTimerIfNeeded();

        Logger.Info("DeepSeek 查询插件 v2.0 已加载");
    }

    private string? GetApiKey() => _host?.GetConfig(KeyApiKey);
    private bool IsAutoQueryEnabled => _host?.GetConfig(KeyAutoQuery) == "true";
    private int GetQueryIntervalMinutes()
    {
        var val = _host?.GetConfig(KeyInterval);
        return int.TryParse(val, out var m) && m >= 1 ? m : 30;
    }

    // ── 定时查询 ──

    private void RestartTimerIfNeeded()
    {
        if (_host == null) return;

        Scheduler.Unregister(AutoQueryJobId);
        if (!IsAutoQueryEnabled) return;

        var interval = TimeSpan.FromMinutes(GetQueryIntervalMinutes());
        Scheduler.RegisterInterval(
            AutoQueryJobId,
            interval,
            async () =>
            {
                if (!IsAutoQueryEnabled) return;
                if (string.IsNullOrEmpty(GetApiKey())) return;
                await QueryBalance(silent: true);
            },
            "💰 自动查询 DeepSeek 余额",
            runImmediately: false);
    }

    private void OnConfigChanged(object? sender, string key)
    {
        if (key is KeyAutoQuery or KeyInterval)
            RestartTimerIfNeeded();
    }

    // ── 查询余额 ──

    private async Task QueryBalance(bool silent = false)
    {
        if (_host == null) return;
        var apiKey = GetApiKey();

        if (string.IsNullOrEmpty(apiKey))
        {
            if (!silent)
                _host.ShowThought(T("NotConfiguredTitle"), T("NotConfiguredMsg"));
            return;
        }

        try
        {
            using var client = new HttpClient();
            client.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");

            var json = await client.GetStringAsync(BalanceUrl);
            using var doc = JsonDocument.Parse(json);

            var balance = "";
            var isAvailable = false;
            if (doc.RootElement.TryGetProperty("balance_infos", out var infos) &&
                infos.ValueKind == JsonValueKind.Array && infos.GetArrayLength() > 0)
            {
                balance = infos[0].TryGetProperty("total_balance", out var b)
                    ? b.GetString() ?? "0" : "0";
            }
            if (doc.RootElement.TryGetProperty("is_available", out var avail))
                isAvailable = avail.GetBoolean();

            if (string.IsNullOrEmpty(balance) || balance == "0")
            {
                if (!silent)
                    _host.ShowThought(T("NoBalanceTitle"), T("NoBalanceMsg"));
                return;
            }

            var status = isAvailable ? T("StatusAvailable") : T("StatusUnavailable");
            var title = T("BalanceTitle");
            _host.ShowThought(title, string.Format(T("BalanceText"), status, balance));
            _host.ShowReaction("💰");
        }
        catch (HttpRequestException ex)
        {
            if (!silent)
                _host.ShowThought(T("QueryFailedTitle"),
                    string.Format(T("NetworkErrorMsg"), ex.Message));
        }
        catch (Exception ex)
        {
            if (!silent)
                _host.ShowThought(T("QueryFailedTitle"), ex.Message);
        }
    }

    public override Task CleanupAsync()
    {
        if (_host != null)
        {
            Scheduler.Unregister(AutoQueryJobId);
            _host.ConfigValueChanged -= OnConfigChanged;
        }
        return base.CleanupAsync();
    }
}
