using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Lang.Avalonia;
using TKCode.Arc;
using yopet.Sdk;
using yopet.UI.Components;

namespace yopet.UI.Views;

/// <summary>统一设置中心，交互控件全部使用 Arc。</summary>
public sealed class UnifiedSettingsView : UserControl
{
    private static string T(string key) =>
        I18nManager.Instance.GetResource($"Localization.SettingPlugin.{key}");

    private readonly IPluginHost _host;
    private readonly ArcDialogCard _card;
    private readonly ThemePicker _themePicker;
    private readonly TKCode.Arc.Select _selector;
    private readonly ScrollViewer _contentScroll;
    private readonly StackPanel _content;
    private readonly TextBlock _status;
    private readonly TKCode.Arc.Button _saveButton;
    private readonly List<SectionItem> _sections = [];
    private readonly Dictionary<string, Control> _inputs = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Func<string?>> _readers = new(StringComparer.OrdinalIgnoreCase);

    private PluginConfigSection? _currentSection;
    private SectionItem? _currentItem;
    private bool _suppressSelection;

    public UnifiedSettingsView(IPluginHost host)
    {
        _host = host;
        Width = 460;
        MaxHeight = 720;

        var themeId = _host.GetConfig("st_theme") ?? (_host.GetDarkTheme() ? "dark" : "light");
        _themePicker = new ThemePicker(ArcUi.ThemeChoices, themeId, OnThemeSelected);

        _selector = new TKCode.Arc.Select
        {
            Label = T("SelectPlugin"),
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        _selector.SelectionChanged += (_, _) => OnSectionChanged();

        _content = new StackPanel { Spacing = 14 };
        _contentScroll = new ScrollViewer
        {
            Content = new Border
            {
                Padding = new Thickness(0, 0, 12, 0),
                Child = _content,
            },
            Height = 360,
            MaxHeight = 360,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
        };

        _status = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 20,
        };
        TKCode.Arc.ArcTokens.Bind(_status, TextBlock.ForegroundProperty,
            TKCode.Arc.DesignTokens.ArcDesignTokenKind.Danger);
        TKCode.Arc.ArcTokens.Bind(_status, TextBlock.FontSizeProperty,
            TKCode.Arc.DesignTokens.ArcDesignTokenKind.TypeSM);

        _saveButton = ArcActions.Primary(T("Save"));
        _card = new ArcDialogCard
        {
            Title = T("SettingsCenter"),
            Body = new StackPanel
            {
                Spacing = 12,
                Children = { _selector, _contentScroll, _status },
            },
            Actions = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Children = { _saveButton },
            },
        };
        Content = _card;

        _saveButton.Click += (_, _) => SaveCurrentSection(showStatus: true);
        _host.LanguageChanged += OnLanguageChanged;
        DetachedFromVisualTree += (_, _) =>
        {
            _host.LanguageChanged -= OnLanguageChanged;
            SaveCurrentSection(showStatus: false);
        };

