using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;

namespace yopet.Controls;

/// <summary>
/// 气泡风格容器 —— 与消息气泡完全相同的视觉样式。
/// 通过 ShowPopup 在指定目标上方弹出。
/// </summary>
public class BubbleDialog : ContentControl
{
    private readonly Border _border;

    public BubbleDialog()
    {
        _border = new Border
        {
            Background = GetBrush("BgOverlay", 0xCCF0ECE3),
            BorderBrush = GetBrush("BorderColor", 0xFFc4b89e),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(14, 8),
            BoxShadow = new BoxShadows(new BoxShadow
            {
                OffsetX = 0, OffsetY = 2, Blur = 8,
                Color = Color.Parse("#30000000"),
            }),
        };
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ContentProperty)
            _border.Child = base.Content as Control;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        _border.Measure(availableSize);
        return _border.DesiredSize;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        _border.Arrange(new Rect(finalSize));
        return finalSize;
    }

    public static Popup ShowPopup(Control target, Control innerContent)
    {
        var dialog = new BubbleDialog { Content = innerContent };
        var popup = new Popup
        {
            Placement = PlacementMode.Top,
            PlacementTarget = target,
            HorizontalOffset = 0,
            VerticalOffset = -8,
            WindowManagerAddShadowHint = false,
            IsOpen = true,
            Child = dialog,
        };
        return popup;
    }

    private static SolidColorBrush GetBrush(string key, uint fallback)
    {
        var c = Application.Current?.TryFindResource(key, out var v) == true && v is Color color
            ? color : Color.Parse($"#{fallback:X8}");
        return new SolidColorBrush(c);
    }
}
