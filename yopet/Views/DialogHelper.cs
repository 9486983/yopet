using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace yopet.Views;

internal static class DialogHelper
{
    /// <summary>气泡风格统一颜色取值</summary>
    public static Color GetColor(string key, uint fallback)
    {
        if (Application.Current?.TryFindResource(key, out var value) == true && value is Color c)
            return c;
        return Color.Parse($"#{fallback:X8}");
    }

    /// <summary>创建气泡风格的 Window 容器，Content 由调用方提供</summary>
    public static Window CreateBubbleWindow(Control content, int width = 360)
    {
        var overlay = GetColor("BgOverlay", 0xCCF0ECE3);
        var border = GetColor("BorderColor", 0xFFc4b89e);

        return new Window
        {
            Width = width,
            Height = 0,
            SizeToContent = SizeToContent.Height,
            WindowDecorations = WindowDecorations.None,
            CanResize = false,
            ShowInTaskbar = false,
            Background = Brushes.Transparent,
            TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent },
            WindowStartupLocation = WindowStartupLocation.Manual,
            Content = new Border
            {
                Background = new SolidColorBrush(overlay),
                BorderBrush = new SolidColorBrush(border),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Margin = new Thickness(8),
                Padding = new Thickness(14, 8),
                BoxShadow = new BoxShadows(new BoxShadow
                {
                    OffsetX = 0, OffsetY = 2, Blur = 8,
                    Color = Color.Parse("#30000000"),
                }),
                Child = content,
            },
        };
    }

    /// <summary>气泡风格标题：💬 + 粗体文字（灰色，与气泡 Popup 一致）</summary>
    public static Grid CreateTitle(string text)
    {
        return new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*"),
            Children =
            {
                new TextBlock { Text = "💬", FontSize = 12, VerticalAlignment = VerticalAlignment.Center },
                new TextBlock
                {
                    Text = text, FontSize = 10, FontWeight = FontWeight.Bold,
                    Foreground = new SolidColorBrush(Color.Parse("#AAAAAA")),
                    Margin = new Thickness(4, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    TextWrapping = TextWrapping.Wrap,
                }.WithGridCol(1),
            },
        };
    }

    /// <summary>气泡风格正文（TextPrimary 色，与气泡 Popup 一致）</summary>
    public static TextBlock CreateBody(string text)
    {
        var fg = GetColor("TextPrimary", 0xFF794f27);
        return new TextBlock
        {
            Text = text, FontSize = 11,
            Foreground = new SolidColorBrush(fg),
            TextWrapping = TextWrapping.Wrap,
            MaxHeight = 160,
            Margin = new Thickness(0, 4, 0, 0),
        };
    }

    /// <summary>气泡风格按钮（inline 用，免去每次重复构造）</summary>
    public static Button CreateButton(string text, int width = 90, bool primary = false)
    {
        var accent = GetColor("AccentPrimary", 0xFF19c8b9);
        var muted = GetColor("TextMuted", 0xFF9f927d);

        return new Button
        {
            Content = text,
            Width = width,
            Height = 34,
            CornerRadius = new CornerRadius(8),
            FontSize = 13,
            Padding = new Thickness(6, 0),
            HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Center,
            Cursor = new Cursor(StandardCursorType.Hand),
            Background = primary ? new SolidColorBrush(accent) : Brushes.Transparent,
            Foreground = primary ? Brushes.White : new SolidColorBrush(muted),
            BorderThickness = primary ? new Thickness(0) : new Thickness(1),
            BorderBrush = primary ? null : new SolidColorBrush(muted),
        };
    }

    /// <summary>定位气泡到宠物窗口上方</summary>
    public static void PositionAboveOwner(Window popup, Window owner, int aboveOffset = 300, int dialogWidth = 380)
    {
        const int margin = 10;
        var screen = owner.Screens.ScreenFromWindow(owner);
        if (screen == null) { popup.WindowStartupLocation = WindowStartupLocation.CenterScreen; return; }

        var scrLeft = screen.Bounds.X;
        var scrRight = screen.Bounds.X + screen.Bounds.Width;
        var scrTop = screen.Bounds.Y;
        var scrBottom = screen.Bounds.Y + screen.Bounds.Height;

        // 水平：居中 → 右对齐宠物 → 贴屏幕
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

    /// <summary>附加属性：设置 Grid.Column</summary>
    public static T WithGridCol<T>(this T element, int col) where T : Control
    {
        Grid.SetColumn(element, col);
        return element;
    }
}
