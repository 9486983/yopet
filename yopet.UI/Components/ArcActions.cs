using Avalonia.Media;
using TKCode.Arc;

namespace yopet.UI.Components;

/// <summary>项目内统一语义的 Arc 按钮。</summary>
public static class ArcActions
{
    public static TKCode.Arc.Button Primary(string text) =>
        Create(text, ButtonVariant.Primary);

    public static TKCode.Arc.Button Secondary(string text, ControlSize size = ControlSize.Small) =>
        Create(text, ButtonVariant.Secondary, size);

    public static TKCode.Arc.Button Ghost(string text) =>
        Create(text, ButtonVariant.Ghost);

    public static TKCode.Arc.Button Danger(string text) =>
        Create(text, ButtonVariant.Danger);

    private static TKCode.Arc.Button Create(string text, ButtonVariant variant, ControlSize size = ControlSize.Small)
    {
        var button = new TKCode.Arc.Button
        {
            Content = text,
            Variant = variant,
            Size = size,
        };

        if (variant == ButtonVariant.Primary)
        {
            button.AttachedToVisualTree += (_, _) => ApplyPrimary(button);
            ApplyPrimary(button);
        }

        return button;
    }

    public static void ApplyPrimary(TKCode.Arc.Button button)
    {
        var theme = ArcUi.CurrentTheme;
        button.Background = new SolidColorBrush(theme.PreviewAccent);
        button.Foreground = new SolidColorBrush(theme.OnAccent);
    }
}
