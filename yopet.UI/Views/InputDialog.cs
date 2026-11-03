using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Lang.Avalonia;
using yopet.UI.Components;

namespace yopet.Views;

/// <summary>Pet 气泡输入框，内容全部使用 Arc 组件。</summary>
public static class InputDialog
{
    public static async Task<string?> ShowAsync(Window owner, string title,
        string placeholder, string? initialValue = null)
    {
        var tcs = new TaskCompletionSource<string?>();

        var input = new TKCode.Arc.Input
        {
            Label = title,
            Placeholder = placeholder,
            Text = initialValue ?? "",
            Width = 300,
        };

        var okBtn = ArcActions.Primary(I18nManager.Instance.GetResource("Localization.Dialogs.Confirm"));
        var cancelBtn = ArcActions.Secondary(I18nManager.Instance.GetResource("Localization.Dialogs.Cancel"));

        okBtn.Click += (_, _) => tcs.TrySetResult(input.Text);
        cancelBtn.Click += (_, _) => tcs.TrySetResult(null);
        input.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
                tcs.TrySetResult(input.Text);
        };

        var card = new ArcDialogCard
        {
            Title = title,
            Body = input,
            Width = 340,
            Actions = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Children = { cancelBtn, okBtn },
            },
        };

        var popup = PetWindow.ShowDialogOn(owner, card);
        popup.Closed += (_, _) => tcs.TrySetResult(null);

        try { return await tcs.Task; }
        finally { popup.Close(); }
    }
}
