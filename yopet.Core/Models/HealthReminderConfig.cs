namespace yopet.Core.Models;

/// <summary>健康提醒配置</summary>
public class HealthReminderConfig
{
    public bool Enabled { get; set; } = true;
    public int SitIntervalMinutes { get; set; } = 55;
    public int EyeIntervalMinutes { get; set; } = 25;
    public int DrinkIntervalMinutes { get; set; } = 40;
}
