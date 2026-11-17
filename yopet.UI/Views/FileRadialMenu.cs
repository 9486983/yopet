using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using TKCode.Arc;
using TKCode.Arc.DesignTokens;
using yopet.Core.Models;
using yopet.UI.Components;

namespace yopet.Views;

public class FileRadialMenu : Window
{
    private readonly List<FileActionConfig> _actions;
    private readonly PixelPoint _anchorCenter;
    private readonly List<Border> _items = [];
    private readonly List<Border> _highlights = [];
    private readonly bool[] _itemReady = [];
    private Border? _tipCard;
    private TextBlock? _tipTitle;
    private TextBlock? _tipDesc;
    private DispatcherTimer? _cursorTimer;
    private readonly Action<FileActionConfig>? _onActivate;
    private bool _closed;
    private int _hoveredIndex = -1;
    private int _activatedIndex = -1; // 防止重复激活
    private double _menuSize;
    private double _radius;
    private double _itemSize;

    private const double TipWidth = 120;
    private const double TipHeight = 56;
    private const int ItemAppearStaggerMs = 10;
    private const int ItemScaleDurationMs = 110;

    private FileRadialMenu(List<FileActionConfig> actions, PixelPoint anchorCenter, Action<FileActionConfig>? onActivate)
    {
        _actions = actions;
        _anchorCenter = anchorCenter;
        _onActivate = onActivate;

        var count = actions.Count;
        _itemSize = 56;

        // 均匀铺满整圈，并根据选项数量自动计算半径与窗口尺寸，保证选项之间不相交
        var tileRadius = _itemSize * Math.Sqrt(2) / 2;
        var step = count > 1 ? 2 * Math.PI / count : 0;
        var requiredRadius = count > 1
            ? (2 * tileRadius + 14) / (2 * Math.Sin(step / 2))
            : 0;
        _radius = count > 1 ? Math.Max(96, requiredRadius) : 0;
        _menuSize = Math.Max(260, 2 * (_radius + tileRadius + 14));

        Title = "";
        Width = _menuSize;
        Height = _menuSize;
        WindowDecorations = WindowDecorations.None;
        CanResize = false;
        ShowInTaskbar = false;
        Background = Brushes.Transparent;
        TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
        Topmost = true;
        Opacity = 0;
        DragDrop.SetAllowDrop(this, true);

        var scaling = this.RenderScaling;
        Position = new PixelPoint(
            _anchorCenter.X - (int)(_menuSize * scaling / 2),
            _anchorCenter.Y - (int)(_menuSize * scaling / 2));

        var canvas = new Canvas { Width = _menuSize, Height = _menuSize };
        DragDrop.SetAllowDrop(canvas, true);
        Content = canvas;

        var halfItem = _itemSize / 2;

        // 选项从 12 点方向开始，均匀分布在整圈
        for (var i = 0; i < count; i++)
        {
            var action = actions[i];
            var angle = -Math.PI / 2 + i * step;
            var cx = _menuSize / 2 + _radius * Math.Cos(angle);
            var cy = _menuSize / 2 + _radius * Math.Sin(angle);

            var nameBlock = new TextBlock
            {
                Text = YopetIcons.DisplayText(action.Name),
                FontSize = 10,
                HorizontalAlignment = HorizontalAlignment.Center,
                FontWeight = FontWeight.SemiBold,
                MaxWidth = _itemSize - 8,
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                MaxLines = 2,
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            ArcTokens.Bind(nameBlock, TextBlock.ForegroundProperty, ArcDesignTokenKind.Foreground);

            var content = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                Spacing = 2,
                Children =
                {
                    YopetIcons.Create(action.Emoji, 18),
                    nameBlock,
                },
            };

            var highlight = new Border
            {
                CornerRadius = new CornerRadius(12),
                Opacity = 0,
                IsHitTestVisible = false,
            };
            ArcTokens.Bind(highlight, Border.BackgroundProperty, ArcDesignTokenKind.AccentSubtle);

            var btn = new Border
            {
                Width = _itemSize,
                Height = _itemSize,
                CornerRadius = new CornerRadius(12),
                BorderThickness = new Thickness(1),
                Cursor = new Cursor(StandardCursorType.Hand),
                Opacity = 0,
                RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative),
                RenderTransform = new ScaleTransform(0.6, 0.6),
                Transitions = new Transitions
                {
                    new DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(100), Easing = new CubicEaseOut() },
                },
                Child = new Grid
                {
                    Children = { highlight, content },
                },
            };
            ArcTokens.Bind(btn, Border.BackgroundProperty, ArcDesignTokenKind.SurfaceRaised);
            ArcTokens.Bind(btn, Border.BorderBrushProperty, ArcDesignTokenKind.Border);

