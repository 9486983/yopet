using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using TKCode.Arc;
using TKCode.Arc.Icons.Lucide;
using yopet.Sdk;
using yopet.UI.Components;

namespace yopet.Views;

/// <summary>插件配置弹窗，字段和操作全部使用 Arc 组件。</summary>
public static class PluginConfigDialog
{
    public static async Task ShowAsync(
        Window owner,
        PluginConfigSection section,
        Func<string, string?> getValue,
        Action<Dictionary<string, string?>> onSave)
    {
        var fields = new Dictionary<string, Func<string?>>(StringComparer.OrdinalIgnoreCase);
        var inputs = new Dictionary<string, Control>(StringComparer.OrdinalIgnoreCase);
        var body = new StackPanel { Spacing = 14 };
        var status = new TextBlock
        {
            IsVisible = false,
            TextWrapping = TextWrapping.Wrap,
        };
        TKCode.Arc.ArcTokens.Bind(status, TextBlock.ForegroundProperty, TKCode.Arc.DesignTokens.ArcDesignTokenKind.Danger);
        TKCode.Arc.ArcTokens.Bind(status, TextBlock.FontSizeProperty, TKCode.Arc.DesignTokens.ArcDesignTokenKind.TypeSM);

        var groupedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var group in section.Groups ?? [])
        {
            if (!string.IsNullOrWhiteSpace(group.Title))
                body.Children.Add(Heading(group.Title, group.Description));

            foreach (var key in group.FieldKeys)
            {
                groupedKeys.Add(key);
                var field = section.Fields.FirstOrDefault(f =>
                    string.Equals(f.Key, key, StringComparison.OrdinalIgnoreCase));
                if (field is not null)
                    AppendField(field);
            }
        }

        foreach (var field in section.Fields)
        {
            if (!groupedKeys.Contains(field.Key))
                AppendField(field);
        }

        body.Children.Add(status);

        var closeButton = ArcActions.Secondary(
            Localization.T("Localization.Dialogs.Cancel"),
            icon: LucideIconKind.X);
        var saveButton = ArcActions.Primary(
            Localization.T("Localization.Dialogs.Confirm"),
            icon: LucideIconKind.Check);

