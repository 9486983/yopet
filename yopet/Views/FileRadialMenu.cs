using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using yopet.Core.Models;

namespace yopet.Views;

public class FileRadialMenu : Window
{
    private readonly List<FileActionConfig> _actions;
    private readonly PixelPoint _anchorCenter;
    private readonly List<Border> _items = [];
    private readonly bool[] _itemReady = [];
    private readonly Action<FileActionConfig>? _onActivate;
    private bool _closed;
    private int _hoveredIndex = -1;
    private int _activatedIndex = -1; // 防止重复激活

    private const double MenuSize = 300;
    private const double Radius = 88;
    private const double ItemSize = 72;

    // 全圆分 12 份（以时钟为参考），ArcPosition 指定从第几份开始
    // 选项依次占据连续的 1 份位置，不跳格
    private const int TotalParts = 12;
    private const int ArcPosition = 0;   // 0=12点  1=1点  2=2点  ... 11=11点

    private static double ArcStep => 2 * Math.PI / TotalParts;
    private static double ArcCenterAngle => -Math.PI / 2 + ArcPosition * ArcStep;

    private static readonly Lazy<Color> BgOverlay = new(() => GetColor("BgOverlay", 0xCC2C2420));
    private static readonly Lazy<Color> BorderColor = new(() => GetColor("BorderColor", 0xFF5D4F45));
    private static readonly Lazy<Color> TextPrimary = new(() => GetColor("TextPrimary", 0xFFF0E6D3));
    private static readonly Lazy<Color> AccentPrimary = new(() => GetColor("AccentPrimary", 0xFF19c8b9));
    private static readonly Lazy<Color> BgHover = new(() => GetColor("BgHover", 0xFF4D3F37));

    private FileRadialMenu(List<FileActionConfig> actions, PixelPoint anchorCenter, Action<FileActionConfig>? onActivate)
    {
        _actions = actions;
        _anchorCenter = anchorCenter;
        _onActivate = onActivate;

        Title = "";
        Width = MenuSize;
        Height = MenuSize;
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
            _anchorCenter.X - (int)(MenuSize * scaling / 2),
            _anchorCenter.Y - (int)(MenuSize * scaling / 2));

        var canvas = new Canvas { Width = MenuSize, Height = MenuSize };
        Content = canvas;

        var count = actions.Count;
        var baseBg = new SolidColorBrush(BgOverlay.Value);
        var baseBorder = new SolidColorBrush(BorderColor.Value);
        var textClr = new SolidColorBrush(TextPrimary.Value);

        var halfItem = ItemSize / 2;

        // 每个选项占据 1 份位置，从 ArcPosition 开始顺时针排列
        for (var i = 0; i < count; i++)
        {
            var action = actions[i];
            var angle = ArcCenterAngle + i * ArcStep;
            var cx = MenuSize / 2 + Radius * Math.Cos(angle);
            var cy = MenuSize / 2 + Radius * Math.Sin(angle);

            var btn = new Border
            {
                Width = ItemSize,
                Height = ItemSize,
                CornerRadius = new CornerRadius(halfItem),
                Background = baseBg,
                BorderThickness = new Thickness(2),
                BorderBrush = baseBorder,
                Cursor = new Cursor(StandardCursorType.Hand),
                Opacity = 0,
                RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative),
                RenderTransform = new ScaleTransform(0.6, 0.6),
                Transitions = new Transitions
                {
                    new DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(250), Easing = new CubicEaseOut() },
                },
                Child = new StackPanel
                {
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Spacing = 2,
                    Children =
                    {
                        new TextBlock { Text = action.Emoji, FontSize = 24, HorizontalAlignment = HorizontalAlignment.Center },
                        new TextBlock
                        {
                            Text = action.Name, FontSize = 11,
                            Foreground = textClr,
                            HorizontalAlignment = HorizontalAlignment.Center,
                            FontWeight = FontWeight.SemiBold,
                        },
                    },
                },
            };

            Canvas.SetLeft(btn, cx - halfItem);
            Canvas.SetTop(btn, cy - halfItem);
            canvas.Children.Add(btn);
            _items.Add(btn);
        }
        _itemReady = new bool[_items.Count];

        AddHandler(DragDrop.DragOverEvent, (_, e) =>
        {
            e.DragEffects = DragDropEffects.Copy;
            var pt = e.GetPosition(canvas);
            var idx = FindNearestIndex(pt);

            HighlightNearest(pt);

            // Ctrl + 悬浮 → 激活该选项（仅触发一次）
            if (idx >= 0 && idx != _activatedIndex && e.KeyModifiers.HasFlag(KeyModifiers.Control))
            {
                _activatedIndex = idx;
                // 激活视觉：金色边框，不覆盖 HighlightNearest 效果
                if (!_closed) _onActivate?.Invoke(_actions[idx]);
            }
        });

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

        Deactivated += (_, _) => Close();

        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) Close();
        };

        this.Transitions = new Transitions
        {
            new DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(150) },
        };
    }

    public void Open()
    {
        Show();
        Opacity = 1;
        _ = AnimateItemsIn();
    }

    private async Task AnimateItemsIn()
    {
        StartFloating();

        for (var i = 0; i < _items.Count; i++)
        {
            var delay = Math.Max(10, 30 - i * 10);
            var scaleMs = Math.Max(60, 180 - i * 50);
            await Task.Delay(delay);
            _items[i].Opacity = 1;
            await AnimateScale(_items[i], 0.6, 1.0, scaleMs, new CubicEaseOut());
            _itemReady[i] = true;
        }
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
                var offset = Math.Sin(phase) * 5;
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
        if (_hoveredIndex >= 0 && _hoveredIndex < _items.Count)
        {
            _items[_hoveredIndex].Background = new SolidColorBrush(BgOverlay.Value);
            _items[_hoveredIndex].BorderBrush = new SolidColorBrush(BorderColor.Value);
        }
        if (idx >= 0 && idx < _items.Count)
        {
            _items[idx].Background = new SolidColorBrush(BgHover.Value);
            _items[idx].BorderBrush = new SolidColorBrush(AccentPrimary.Value);
        }
        _hoveredIndex = idx;
    }

    private int FindNearestIndex(Point pt)
    {
        var halfItem = ItemSize / 2;
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
        return bestDist <= 55 * 55 ? best : -1;
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

    private static Color GetColor(string key, uint fallback)
    {
        if (Application.Current?.TryFindResource(key, out var value) == true && value is Color c)
            return c;
        return Color.Parse($"#{fallback:X8}");
    }
}
