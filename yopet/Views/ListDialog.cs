using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using yopet.Sdk;

namespace yopet.Views;

/// <summary>
/// 气泡风格的列表弹窗 —— 使用 PetWindow 的 DialogPopup 弹出。
/// 支持两种布局模式：Table（行 x 列）和 CardGrid（卡片网格）。
/// </summary>
public static class ListDialog
{
    public static async Task<Dictionary<string, string>?> ShowAsync(Window owner, ListDialogConfig config)
    {
        var tcs = new TaskCompletionSource<bool>();

        // ── 加载数据 ──
        var items = config.Items ?? new List<Dictionary<string, string>>();
        if (config.DataSource != null)
            items = await config.DataSource();

        if (config.LayoutMode == ListDialogLayoutMode.CardGrid)
            return await ShowCardGridAsync(owner, config, items, tcs);

        return await ShowTableAsync(owner, config, items, tcs);
    }

    // ═══════════════════════════════════════════════════════
    //  Table 模式（已有逻辑，保持完整兼容）
    // ═══════════════════════════════════════════════════════

    private static async Task<Dictionary<string, string>?> ShowTableAsync(
        Window owner, ListDialogConfig config,
        List<Dictionary<string, string>> items,
        TaskCompletionSource<bool> tcs)
    {
        // ── 标题 ──
        var title = DialogHelper.CreateTitle(config.Title);
        var closeBtn = new Button
        {
            Content = "✕",
            Width = 24,
            Height = 24,
            FontSize = 12,
            Padding = new Thickness(0),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            Cursor = new Cursor(StandardCursorType.Hand),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = new SolidColorBrush(DialogHelper.GetColor("TextMuted", 0xFF9f927d)),
        };

        var titleRow = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Children = { title },
        };
        Grid.SetColumn(closeBtn, 1);
        titleRow.Children.Add(closeBtn);

