namespace yopet.Core.Models;

/// <summary>跨程序集事件总线，用于主程序与插件/设置之间的通信</summary>
public static class PetEvents
{
    /// <summary>配置已保存（设置页点击保存时触发）</summary>
    public static event Action? ConfigSaved;

    public static void NotifyConfigSaved() => ConfigSaved?.Invoke();

    /// <summary>主题切换（传 true=深色, false=浅色）</summary>
    public static event Action<bool>? ThemeChanged;

    public static void NotifyThemeChanged(bool isDark) => ThemeChanged?.Invoke(isDark);
}
