using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Lang.Avalonia;
using yopet.Sdk;

namespace SettingPlugin;

internal sealed class UnifiedSettingsView : UserControl
{
    private static string T(string key) =>
        I18nManager.Instance.GetResource($"Localization.SettingPlugin.{key}");

    private readonly IPluginHost _host;
    private readonly ComboBox _selector;
    private readonly ScrollViewer _contentScroll;
    private readonly StackPanel _content;
    private readonly TextBlock _status;
    private readonly TextBlock _titleBlock;
    private readonly TextBlock _selectLabel;
    private readonly Button _saveButton;
    private readonly List<SectionItem> _sections = new();
    private readonly Dictionary<string, Control> _inputs = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, string?> _originalValues = new(StringComparer.OrdinalIgnoreCase);

    private readonly Color _foreground;
    private readonly Color _muted;
    private readonly Color _border;
    private readonly Color _pageBackground;
    private readonly Color _overlay;
    private readonly Color _accent;

    private PluginConfigSection? _currentSection;
    private SectionItem? _currentItem;
    private bool _suppressSelection;

    public UnifiedSettingsView(IPluginHost host)
    {
        _host = host;
        _foreground = GetColor("TextPrimary", 0xFF794f27);
        _muted = GetColor("TextMuted", 0xFF9f927d);
        _border = GetColor("BorderColor", 0xFFc4b89e);
        _pageBackground = GetColor("BgPage", 0xFFF5F0E8);
        _overlay = GetColor("BgOverlay", 0xCCF0ECE3);
        _accent = GetColor("AccentPrimary", 0xFF19c8b9);

        Width = 420;
        MaxHeight = 560;

        _selector = new ComboBox
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Height = 36,
            CornerRadius = new CornerRadius(8),
            FontSize = 13,
            Padding = new Thickness(10, 0),
            Foreground = new SolidColorBrush(_foreground),
            Background = new SolidColorBrush(_pageBackground),
            BorderBrush = new SolidColorBrush(_border),
        };
        _selector.SelectionChanged += (_, _) => OnSectionChanged();

        _content = new StackPanel { Spacing = 10 };
        _contentScroll = new ScrollViewer
        {
            Content = _content,
            MaxHeight = 360,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };

        _status = new TextBlock
        {
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            IsVisible = false,
        };

        _titleBlock = new TextBlock
        {
            Text = T("SettingsCenter"),
            FontSize = 18,
            FontWeight = FontWeight.Bold,
            Foreground = new SolidColorBrush(_foreground),
        };

        _selectLabel = new TextBlock
        {
            Text = T("SelectPlugin"),
            FontSize = 11,
            Foreground = new SolidColorBrush(_muted),
        };

