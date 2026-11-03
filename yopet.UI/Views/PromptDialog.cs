using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using TKCode.Arc;
using TKCode.Arc.DesignTokens;
using yopet.Sdk;
using yopet.UI;
using yopet.UI.Components;

namespace yopet.Views;

public static class PromptDialog
{
    public static async Task<IReadOnlyList<string>?> ShowAsync(Window owner, PromptDialogConfig config)
    {
        var result = new TaskCompletionSource<IReadOnlyList<string>?>();
        var body = new StackPanel { Spacing = 8 };
        var checkboxes = new List<(PromptOption Option, TKCode.Arc.Checkbox Box)>();
        var validation = new TextBlock
        {
            IsVisible = false,
            TextWrapping = TextWrapping.Wrap,
        };
        ArcTokens.Bind(validation, TextBlock.ForegroundProperty, ArcDesignTokenKind.Danger);
        ArcTokens.Bind(validation, TextBlock.FontSizeProperty, ArcDesignTokenKind.TypeXS);

        var customInput = new TKCode.Arc.Input
        {
            Label = config.CustomLabel ?? Localization.T("Localization.Dialogs.Required"),
            Placeholder = config.CustomPlaceholder ?? "",
            IsVisible = config.AllowCustom,
        };

        foreach (var option in config.Options)
        {
            if (config.AllowMultiple)
            {
                var box = new TKCode.Arc.Checkbox
                {
                    Content = option.Label,
                    Description = option.Description,
                };
                checkboxes.Add((option, box));
                body.Children.Add(box);
            }
            else
            {
                var button = CreateOptionButton(option);
                button.Click += (_, _) => result.TrySetResult([option.Label]);
                body.Children.Add(button);
            }
        }

        if (config.AllowCustom)
            body.Children.Add(customInput);

        body.Children.Add(validation);

        void Submit()
        {
            var values = new List<string>();
            foreach (var (option, box) in checkboxes)
            {
                if (box.IsChecked == true)
                    values.Add(option.Label);
            }

            var custom = customInput.Text?.Trim();
            if (!string.IsNullOrEmpty(custom))
                values.Add(custom);

            if (values.Count == 0)
            {
                validation.Text = config.ValidationMessage ?? "";
                validation.IsVisible = true;
                return;
            }

            result.TrySetResult(values);
        }

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children = { ArcActions.Secondary(config.CancelLabel) },
        };
        ((TKCode.Arc.Button)actions.Children[0]).Click += (_, _) => result.TrySetResult(null);

        if (config.AllowMultiple || config.AllowCustom)
        {
            var confirm = ArcActions.Primary(config.ConfirmLabel);
            confirm.Click += (_, _) => Submit();
            actions.Children.Add(confirm);
        }

        customInput.TextChanged += (_, _) => validation.IsVisible = false;
        customInput.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
                Submit();
        };

        var card = new ArcDialogCard
        {
            Title = config.Title,
            Description = config.Description,
            Width = 460,
            Body = new ScrollViewer
            {
                Content = body,
                MaxHeight = 420,
                HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            },
            Actions = actions,
        };

        var popup = PetWindow.ShowDialogOn(owner, card);
        popup.Closed += (_, _) => result.TrySetResult(null);

        try
        {
            return await result.Task;
        }
        finally
        {
            popup.Close();
        }
    }

    private static TKCode.Arc.Button CreateOptionButton(PromptOption option)
    {
        var button = option.Kind switch
        {
            PromptOptionKind.Primary => ArcActions.Primary(option.Label),
            PromptOptionKind.Danger => ArcActions.Danger(option.Label),
            _ => ArcActions.Secondary(option.Label),
        };

        button.HorizontalAlignment = HorizontalAlignment.Stretch;
        button.HorizontalContentAlignment = HorizontalAlignment.Left;
        button.MinHeight = 42;

        if (!string.IsNullOrWhiteSpace(option.Description))
        {
            var label = new TextBlock
            {
                Text = option.Label,
                TextWrapping = TextWrapping.Wrap,
                FontWeight = FontWeight.SemiBold,
            };
            ArcTokens.Bind(label, TextBlock.FontSizeProperty, ArcDesignTokenKind.TypeSM);

            var description = new TextBlock
            {
                Text = option.Description,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 2, 0, 0),
            };
            ArcTokens.Bind(description, TextBlock.ForegroundProperty, ArcDesignTokenKind.TextSecondary);
            ArcTokens.Bind(description, TextBlock.FontSizeProperty, ArcDesignTokenKind.TypeXS);

            if (option.Kind == PromptOptionKind.Primary)
            {
                var theme = ArcUi.CurrentTheme;
                label.Foreground = new SolidColorBrush(theme.OnAccent);
                description.Foreground = new SolidColorBrush(theme.OnAccent) { Opacity = 0.85 };
            }

            button.Content = new StackPanel
            {
                Spacing = 0,
                Children = { label, description },
            };
        }

        return button;
    }
}