            Canvas.SetLeft(btn, cx - halfItem);
            Canvas.SetTop(btn, cy - halfItem);
            canvas.Children.Add(btn);
            _items.Add(btn);
            _highlights.Add(highlight);
        }

        // 悬停在某个选项上时，在菜单中心显示功能说明（始终位于宠物附近，不会被屏幕边缘裁掉）
        _tipTitle = new TextBlock
        {
            FontSize = 11,
            FontWeight = FontWeight.SemiBold,
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            MaxLines = 1,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        ArcTokens.Bind(_tipTitle, TextBlock.ForegroundProperty, ArcDesignTokenKind.Foreground);

        _tipDesc = new TextBlock
        {
            FontSize = 10,
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            MaxLines = 2,
            TextTrimming = TextTrimming.CharacterEllipsis,
            IsVisible = false,
        };
        ArcTokens.Bind(_tipDesc, TextBlock.ForegroundProperty, ArcDesignTokenKind.Foreground);

        _tipCard = new Border
        {
            Width = TipWidth,
            Height = TipHeight,
            Padding = new Thickness(8, 4),
            CornerRadius = new CornerRadius(10),
            BorderThickness = new Thickness(1),
            IsHitTestVisible = false,
            IsVisible = false,
            ZIndex = 20,
            Child = new StackPanel
            {
                Spacing = 1,
                VerticalAlignment = VerticalAlignment.Center,
                Children = { _tipTitle, _tipDesc },
            },
        };
        ArcTokens.Bind(_tipCard, Border.BackgroundProperty, ArcDesignTokenKind.SurfaceRaised);
        ArcTokens.Bind(_tipCard, Border.BorderBrushProperty, ArcDesignTokenKind.Accent);

        Canvas.SetLeft(_tipCard, (_menuSize - TipWidth) / 2);
        Canvas.SetTop(_tipCard, (_menuSize - TipHeight) / 2);
        canvas.Children.Add(_tipCard);

        _itemReady = new bool[_items.Count];

        void HandleDragOver(DragEventArgs e)
        {
            e.DragEffects = DragDropEffects.Copy;
            var pt = e.GetPosition(canvas);
            if (pt.X < 0 || pt.Y < 0 || pt.X > _menuSize || pt.Y > _menuSize)
            {
                HighlightNearest(new Point(-9999, -9999));
                return;
            }

            var idx = FindNearestIndex(pt);

            HighlightNearest(pt);

            // Ctrl + 悬浮 → 激活该选项（仅触发一次）
            if (idx >= 0 && idx != _activatedIndex && e.KeyModifiers.HasFlag(KeyModifiers.Control))
            {
                _activatedIndex = idx;
                // 激活视觉：金色边框，不覆盖 HighlightNearest 效果
                if (!_closed) _onActivate?.Invoke(_actions[idx]);
            }
        }

        // 同时挂到窗口与内容控件上，避免不同平台/窗口的拖放事件落点差异
        AddHandler(DragDrop.DragOverEvent, (_, e) => HandleDragOver(e));
        canvas.AddHandler(DragDrop.DragOverEvent, (_, e) => HandleDragOver(e));

        AddHandler(DragDrop.DropEvent, async (_, e) =>
        {
            e.DragEffects = DragDropEffects.Copy;
            var pt = e.GetPosition(canvas);
            var idx = FindNearestIndex(pt);
            if (idx >= 0 && idx < _actions.Count)
            {
                var files = await ReadFiles(e);
                if (files.Length > 0)
                {
                    // 先关菜单，再异步执行动作（避免动作中弹窗阻塞菜单关闭）
                    _closed = true;
                    var act = _actions[idx];
                    Close();
                    _ = Dispatcher.UIThread.InvokeAsync(async () =>
                    {
                        try { await act.ActionCallback!(files); }
                        catch { }
                    });
                    return;
                }
            }
            Close();
        });

        // 鼠标离开窗口时延迟关闭（300ms），如果在此期间 DragEnter 回来则取消关闭
        // 避免从 PetWindow 移到菜单时误关闭，同时确保取消拖放时能自动关
        CancellationTokenSource? leaveCts = null;
        AddHandler(DragDrop.DragLeaveEvent, (_, _) =>
        {
            leaveCts?.Cancel();
            leaveCts = new CancellationTokenSource();
            var token = leaveCts.Token;
            _ = Task.Delay(300, token).ContinueWith(_ =>
            {
                if (!token.IsCancellationRequested)
                    Dispatcher.UIThread.Post(Close);
            }, token);
        });
        AddHandler(DragDrop.DragEnterEvent, (_, _) =>
        {
            leaveCts?.Cancel();
        });

        Deactivated += (_, _) =>
        {
            StopCursorTracking();
            Close();
        };

        Closed += (_, _) => StopCursorTracking();

        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) Close();
        };

        this.Transitions = new Transitions
        {
            new DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(80) },
        };
    }

    public void Open()
    {
        Show();
        Opacity = 1;
        _ = AnimateItemsIn();
        StartCursorTracking();
    }

    private async Task AnimateItemsIn()
    {
        StartFloating();

        for (var i = 0; i < _items.Count; i++)
        {
            await Task.Delay(ItemAppearStaggerMs);
            _items[i].Opacity = 1;
            _ = AnimateItemIn(_items[i], i);
        }
    }

    private async Task AnimateItemIn(Border item, int index)
    {
        await AnimateScale(item, 0.6, 1.0, ItemScaleDurationMs, new CubicEaseOut());
        _itemReady[index] = true;
    }

    private static async Task AnimateScale(Border item, double from, double to, int ms, Easing easing)
    {
        var frames = ms / 16;
        for (var f = 0; f <= frames; f++)
        {
            var t = easing.Ease((double)f / frames);
            var s = from + (to - from) * t;
            item.RenderTransform = new ScaleTransform(s, s);
            await Task.Delay(16);
        }
        item.RenderTransform = new ScaleTransform(to, to);
    }

    private void StartFloating()
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(30) };
        var startTime = DateTime.UtcNow;
        timer.Tick += (_, _) =>
        {
            var t = (DateTime.UtcNow - startTime).TotalSeconds;
            for (var i = 0; i < _items.Count; i++)
            {
                if (!_itemReady[i]) continue;
                var phase = t * 2 * Math.PI / 1.8 + i * Math.PI / 3;
                var offset = Math.Sin(phase) * 2;
                _items[i].RenderTransform = new TransformGroup
                {
                    Children = new Transforms
                    {
                        new ScaleTransform(1, 1),
                        new TranslateTransform(0, offset),
                    }
                };
            }
        };
        timer.Start();
    }

    private void HighlightNearest(Point pt)
    {
        var idx = FindNearestIndex(pt);
        if (idx == _hoveredIndex) return;

        if (_hoveredIndex >= 0 && _hoveredIndex < _highlights.Count)
        {
            _highlights[_hoveredIndex].Opacity = 0;
            _items[_hoveredIndex].BorderThickness = new Thickness(1);
        }
        if (idx >= 0 && idx < _highlights.Count)
        {
            _highlights[idx].Opacity = 1;
            _items[idx].BorderThickness = new Thickness(2);
        }
        _hoveredIndex = idx;
        UpdateTip(idx);
    }

    private void UpdateTip(int idx)
    {
        if (_tipCard == null || _tipTitle == null || _tipDesc == null) return;

        if (idx < 0 || idx >= _actions.Count || idx >= _items.Count)
        {
            _tipCard.IsVisible = false;
            return;
        }

        var action = _actions[idx];
        _tipTitle.Text = YopetIcons.DisplayText(action.Name);
        _tipDesc.Text = action.Description ?? "";
        _tipDesc.IsVisible = !string.IsNullOrWhiteSpace(_tipDesc.Text);
        _tipCard.IsVisible = true;
    }

    private int FindNearestIndex(Point pt)
    {
        var halfItem = _itemSize / 2;
        var best = -1;
        var bestDist = double.MaxValue;
        for (var i = 0; i < _items.Count; i++)
        {
            var left = Canvas.GetLeft(_items[i]) + halfItem;
            var top = Canvas.GetTop(_items[i]) + halfItem;
            var dx = pt.X - left;
            var dy = pt.Y - top;
            var dist = dx * dx + dy * dy;
            if (dist < bestDist) { bestDist = dist; best = i; }
        }

        var threshold = _itemSize * 0.8;
        return bestDist <= threshold * threshold ? best : -1;
    }

    private static Task<string[]> ReadFiles(DragEventArgs e)
    {
        try
        {
            var items = e.DataTransfer?.TryGetFiles();
            if (items != null) return Task.FromResult(items.Select(i => i.Path.LocalPath).ToArray());
        }
        catch { }
        return Task.FromResult(Array.Empty<string>());
    }

    private async Task ExecuteAction(FileActionConfig action, string[] files)
    {
        if (_closed) return;
        _closed = true;
        try
        {
            if (action.ActionCallback != null) await action.ActionCallback(files);
        }
        catch { }
        Close();
    }

    public static void ShowDuringDrag(Window owner, List<FileActionConfig> actions, PixelPoint anchorCenter, Action<FileActionConfig>? onActivate = null)
    {
        if (actions.Count == 0) return;
        var menu = new FileRadialMenu(actions, anchorCenter, onActivate);
        menu.Open();
    }

    // 原生拖放过程中部分环境下 DragOver 不会触发视觉更新；
    // 因此额外用系统光标位置定时刷新悬停项，保证 tips 实时显示。
    private void StartCursorTracking()
    {
        if (!OperatingSystem.IsWindows() || _cursorTimer != null) return;

        _cursorTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(40) };
        _cursorTimer.Tick += (_, _) =>
        {
            if (_closed || !IsVisible) return;

            try
            {
                if (!GetCursorPos(out var screenPoint)) return;
                var pt = this.PointToClient(new PixelPoint(screenPoint.X, screenPoint.Y));

                if (pt.X < 0 || pt.Y < 0 || pt.X > _menuSize || pt.Y > _menuSize)
                {
                    // 光标不在菜单内：清除高亮并隐藏说明
                    HighlightNearest(new Point(-9999, -9999));
                    return;
                }

                HighlightNearest(pt);
            }
            catch
            {
                // 窗口正在关闭/句柄失效时忽略
            }
        };
        _cursorTimer.Start();
    }

    private void StopCursorTracking()
    {
        _cursorTimer?.Stop();
        _cursorTimer = null;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out NativePoint point);
}
