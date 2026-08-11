using Lang.Avalonia;
using yopet.Core.Models;
using yopet.Sdk;

namespace HealthReminder;

[Plugin("健康提醒", Version = "1.0.0", Description = "久坐/用眼/喝水定时提醒，配置请在右键菜单中打开")]
public class HealthReminderPlugin : PluginBase
{
    private const string KeyEnabled = "hr_enabled";
    private const string KeySit = "hr_sit";
    private const string KeyEye = "hr_eye";
    private const string KeyDrink = "hr_drink";

    private IPluginHost? _host;
    private CancellationTokenSource? _cts;

    /// <summary>取当前语言的健康提醒词条</summary>
    private static string T(string key) =>
        I18nManager.Instance.GetResource($"Localization.HealthReminder.{key}");

    private static readonly string[] SitMessageKeys = ["SitMsg1", "SitMsg2", "SitMsg3", "SitMsg4"];
    private static readonly string[] EyeMessageKeys = ["EyeMsg1", "EyeMsg2", "EyeMsg3", "EyeMsg4"];
    private static readonly string[] DrinkMessageKeys = ["DrinkMsg1", "DrinkMsg2", "DrinkMsg3", "DrinkMsg4"];

    private DateTime _lastSit = DateTime.MinValue;
    private DateTime _lastEye = DateTime.MinValue;
    private DateTime _lastDrink = DateTime.MinValue;
    private DateTime _lastActiveDate = DateTime.Today;

    public override string Name => T("Name");

    public override async Task InitializeAsync(IPluginHost host)
    {
        _host = host;
        await base.InitializeAsync(host);

        // 注册配置
        host.RegisterConfig(new PluginConfigSection
        {
            Key = "health_reminder",
            Title = T("Name"),
            Emoji = "🧘",
            Fields = new()
            {
                new()
                {
                    Key = KeyEnabled, Label = T("EnabledLabel"),
                    Type = PluginConfigFieldType.Boolean,
                    DefaultValue = "true",
                    Description = T("EnabledDesc"),
                },
                new()
                {
                    Key = KeySit, Label = T("SitLabel"),
                    Type = PluginConfigFieldType.Number,
                    DefaultValue = "55", MinValue = 15, MaxValue = 120,
                },
                new()
                {
                    Key = KeyEye, Label = T("EyeLabel"),
                    Type = PluginConfigFieldType.Number,
                    DefaultValue = "25", MinValue = 10, MaxValue = 90,
                },
                new()
                {
                    Key = KeyDrink, Label = T("DrinkLabel"),
                    Type = PluginConfigFieldType.Number,
                    DefaultValue = "40", MinValue = 15, MaxValue = 120,
                },
            },
        }, Name);

        // 设置入口
        host.RegisterAction(new PluginAction
        {
            Name = T("Settings"),
            Emoji = "⚙️",
            Group = T("Group"),
            Display = LocalizedDisplay.Of(name: () => T("Settings"), group: () => T("Group")),
            Target = ActionTarget.ContextMenu,
            Callback = () =>
            {
                host.ShowConfigDialog("health_reminder");
                return Task.CompletedTask;
            },
        });

        // 监听配置变更
        host.ConfigValueChanged += OnConfigChanged;

        // 启动定时器
        _cts = new CancellationTokenSource();
        _ = RunTimerAsync(_cts.Token);

        host.Log("健康提醒插件已加载");
    }

    private void OnConfigChanged(object? sender, string key)
    {
        // 重置所有计时器，使新间隔立即生效
        _lastSit = DateTime.MinValue;
        _lastEye = DateTime.MinValue;
        _lastDrink = DateTime.MinValue;
        _lastActiveDate = DateTime.Today;
    }

    private async Task RunTimerAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(60_000, ct);
                CheckReminders();
            }
            catch (TaskCanceledException) { break; }
            catch { }
        }
    }

    private void CheckReminders()
    {
        if (_host == null) return;
        if (_host.GetConfig(KeyEnabled) == "false") return;

        var now = DateTime.Now;

        // 跨天重置
        if (now.Date > _lastActiveDate)
        {
            _lastActiveDate = now.Date;
            _lastSit = _lastEye = _lastDrink = DateTime.MinValue;
            return;
        }

        var sitMin = int.TryParse(_host.GetConfig(KeySit), out var s) ? s : 55;
        var eyeMin = int.TryParse(_host.GetConfig(KeyEye), out var e) ? e : 25;
        var drinkMin = int.TryParse(_host.GetConfig(KeyDrink), out var d) ? d : 40;

        if ((now - _lastSit).TotalMinutes >= sitMin)
        {
            _lastSit = now;
            _host.ShowThought(T("SitTitle"), T(SitMessageKeys[Random.Shared.Next(SitMessageKeys.Length)]));
        }

        if ((now - _lastEye).TotalMinutes >= eyeMin)
        {
            _lastEye = now;
            _host.ShowThought(T("EyeTitle"), T(EyeMessageKeys[Random.Shared.Next(EyeMessageKeys.Length)]));
        }

        if ((now - _lastDrink).TotalMinutes >= drinkMin)
        {
            _lastDrink = now;
            _host.ShowThought(T("DrinkTitle"), T(DrinkMessageKeys[Random.Shared.Next(DrinkMessageKeys.Length)]));
        }
    }

    public override Task CleanupAsync()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        return base.CleanupAsync();
    }
}
