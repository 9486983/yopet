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

    private static readonly string[] SitMessages =
    [
        "起来活动一下吧～坐太久尾巴要长在椅子上啦！🐱",
        "站起来伸个懒腰～你现在的姿势像一只煮熟的小虾米🦐",
        "该起来走走啦！再坐下去椅子都要长在你身上了🪑",
        "活动时间到！让血液循环起来，不然腿要变成果冻了🦵✨",
    ];

    private static readonly string[] EyeMessages =
    [
        "看看窗外吧～一直盯着屏幕，眼睛会变成熊猫眼的🐼",
        "闭眼休息10秒？你的眼睛已经为你工作很久了哦～👀💤",
        "远方看一看～屏幕虽好看，眼睛更重要呀🌈",
        "眨眼运动时间！盯着屏幕太久眼睛都忘记怎么眨啦👁️",
    ];

    private static readonly string[] DrinkMessages =
    [
        "喝水时间到！你的身体正在喊「我好渴啊～」💧",
        "该喝水啦！皮肤的水分余额已不足，请及时充值💦",
        "吨吨吨～喝口水再继续吧！你认真的样子很可爱，但也要记得喝水🥤",
        "水！你现在需要水！不然就要变成小鱼干了🐟",
    ];

    private DateTime _lastSit = DateTime.MinValue;
    private DateTime _lastEye = DateTime.MinValue;
    private DateTime _lastDrink = DateTime.MinValue;
    private DateTime _lastActiveDate = DateTime.Today;

    public override string Name => "健康提醒";

    public override async Task InitializeAsync(IPluginHost host)
    {
        _host = host;
        await base.InitializeAsync(host);

        // 注册配置
        host.RegisterConfig(new PluginConfigSection
        {
            Title = "健康提醒",
            Emoji = "🧘",
            Fields = new()
            {
                new()
                {
                    Key = KeyEnabled, Label = "启用健康提醒",
                    Type = PluginConfigFieldType.Boolean,
                    DefaultValue = "true",
                    Description = "开启后将按设定间隔自动弹出健康提醒",
                },
                new()
                {
                    Key = KeySit, Label = "久坐间隔（分钟）",
                    Type = PluginConfigFieldType.Number,
                    DefaultValue = "55", MinValue = 15, MaxValue = 120,
                },
                new()
                {
                    Key = KeyEye, Label = "用眼间隔（分钟）",
                    Type = PluginConfigFieldType.Number,
                    DefaultValue = "25", MinValue = 10, MaxValue = 90,
                },
                new()
                {
                    Key = KeyDrink, Label = "喝水间隔（分钟）",
                    Type = PluginConfigFieldType.Number,
                    DefaultValue = "40", MinValue = 15, MaxValue = 120,
                },
            },
        }, Name);

        // 设置入口
        host.RegisterAction(new PluginAction
        {
            Name = "设置",
            Emoji = "⚙️",
            Group = "🧘 健康提醒",
            Target = ActionTarget.ContextMenu,
            Callback = () =>
            {
                host.ShowConfigDialog("健康提醒");
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
            _host.ShowThought("🧘 久坐提醒", SitMessages[Random.Shared.Next(SitMessages.Length)]);
        }

        if ((now - _lastEye).TotalMinutes >= eyeMin)
        {
            _lastEye = now;
            _host.ShowThought("👀 用眼提醒", EyeMessages[Random.Shared.Next(EyeMessages.Length)]);
        }

        if ((now - _lastDrink).TotalMinutes >= drinkMin)
        {
            _lastDrink = now;
            _host.ShowThought("💧 喝水提醒", DrinkMessages[Random.Shared.Next(DrinkMessages.Length)]);
        }
    }

    public override Task CleanupAsync()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        return base.CleanupAsync();
    }
}