        var card = new ArcDialogCard
        {
            Title = section.Title,
            Icon = YopetIcons.Resolve(section.Emoji, LucideIconKind.Settings),
            Width = 480,
            Body = new ScrollViewer
            {
                Content = new Border
                {
                    Padding = new Thickness(0, 0, 12, 0),
                    Child = body,
                },
                MaxHeight = 480,
                HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            },
            Actions = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Children = { closeButton, saveButton },
            },
        };

        var popup = PetWindow.ShowDialogOn(owner, card);
        closeButton.Click += (_, _) => popup.Close();

        saveButton.Click += (_, _) =>
        {
            var values = fields.ToDictionary(pair => pair.Key, pair => pair.Value());
            var errors = new List<string>();
            foreach (var field in section.Fields)
            {
                if (field.IsRequired && string.IsNullOrWhiteSpace(values.GetValueOrDefault(field.Key)))
                    errors.Add(string.Format(
                        Localization.T("Localization.Dialogs.RequiredField"),
                        field.Label));
            }

            if (errors.Count == 0 && section.Validate is not null)
                errors = section.Validate(values);

            if (errors.Count > 0)
            {
                status.Text = string.Join(Environment.NewLine, errors);
                status.IsVisible = true;
                return;
            }

            onSave(values);
            popup.Close();
        };

        if (inputs.Values.FirstOrDefault() is { } first)
            first.Focus();

        void AppendField(PluginConfigField field)
        {
            var currentValue = getValue(field.Key) ?? field.DefaultValue ?? "";
            var (control, read) = CreateControl(owner, field, currentValue);
            fields[field.Key] = read;
            inputs[field.Key] = control;
            body.Children.Add(control);
        }
    }

    private static (Control Control, Func<string?> Read) CreateControl(
        Window owner,
        PluginConfigField field,
        string currentValue)
    {
        var description = field.Description;
        if (string.IsNullOrWhiteSpace(description) && field.IsRequired)
            description = Localization.T("Localization.Dialogs.Required");

        switch (field.Type)
        {
            case PluginConfigFieldType.Password:
            {
                var input = new TKCode.Arc.Input
                {
                    Label = field.Label,
                    Description = description,
                    Placeholder = field.Placeholder,
                    Text = currentValue,
                    PasswordChar = '●',
                };
                return (input, () => input.Text);
            }

            case PluginConfigFieldType.Number:
            {
                var number = new TKCode.Arc.NumberField
                {
                    Label = field.Label,
                    Description = description,
                    Placeholder = field.Placeholder,
                    Minimum = (decimal)(field.MinValue ?? 0),
                    Maximum = (decimal)(field.MaxValue ?? 1_000_000_000),
                    Value = decimal.TryParse(currentValue, NumberStyles.Number, CultureInfo.InvariantCulture, out var value)
                        ? value
                        : (decimal)(field.MinValue ?? 0),
                };
                return (number, () => number.Value.ToString(CultureInfo.InvariantCulture));
            }

            case PluginConfigFieldType.Boolean:
            {
                var toggle = new TKCode.Arc.Switch
                {
                    Content = field.Label,
                    IsChecked = string.Equals(currentValue, "true", StringComparison.OrdinalIgnoreCase),
                };
                AutomationProperties.SetName(toggle, field.Label);
                return (toggle, () => toggle.IsChecked == true ? "true" : "false");
            }

            case PluginConfigFieldType.Dropdown when field.Options is { Count: > 0 }:
            {
                var select = new TKCode.Arc.Select
                {
                    Label = field.Label,
                    Description = description,
                    Placeholder = field.Placeholder ?? "Select an option",
                    ItemsSource = field.Options
                        .Select(option => new TKCode.Arc.SelectOption(option.Value, option.Label))
                        .ToArray(),
                    SelectedValue = currentValue,
                };
                return (select, () => select.SelectedValue?.ToString() ?? "");
            }

            case PluginConfigFieldType.TextArea:
            {
                var textarea = new TKCode.Arc.Textarea
                {
                    Label = field.Label,
                    Description = description,
                    Placeholder = field.Placeholder,
                    Text = currentValue,
                    FrameMinHeight = Math.Max(96, field.TextAreaRows * 28),
                };
                return (textarea, () => textarea.Text);
            }

            case PluginConfigFieldType.FilePath:
            case PluginConfigFieldType.FolderPath:
            {
                var input = new TKCode.Arc.Input
                {
                    Label = field.Label,
                    Description = description,
                    Placeholder = field.Placeholder,
                    Text = currentValue,
                };
                var browse = ArcActions.Secondary("Browse", ControlSize.Small);
                browse.VerticalAlignment = VerticalAlignment.Center;
                browse.Margin = new Thickness(0, 4, 4, 4);
                browse.Click += async (_, _) =>
                {
                    if (field.Type == PluginConfigFieldType.FolderPath)
                    {
                        var folders = await owner.StorageProvider.OpenFolderPickerAsync(
                            new FolderPickerOpenOptions { AllowMultiple = false, Title = field.Label });
                        if (folders.Count > 0)
                            input.Text = folders[0].Path.LocalPath;
                    }
                    else
                    {
                        var files = await owner.StorageProvider.OpenFilePickerAsync(
                            new FilePickerOpenOptions { AllowMultiple = false, Title = field.Label });
                        if (files.Count > 0)
                            input.Text = files[0].Path.LocalPath;
                    }
                };

                input.Suffix = browse;
                return (input, () => input.Text);
            }

            case PluginConfigFieldType.CronExpression when field.CronPresets is { Count: > 0 }:
            {
                var select = new TKCode.Arc.Select
                {
                    Label = field.Label,
                    Description = description,
                    ItemsSource = field.CronPresets
                        .Select(option => new TKCode.Arc.SelectOption(option.Value, option.Label))
                        .ToArray(),
                    SelectedValue = currentValue,
                };
                return (select, () => select.SelectedValue?.ToString() ?? "");
            }

            default:
            {
                var input = new TKCode.Arc.Input
                {
                    Label = field.Label,
                    Description = description,
                    Placeholder = field.Placeholder,
                    Text = currentValue,
                };
                return (input, () => input.Text);
            }
        }
    }

    private static Control Heading(string title, string? description)
    {
        var heading = new TextBlock
        {
            Text = title,
            FontWeight = FontWeight.SemiBold,
            Margin = new Thickness(0, 8, 0, 0),
        };
        TKCode.Arc.ArcTokens.Bind(heading, TextBlock.ForegroundProperty, TKCode.Arc.DesignTokens.ArcDesignTokenKind.Foreground);
        TKCode.Arc.ArcTokens.Bind(heading, TextBlock.FontSizeProperty, TKCode.Arc.DesignTokens.ArcDesignTokenKind.TypeSM);

        if (string.IsNullOrWhiteSpace(description))
            return heading;

        var stack = new StackPanel { Spacing = 2, Children = { heading } };
        var detail = new TextBlock { Text = description, TextWrapping = TextWrapping.Wrap };
        TKCode.Arc.ArcTokens.Bind(detail, TextBlock.ForegroundProperty, TKCode.Arc.DesignTokens.ArcDesignTokenKind.TextMuted);
        TKCode.Arc.ArcTokens.Bind(detail, TextBlock.FontSizeProperty, TKCode.Arc.DesignTokens.ArcDesignTokenKind.TypeXS);
        stack.Children.Add(detail);
        return stack;
    }
}
