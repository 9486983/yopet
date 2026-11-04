using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using TKCode.Arc;
using TKCode.Arc.DesignTokens;
using TKCode.Arc.Icons.Lucide;

namespace yopet.UI.Components;

/// <summary>项目内统一语义的 Arc 按钮。</summary>
public static class ArcActions
{
    public static TKCode.Arc.Button Primary(string text, LucideIconKind? icon = null) =>
        Create(text, ButtonVariant.Primary, ControlSize.Small, icon);

    public static TKCode.Arc.Button Secondary(
        string text,
        ControlSize size = ControlSize.Small,
        LucideIconKind? icon = null) =>
        Create(text, ButtonVariant.Secondary, size, icon);

    public static TKCode.Arc.Button Ghost(string text, LucideIconKind? icon = null) =>
        Create(text, ButtonVariant.Ghost, ControlSize.Small, icon);

    public static TKCode.Arc.Button Danger(string text, LucideIconKind? icon = null) =>
        Create(text, ButtonVariant.Danger, ControlSize.Small, icon);

    private static TKCode.Arc.Button Create(
        string text,
        ButtonVariant variant,
        ControlSize size = ControlSize.Small,
        LucideIconKind? icon = null)
    {
        var button = new TKCode.Arc.Button
        {
            Variant = variant,
            Size = size,
        };

        if (icon is { } kind)
        {
            button.Content = CreateContent(button, text, kind, variant);
            AutomationProperties.SetName(button, text);
        }
        else
        {
            button.Content = text;
        }

        if (variant == ButtonVariant.Primary)
        {
            button.AttachedToVisualTree += (_, _) => ApplyPrimary(button);
            ApplyPrimary(button);
        }

        return button;
    }

    private static Control CreateContent(
        TKCode.Arc.Button button,
        string text,
        LucideIconKind icon,
        ButtonVariant variant)
    {
        var iconView = new LucideIcon
        {
            Kind = icon,
            Size = 14,
            VerticalAlignment = VerticalAlignment.Center,
        };

        if (variant == ButtonVariant.Primary)
        {
            iconView.Bind(
                ArcGlyph.ForegroundProperty,
                new Binding(nameof(TKCode.Arc.Button.Foreground)) { Source = button });
        }
        else if (variant == ButtonVariant.Danger)
        {
            ArcTokens.Bind(iconView, ArcGlyph.ForegroundProperty, ArcDesignTokenKind.Danger);
        }
        else
        {
            iconView.Bind(
                ArcGlyph.ForegroundProperty,
                new Binding(nameof(TKCode.Arc.Button.Foreground)) { Source = button });
        }

        var label = new TextBlock
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
        };
        label.Bind(
            TextBlock.ForegroundProperty,
            new Binding(nameof(TKCode.Arc.Button.Foreground)) { Source = button });

        return new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { iconView, label },
        };
    }

    public static void ApplyPrimary(TKCode.Arc.Button button)
    {
        var theme = ArcUi.CurrentTheme;
        button.Background = new SolidColorBrush(theme.PreviewAccent);
        button.Foreground = new SolidColorBrush(theme.OnAccent);
    }
}
