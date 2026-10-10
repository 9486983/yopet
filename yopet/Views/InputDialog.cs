using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Lang.Avalonia;
using yopet.Sdk;

namespace yopet.Views;

/// <summary>
/// 气泡风格的输入弹窗 —— 使用 PetWindow 的 DialogPopup 弹出。
/// </summary>
public static class InputDialog
{
    public static async Task<string?> ShowAsync(Window owner, string title,
        string placeholder, string? initialValue = null)
    {
        var tcs = new TaskCompletionSource<string?>();

        var textBox = new TextBox
        {
            PlaceholderText = placeholder,
            Text = initialValue ?? "",
            MinWidth = 260,
            Height = 40,
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(10, 6),
            FontSize = 13,
        };

        var okBtn = DialogHelper.CreateButton(I18nManager.Instance.GetResource("Localization.Dialogs.Confirm"), primary: true);
        var cancelBtn = DialogHelper.CreateButton(I18nManager.Instance.GetResource("Localization.Dialogs.Cancel"));

        okBtn.Click += (_, _) => tcs.TrySetResult(textBox.Text);
        cancelBtn.Click += (_, _) => tcs.TrySetResult(null);
        textBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
                tcs.TrySetResult(textBox.Text);
        };

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
                RowDefinitions = new RowDefinitions("Auto,*,Auto"),
                Children =
                {
                    DialogHelper.CreateTitle(title).WithGridRow(0),
                    textBox.WithGridRow(1),
                    new StackPanel
                    {
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Orientation = Orientation.Horizontal,
                        Spacing = 8,
                        Children = { cancelBtn, okBtn },
                    }.WithGridRow(2),
                },
            },
        };

        var popup = PetWindow.ShowDialogOn(owner, border);

        string? result;
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
