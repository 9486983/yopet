using System.Globalization;

namespace ProgramSchedulerPlugin;

/// <summary>调度模式</summary>
public enum ScheduleMode
{
    /// <summary>时段内运行：到启动时间拉起，到停止时间关闭</summary>
    Window,

    /// <summary>仅启动一次：到启动时间拉起，不负责关闭</summary>
    Once,
}

/// <summary>一条「程序定时启停」规则</summary>
public sealed class ScheduleRule
{
    private static readonly string[] TimeFormats = { "H:mm", "HH:mm", "H:m", "HH:m" };

    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";
    public string Arguments { get; set; } = "";
    public string WorkingDir { get; set; } = "";
    public ScheduleMode Mode { get; set; } = ScheduleMode.Window;
    public string StartTime { get; set; } = "";
    public string StopTime { get; set; } = "";
    public bool Enabled { get; set; } = true;

    /// <summary>启动时间的每日 cron 表达式（5 字段）</summary>
    public string StartCron => ToDailyCron(StartTime);

    /// <summary>停止时间的每日 cron 表达式（5 字段）</summary>
    public string StopCron => ToDailyCron(StopTime);

    /// <summary>把 "H:mm" / "HH:mm" 规范化为 "HH:mm"，格式非法时返回 null</summary>
    public static string? NormalizeTime(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;
        return TimeOnly.TryParseExact(input.Trim(), TimeFormats,
            CultureInfo.InvariantCulture, DateTimeStyles.None, out var t)
            ? t.ToString("HH:mm")
            : null;
    }

    private static string ToDailyCron(string time)
    {
        return TimeOnly.TryParseExact(time, TimeFormats,
            CultureInfo.InvariantCulture, DateTimeStyles.None, out var t)
            ? $"{t.Minute} {t.Hour} * * *"
            : "";
    }
}
