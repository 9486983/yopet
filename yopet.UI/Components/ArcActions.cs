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

    private static TKCode.Arc.Button Create(string text, ButtonVariant variant, ControlSize size = ControlSize.Small) =>
        new()
        {
            Content = text,
            Variant = variant,
            Size = size,
        };
}
