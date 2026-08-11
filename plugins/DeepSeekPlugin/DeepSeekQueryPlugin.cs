using yopet.Sdk;
using System.Text.Json;
using Lang.Avalonia;

namespace DeepSeekPlugin;

/// <summary>
/// DeepSeek 余额查询 &amp; 缓存测试插件
/// 配置在设置页中管理（API Key、定时查询间隔）
/// </summary>
[Plugin("DeepSeek 查询", Version = "2.0.0", Description = "查询 DeepSeek API 余额、用量和缓存命中率，支持定时自动查询")]
public class DeepSeekQueryPlugin : PluginBase
{
    private const string KeyApiKey = "deepseek_api_key";
    private const string KeyInterval = "deepseek_interval";
    private const string KeyAutoQuery = "deepseek_auto_query";
    private const string KeySummaryUrl = "deepseek_summary_url";
    private const string BalanceUrl = "https://api.deepseek.com/user/balance";
    private const string ChatUrl = "https://api.deepseek.com/v1/chat/completions";

    private IPluginHost? _host;
    private PeriodicTimer? _timer;
    private CancellationTokenSource? _timerCts;

    /// <summary>取当前语言的 DeepSeek 词条</summary>
    private static string T(string key) =>
        I18nManager.Instance.GetResource($"Localization.DeepSeekPlugin.{key}");

    public override string Name => T("Name");
    public override string Description => T("Description");

    public override async Task InitializeAsync(IPluginHost host)
    {
        _host = host;
        await base.InitializeAsync(host);

        // ── 注册配置定义 ──
        host.RegisterConfig(new PluginConfigSection
        {
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
            Name = T("SettingsAction"),
            Emoji = "⚙️",
            Description = T("SettingsActionDesc"),
            Group = T("Group"),
            Target = ActionTarget.ContextMenu,
            Callback = () =>
            {
                host.ShowConfigDialog("DeepSeek API");
                return Task.CompletedTask;
            },
        });

        host.RegisterAction(new PluginAction
        {
            Name = T("QueryBalance"),
            Emoji = "💰",
            Description = T("QueryBalanceDesc"),
            Group = T("Group"),
            Target = ActionTarget.ContextMenu,
            Callback = async () => await QueryBalance(),
        });

        // ── 启动定时查询（如果已启用） ──
        RestartTimerIfNeeded();

        host.Log("DeepSeek 查询插件 v2.0 已加载");
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
        StopTimer();

        if (!IsAutoQueryEnabled) return;
        if (_host == null) return;

        var interval = GetQueryIntervalMinutes();
        _timerCts = new CancellationTokenSource();
        var ct = _timerCts.Token;
        _timer = new PeriodicTimer(TimeSpan.FromMinutes(interval));

        _ = Task.Run(async () =>
        {
            try
            {
                while (await _timer.WaitForNextTickAsync(ct))
                {
                    if (string.IsNullOrEmpty(GetApiKey())) continue;
                    await QueryBalance(silent: true);
                    if (!IsAutoQueryEnabled) break;
                }
            }
            catch (OperationCanceledException) { }
        }, ct);
    }

    private void StopTimer()
    {
        _timerCts?.Cancel();
        _timerCts?.Dispose();
        _timerCts = null;
        _timer?.Dispose();
        _timer = null;
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
        StopTimer();
        return base.CleanupAsync();
    }
}
