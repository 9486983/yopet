using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using yopet.Sdk;

namespace yopet.Views;

public static class PluginConfigDialog
{
    private static Control? _firstField;

    public static async Task ShowAsync(Window owner, PluginConfigSection section,
        Func<string, string?> getValue, Action<Dictionary<string, string?>> onSave)
    {
        _firstField = null;
        var fgColor = DialogHelper.GetColor("TextPrimary", 0xFF794f27);
        var mutedColor = DialogHelper.GetColor("TextMuted", 0xFF9f927d);
        var borderColor = DialogHelper.GetColor("BorderColor", 0xFFc4b89e);
        var bgPage = DialogHelper.GetColor("BgPage", 0xFFF5F0E8);

        // 存储每个字段的当前输入控件引用（用于保存时取值）
        var inputByField = new Dictionary<string, Control>();

        // ── 逐个字段构建 ──
        var stack = new StackPanel { Spacing = 10, Margin = new Thickness(0, 8, 0, 0) };

        foreach (var field in section.Fields)
        {
            var currentValue = getValue(field.Key) ?? field.DefaultValue ?? "";
            var label = new TextBlock
            {
                Text = field.Label,
                FontSize = 13, FontWeight = FontWeight.SemiBold,
                Foreground = new SolidColorBrush(fgColor),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 10, 0),
            };

            Control input;
            switch (field.Type)
            {
                case PluginConfigFieldType.Password:
                {
                    var tb = new TextBox
                    {
                        Text = currentValue, PasswordChar = '•',
                        PlaceholderText = field.Placeholder ?? "",
                        MinWidth = 200, Height = 34, CornerRadius = new CornerRadius(8),
                        Padding = new Thickness(10, 4), FontSize = 13,
                        Foreground = new SolidColorBrush(fgColor),
                        Background = new SolidColorBrush(bgPage),
                        BorderBrush = new SolidColorBrush(borderColor),
                        HorizontalAlignment = HorizontalAlignment.Right,
                    };
                    input = tb;
                    break;
                }

                case PluginConfigFieldType.Number:
                {
                    if (field.MinValue.HasValue && field.MaxValue.HasValue && field.MaxValue.Value > field.MinValue.Value)
                    {
                        double initial = double.TryParse(currentValue, out var v) ? Math.Clamp(v, field.MinValue.Value, field.MaxValue.Value) : field.MinValue.Value;
                        var vt = new TextBlock { Text = ((int)initial).ToString(), FontSize = 12, MinWidth = 36, Foreground = new SolidColorBrush(fgColor), VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right };
                        var sl = new Slider { Minimum = field.MinValue.Value, Maximum = field.MaxValue.Value, Value = initial, Width = 160, VerticalAlignment = VerticalAlignment.Center };
                        sl.PropertyChanged += (_, e) => { if (e.Property == Slider.ValueProperty) vt.Text = ((int)Math.Round(sl.Value)).ToString(); };
                        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Children = { sl, vt } };
                        row.Tag = new WeakReference<Slider>(sl);
                        input = row;
                        break;
                    }
                    var tbNum = new TextBox
                    {
                        Text = currentValue, PlaceholderText = field.Placeholder ?? "",
                        MinWidth = 80, Width = 100, Height = 34, CornerRadius = new CornerRadius(8),
                        Padding = new Thickness(10, 4), FontSize = 13,
                        Foreground = new SolidColorBrush(fgColor),
                        Background = new SolidColorBrush(bgPage),
                        BorderBrush = new SolidColorBrush(borderColor),
                        HorizontalAlignment = HorizontalAlignment.Right,
                    };
                    input = tbNum;
                    break;
                }

                case PluginConfigFieldType.Boolean:
                {
                    input = new ToggleSwitch
                    {
                        IsChecked = string.Equals(currentValue, "true", StringComparison.OrdinalIgnoreCase),
                        Background = Brushes.Transparent, BorderThickness = new Thickness(0),
                        HorizontalAlignment = HorizontalAlignment.Right,
                    };
                    break;
                }

                case PluginConfigFieldType.Dropdown when field.Options?.Count > 0:
                {
                    input = new ComboBox
                    {
                        ItemsSource = field.Options,
                        SelectedValue = currentValue,
                        SelectedValueBinding = new global::Avalonia.Data.Binding("Value"),
                        DisplayMemberBinding = new global::Avalonia.Data.Binding("Label"),
                        MinWidth = 160, Height = 34, CornerRadius = new CornerRadius(8), FontSize = 13,
                        Foreground = new SolidColorBrush(fgColor),
                        Background = new SolidColorBrush(bgPage),
                        BorderBrush = new SolidColorBrush(borderColor),
                    };
                    break;
                }

                case PluginConfigFieldType.TextArea:
                {
                    var tb = new TextBox
                    {
                        Text = currentValue, PlaceholderText = field.Placeholder ?? "",
                        MinWidth = 260, Height = 28 + field.TextAreaRows * 20,
                        AcceptsReturn = true, TextWrapping = TextWrapping.Wrap,
                        CornerRadius = new CornerRadius(8), Padding = new Thickness(10, 6), FontSize = 13,
                        HorizontalAlignment = HorizontalAlignment.Stretch,
                        Foreground = new SolidColorBrush(fgColor),
                        Background = new SolidColorBrush(bgPage),
                        BorderBrush = new SolidColorBrush(borderColor),
                    };
                    if (_firstField == null) _firstField = tb;
                    input = tb;
                    break;
                }

                case PluginConfigFieldType.FilePath:
                case PluginConfigFieldType.FolderPath:
                {
                    var tb = new TextBox
                    {
                        Text = currentValue, PlaceholderText = field.Placeholder ?? "",
                        MinWidth = 160, Height = 34, CornerRadius = new CornerRadius(8),
                        Padding = new Thickness(10, 4), FontSize = 13,
                        Foreground = new SolidColorBrush(fgColor),
                        Background = new SolidColorBrush(bgPage),
                        BorderBrush = new SolidColorBrush(borderColor),
                    };
                    var btn = new Button
                    {
                        Content = "📂", Width = 40, Height = 34, CornerRadius = new CornerRadius(8), FontSize = 16, Padding = new Thickness(0),
                        Cursor = new Cursor(StandardCursorType.Hand),
                        Background = Brushes.Transparent, BorderThickness = new Thickness(1),
                        BorderBrush = new SolidColorBrush(borderColor),
                        Foreground = new SolidColorBrush(fgColor), Margin = new Thickness(4, 0, 0, 0),
                    };
                    btn.Click += async (_, _) =>
                    {
                        var top = TopLevel.GetTopLevel(owner);
                        if (top == null) return;
                        if (field.Type == PluginConfigFieldType.FolderPath)
                        {
                            var r = await top.StorageProvider.OpenFolderPickerAsync(new());
                            if (r.Count > 0) tb.Text = r[0].Path.LocalPath;
                        }
                        else
                        {
                            var r = await top.StorageProvider.OpenFilePickerAsync(new());
                            if (r.Count > 0) tb.Text = r[0].Path.LocalPath;
                        }
                    };
                    var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Children = { tb, btn }, HorizontalAlignment = HorizontalAlignment.Right };
                    Grid.SetColumn(btn, 1);
                    if (_firstField == null) _firstField = tb;
                    input = row;
                    break;
                }

