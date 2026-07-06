namespace yopet.Core.Models;

/// <summary>应用配置</summary>
public class AppConfig
{
    public double WindowWidth { get; set; } = 420;
    public double WindowHeight { get; set; } = 768;
    public string PetName { get; set; } = "小宠";
    public string CurrentPetId { get; set; } = "petdex:kirby";
    public double PetWindowX { get; set; } = 1200;
    public double PetWindowY { get; set; } = 100;
    public bool IsDarkTheme { get; set; } = true;
    public bool EnableAutoStart { get; set; }
    public double AnimFrameDurationMs { get; set; } = 100.0;
    public HealthReminderConfig HealthReminder { get; set; } = new();
    public List<PetActionConfig> PetActions { get; set; } = new();

    /// <summary>插件自定义配置存储</summary>
    public Dictionary<string, string> PluginValues { get; set; } = new();

    /// <summary>已激活的默认拖放操作名称（持久化，重启恢复）</summary>
    public string? ActivatedFileActionName { get; set; }
}
