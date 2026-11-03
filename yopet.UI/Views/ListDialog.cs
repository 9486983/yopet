using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using TKCode.Arc;
using TKCode.Arc.DesignTokens;
using yopet.Sdk;
using yopet.UI.Components;

namespace yopet.Views;

/// <summary>Pet 气泡列表，使用 Arc 的卡片、按钮、下拉菜单和空状态。</summary>
public static class ListDialog
{
    public static async Task<Dictionary<string, string>?> ShowAsync(Window owner, ListDialogConfig config)
    {
        var items = config.Items ?? [];
        if (config.DataSource is not null)
            items = await config.DataSource();


        var content = new StackPanel { Spacing = 12 };
        var body = new ScrollViewer
        {
            Content = new Border
            {
                Padding = new Thickness(0, 0, 12, 0),
                Child = content,
            },
            MaxHeight = 360,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
        };

        var card = new ArcDialogCard
        {
            Title = $"{config.Emoji} {config.Title}".Trim(),
            Width = config.LayoutMode == ListDialogLayoutMode.CardGrid
                ? 520
                : ResolveTableWidth(config.Columns),
            Body = body,
        };

        var popup = PetWindow.ShowDialogOn(owner, card);
        var result = new TaskCompletionSource<Dictionary<string, string>?>();
        popup.Closed += (_, _) => result.TrySetResult(null);

        void Close(Dictionary<string, string>? value)
        {
            result.TrySetResult(value);
            popup.Close();
        }

        async Task RebuildAsync()
        {
            if (config.DataSource is not null)
                items = await config.DataSource();

            content.Children.Clear();
            if (config.ToolbarActions.Count > 0)
                content.Children.Add(BuildToolbar(config));

            if (items.Count == 0)
            {
                content.Children.Add(new TKCode.Arc.EmptyState
                {
                    Title = "No items",
                    Description = "There is nothing to show yet.",
                });
                return;
            }

            content.Children.Add(config.LayoutMode == ListDialogLayoutMode.CardGrid
                ? BuildCardGrid(config, items, Close)
                : BuildTable(config, items, Close));
        }

        config.DataChanged += OnDataChanged;
        popup.Closed += (_, _) => config.DataChanged -= OnDataChanged;

        async void OnDataChanged(object? _, EventArgs __) => await RebuildAsync();

        await RebuildAsync();
        return await result.Task;
    }

    private static Control BuildToolbar(ListDialogConfig config)
    {
        var toolbar = new WrapPanel { ItemSpacing = 8, LineSpacing = 8 };
        foreach (var action in config.ToolbarActions)
            toolbar.Children.Add(BuildToolbarAction(action));
        return toolbar;
    }

    private static Control BuildToolbarAction(ListToolbarAction action)
    {
        if (action.Type == ListToolbarActionType.Dropdown && action.Children is { Count: > 0 })
        {
            var menu = new TKCode.Arc.DropdownMenu { Label = $"{action.Emoji} {action.Label}".Trim() };
            foreach (var child in action.Children)
            {
                var entry = new TKCode.Arc.MenuEntry { Header = $"{child.Emoji} {child.Label}".Trim() };
                entry.Click += async (_, _) =>
                {
                    if (child.Callback is not null) await child.Callback();
                };
                menu.Items.Add(entry);
            }
            return menu;
        }

        var button = ArcActions.Secondary($"{action.Emoji} {action.Label}".Trim());
        button.Click += async (_, _) =>
        {
            if (action.Callback is not null) await action.Callback();
        };
        return button;
    }

    private static Control BuildTable(
        ListDialogConfig config,
        List<Dictionary<string, string>> items,
        Action<Dictionary<string, string>?> close)
    {
        var grid = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*"),
        };

        var columns = config.Columns;
        var colDefs = string.Join(",", columns.Select(column =>
            double.IsNaN(column.Width) ? "*" : column.Width.ToString("0")));

