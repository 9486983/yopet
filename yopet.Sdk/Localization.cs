namespace yopet.Sdk;

/// <summary>
/// 进程级本地化入口 —— 由主程序启动时注入。
/// 供无法使用构造函数注入的 UI / 静态类使用；插件请优先使用 <see cref="PluginBase.T"/>。
/// </summary>
public static class Localization
{
    public static ILocalizer Current { get; set; } = NullLocalizer.Instance;

    public static string T(string key) => Current.Get(key);
}
