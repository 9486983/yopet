using Microsoft.Win32;
using yopet.Core.Interfaces;
using yopet.Core.Models;

namespace yopet.Services;

/// <summary>
/// 健康提醒服务 —— 久坐/用眼/喝水定时提醒
/// 自动检测锁屏重置，第二天开屏继续
/// </summary>
public class HealthReminderService
{
    private readonly IConfigService _configService;
    private readonly IDispatcherService _dispatcher;
    private readonly System.Timers.Timer _tickTimer;
    private DateTime _lastSitReminder = DateTime.MinValue;
    private DateTime _lastEyeReminder = DateTime.MinValue;
    private DateTime _lastDrinkReminder = DateTime.MinValue;
    private DateTime _lastActiveDate = DateTime.Today;
    private bool _initialized;

    /// <summary>提醒事件（ViewModel 订阅）</summary>
    public event Action<string, string>? ReminderTriggered; // (type, message)

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

    public HealthReminderService(IConfigService configService, IDispatcherService dispatcher)
    {
        _configService = configService;
        _dispatcher = dispatcher;

        // 锁屏/开屏检测（仅 Windows）
        if (OperatingSystem.IsWindows())
        {
            try { SystemEvents.SessionSwitch += OnSessionSwitch; }
            catch { }
        }

        _tickTimer = new System.Timers.Timer(60_000); // 每分钟检查一次
        _tickTimer.Elapsed += OnTick;
        _tickTimer.AutoReset = true;
    }

    public void Start()
    {
        if (_initialized) return;
        _initialized = true;
        _lastActiveDate = DateTime.Today;
        ResetTimers();
        _tickTimer.Start();
    }

    public void Stop()
    {
        _tickTimer.Stop();
    }

    private void OnTick(object? sender, System.Timers.ElapsedEventArgs e)
    {
        var cfg = _configService.Config.HealthReminder;
        if (!cfg.Enabled) return;

        var now = DateTime.Now;

        // 跨天重置（新的一天重新计时）
        if (now.Date > _lastActiveDate)
        {
            _lastActiveDate = now.Date;
            ResetTimers();
            return;
        }

        // 久坐提醒
        if ((now - _lastSitReminder).TotalMinutes >= cfg.SitIntervalMinutes)
        {
            _lastSitReminder = now;
            FireReminder("sit", SitMessages[Random.Shared.Next(SitMessages.Length)]);
        }

        // 用眼提醒
        if ((now - _lastEyeReminder).TotalMinutes >= cfg.EyeIntervalMinutes)
        {
            _lastEyeReminder = now;
            FireReminder("eye", EyeMessages[Random.Shared.Next(EyeMessages.Length)]);
        }

        // 喝水提醒
        if ((now - _lastDrinkReminder).TotalMinutes >= cfg.DrinkIntervalMinutes)
        {
            _lastDrinkReminder = now;
            FireReminder("drink", DrinkMessages[Random.Shared.Next(DrinkMessages.Length)]);
        }
    }

    private void FireReminder(string type, string message)
    {
        _dispatcher.Post(() => ReminderTriggered?.Invoke(type, message));
    }

    /// <summary>锁屏时重置所有计时器</summary>
#pragma warning disable CA1416 // Windows-only API
    private void OnSessionSwitch(object? sender, SessionSwitchEventArgs e)
    {
        if (e.Reason == SessionSwitchReason.SessionLock)
        {
            ResetTimers();
            _lastActiveDate = DateTime.Today;
        }
    }
#pragma warning restore CA1416

    private void ResetTimers()
    {
        _lastSitReminder = DateTime.MinValue;
        _lastEyeReminder = DateTime.MinValue;
        _lastDrinkReminder = DateTime.MinValue;
    }
}