        ReloadSections();
    }

    private void ReloadSections(bool preserveSelection = false)
    {
        var selectedKey = preserveSelection ? _currentItem?.Section.Key : null;
        _sections.Clear();

        foreach (var section in _host.PluginConfigSections.Where(s => !s.HideFromSettings))
            _sections.Add(new SectionItem(section));

        var ordered = _sections
            .OrderByDescending(item => item.Section.Key == "setting")
            .ThenBy(item => item.Section.PluginName ?? "", StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(item => item.Section.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        _sections.Clear();
        _sections.AddRange(ordered);

        var defaultIndex = _sections.FindIndex(item => item.Section.Key == "setting");
        if (defaultIndex < 0 && _sections.Count > 0) defaultIndex = 0;

        var selectedIndex = string.IsNullOrEmpty(selectedKey)
            ? defaultIndex
            : _sections.FindIndex(item =>
                string.Equals(item.Section.Key, selectedKey, StringComparison.OrdinalIgnoreCase));
        if (selectedIndex < 0) selectedIndex = defaultIndex;

        _selector.IsEnabled = _sections.Count > 0;
        _suppressSelection = true;
        _selector.ItemsSource = _sections.Select(item => item.Label).ToArray();
        _selector.SelectedIndex = selectedIndex;
        _suppressSelection = false;

        if (selectedIndex < 0)
        {
            _currentItem = null;
            _currentSection = null;
            _content.Children.Clear();
            _content.Children.Add(new TKCode.Arc.EmptyState
            {
                Title = T("NoSettings"),
            });
            _status.Text = "";
            return;
        }

        _currentItem = _sections[selectedIndex];
        RenderSection(_currentItem.Section);
    }

    private void OnLanguageChanged(object? sender, string cultureName)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (VisualRoot == null) return;
            _card.Title = T("SettingsCenter");
            _themePicker.RefreshLabels();
            _selector.Label = T("SelectPlugin");
            _saveButton.Content = T("Save");
            ReloadSections(preserveSelection: true);
        });
    }

    private void OnThemeSelected(ArcUi.ThemeChoice choice)
    {
        _host.SetConfig("st_theme", choice.Id);
        ArcUi.ApplyTheme(choice.Id);
        ArcActions.ApplyPrimary(_saveButton);
    }

    private void OnSectionChanged()
    {
        if (_suppressSelection) return;

        var index = _selector.SelectedIndex;
        if (index < 0 || index >= _sections.Count) return;

        var item = _sections[index];
        if (ReferenceEquals(item, _currentItem)) return;

        if (_currentSection != null && !SaveCurrentSection(showStatus: false))
        {
            _suppressSelection = true;
            _selector.SelectedIndex = _currentItem == null ? -1 : _sections.IndexOf(_currentItem);
            _suppressSelection = false;
            return;
        }

        _currentItem = item;
        RenderSection(item.Section);
    }

    private void RenderSection(PluginConfigSection section)
    {
        _currentSection = section;
        _inputs.Clear();
        _readers.Clear();
        _content.Children.Clear();
        _status.Text = "";

        var fields = section.Fields
            .GroupBy(field => field.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var rendered = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var group in section.Groups ?? [])
        {
            var groupFields = group.FieldKeys
                .Select(key => fields.GetValueOrDefault(key))
                .Where(field => field is not null && !rendered.Contains(field.Key))
                .ToList();

            if (groupFields.Count == 0) continue;

            _content.Children.Add(Heading($"{group.Emoji} {group.Title}".Trim(), group.Description));
            foreach (var field in groupFields)
            {
                AddField(field!);
                rendered.Add(field!.Key);
            }
        }

        var themeInserted = false;
        foreach (var field in section.Fields)
        {
            if (!rendered.Contains(field.Key))
                AddField(field);

            if (!themeInserted
                && string.Equals(section.Key, "setting", StringComparison.OrdinalIgnoreCase)
                && string.Equals(field.Key, "st_anim_speed", StringComparison.OrdinalIgnoreCase))
            {
                _content.Children.Add(_themePicker);
                themeInserted = true;
            }
        }

        if (!themeInserted && string.Equals(section.Key, "setting", StringComparison.OrdinalIgnoreCase))
            _content.Children.Add(_themePicker);
    }

    private void AddField(PluginConfigField field)
    {
        var currentValue = _host.GetConfig(field.Key) ?? field.DefaultValue ?? "";
        var (control, read) = CreateInput(field, currentValue);
        _inputs[field.Key] = control;
        _readers[field.Key] = read;
        _content.Children.Add(control);
    }

    private (Control Control, Func<string?> Read) CreateInput(PluginConfigField field, string currentValue)
    {
        var description = string.IsNullOrWhiteSpace(field.Description) && field.IsRequired
            ? I18nManager.Instance.GetResource("Localization.Dialogs.Required")
            : field.Description;

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
                if (field.Key == "st_dark_theme")
                {
                    toggle.PropertyChanged += (_, e) =>
                    {
                        if (e.Property != Avalonia.Controls.Primitives.ToggleButton.IsCheckedProperty) return;
                        var isDark = toggle.IsChecked == true;
                        _host.SetConfig(field.Key, isDark ? "true" : "false");
                        _host.SetDarkTheme(isDark);
                    };
                }
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
                if (field.Key == "st_language")
                {
                    select.SelectionChanged += (_, _) =>
                    {
                        if (select.SelectedValue is not string language || string.IsNullOrWhiteSpace(language))
                            return;
                        _host.SetConfig(field.Key, language);
                        _host.SetLanguage(language);
                    };
                }
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
                    if (TopLevel.GetTopLevel(this) is not { } topLevel) return;
                    if (field.Type == PluginConfigFieldType.FolderPath)
                    {
                        var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(
                            new FolderPickerOpenOptions { AllowMultiple = false, Title = field.Label });
                        if (folders.Count > 0) input.Text = folders[0].Path.LocalPath;
                    }
                    else
                    {
                        var files = await topLevel.StorageProvider.OpenFilePickerAsync(
                            new FilePickerOpenOptions { AllowMultiple = false, Title = field.Label });
                        if (files.Count > 0) input.Text = files[0].Path.LocalPath;
                    }
                };

                input.Suffix = browse;
                return (input, () => input.Text);
            }

            case PluginConfigFieldType.CronExpression:
            {
                var input = new TKCode.Arc.Input
                {
                    Label = field.Label,
                    Description = description,
                    Placeholder = field.Placeholder ?? "*/10 * * * *",
                    Text = currentValue,
                };
                var presets = field.CronPresets ?? DefaultCronPresets();
                var select = new TKCode.Arc.Select
                {
                    Label = I18nManager.Instance.GetResource("Localization.Dialogs.CronTemplate"),
                    ItemsSource = presets
                        .Select(option => new TKCode.Arc.SelectOption(option.Value, option.Label))
                        .ToArray(),
                    Width = 180,
                };
                select.SelectionChanged += (_, _) =>
                {
                    if (select.SelectedValue is string cron && !string.IsNullOrWhiteSpace(cron))
                        input.Text = cron;
                };

                var row = new Grid
                {
                    ColumnDefinitions = new ColumnDefinitions("*,Auto"),
                    ColumnSpacing = 8,
                    Children = { input, select },
                };
                Grid.SetColumn(select, 1);
                return (row, () => input.Text);
            }

            case PluginConfigFieldType.Color:
            {
                var input = new TKCode.Arc.Input
                {
                    Label = field.Label,
                    Description = description,
                    Placeholder = "Color value",
                    Text = currentValue,
                };
                return (input, () => input.Text);
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

    private bool SaveCurrentSection(bool showStatus)
    {
        var section = _currentSection;
        if (section == null) return true;

        var values = section.Fields
            .GroupBy(field => field.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => _readers.GetValueOrDefault(group.Key)?.Invoke(),
                StringComparer.OrdinalIgnoreCase);

        var errors = new List<string>();
        foreach (var field in section.Fields)
        {
            if (field.IsRequired && string.IsNullOrWhiteSpace(values.GetValueOrDefault(field.Key)))
                errors.Add(string.Format(
                    I18nManager.Instance.GetResource("Localization.Dialogs.RequiredField"),
                    field.Label));
        }

        if (errors.Count == 0 && section.Validate is not null)
            errors = section.Validate(values);

        if (errors.Count > 0)
        {
            _status.Text = showStatus ? string.Join(Environment.NewLine, errors) : "";
            return false;
        }

        _host.SaveConfigValues(values);
        _status.Text = showStatus ? T("Saved") : "";
        return true;
    }

    private static Control Heading(string title, string? description)
    {
        var heading = new TextBlock
        {
            Text = title,
            FontWeight = FontWeight.SemiBold,
            TextWrapping = TextWrapping.Wrap,
        };
        TKCode.Arc.ArcTokens.Bind(heading, TextBlock.ForegroundProperty,
            TKCode.Arc.DesignTokens.ArcDesignTokenKind.Foreground);
        TKCode.Arc.ArcTokens.Bind(heading, TextBlock.FontSizeProperty,
            TKCode.Arc.DesignTokens.ArcDesignTokenKind.TypeSM);

        if (string.IsNullOrWhiteSpace(description))
            return heading;

        var stack = new StackPanel { Spacing = 2, Children = { heading } };
        var detail = new TextBlock { Text = description, TextWrapping = TextWrapping.Wrap };
        TKCode.Arc.ArcTokens.Bind(detail, TextBlock.ForegroundProperty,
            TKCode.Arc.DesignTokens.ArcDesignTokenKind.TextMuted);
        TKCode.Arc.ArcTokens.Bind(detail, TextBlock.FontSizeProperty,
            TKCode.Arc.DesignTokens.ArcDesignTokenKind.TypeXS);
        stack.Children.Add(detail);
        return stack;
    }

    private static List<PluginConfigOption> DefaultCronPresets() =>
    [
        new() { Label = "Every 5 minutes", Value = "*/5 * * * *" },
        new() { Label = "Every 10 minutes", Value = "*/10 * * * *" },
        new() { Label = "Every hour", Value = "0 * * * *" },
        new() { Label = "Every day at 9", Value = "0 9 * * *" },
        new() { Label = "Every Monday at 9", Value = "0 9 * * 1" },
    ];

    private sealed record SectionItem(PluginConfigSection Section)
    {
        public string Label =>
            $"{Section.Emoji} {Section.Title}".Trim();
    }
}