        // ── 工具栏 ──
        var toolbar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Margin = new Thickness(0, 6, 0, 0),
            IsVisible = config.ToolbarActions.Count > 0,
        };
        foreach (var act in config.ToolbarActions)
            toolbar.Children.Add(BuildToolbarItem(act));

        // ── 列定义 → ColumnDefinitions（第一个 NaN 用 * 撑满，其余 Auto）──
        var colDefBuilder = new List<string>();
        bool firstStar = true;
        foreach (var col in config.Columns)
        {
            if (double.IsNaN(col.Width))
            {
                colDefBuilder.Add(firstStar ? "*" : "Auto");
                firstStar = false;
            }
            else colDefBuilder.Add($"{col.Width}");
        }
        var colDefs = string.Join(",", colDefBuilder);

        // ── Header 行 ──
        var headerGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions(colDefs),
            Margin = new Thickness(0, 6, 0, 2),
        };
        var fgColor = new SolidColorBrush(DialogHelper.GetColor("TextPrimary", 0xFF794f27));
        var borderColor = new SolidColorBrush(DialogHelper.GetColor("BorderColor", 0xFFc4b89e));
        foreach (var (col, idx) in config.Columns.Select((c, i) => (c, i)))
        {
            var tb = new TextBlock
            {
                Text = col.Header,
                FontSize = 11,
                FontWeight = FontWeight.Bold,
                Foreground = fgColor,
                Margin = new Thickness(4, 2),
                VerticalAlignment = VerticalAlignment.Center,
            };
            headerGrid.Children.Add(tb);
            Grid.SetColumn(tb, idx);
        }

        // ── 数据行 ──
        var rowsPanel = new StackPanel { Spacing = 1 };

        void RebuildRows()
        {
            rowsPanel.Children.Clear();
            foreach (var row in items)
            {
                var rowGrid = new Grid
                {
                    ColumnDefinitions = new ColumnDefinitions(colDefs),
                    MinHeight = 28,
                };
                foreach (var (col, idx) in config.Columns.Select((c, i) => (c, i)))
                {
                    var cell = CreateCell(col, row, RebuildRows);
                    rowGrid.Children.Add(cell);
                    Grid.SetColumn(cell, idx);
                }
                rowsPanel.Children.Add(rowGrid);
            }
            // 所有行被移除后自动关闭
            if (items.Count == 0)
                tcs.TrySetResult(true);
        }
        RebuildRows();

        // ── 整体布局 ──
        var scrollViewer = new ScrollViewer
        {
            Content = rowsPanel,
            MaxHeight = 320,
        };
        var tableContainer = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,Auto"),
            Children = { headerGrid, scrollViewer },
            Margin = new Thickness(0, 4, 0, 0),
        };
        Grid.SetRow(headerGrid, 0);
        Grid.SetRow(scrollViewer, 1);

        var layout = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,Auto"),
            Children =
            {
                titleRow,
                toolbar,
                tableContainer,
            },
        };
        Grid.SetRow(titleRow, 0);
        Grid.SetRow(toolbar, 1);
        Grid.SetRow(tableContainer, 2);

        // ── 气泡容器 ──
        var border = new Border
        {
            Background = new SolidColorBrush(DialogHelper.GetColor("BgOverlay", 0xCCF0ECE3)),
            BorderBrush = borderColor,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(14, 8),
            MaxWidth = 450,
            BoxShadow = new BoxShadows(new BoxShadow
            {
                OffsetX = 0, OffsetY = 2, Blur = 8,
                Color = Color.Parse("#30000000"),
            }),
            Child = layout,
        };

        // ── Popup ──
        var popup = PetWindow.ShowDialogOn(owner, border);

        // ── 订阅 DataChanged 事件 ──
        EventHandler? dataHandler = null;
        dataHandler = (_, _) =>
        {
            if (config.DataSource == null) return;
            Dispatcher.UIThread.Post(async () =>
            {
                items = await config.DataSource();
                RebuildRows();
            });
        };
        if (config.DataSource != null)
            config.DataChanged += dataHandler;

        closeBtn.Click += (_, _) => tcs.TrySetResult(true);

        try { await tcs.Task; }
        finally
        {
            if (config.DataSource != null)
                config.DataChanged -= dataHandler;
            if (popup != null) popup.IsOpen = false;
        }

        return null;
    }

    // ═══════════════════════════════════════════════════════
    //  CardGrid 模式
    // ═══════════════════════════════════════════════════════

    private static async Task<Dictionary<string, string>?> ShowCardGridAsync(
        Window owner, ListDialogConfig config,
        List<Dictionary<string, string>> items,
        TaskCompletionSource<bool> dismissTcs)
    {
        var selectionTcs = new TaskCompletionSource<Dictionary<string, string>?>();
        var borderColor = new SolidColorBrush(DialogHelper.GetColor("BorderColor", 0xFFc4b89e));

        var cardTextKey = !string.IsNullOrEmpty(config.CardTextKey)
            ? config.CardTextKey
            : config.Columns.FirstOrDefault()?.Key ?? "";

        // ── 标题（与 Table 模式一致） ──
        var title = DialogHelper.CreateTitle(config.Title);
        var closeBtn = new Button
        {
            Content = "✕", Width = 24, Height = 24, FontSize = 12,
            Padding = new Thickness(0),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            Cursor = new Cursor(StandardCursorType.Hand),
            Background = Brushes.Transparent, BorderThickness = new Thickness(0),
            Foreground = new SolidColorBrush(DialogHelper.GetColor("TextMuted", 0xFF9f927d)),
        };
        var titleRow = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Children = { title },
        };
        Grid.SetColumn(closeBtn, 1);
        titleRow.Children.Add(closeBtn);

        // ── 工具栏 ──
        var toolbar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Margin = new Thickness(0, 6, 0, 0),
            IsVisible = config.ToolbarActions.Count > 0,
        };
        foreach (var act in config.ToolbarActions)
            toolbar.Children.Add(BuildToolbarItem(act));

        // ── 卡片网格 ──
        var wrapPanel = new WrapPanel
        {
            Width = 400,
            HorizontalAlignment = HorizontalAlignment.Center,
        };

        void RebuildCards()
        {
            wrapPanel.Children.Clear();
            foreach (var row in items)
            {
                wrapPanel.Children.Add(BuildCardItem(config, row, cardTextKey, clickedRow =>
                {
                    selectionTcs.TrySetResult(clickedRow);
                    dismissTcs.TrySetResult(true);
                }));
            }
            if (items.Count == 0)
                selectionTcs.TrySetResult(null);
        }
        RebuildCards();

        var scrollViewer = new ScrollViewer { Content = wrapPanel, MaxHeight = 380 };

        var layout = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,Auto"),
            Children = { titleRow, toolbar, scrollViewer },
        };
        Grid.SetRow(titleRow, 0);
        Grid.SetRow(toolbar, 1);
        Grid.SetRow(scrollViewer, 2);

        // ── 气泡容器 ──
        var border = new Border
        {
            Background = new SolidColorBrush(DialogHelper.GetColor("BgOverlay", 0xCCF0ECE3)),
            BorderBrush = borderColor,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(14, 8),
            MaxWidth = 450,
            BoxShadow = new BoxShadows(new BoxShadow
            {
                OffsetX = 0, OffsetY = 2, Blur = 8,
                Color = Color.Parse("#30000000"),
            }),
            Child = layout,
        };

        var popup = PetWindow.ShowDialogOn(owner, border);

        closeBtn.Click += (_, _) =>
        {
            selectionTcs.TrySetResult(null);
            dismissTcs.TrySetResult(true);
        };

        // ── DataChanged 刷新 ──
        EventHandler? dataHandler = null;
        dataHandler = (_, _) =>
        {
            if (config.DataSource == null) return;
            Dispatcher.UIThread.Post(async () =>
            {
                items = await config.DataSource();
                RebuildCards();
            });
        };
        if (config.DataSource != null)
            config.DataChanged += dataHandler;

        try
        {
            return await selectionTcs.Task;
        }
        finally
        {
            if (config.DataSource != null)
                config.DataChanged -= dataHandler;
            if (popup != null) popup.IsOpen = false;
        }
    }

    /// <summary>构建一张卡片</summary>
    private static Border BuildCardItem(ListDialogConfig config, Dictionary<string, string> row,
        string textKey, Action<Dictionary<string, string>> onClick)
    {
        var bgCard = new SolidColorBrush(DialogHelper.GetColor("BgCard", 0xFF252540));
        var bgHover = new SolidColorBrush(DialogHelper.GetColor("BgHover", 0xFF3D3D5C));
        var textPrimary = new SolidColorBrush(DialogHelper.GetColor("TextPrimary", 0xFFFFFFFF));

        var border = new Border
        {
            Width = 110,
            Height = 130,
            CornerRadius = new CornerRadius(12),
            Background = bgCard,
            Margin = new Thickness(4),
            Cursor = new Cursor(StandardCursorType.Hand),
        };

        var stack = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
        };

        // ── 图片或回退 Emoji ──
        var imagePath = config.CardImageProvider?.Invoke(row);
        if (!string.IsNullOrEmpty(imagePath) && File.Exists(imagePath))
        {
            try
            {
                var bmp = new Bitmap(imagePath);
                stack.Children.Add(new Image
                {
                    Source = bmp,
                    Width = 80,
                    Height = 86,
                    Stretch = Stretch.Uniform,
                    HorizontalAlignment = HorizontalAlignment.Center,
                });
            }
            catch
            {
                stack.Children.Add(new TextBlock
                {
                    Text = config.CardFallbackEmoji, FontSize = 36,
                    HorizontalAlignment = HorizontalAlignment.Center,
                });
            }
        }
        else
        {
            stack.Children.Add(new TextBlock
            {
                Text = config.CardFallbackEmoji, FontSize = 36,
                HorizontalAlignment = HorizontalAlignment.Center,
            });
        }

        // ── 文字标签 ──
        var label = row.GetValueOrDefault(textKey, "");
        stack.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 12,
            Foreground = textPrimary,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 4, 0, 0),
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = 100,
        });

        border.Child = stack;

        // ── 交互 ──
        border.PointerEntered += (_, _) => border.Background = bgHover;
        border.PointerExited += (_, _) => border.Background = bgCard;
        border.PointerPressed += (_, _) => onClick(row);

        return border;
    }

    // ═══════════════════════════════════════════════════════
    //  单元格 / 工具栏创建（Table 模式共用）
    // ═══════════════════════════════════════════════════════

    private static Control CreateCell(ListColumn col, Dictionary<string, string> row, Action rebuild)
    {
        return col.Type switch
        {
            ListColumnType.Editable => CreateEditableCell(col, row),
            ListColumnType.Action => CreateActionCell(col, row, rebuild),
            _ => CreateTextCell(col, row),
        };
    }

    private static Control CreateTextCell(ListColumn col, Dictionary<string, string> row)
    {
        var fg = new SolidColorBrush(DialogHelper.GetColor("TextPrimary", 0xFF794f27));
        return new TextBlock
        {
            Text = row.GetValueOrDefault(col.Key, ""),
            FontSize = 11,
            Foreground = fg,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(4, 2),
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
    }

    private static Control CreateEditableCell(ListColumn col, Dictionary<string, string> row)
    {
        var fg = new SolidColorBrush(DialogHelper.GetColor("TextPrimary", 0xFF794f27));
        var bgPage = new SolidColorBrush(DialogHelper.GetColor("BgPage", 0xFFF5F0E8));
        var borderColor = new SolidColorBrush(DialogHelper.GetColor("BorderColor", 0xFFc4b89e));

        var textBlock = new TextBlock
        {
            Text = row.GetValueOrDefault(col.Key, ""),
            FontSize = 11,
            Foreground = fg,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(4, 2),
            Cursor = new Cursor(StandardCursorType.Hand),
        };

        var textBox = new TextBox
        {
            Text = row.GetValueOrDefault(col.Key, ""),
            FontSize = 11,
            Height = 26,
            Padding = new Thickness(6, 2),
            CornerRadius = new CornerRadius(4),
            Foreground = fg,
            Background = bgPage,
            BorderBrush = borderColor,
            IsVisible = false,
        };

        void CommitEdit()
        {
            var newVal = textBox.Text ?? "";
            row[col.Key] = newVal;
            textBlock.Text = newVal;
            textBlock.IsVisible = true;
            textBox.IsVisible = false;
            if (col.OnCellEdit != null)
                _ = col.OnCellEdit(row, newVal);
        }

        void StartEdit()
        {
            textBlock.IsVisible = false;
            textBox.IsVisible = true;
            textBox.Focus();
            textBox.SelectAll();
        }

        textBlock.PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(textBlock).Properties.IsLeftButtonPressed)
                StartEdit();
        };

        textBox.LostFocus += (_, _) => CommitEdit();
        textBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) CommitEdit();
            else if (e.Key == Key.Escape)
            {
                textBox.Text = row.GetValueOrDefault(col.Key, "");
                textBlock.IsVisible = true;
                textBox.IsVisible = false;
            }
        };

        var panel = new Grid { Children = { textBlock, textBox } };
        return panel;
    }

    private static Control CreateActionCell(ListColumn col, Dictionary<string, string> row, Action rebuild)
    {
        if (col.RowActions == null || col.RowActions.Count == 0)
            return new TextBlock { Text = "", FontSize = 11 };

        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(2, 0),
        };

        foreach (var act in col.RowActions)
        {
            // 支持 @key 从行数据动态读取 Emoji/Label
            var emoji = act.Emoji.StartsWith("@") && row.TryGetValue(act.Emoji[1..], out var e)
                ? e : act.Emoji;
            var label = act.Label.StartsWith("@") && row.TryGetValue(act.Label[1..], out var l)
                ? l : act.Label;

            if (act.Type == ListRowActionType.Dropdown && act.Children != null && act.Children.Count > 0)
            {
                // 下拉菜单按钮
                var btn = new Button
                {
                    Content = $"{emoji} {label} ▾",
                    FontSize = 11,
                    Height = 26,
                    Padding = new Thickness(6, 0),
                    CornerRadius = new CornerRadius(6),
                    Cursor = new Cursor(StandardCursorType.Hand),
                    Background = Brushes.Transparent,
                    BorderThickness = new Thickness(1),
                    BorderBrush = new SolidColorBrush(DialogHelper.GetColor("BorderColor", 0xFFc4b89e)),
                    Foreground = new SolidColorBrush(DialogHelper.GetColor("TextPrimary", 0xFF794f27)),
                };

                var menu = new ContextMenu();
                foreach (var child in act.Children)
                {
                    var childEmoji = child.Emoji.StartsWith("@") && row.TryGetValue(child.Emoji[1..], out var ce)
                        ? ce : child.Emoji;
                    var childLabel = child.Label.StartsWith("@") && row.TryGetValue(child.Label[1..], out var cl)
                        ? cl : child.Label;

                    var mi = new MenuItem
                    {
                        Header = $"{childEmoji} {childLabel}",
                        FontSize = 12,
                        Foreground = new SolidColorBrush(DialogHelper.GetColor("TextPrimary", 0xFF794f27)),
                    };
                    if (!string.IsNullOrEmpty(child.Tooltip))
                        ToolTip.SetTip(mi, child.Tooltip);
                    if (child.Callback != null)
                    {
                        var capturedRow = row;
                        mi.Click += async (_, _) =>
                        {
                            await child.Callback(capturedRow);
                            rebuild();
                        };
                    }
                    menu.Items.Add(mi);
                }

                btn.Click += (_, _) => menu.Open(btn);
                panel.Children.Add(btn);
            }
            else
            {
                // 普通按钮
                var btn = new Button
                {
                    Content = $"{emoji} {label}",
                    FontSize = 11,
                    Height = 26,
                    Padding = new Thickness(6, 0),
                    CornerRadius = new CornerRadius(6),
                    Cursor = new Cursor(StandardCursorType.Hand),
                    Background = Brushes.Transparent,
                    BorderThickness = new Thickness(1),
                    BorderBrush = new SolidColorBrush(DialogHelper.GetColor("BorderColor", 0xFFc4b89e)),
                    Foreground = new SolidColorBrush(DialogHelper.GetColor("TextPrimary", 0xFF794f27)),
                };

                if (!string.IsNullOrEmpty(act.Tooltip))
                    ToolTip.SetTip(btn, act.Tooltip);

                if (act.Callback != null)
                {
                    var capturedRow = row;
                    btn.Click += async (_, _) =>
                    {
                        await act.Callback(capturedRow);
                        rebuild();
                    };
                }

                panel.Children.Add(btn);
            }
        }

        return panel;
    }

    // ═══════════════════════════════════════════════════════
    //  工具栏项
    // ═══════════════════════════════════════════════════════

    internal static Control BuildToolbarItem(ListToolbarAction action)
    {
        if (action.Type == ListToolbarActionType.Dropdown)
        {
            var btn = new Button
            {
                Content = $"{action.Emoji} {action.Label} ▾",
                Height = 30,
                FontSize = 12,
                Padding = new Thickness(10, 0),
                CornerRadius = new CornerRadius(6),
                Cursor = new Cursor(StandardCursorType.Hand),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(DialogHelper.GetColor("BorderColor", 0xFFc4b89e)),
                Foreground = new SolidColorBrush(DialogHelper.GetColor("TextPrimary", 0xFF794f27)),
            };

            if (action.Children != null && action.Children.Count > 0)
            {
                var menu = new ContextMenu();
                foreach (var child in action.Children)
                {
                    var mi = new MenuItem
                    {
                        Header = $"{child.Emoji} {child.Label}",
                        FontSize = 12,
                        Foreground = new SolidColorBrush(DialogHelper.GetColor("TextPrimary", 0xFF794f27)),
                    };
                    if (child.Callback != null)
                    {
                        var captured = child;
                        mi.Click += (_, _) => _ = captured.Callback();
                    }
                    menu.Items.Add(mi);
                }
                btn.Click += (_, _) => menu.Open(btn);
            }

            return btn;
        }
        else
        {
            var btn = new Button
            {
                Content = $"{action.Emoji} {action.Label}",
                Height = 30,
                FontSize = 12,
                Padding = new Thickness(10, 0),
                CornerRadius = new CornerRadius(6),
                Cursor = new Cursor(StandardCursorType.Hand),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(DialogHelper.GetColor("BorderColor", 0xFFc4b89e)),
                Foreground = new SolidColorBrush(DialogHelper.GetColor("TextPrimary", 0xFF794f27)),
            };

            if (action.Callback != null)
                btn.Click += (_, _) => _ = action.Callback();

            return btn;
        }
    }
}
