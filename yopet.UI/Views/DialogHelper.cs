using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using TKCode.Arc.DesignTokens;
using TKCode.Arc;
using yopet.UI.Components;

namespace yopet.Views;

internal static class DialogHelper
{
    public static TextBlock CreateTitle(string text)
    {
        var title = new TextBlock
        {
            Text = text,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            FontWeight = Avalonia.Media.FontWeight.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
        };
        ArcTokens.Bind(title, TextBlock.ForegroundProperty, ArcDesignTokenKind.Foreground);
        ArcTokens.Bind(title, TextBlock.FontSizeProperty, ArcDesignTokenKind.TypeBase);
        return title;
    }

    public static TextBlock CreateBody(string text)
    {
        var body = new TextBlock
        {
            Text = text,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            MaxHeight = 200,
            Margin = new Thickness(0, 8, 0, 0),
        };
        ArcTokens.Bind(body, TextBlock.ForegroundProperty, ArcDesignTokenKind.TextSecondary);
        ArcTokens.Bind(body, TextBlock.FontSizeProperty, ArcDesignTokenKind.TypeSM);
        return body;
    }

    public static TKCode.Arc.Button CreateButton(string text, int width = 90, bool primary = false)
    {
        var button = primary ? ArcActions.Primary(text) : ArcActions.Secondary(text);
        button.MinWidth = width;
        return button;
    }

    public static void PositionAboveOwner(Window popup, Window owner, int aboveOffset = 300, int dialogWidth = 380)
    {
        const int margin = 10;
        var screen = owner.Screens.ScreenFromWindow(owner);
        if (screen == null) { popup.WindowStartupLocation = WindowStartupLocation.CenterScreen; return; }

        var scrLeft = screen.Bounds.X;
        var scrRight = screen.Bounds.X + screen.Bounds.Width;
        var scrTop = screen.Bounds.Y;
        var scrBottom = screen.Bounds.Y + screen.Bounds.Height;

        var x = owner.Position.X + (int)((owner.Width - dialogWidth) / 2);
        if (x + dialogWidth + margin > scrRight)
        {
            x = owner.Position.X + (int)owner.Width - dialogWidth - margin;
            if (x + dialogWidth + margin > scrRight)
                x = scrRight - dialogWidth - margin;
        }
        if (x < scrLeft + margin)
            x = scrLeft + margin;

        var dlgH = 420;
        var y = owner.Position.Y - aboveOffset;
        if (y < scrTop + margin)
            y = owner.Position.Y + (int)owner.Height + margin;
        if (y + dlgH + margin > scrBottom)
            y = scrBottom - dlgH - margin;

        popup.Position = new PixelPoint(x, y);
    }

    public static T WithGridCol<T>(this T element, int col) where T : Control
    {
        Grid.SetColumn(element, col);
        return element;
    }
}
