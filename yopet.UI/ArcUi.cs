using Avalonia;
using Avalonia.Media;
using TKCode.Arc;
using TKCode.Arc.Fonts;
using TKCode.Arc.Localization;

namespace yopet.UI;

/// <summary>Arc theme, font and language registration.</summary>
public static class ArcUi
{
    private static readonly ArcAccentPreset AccentPreset = ArcAccentPreset.Blue;

    public sealed record ThemeChoice(
        string Id,
        string LabelKey,
        ArcAppearance Appearance,
        ArcAccentPreset Accent,
        Color PreviewSurface,
        Color PreviewAccent,
        Color OnAccent,
        Color? AccentSeed = null);

    public static IReadOnlyList<ThemeChoice> ThemeChoices { get; } =
    [
        new("dark", "Localization.Theme.Dark", ArcAppearance.Dark, ArcAccentPreset.Neutral,
            Color.Parse("#15161A"), Color.Parse("#F5F5F5"), Color.Parse("#15161A")),
        new("light", "Localization.Theme.Light", ArcAppearance.Light, ArcAccentPreset.Neutral,
            Color.Parse("#FFFFFF"), Color.Parse("#18181B"), Color.Parse("#FFFFFF")),
        new("blue", "Localization.Theme.Blue", ArcAppearance.Light, ArcAccentPreset.Blue,
            Color.Parse("#FFFFFF"), Color.Parse("#3B82F6"), Color.Parse("#FFFFFF")),
        new("pink", "Localization.Theme.Pink", ArcAppearance.Light, ArcAccentPreset.Rose,
            Color.Parse("#FFFFFF"), Color.Parse("#F4B6C2"), Color.Parse("#5A2A3A"),
            Color.Parse("#F4B6C2")),
        new("yellow", "Localization.Theme.Yellow", ArcAppearance.Light, ArcAccentPreset.Amber,
            Color.Parse("#FFFFFF"), Color.Parse("#F59E0B"), Color.Parse("#18181B")),
        new("green", "Localization.Theme.Green", ArcAppearance.Light, ArcAccentPreset.Green,
            Color.Parse("#FFFFFF"), Color.Parse("#22C55E"), Color.Parse("#FFFFFF")),
    ];

    public static AppBuilder UseYopetArc(this AppBuilder builder) =>
        builder
            .WithArcFonts()
            .UseArc(arc => arc
                .UseArcControls()
                .UseLanguages(LanguageTags.EnUS,
                    [LanguageTags.EnUS, LanguageTags.ZhCN, LanguageTags.ZhTW])
                .WithTheme(theme => theme
                    .Appearance(ArcAppearance.System)
                    .Accent(AccentPreset)));

    public static ThemeChoice CurrentTheme { get; private set; } = ThemeChoices[0];

    public static ThemeChoice ResolveTheme(string? id) =>
        ThemeChoices.FirstOrDefault(choice =>
            string.Equals(choice.Id, id, StringComparison.OrdinalIgnoreCase))
        ?? ThemeChoices[0];

    public static void ApplyTheme(string? id)
    {
        var choice = ResolveTheme(id);
        CurrentTheme = choice;
        ArcTheme.Apply(theme =>
        {
            var builder = theme.Appearance(choice.Appearance);
            if (choice.AccentSeed is { } seed)
                builder.Accent(seed);
            else
                builder.Accent(choice.Accent);
        });
    }

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
