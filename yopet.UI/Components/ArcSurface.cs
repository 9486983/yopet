using Avalonia;
using Avalonia.Controls;
using TKCode.Arc;
using TKCode.Arc.DesignTokens;

namespace yopet.UI.Components;

/// <summary>Arc 抬升表面，统一卡片、气泡和弹窗的背景、描边与圆角。</summary>
public class ArcSurface : Border
{
    public ArcSurface()
    {
        ArcTokens.Bind(this, BackgroundProperty, ArcDesignTokenKind.SurfaceRaised);
        ArcTokens.Bind(this, BorderBrushProperty, ArcDesignTokenKind.Border);
        this.BindCornerRadius(ArcDesignTokenKind.RadiusPanel);
        BorderThickness = new Thickness(1);
        Padding = new Thickness(20);
    }
}