        var header = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions(colDefs),
            Margin = new Thickness(0, 0, 0, 4),
        };
        for (var i = 0; i < columns.Count; i++)
        {
            var text = new TextBlock
            {
                Text = columns[i].Header,
                FontWeight = FontWeight.SemiBold,
                Margin = new Thickness(6, 4),
                TextWrapping = TextWrapping.NoWrap,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
            };
            ArcTokens.Bind(text, TextBlock.ForegroundProperty, ArcDesignTokenKind.TextSecondary);
            ArcTokens.Bind(text, TextBlock.FontSizeProperty, ArcDesignTokenKind.TypeXS);
            header.Children.Add(text);
            Grid.SetColumn(text, i);
        }

        var rows = new StackPanel();
        foreach (var row in items)
        {
            var rowGrid = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions(colDefs),
                MinHeight = 36,
            };
            for (var i = 0; i < columns.Count; i++)
            {
                var cell = BuildCell(columns[i], row, () => close(row));
                rowGrid.Children.Add(cell);
                Grid.SetColumn(cell, i);
            }

            var rowHost = new Border
            {
                Child = rowGrid,
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(0, 2),
            };
            ArcTokens.Bind(rowHost, Border.BorderBrushProperty, ArcDesignTokenKind.BorderSubtle);
            rowHost.PointerPressed += (_, _) => close(row);
            rows.Children.Add(rowHost);
        }

        var scroll = new ScrollViewer
        {
            Content = new Border
            {
                Padding = new Thickness(0, 0, 10, 0),
                Child = rows,
            },
            MaxHeight = 300,
        };
        grid.Children.Add(header);
        grid.Children.Add(scroll);
        Grid.SetRow(scroll, 1);
        return grid;
    }

    private static Control BuildCell(
        ListColumn column,
        Dictionary<string, string> row,
        Action select)
    {
        if (column.Type == ListColumnType.Action)
        {
            var actions = new WrapPanel { ItemSpacing = 6, LineSpacing = 4 };
            foreach (var action in column.RowActions ?? [])
                actions.Children.Add(BuildRowAction(action, row));
            return actions;
        }

        var value = row.GetValueOrDefault(column.Key, "");
        if (column.Type == ListColumnType.Editable && column.OnCellEdit is not null)
        {
            var input = new TKCode.Arc.Input
            {
                Text = value,
                IsLabelVisible = false,
                Size = ControlSize.Small,
                Margin = new Thickness(2),
            };
            input.LostFocus += async (_, _) =>
            {
                if (!string.Equals(input.Text, value, StringComparison.Ordinal))
                    await column.OnCellEdit(row, input.Text ?? "");
            };
            input.KeyDown += async (_, e) =>
            {
                if (e.Key != Key.Enter) return;
                if (!string.Equals(input.Text, value, StringComparison.Ordinal))
                    await column.OnCellEdit(row, input.Text ?? "");
                select();
            };
            return input;
        }

        var text = new TextBlock
        {
            Text = value,
            TextWrapping = TextWrapping.NoWrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(6, 4),
            VerticalAlignment = VerticalAlignment.Center,
        };
        ArcTokens.Bind(text, TextBlock.ForegroundProperty, ArcDesignTokenKind.Foreground);
        ArcTokens.Bind(text, TextBlock.FontSizeProperty, ArcDesignTokenKind.TypeSM);
        if (!string.IsNullOrEmpty(value))
            ToolTip.SetTip(text, value);
        return text;
    }

    private static Control BuildRowAction(ListRowAction action, Dictionary<string, string> row)
    {
        var emoji = ResolveRowToken(action.Emoji, row);
        var label = ResolveRowToken(action.Label, row);

        if (action.Type == ListRowActionType.Dropdown && action.Children is { Count: > 0 })
        {
            var menu = new TKCode.Arc.DropdownMenu { Label = $"{emoji} {label}".Trim() };
            foreach (var child in action.Children)
            {
                var childEmoji = ResolveRowToken(child.Emoji, row);
                var childLabel = ResolveRowToken(child.Label, row);
                var entry = new TKCode.Arc.MenuEntry { Header = $"{childEmoji} {childLabel}".Trim() };
                entry.Click += async (_, _) =>
                {
                    if (child.Callback is not null) await child.Callback(row);
                };
                menu.Items.Add(entry);
            }
            return menu;
        }

        var button = ArcActions.Secondary($"{emoji} {label}".Trim());
        if (!string.IsNullOrWhiteSpace(action.Tooltip))
            ToolTip.SetTip(button, action.Tooltip);
        button.Click += async (_, _) =>
        {
            if (action.Callback is not null) await action.Callback(row);
        };
        return button;
    }

    private static string ResolveRowToken(string? text, Dictionary<string, string> row)
    {
        if (string.IsNullOrEmpty(text) || text[0] != '@') return text ?? "";
        var key = text[1..];
        return row.GetValueOrDefault(key, text);
    }

    private static double ResolveTableWidth(IReadOnlyList<ListColumn> columns)
    {
        var fixedWidth = columns
            .Where(column => !double.IsNaN(column.Width))
            .Sum(column => column.Width);
        return Math.Clamp(fixedWidth + 300, 560, 900);
    }

    private static Control BuildCardGrid(
        ListDialogConfig config,
        List<Dictionary<string, string>> items,
        Action<Dictionary<string, string>?> close)
    {
        var grid = new Avalonia.Controls.Primitives.UniformGrid
        {
            Columns = 3,
            ColumnSpacing = 10,
            RowSpacing = 10,
        };
        var textKey = string.IsNullOrWhiteSpace(config.CardTextKey)
            ? config.Columns.FirstOrDefault()?.Key ?? "name"
            : config.CardTextKey;

        foreach (var row in items)
        {
            var card = new TKCode.Arc.Card
            {
                Title = row.GetValueOrDefault(textKey, ""),
                VerticalAlignment = VerticalAlignment.Top,
                HorizontalAlignment = HorizontalAlignment.Stretch,
            };

            var imagePath = config.CardImageProvider?.Invoke(row);
            if (!string.IsNullOrWhiteSpace(imagePath) && File.Exists(imagePath))
            {
                try
                {
                    card.Media = new Image
                    {
                        Source = new Bitmap(imagePath),
                        Height = 110,
                        Stretch = Stretch.Uniform,
                    };
                }
                catch { }
            }
            else
            {
                card.Media = new TextBlock
                {
                    Text = config.CardFallbackEmoji,
                    FontSize = 42,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                };
            }

            card.PointerPressed += async (_, _) =>
            {
                if (config.OnCardClick is not null && !await config.OnCardClick(row))
                    return;
                close(row);
            };
            grid.Children.Add(card);
        }

        return grid;
    }
}
