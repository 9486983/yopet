using Avalonia.Controls;
using Avalonia.Layout;
using yopet.Sdk;
using yopet.UI.Components;

namespace yopet.Views;

/// <summary>Pet 气泡确认框，内容全部使用 Arc 组件。</summary>
public static class ConfirmDialog
{
    public static async Task<bool> ShowAsync(Window owner, string title, string text)
    {
        var tcs = new TaskCompletionSource<bool>();

        var yesBtn = ArcActions.Primary(Localization.T("Localization.Dialogs.Confirm"));
        var noBtn = ArcActions.Secondary(Localization.T("Localization.Dialogs.Cancel"));

        yesBtn.Click += (_, _) => tcs.TrySetResult(true);
        noBtn.Click += (_, _) => tcs.TrySetResult(false);

        var card = new ArcDialogCard
        {
            Title = title,
            Description = text,
            Width = 300,
            Actions = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Children = { noBtn, yesBtn },
            },
        };

        var popup = PetWindow.ShowDialogOn(owner, card);
        popup.Closed += (_, _) => tcs.TrySetResult(false);

        try { return await tcs.Task; }
        finally { popup.Close(); }
    }
}