        _saveButton = new Button
        {
            Content = T("Save"),
            MinWidth = 90,
            Height = 34,
            CornerRadius = new CornerRadius(8),
            FontSize = 13,
            Padding = new Thickness(10, 0),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            Cursor = new Cursor(StandardCursorType.Hand),
            Background = new SolidColorBrush(_accent),
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0),
        };
        _saveButton.Click += (_, _) => SaveCurrentSection(showStatus: true);

        var root = new StackPanel
        {
            Spacing = 10,
            Margin = new Thickness(14, 10),
            Children =
            {
                _titleBlock,
                _selectLabel,
                _selector,
                _contentScroll,
                _status,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Children = { _saveButton },
                },
            },
        };

        Content = new Border
        {
            Background = new SolidColorBrush(_overlay),
            BorderBrush = new SolidColorBrush(_border),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            BoxShadow = new BoxShadows(new BoxShadow
            {
                OffsetX = 0,
                OffsetY = 2,
                Blur = 8,
                Color = Color.Parse("#30000000"),
            }),
            Child = root,
        };

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
        _selector.ItemsSource = _sections.Select(item => item.Label).ToList();
        _selector.SelectedIndex = selectedIndex;
        _suppressSelection = false;

        if (selectedIndex < 0)
        {
            _currentItem = null;
            _currentSection = null;
            _content.Children.Clear();
            _content.Children.Add(new TextBlock
            {
                Text = T("NoSettings"),
                FontSize = 12,
                Foreground = new SolidColorBrush(_muted),
            });
            _status.IsVisible = false;
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

            _titleBlock.Text = T("SettingsCenter");
            _selectLabel.Text = T("SelectPlugin");
            _saveButton.Content = T("Save");
            ReloadSections(preserveSelection: true);
        });
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
        _originalValues = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        _content.Children.Clear();
        _status.IsVisible = false;

        var fields = section.Fields
            .GroupBy(field => field.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var rendered = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (section.Groups is { Count: > 0 })
        {
            foreach (var group in section.Groups)
            {
                var groupFields = group.FieldKeys
                    .Select(key => fields.GetValueOrDefault(key))
                    .Where(field => field != null && !rendered.Contains(field.Key))
                    .ToList();

                if (groupFields.Count == 0) continue;

                _content.Children.Add(new TextBlock
                {
                    Text = $"{group.Emoji ?? "📁"} {group.Title}",
                    FontSize = 13,
                    FontWeight = FontWeight.SemiBold,
                    Foreground = new SolidColorBrush(_foreground),
                    Margin = new Thickness(0, 6, 0, 0),
                });

                if (!string.IsNullOrWhiteSpace(group.Description))
                {
                    _content.Children.Add(new TextBlock
                    {
                        Text = group.Description,
                        FontSize = 10,
                        Foreground = new SolidColorBrush(_muted),
                        TextWrapping = TextWrapping.Wrap,
                    });
                }

                foreach (var field in groupFields)
                {
                    AddField(field!);
                    rendered.Add(field!.Key);
                }
            }
        }

        foreach (var field in section.Fields)
        {
            if (rendered.Contains(field.Key)) continue;
            AddField(field);
        }
    }

    private void AddField(PluginConfigField field)
    {
        var currentValue = _host.GetConfig(field.Key) ?? field.DefaultValue ?? "";
        _originalValues[field.Key] = currentValue;

        var input = CreateInput(field, currentValue);
        _inputs[field.Key] = input;

        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*"),
            RowDefinitions = new RowDefinitions("Auto,Auto"),
        };

        row.Children.Add(new TextBlock
        {
            Text = field.Label,
            FontSize = 13,
            FontWeight = FontWeight.SemiBold,
            Foreground = new SolidColorBrush(_foreground),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 10, 0),
        });

        Grid.SetColumn(input, 1);
        row.Children.Add(input);

        if (!string.IsNullOrWhiteSpace(field.Description))
        {
            var description = new TextBlock
            {
                Text = field.Description,
                FontSize = 10,
                Foreground = new SolidColorBrush(_muted),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 2, 0, 0),
            };
            Grid.SetRow(description, 1);
            Grid.SetColumnSpan(description, 2);
            row.Children.Add(description);
        }

        _content.Children.Add(row);
    }

    private Control CreateInput(PluginConfigField field, string currentValue)
    {
        switch (field.Type)
        {
            case PluginConfigFieldType.Password:
                return CreateTextBox(currentValue, field.Placeholder, password: true);

            case PluginConfigFieldType.Number when field.MinValue.HasValue && field.MaxValue.HasValue &&
                                                   field.MaxValue.Value > field.MinValue.Value:
            {
                var initial = double.TryParse(currentValue, out var value)
                    ? Math.Clamp(value, field.MinValue.Value, field.MaxValue.Value)
                    : field.MinValue.Value;
                var valueText = new TextBlock
                {
                    Text = ((int)Math.Round(initial)).ToString(),
                    FontSize = 12,
                    MinWidth = 36,
                    Foreground = new SolidColorBrush(_foreground),
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Right,
                };
                var slider = new Slider
                {
                    Minimum = field.MinValue.Value,
                    Maximum = field.MaxValue.Value,
                    Value = initial,
                    Width = 170,
                    VerticalAlignment = VerticalAlignment.Center,
                };
                slider.PropertyChanged += (_, e) =>
                {
                    if (e.Property == Slider.ValueProperty)
                        valueText.Text = ((int)Math.Round(slider.Value)).ToString();
                };

                return new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Tag = slider,
                    Children = { slider, valueText },
                };
            }

            case PluginConfigFieldType.Number:
                return CreateTextBox(currentValue, field.Placeholder);

            case PluginConfigFieldType.Boolean:
                return new ToggleSwitch
                {
                    IsChecked = string.Equals(currentValue, "true", StringComparison.OrdinalIgnoreCase),
                    Background = Brushes.Transparent,
                    BorderThickness = new Thickness(0),
                    HorizontalAlignment = HorizontalAlignment.Right,
                };

            case PluginConfigFieldType.Dropdown when field.Options is { Count: > 0 }:
                return new ComboBox
                {
                    ItemsSource = field.Options,
                    SelectedItem = field.Options.FirstOrDefault(option =>
                        string.Equals(option.Value, currentValue, StringComparison.OrdinalIgnoreCase)),
                    DisplayMemberBinding = new Binding(nameof(PluginConfigOption.Label)),
                    MinWidth = 160,
                    Height = 34,
                    CornerRadius = new CornerRadius(8),
                    FontSize = 13,
                    Foreground = new SolidColorBrush(_foreground),
                    Background = new SolidColorBrush(_pageBackground),
                    BorderBrush = new SolidColorBrush(_border),
                    HorizontalAlignment = HorizontalAlignment.Right,
                };

            case PluginConfigFieldType.TextArea:
                return new TextBox
                {
                    Text = currentValue,
                    PlaceholderText = field.Placeholder ?? "",
                    MinWidth = 260,
                    Height = 28 + field.TextAreaRows * 20,
                    AcceptsReturn = true,
                    TextWrapping = TextWrapping.Wrap,
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(10, 6),
                    FontSize = 13,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    Foreground = new SolidColorBrush(_foreground),
                    Background = new SolidColorBrush(_pageBackground),
                    BorderBrush = new SolidColorBrush(_border),
                };

            case PluginConfigFieldType.FilePath:
            case PluginConfigFieldType.FolderPath:
            {
                var textBox = CreateTextBox(currentValue, field.Placeholder);
                var browseButton = CreateSmallButton("…");
                browseButton.Click += async (_, _) =>
                {
                    var owner = GetOwnerWindow();
                    if (owner == null) return;

                    if (field.Type == PluginConfigFieldType.FolderPath)
                    {
                        var folders = await owner.StorageProvider.OpenFolderPickerAsync(
                            new FolderPickerOpenOptions { AllowMultiple = false, Title = field.Label });
                        if (folders.Count > 0) textBox.Text = folders[0].Path.LocalPath;
                    }
                    else
                    {
                        var files = await owner.StorageProvider.OpenFilePickerAsync(
                            new FilePickerOpenOptions { AllowMultiple = false, Title = field.Label });
                        if (files.Count > 0) textBox.Text = files[0].Path.LocalPath;
                    }
                };

                var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
                row.Children.Add(textBox);
                Grid.SetColumn(browseButton, 1);
                browseButton.Margin = new Thickness(4, 0, 0, 0);
                row.Children.Add(browseButton);
                return row;
            }

            case PluginConfigFieldType.CronExpression:
            {
                var textBox = CreateTextBox(currentValue, field.Placeholder ?? "*/10 * * * *");
                var presets = field.CronPresets ?? DefaultCronPresets();
                var presetBox = new ComboBox
                {
                    ItemsSource = presets,
                    DisplayMemberBinding = new Binding(nameof(PluginConfigOption.Label)),
                    Height = 34,
                    Width = 120,
                    CornerRadius = new CornerRadius(8),
                    FontSize = 12,
                    PlaceholderText = I18nManager.Instance.GetResource("Localization.Dialogs.CronTemplate"),
                    Foreground = new SolidColorBrush(_foreground),
                    Background = new SolidColorBrush(_pageBackground),
                    BorderBrush = new SolidColorBrush(_border),
                    Margin = new Thickness(4, 0, 0, 0),
                };
                presetBox.SelectionChanged += (_, _) =>
                {
                    if (presetBox.SelectedItem is PluginConfigOption option)
                        textBox.Text = option.Value;
                };

                var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
                row.Children.Add(textBox);
                Grid.SetColumn(presetBox, 1);
                row.Children.Add(presetBox);
                return row;
            }

            case PluginConfigFieldType.Color:
            {
                var preview = new Border
                {
                    Width = 34,
                    Height = 34,
                    CornerRadius = new CornerRadius(8),
                    BorderThickness = new Thickness(1),
                    BorderBrush = new SolidColorBrush(_border),
                    Margin = new Thickness(4, 0, 0, 0),
                };
                var textBox = CreateTextBox(currentValue, "#FF794f27");
                textBox.TextChanged += (_, _) =>
                {
                    try { preview.Background = new SolidColorBrush(Color.Parse(textBox.Text ?? "")); }
                    catch { }
                };
                try { preview.Background = new SolidColorBrush(Color.Parse(currentValue)); }
                catch { }

                var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
                row.Children.Add(textBox);
                Grid.SetColumn(preview, 1);
                row.Children.Add(preview);
                return row;
            }

            default:
                return CreateTextBox(currentValue, field.Placeholder);
        }
    }

    private TextBox CreateTextBox(string? value, string? placeholder, bool password = false) => new()
    {
        Text = value ?? "",
        PlaceholderText = placeholder ?? "",
        PasswordChar = password ? '●' : '\0',
        MinWidth = 200,
        Height = 34,
        CornerRadius = new CornerRadius(8),
        Padding = new Thickness(10, 4),
        FontSize = 13,
        HorizontalAlignment = HorizontalAlignment.Stretch,
        Foreground = new SolidColorBrush(_foreground),
        Background = new SolidColorBrush(_pageBackground),
        BorderBrush = new SolidColorBrush(_border),
    };

    private Button CreateSmallButton(string text) => new()
    {
        Content = text,
        Width = 40,
        Height = 34,
        CornerRadius = new CornerRadius(8),
        FontSize = 16,
        Padding = new Thickness(0),
        Cursor = new Cursor(StandardCursorType.Hand),
        Background = Brushes.Transparent,
        BorderThickness = new Thickness(1),
        BorderBrush = new SolidColorBrush(_border),
        Foreground = new SolidColorBrush(_foreground),
    };

    private bool SaveCurrentSection(bool showStatus)
    {
        var section = _currentSection;
        if (section == null) return true;

        var values = CollectValues(section);
        var errors = new List<string>();

        if (section.Validate != null)
        {
            try
            {
                var validationErrors = section.Validate(values);
                if (validationErrors != null) errors.AddRange(validationErrors);
            }
            catch (Exception ex)
            {
                errors.Add(ex.Message);
            }
        }

        if (errors.Count > 0)
        {
            SetStatus(string.Join("\n", errors), isError: true);
            return false;
        }

        var changed = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in values)
        {
            if (!_originalValues.TryGetValue(key, out var oldValue) ||
                !string.Equals(oldValue, value, StringComparison.Ordinal))
            {
                changed[key] = value;
            }
        }

        if (changed.Count > 0)
        {
            try
            {
                _host.SaveConfigValues(changed);
            }
            catch (Exception ex)
            {
                SetStatus(ex.Message, isError: true);
                return false;
            }

            foreach (var (key, value) in changed)
                _originalValues[key] = value;
        }

        if (showStatus)
            SetStatus(changed.Count > 0 ? T("Saved") : T("NoChanges"), isError: false);

        return true;
    }

    private Dictionary<string, string?> CollectValues(PluginConfigSection section)
    {
        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var field in section.Fields)
        {
            if (_inputs.TryGetValue(field.Key, out var input))
                values[field.Key] = GetControlValue(input);
        }
        return values;
    }

    private static string? GetControlValue(Control control) => control switch
    {
        TextBox textBox => textBox.Text,
        ToggleSwitch toggle => toggle.IsChecked == true ? "true" : "false",
        ComboBox comboBox => (comboBox.SelectedItem as PluginConfigOption)?.Value,
        Slider slider => ((int)Math.Round(slider.Value)).ToString(),
        StackPanel panel when panel.Tag is Slider slider => ((int)Math.Round(slider.Value)).ToString(),
        Grid grid => FindFirstTextBox(grid)?.Text,
        _ => null,
    };

    private static TextBox? FindFirstTextBox(Control control)
    {
        if (control is TextBox textBox) return textBox;

        if (control is Panel panel)
        {
            foreach (var child in panel.Children)
            {
                var found = FindFirstTextBox(child);
                if (found != null) return found;
            }
        }

        return null;
    }

    private void SetStatus(string text, bool isError)
    {
        _status.Text = text;
        _status.Foreground = new SolidColorBrush(isError ? Color.Parse("#D9534F") : _accent);
        _status.IsVisible = !string.IsNullOrWhiteSpace(text);
    }

    private List<PluginConfigOption> DefaultCronPresets() => new()
    {
        new() { Label = I18nManager.Instance.GetResource("Localization.Dialogs.CronEveryMinute"), Value = "* * * * *" },
        new() { Label = I18nManager.Instance.GetResource("Localization.Dialogs.CronEvery5Minutes"), Value = "*/5 * * * *" },
        new() { Label = I18nManager.Instance.GetResource("Localization.Dialogs.CronEvery10Minutes"), Value = "*/10 * * * *" },
        new() { Label = I18nManager.Instance.GetResource("Localization.Dialogs.CronEvery30Minutes"), Value = "*/30 * * * *" },
        new() { Label = I18nManager.Instance.GetResource("Localization.Dialogs.CronEveryHour"), Value = "0 * * * *" },
        new() { Label = I18nManager.Instance.GetResource("Localization.Dialogs.CronDaily0"), Value = "0 0 * * *" },
        new() { Label = I18nManager.Instance.GetResource("Localization.Dialogs.CronDaily9"), Value = "0 9 * * *" },
        new() { Label = I18nManager.Instance.GetResource("Localization.Dialogs.CronWeekly0"), Value = "0 0 * * 0" },
        new() { Label = I18nManager.Instance.GetResource("Localization.Dialogs.CronMonthly1"), Value = "0 0 1 * *" },
    };

    private Window? GetOwnerWindow()
    {
        if (TopLevel.GetTopLevel(this) is Window window) return window;
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            return desktop.MainWindow;
        return null;
    }

    private static Color GetColor(string key, uint fallback)
    {
        if (Application.Current?.TryFindResource(key, out var value) == true && value is Color color)
            return color;
        return Color.Parse($"#{fallback:X8}");
    }

    private sealed class SectionItem
    {
        public SectionItem(PluginConfigSection section)
        {
            Section = section;
            Label = BuildLabel(section);
        }

        public PluginConfigSection Section { get; }
        public string Label { get; }

        private static string BuildLabel(PluginConfigSection section)
        {
            var emoji = section.Emoji ?? "⚙️";
            if (section.Key == "setting") return $"{emoji} {section.Title}";

            if (string.IsNullOrWhiteSpace(section.PluginName))
                return $"{emoji} {section.Title}";

            return string.Equals(section.PluginName, section.Title, StringComparison.OrdinalIgnoreCase)
                ? $"{emoji} {section.PluginName}"
                : $"{emoji} {section.PluginName} · {section.Title}";
        }
    }
}
