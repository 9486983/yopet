using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace yopet.Views;

/// <summary>
/// 气泡样式的确认弹窗 —— 使用 PetWindow 的 DialogPopup 弹出。
/// </summary>
public static class ConfirmDialog
{
    public static async Task<bool> ShowAsync(Window owner, string title, string text)
    {
        var tcs = new TaskCompletionSource<bool>();

        var yesBtn = DialogHelper.CreateButton("✅ 确定", primary: true);
        var noBtn = DialogHelper.CreateButton("❌ 取消");

        yesBtn.Click += (_, _) => tcs.TrySetResult(true);
        noBtn.Click += (_, _) => tcs.TrySetResult(false);

        var btnPanel = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Right,
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Margin = new Thickness(0, 8, 0, 0),
        };
        btnPanel.Children.Add(noBtn);
        btnPanel.Children.Add(yesBtn);

        var border = new Border
        {
            Background = new SolidColorBrush(DialogHelper.GetColor("BgOverlay", 0xCCF0ECE3)),
            BorderBrush = new SolidColorBrush(DialogHelper.GetColor("BorderColor", 0xFFc4b89e)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(14, 8),
            BoxShadow = new BoxShadows(new BoxShadow
            {
                OffsetX = 0, OffsetY = 2, Blur = 8,
                Color = Color.Parse("#30000000"),
            }),
            Child = new Grid
            {
                RowDefinitions = new RowDefinitions("Auto,Auto,Auto"),
                Children =
                {
                    DialogHelper.CreateTitle(title).WithGridRow(0),
                    DialogHelper.CreateBody(text).WithGridRow(1),
                    btnPanel.WithGridRow(2),
                },
            },
        };

        var popup = PetWindow.ShowDialogOn(owner, border);

        bool result;
        try { result = await tcs.Task; }
        finally { if (popup != null) popup.IsOpen = false; }

        return result;
    }

    private static T WithGridRow<T>(this T element, int row) where T : Control
    {
        Grid.SetRow(element, row);
        return element;
    }
}