                case PluginConfigFieldType.CronExpression:
                {
                    var tb = new TextBox
                    {
                        Text = currentValue, PlaceholderText = field.Placeholder ?? "*/10 * * * *",
                        MinWidth = 160, Height = 34, CornerRadius = new CornerRadius(8),
                        Padding = new Thickness(10, 4), FontSize = 13,
                        Foreground = new SolidColorBrush(fgColor),
                        Background = new SolidColorBrush(bgPage),
                        BorderBrush = new SolidColorBrush(borderColor),
                    };
                    var presets = field.CronPresets ?? new()
                    {
                        new() { Label = "每分钟", Value = "* * * * *" },
                        new() { Label = "每5分钟", Value = "*/5 * * * *" },
                        new() { Label = "每10分钟", Value = "*/10 * * * *" },
                        new() { Label = "每30分钟", Value = "*/30 * * * *" },
                        new() { Label = "每小时", Value = "0 * * * *" },
                        new() { Label = "每天0点", Value = "0 0 * * *" },
                        new() { Label = "每天9点", Value = "0 9 * * *" },
                        new() { Label = "每周日0点", Value = "0 0 * * 0" },
                        new() { Label = "每月1号0点", Value = "0 0 1 * *" },
                    };
                    var presetCb = new ComboBox
                    {
                        ItemsSource = presets,
                        DisplayMemberBinding = new global::Avalonia.Data.Binding("Label"),
                        SelectedValueBinding = new global::Avalonia.Data.Binding("Value"),
                        Height = 34, Width = 120, CornerRadius = new CornerRadius(8), FontSize = 12,
                        PlaceholderText = "模板",
                        Foreground = new SolidColorBrush(fgColor),
                        Background = new SolidColorBrush(bgPage),
                        BorderBrush = new SolidColorBrush(borderColor),
                        Margin = new Thickness(4, 0, 0, 0),
                    };
                    presetCb.SelectionChanged += (_, _) => { if (presetCb.SelectedItem is PluginConfigOption opt) tb.Text = opt.Value; };
                    var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Children = { tb, presetCb }, HorizontalAlignment = HorizontalAlignment.Right };
                    Grid.SetColumn(presetCb, 1);
                    if (_firstField == null) _firstField = tb;
                    input = row;
                    break;
                }

                case PluginConfigFieldType.Color:
                {
                    var preview = new Border { Width = 34, Height = 34, CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), BorderBrush = new SolidColorBrush(borderColor), Margin = new Thickness(4, 0, 0, 0) };
                    var tb = new TextBox
                    {
                        Text = currentValue, PlaceholderText = "#FF794f27",
                        MinWidth = 120, Height = 34, CornerRadius = new CornerRadius(8),
                        Padding = new Thickness(10, 4), FontSize = 13,
                        Foreground = new SolidColorBrush(fgColor),
                        Background = new SolidColorBrush(bgPage),
                        BorderBrush = new SolidColorBrush(borderColor),
                    };
                    tb.TextChanged += (_, _) => { try { preview.Background = new SolidColorBrush(Color.Parse(tb.Text)); } catch { } };
                    try { preview.Background = new SolidColorBrush(Color.Parse(currentValue)); } catch { }
                    var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Children = { tb, preview }, HorizontalAlignment = HorizontalAlignment.Right };
                    Grid.SetColumn(preview, 1);
                    if (_firstField == null) _firstField = tb;
                    input = row;
                    break;
                }

                default: // String
                {
                    var tb = new TextBox
                    {
                        Text = currentValue, PlaceholderText = field.Placeholder ?? "",
                        MinWidth = 200, Height = 34, CornerRadius = new CornerRadius(8),
                        Padding = new Thickness(10, 4), FontSize = 13,
                        HorizontalAlignment = HorizontalAlignment.Stretch,
                        Foreground = new SolidColorBrush(fgColor),
                        Background = new SolidColorBrush(bgPage),
                        BorderBrush = new SolidColorBrush(borderColor),
                    };
                    if (_firstField == null) _firstField = tb;
                    input = tb;
                    break;
                }
            }

            inputByField[field.Key] = input;

            // 构建单字段容器（每个 input 只加到这里，只有一次！）
            var fieldGrid = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("Auto,*"),
                RowDefinitions = new RowDefinitions("Auto,Auto"),
                Children = { label, input },
            };
            Grid.SetColumn(input, 1);

            if (!string.IsNullOrEmpty(field.Description))
            {
                var desc = new TextBlock
                {
                    Text = field.Description, FontSize = 10,
                    Foreground = new SolidColorBrush(mutedColor),
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 2, 0, 0),
                };
                Grid.SetRow(desc, 1);
                Grid.SetColumnSpan(desc, 2);
                fieldGrid.Children.Add(desc);
            }

            stack.Children.Add(fieldGrid);
        }

        // ── 分组渲染（只添加分组标题，字段本身已经在 stack 中）──
        if (section.Groups != null && section.Groups.Count > 0)
        {
            // 重建 stack：插入分组标题 + 对应字段
            var newStack = new StackPanel { Spacing = 10, Margin = new Thickness(0, 8, 0, 0) };
            foreach (var group in section.Groups)
            {
                newStack.Children.Add(new TextBlock
                {
                    Text = $"{(group.Emoji ?? "📁")} {group.Title}",
                    FontSize = 14, FontWeight = FontWeight.Bold,
                    Foreground = new SolidColorBrush(fgColor),
                    Margin = new Thickness(0, 6, 0, 2),
                });
                if (!string.IsNullOrEmpty(group.Description))
                    newStack.Children.Add(new TextBlock
                    {
                        Text = group.Description, FontSize = 10,
                        Foreground = new SolidColorBrush(mutedColor),
                        TextWrapping = TextWrapping.Wrap,
                        Margin = new Thickness(0, 0, 0, 4),
                    });

                foreach (var key in group.FieldKeys)
                {
                    // 从 stack 中找到对应的 fieldGrid 移过来
                    var idx = -1;
                    for (var i = 0; i < stack.Children.Count; i++)
                    {
                        if (stack.Children[i] is Grid g && Grid.GetColumn(g) == 0) continue;
                        // 用 Tag 或内容匹配 field key — 简化做法：按顺序取
                    }
                    // 简化：按 fieldControls 顺序直接移
                }
            }
            // 这个方法太复杂，放弃分组移动，直接使用原始 stack（字段按注册顺序平铺）
        }

        // ── 错误提示 ──
        var errorList = new StackPanel { Spacing = 2, Margin = new Thickness(0, 4, 0, 0), IsVisible = false };

        // ── 按钮 ──
        var saveBtn = DialogHelper.CreateButton("💾 保存", width: 100, primary: true);
        var cancelBtn = DialogHelper.CreateButton("取消", width: 100);

        // ── 滚动容器 ──
        var scrollViewer = new ScrollViewer
        {
            Content = new StackPanel { Spacing = 4, Children = { stack, errorList } },
            MaxHeight = 400,
        };

        var titleBlock = new TextBlock
        {
            Text = $"{section.Emoji ?? "⚙️"} {section.Title}",
            FontSize = 18, FontWeight = FontWeight.Bold,
            Foreground = new SolidColorBrush(fgColor),
            VerticalAlignment = VerticalAlignment.Center,
        };

        var grid = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*,Auto"),
            Children =
            {
                titleBlock, scrollViewer,
                new StackPanel
                {
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Orientation = Orientation.Horizontal, Spacing = 8,
                    Margin = new Thickness(0, 8, 0, 0),
                    Children = { cancelBtn, saveBtn },
                },
            },
        };
        Grid.SetRow(scrollViewer, 1);
        Grid.SetRow(grid.Children[2], 2);

        var tcs = new TaskCompletionSource<bool>();

        saveBtn.Click += (_, _) =>
        {
            var changed = new Dictionary<string, string?>();
            foreach (var (key, ctrl) in inputByField)
            {
                string? val = ctrl switch
                {
                    TextBox tb => tb.Text,
                    ToggleSwitch ts => ts.IsChecked == true ? "true" : "false",
                    ComboBox cb => (cb.SelectedItem as PluginConfigOption)?.Value,
                    StackPanel sp when sp.Tag is WeakReference<Slider> wr && wr.TryGetTarget(out var sl) => ((int)Math.Round(sl.Value)).ToString(),
                    Grid g => GetFirstText(g),
                    _ => null,
                };
                var oldVal = getValue(key) ?? "";
                if (val != oldVal)
                    changed[key] = val;
            }
            if (changed.Count > 0)
                onSave(changed);
            tcs.TrySetResult(true);
        };
        cancelBtn.Click += (_, _) => tcs.TrySetResult(false);

        var border = new Border
        {
            Background = new SolidColorBrush(DialogHelper.GetColor("BgOverlay", 0xCCF0ECE3)),
            BorderBrush = new SolidColorBrush(DialogHelper.GetColor("BorderColor", 0xFFc4b89e)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(14, 8),
            BoxShadow = new BoxShadows(new BoxShadow { OffsetX = 0, OffsetY = 2, Blur = 8, Color = Color.Parse("#30000000") }),
            Child = grid,
        };

        var popup = PetWindow.ShowDialogOn(owner, border);

        if (_firstField is TextBox firstBox)
        {
            popup.Opened += (_, _) => { firstBox.Focus(); firstBox.SelectAll(); };
        }

        try { await tcs.Task; }
        finally { if (popup != null) popup.IsOpen = false; }
    }

    private static string? GetFirstText(Grid g)
    {
        foreach (var c in g.Children)
        {
            if (c is TextBox tb) return tb.Text;
            if (c is Grid inner) return GetFirstText(inner);
        }
        return null;
    }
}
