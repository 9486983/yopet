using Avalonia;
using TKCode.Arc;
using TKCode.Arc.Fonts;
using TKCode.Arc.Localization;

namespace yopet.UI;

/// <summary>Arc 主题、字体和语言的唯一注册入口。</summary>
public static class ArcUi
{
    public static AppBuilder UseYopetArc(this AppBuilder builder) =>
        builder
            .WithArcFonts()
            .UseArc(arc => arc
                .UseArcControls()
                .UseLanguages(LanguageTags.EnUS,
                    [LanguageTags.EnUS, LanguageTags.ZhCN, LanguageTags.ZhTW])
                .WithTheme(theme => theme
                    .Appearance(ArcAppearance.System)
                    .Accent(ArcAccentPreset.Neutral)));

    public static void ApplyAppearance(bool isDark) =>
        ArcTheme.Apply(theme => theme.Appearance(
            isDark ? ArcAppearance.Dark : ArcAppearance.Light));

    public static void ApplyLanguage(string? cultureName)
    {
        var language = cultureName?.StartsWith("zh-TW", StringComparison.OrdinalIgnoreCase) == true
            || cultureName?.StartsWith("zh-HK", StringComparison.OrdinalIgnoreCase) == true
            ? LanguageTags.ZhTW
            : cultureName?.StartsWith("zh", StringComparison.OrdinalIgnoreCase) == true
                ? LanguageTags.ZhCN
                : LanguageTags.EnUS;

        ArcLanguage.Set(language);
    }
}
