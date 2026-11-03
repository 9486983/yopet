using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Lang.Avalonia;
using System.Runtime.CompilerServices;
using yopet.Core.Models;
using yopet.Sdk;
using yopet.ViewModels;
using yopet.UI.Components;
using yopet.Views;

namespace yopet;

public partial class PetWindow : Window
{
    private PetViewModel? _vm;
    private DateTime _lastClick = DateTime.MinValue;
    private bool _isDragging;
    private PixelPoint _dragStartWinPos; // 按下时的窗口 Position
    private PixelPoint _dragStartScreenMouse; // 按下时鼠标屏幕坐标
    private int _lastScreenX; // 上一帧鼠标屏幕 X 坐标
    private Window? _actionsWindow;

    public PetWindow()
    {
        InitializeComponent();
        var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        t.Tick += (_, _) => { if (IsVisible) Topmost = true; };
        t.Start();
    }

    /// <summary>气泡定位目标（供弹窗 Popup 定位用）</summary>
    public Control BubbleTarget => PetBody;

    /// <summary>在独立透明窗口中显示内容，避免 Popup 内无法创建 Arc 覆盖层。</summary>
    public Window ShowDialog(Control content) =>
        CreateDialogWindow(this, content);

    public static Window ShowDialogOn(Window owner, Control content) =>
        CreateDialogWindow(owner, content);

    private static Window CreateDialogWindow(Window owner, Control content, bool showClose = true)
    {
        var screen = owner.Screens.ScreenFromWindow(owner);
        var workArea = screen?.WorkingArea;
        var scaling = screen?.Scaling ?? 1.0;
        var maxWidth = (workArea is { } areaW ? areaW.Width - 16 : 1200) / scaling;
        var maxHeight = (workArea is { } areaH ? areaH.Height - 16 : 900) / scaling;
        var width = double.IsNaN(content.Width) ? 420 : Math.Min(content.Width, maxWidth);
        var estimatedHeight = double.IsNaN(content.Height) ? 600 : Math.Min(content.Height, maxHeight);

        var window = new Window
        {
            Width = width,
            MaxWidth = maxWidth,
            MaxHeight = maxHeight,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.Manual,
            WindowDecorations = WindowDecorations.None,
            CanResize = false,
            ShowInTaskbar = false,
            Topmost = true,
            Background = Brushes.Transparent,
            TransparencyLevelHint = [WindowTransparencyLevel.Transparent],
        };

        var shell = new Grid { Width = width };
        shell.Children.Add(content);

        if (showClose)
        {
            var dragBar = new Border
            {
                Height = 34,
                Background = Brushes.Transparent,
                Cursor = new Cursor(StandardCursorType.SizeAll),
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top,
            };
            dragBar.PointerPressed += (_, e) =>
            {
                if (!e.GetCurrentPoint(dragBar).Properties.IsLeftButtonPressed) return;
                window.BeginMoveDrag(e);
                e.Handled = true;
            };
            shell.Children.Add(dragBar);

            var closeText = new TextBlock
            {
                Text = "\u00d7",
                FontSize = 18,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            };
            TKCode.Arc.ArcTokens.Bind(closeText, TextBlock.ForegroundProperty,
                TKCode.Arc.DesignTokens.ArcDesignTokenKind.Foreground);

            var close = ArcActions.Ghost("");
            close.Content = closeText;
            close.Width = 32;
            close.Height = 32;
            close.Padding = new Thickness(0);
            close.HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center;
            close.VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Center;
            close.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right;
            close.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top;
            close.Margin = new Thickness(0, 6, 6, 0);
            close.Click += (_, _) => window.Close();
            dragBar.ZIndex = 90;
            close.ZIndex = 100;
            shell.Children.Add(close);
        }

        window.Content = shell;

        window.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
                window.Close();
        };

        void Fit()
        {
            if (workArea is not { } bounds) return;
            var actualWidth = (int)Math.Ceiling((window.Bounds.Width <= 1 ? width : window.Bounds.Width) * scaling);
            var actualHeight = (int)Math.Ceiling((window.Bounds.Height <= 1 ? estimatedHeight : window.Bounds.Height) * scaling);
            var x = owner.Position.X + (int)(owner.Bounds.Width * scaling / 2 - actualWidth / 2.0);
            var y = owner.Position.Y - (int)Math.Clamp(actualHeight + 24, 180, bounds.Height - 40);
            x = Math.Clamp(x, bounds.X + 8, bounds.Right - actualWidth - 8);
            y = Math.Clamp(y, bounds.Y + 8, bounds.Bottom - actualHeight - 8);
            window.Position = new PixelPoint(x, y);
        }

        window.Opened += (_, _) =>
            Dispatcher.UIThread.Post(Fit, DispatcherPriority.Loaded);
        window.SizeChanged += (_, _) => Fit();
        Fit();

        window.Show();
        return window;
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        _vm = DataContext as PetViewModel;
        if (_vm == null) return;

        SyncFrameDuration(_vm.AnimFrameDurationMs);
        _vm.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(PetViewModel.AnimFrameDurationMs))
                SyncFrameDuration(_vm.AnimFrameDurationMs);
            else if (args.PropertyName == nameof(PetViewModel.SpritesheetPath))
                UpdateBubbleOffset();
            else if (args.PropertyName == nameof(PetViewModel.IsTaskRunning))
            {
                if (_vm.IsTaskRunning) StartRingAnimation();
                else StopRingAnimation();
            }
        };
        Dispatcher.UIThread.Post(() => AdjustSize());
        Dispatcher.UIThread.Post(() => UpdateBubbleOffset(), DispatcherPriority.Loaded);
    }

    private void SyncFrameDuration(double ms) => PetSprite.FrameDurationMs = (int)Math.Round(ms);
    private void AdjustSize() { Width = 120; Height = 120; }

    private void UpdateBubbleOffset()
    {
        Dispatcher.UIThread.Post(() =>
        {
            var t = PetSprite.ContentTopY;
            if (t < 0) { BubblePopup.VerticalOffset = -8; return; }
            BubblePopup.VerticalOffset = Math.Min(-8 + t * (80.0 / PetSprite.FrameHeight), 0);
        }, DispatcherPriority.Loaded);
    }

    // ── 进度环动画 ──
    private DispatcherTimer? _ringTimer;

    private void StartRingAnimation()
    {
        StopRingAnimation();
        _ringTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        double angle = 0;
        _ringTimer.Tick += (_, _) =>
        {
            angle = (angle + 6) % 360;
            var rad = angle * Math.PI / 180;
            RingDot.Margin = new Thickness(10 * Math.Sin(rad), -10 * Math.Cos(rad), 0, 0);
        };
        _ringTimer.Start();
    }

    private void StopRingAnimation()
    {
        _ringTimer?.Stop();
        _ringTimer = null;
    }

    private void OnCancelTaskPointerPressed(object? sender, PointerPressedEventArgs e)
        => _vm?.CancelRunningTaskCommand.Execute(null);

    // ── 手动拖拽（计算方向 + 更新位置） ──
    private void OnWindowPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        try
        {
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
            _isDragging = true;
            _dragStartWinPos = Position;
            _dragStartScreenMouse = this.PointToScreen(e.GetCurrentPoint(this).Position);
            _lastScreenX = _dragStartScreenMouse.X;
            e.Pointer.Capture(this);
        }
        catch
        {
            // 弹窗/切换时控件可能短暂脱离视觉树，PointToScreen 会抛异常
            _isDragging = false;
        }
    }

    private void OnWindowPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_isDragging || _vm == null) return;

        var curPt = e.GetCurrentPoint(this);
        if (!curPt.Properties.IsLeftButtonPressed)
        {
            EndDrag();
            return;
        }

        // 窗口位置 = 起始窗口位置 + (当前鼠标屏幕坐标 - 起始鼠标屏幕坐标)
        var curScreen = this.PointToScreen(curPt.Position);
        Position = new PixelPoint(
            _dragStartWinPos.X + (curScreen.X - _dragStartScreenMouse.X),
            _dragStartWinPos.Y + (curScreen.Y - _dragStartScreenMouse.Y));

        // 根据鼠标屏幕坐标增量判断方向
        var dScreenX = curScreen.X - _lastScreenX;
        _lastScreenX = curScreen.X;
        if (Math.Abs(dScreenX) > 1)
            _vm.AnimCurrentRow = dScreenX > 0 ? 1 : 2;
    }

    private void OnWindowPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_isDragging) return;
        EndDrag();
    }

    private void EndDrag()
    {
        _isDragging = false;
        _lastScreenX = 0;
        _vm?.ScheduleIdleAfterDrag();
    }

    // ── 单击 / 双击 ──
    private void OnPetPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        try
        {
            var properties = e.GetCurrentPoint(this).Properties;
            if (properties.IsRightButtonPressed)
            {
                ShowPetActions();
                e.Handled = true;
                return;
            }
            if (!properties.IsLeftButtonPressed) return;
        }
        catch { return; }
        var now = DateTime.Now;
        if ((now - _lastClick).TotalMilliseconds < 350)
        {
            _lastClick = DateTime.MinValue;
            _vm?.OnPetDoubleClicked(); // 通知插件（事件池）
            OpenPetdex();
        }
        else
        {
            _lastClick = now;
            Dispatcher.UIThread.Post(async () =>
            {
                await Task.Delay(400);
                if ((DateTime.Now - _lastClick).TotalMilliseconds >= 380)
                {
                    _vm?.OnPetClicked(); // 通知插件（事件池）
                    _vm?.SingleClickCommand.Execute(null);
                }
            });
        }
    }

    // ── 悬浮提示（事件绑定与转发，业务由事件池协调） ──
    private void OnPetPointerEntered(object? sender, PointerEventArgs e)
        => _vm?.OnPetHoverEntered();

    private void OnPetPointerExited(object? sender, PointerEventArgs e)
        => _vm?.OnPetHoverExited();

    // ── 右键菜单 ──
    private void OnPetContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        ShowPetActions();
        e.Handled = true;
    }

    private void ShowPetActions()
    {
        if (_vm == null || _actionsWindow?.IsVisible == true) return;

        var panel = new StackPanel { Spacing = 0, Width = 220 };
        Window? actionWindow = null;

        void AddRow(string header, Action invoke, bool danger = false)
        {
            var button = danger ? ArcActions.Danger(header) : ArcActions.Ghost(header);
            button.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch;
            button.HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Left;
            button.MinHeight = 28;
            button.FontSize = 11.5;
            button.Padding = new Thickness(7, 0);
            button.Click += (_, _) =>
            {
                actionWindow?.Close();
                invoke();
            };
            panel.Children.Add(button);
        }

        void AddHeading(string text)
        {
            var heading = new TextBlock
            {
                Text = text,
                Margin = new Thickness(8, 7, 8, 3),
                TextWrapping = TextWrapping.Wrap,
                FontSize = 9.5,
                FontWeight = Avalonia.Media.FontWeight.SemiBold,
                Opacity = 0.72,
                IsHitTestVisible = false,
            };
            TKCode.Arc.ArcTokens.Bind(heading, TextBlock.ForegroundProperty,
                TKCode.Arc.DesignTokens.ArcDesignTokenKind.TextMuted);
            TKCode.Arc.ArcTokens.Bind(heading, TextBlock.FontSizeProperty,
                TKCode.Arc.DesignTokens.ArcDesignTokenKind.TypeXS);
            panel.Children.Add(heading);
        }

        void AddSeparator()
        {
            var line = new Border { Height = 1, Margin = new Thickness(5, 3) };
            TKCode.Arc.ArcTokens.Bind(line, Border.BackgroundProperty,
                TKCode.Arc.DesignTokens.ArcDesignTokenKind.BorderSubtle);
            panel.Children.Add(line);
        }

        if (_vm.IsSessionActive)
        {
            var session = _vm.CurrentSession;
            var title = session?.Title ?? I18nManager.Instance.GetResource("Localization.PetWindow.Session");
            var status = session?.Status ?? "";
            var statusSuffix = string.IsNullOrEmpty(status) ? "" : $"（{status}）";
            AddRow(string.Format(
                    I18nManager.Instance.GetResource("Localization.PetWindow.MenuEndSession"), title, statusSuffix),
                () => _vm.EndSessionCallback?.Invoke());
            AddSeparator();
        }
        else if (_vm.IsActivated)
        {
            var name = _vm.ActivatedFileAction?.Name ?? "";
            AddRow(string.Format(
                    I18nManager.Instance.GetResource("Localization.PetWindow.MenuUnlock"), name),
                () => _vm.DeactivateAction());
            AddSeparator();
        }

        AddRow(I18nManager.Instance.GetResource("Localization.PetWindow.MenuPetdex"), OpenPetdex);
        AddRow(I18nManager.Instance.GetResource("Localization.PetWindow.ReloadPlugins"),
            () => _vm?.ReloadPluginsCallback?.Invoke());
        AddSeparator();

        _vm.RefreshActionsCallback?.Invoke();
        if (_vm.Actions.Count > 0)
        {
            AddHeading(I18nManager.Instance.GetResource("Localization.PetWindow.MenuPlugins"));

            var groups = _vm.Actions
                .GroupBy(a => string.IsNullOrEmpty(a.Display?.Group?.Invoke() ?? a.Group)
                    ? ""
                    : (a.Display?.Group?.Invoke() ?? a.Group));

            foreach (var group in groups)
            {
                if (!string.IsNullOrEmpty(group.Key))
                    AddHeading(group.Key);

                foreach (var action in group)
                {
                    var name = action.Display?.Name?.Invoke() ?? action.Name;
                    AddRow($"{action.Emoji} {name}",
                        () => _vm?.PerformActionCommand.Execute(action));
                }
            }
            AddSeparator();
        }

        AddRow(I18nManager.Instance.GetResource("Localization.PetWindow.MenuClose"), Close, danger: true);

        var surface = new ArcSurface
        {
            Width = 240,
            Padding = new Thickness(6, 4, 2, 4),
            Child = new ScrollViewer
            {
                Content = panel,
                MaxHeight = Math.Min(440, (Screens.ScreenFromWindow(this)?.WorkingArea.Height ?? 800) - 48),
                HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            },
        };

        actionWindow = CreateDialogWindow(this, surface, showClose: false);
        actionWindow.Deactivated += (_, _) => actionWindow.Close();
        actionWindow.Closed += (_, _) =>
        {
            if (ReferenceEquals(_actionsWindow, actionWindow))
                _actionsWindow = null;
        };
        _actionsWindow = actionWindow;
    }

    private async void OpenPetdex()
    {
        if (_vm == null) return;
        _vm.ReloadPetdexPets();
        var pets = _vm.PetdexPets;

        // 确保缩略图已生成
        foreach (var pet in pets)
            ThumbnailCache.EnsureThumbnail(pet);

        var items = pets.Select(p => new Dictionary<string, string>
        {
            ["id"] = p.Id,
            ["name"] = p.Name,
            ["thumb_path"] = ThumbnailCache.GetThumbPath(p.Id),
        }).ToList();

        var config = new ListDialogConfig
        {
            LayoutMode = ListDialogLayoutMode.CardGrid,
            Title = I18nManager.Instance.GetResource("Localization.Petdex.Title"),
            Emoji = "🎮",
            CardTextKey = "name",
            CardFallbackEmoji = "🎮",
            CardImageProvider = row => row.GetValueOrDefault("thumb_path"),
            Items = items,
        };

        config.ToolbarActions.Add(new ListToolbarAction
        {
            Emoji = "⏬",
            Label = I18nManager.Instance.GetResource("Localization.Petdex.Install"),
            IsPrimary = true,
            Callback = async () =>
            {
                var petName = await InputDialog.ShowAsync(this,
                    I18nManager.Instance.GetResource("Localization.Petdex.InstallPetTitle"),
                    I18nManager.Instance.GetResource("Localization.Petdex.InstallPetPlaceholder"));
                if (string.IsNullOrEmpty(petName)) return;
                await InstallPet(petName, config);
            },
        });

        try
        {
            var selected = await ListDialog.ShowAsync(this, config);
            if (selected != null && selected.TryGetValue("id", out var petId))
            {
                // 确保弹窗完全关闭后再切换宠物
                await Task.Delay(50);
                _vm.ApplyPetById(petId);
                _vm.ShowReaction("✨");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Petdex] {ex}");
        }
    }

    private async Task InstallPet(string petName, ListDialogConfig config)
    {
        var output = new System.Text.StringBuilder();

        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/c npx --yes petdex install {petName}",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        using var proc = new System.Diagnostics.Process { StartInfo = psi };
        proc.OutputDataReceived += (_, e) => { if (e.Data != null) output.AppendLine(e.Data); };
        proc.ErrorDataReceived += (_, e) => { if (e.Data != null) output.AppendLine(e.Data); };
        proc.Start();
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();

        _vm?.ShowFileDropInfo(I18nManager.Instance.GetResource("Localization.PetWindow.InstallingTitle"),
            string.Format(I18nManager.Instance.GetResource("Localization.PetWindow.Installing"), petName));

        var exitTask = proc.WaitForExitAsync();
        var completed = await Task.WhenAny(exitTask, Task.Delay(120_000));
        if (completed != exitTask)
        {
            proc.Kill();
            _vm?.ShowFileDropInfo(I18nManager.Instance.GetResource("Localization.PetWindow.TimeoutTitle"),
                string.Format(I18nManager.Instance.GetResource("Localization.PetWindow.InstallTimeout"), petName));
            return;
        }

        if (proc.ExitCode == 0)
        {
            _vm?.ShowFileDropInfo(I18nManager.Instance.GetResource("Localization.PetWindow.InstalledTitle"),
                string.Format(I18nManager.Instance.GetResource("Localization.PetWindow.Installed"), petName));
            _vm?.ReloadPetdexPets();
            config.NotifyDataChanged();
        }
        else
        {
            var err = output.ToString().Trim();
            if (err.Length > 100) err = err[..100] + "...";
            _vm?.ShowFileDropInfo(I18nManager.Instance.GetResource("Localization.PetWindow.InstallFailedTitle"), err);
        }
    }

    // ── 文件拖放 ──
    private void OnDragEnter(object? sender, DragEventArgs e)
    {
        e.DragEffects = DragDropEffects.Copy;
        if (_vm == null) return;
        if (_vm.IsActivated) return;
        if (_vm.FileActions.Count == 0) return;

        var (exts, tp) = TryGetDropInfo(e);
        var actions = _vm.FileActions.Where(a => a.Matches(exts, tp)).ToList();
        if (actions.Count == 0) return;
        var b = PetBody.Bounds;
        var pt = this.PointToScreen(new Point(b.X + b.Width / 2, b.Y + b.Height / 2));
        Views.FileRadialMenu.ShowDuringDrag(this, actions, pt, act => _vm.ActivateAction(act));
    }

    private static (HashSet<string>, ItemType) TryGetDropInfo(DragEventArgs e)
    {
        try
        {
            var items = e.DataTransfer?.TryGetFiles();
            if (items == null || !items.Any()) return ([], ItemType.Both);
            var isDir = items[0] is IStorageFolder;
            var exts = new HashSet<string>();
            foreach (var item in items)
            {
                var ext = Path.GetExtension(item.Path.LocalPath)?.ToLowerInvariant();
                if (!string.IsNullOrEmpty(ext)) exts.Add(ext);
            }
            return (exts, isDir ? ItemType.Folder : ItemType.File);
        }
        catch { return ([], ItemType.Both); }
    }

    private static string[] ReadFilesSync(DragEventArgs e)
    {
        try
        {
            var items = e.DataTransfer?.TryGetFiles();
            if (items != null) return items.Select(i => i.Path.LocalPath).ToArray();
        }
        catch { }
        return [];
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = DragDropEffects.Copy;
    }

    private void OnDragLeave(object? sender, DragEventArgs e)
    {
        _vm?.ShowReaction("\U0001f44b");
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        if (_vm == null) return;
        if (!_vm.IsActivated || _vm.ActivatedFileAction?.ActionCallback == null) return;
        var files = ReadFilesSync(e);
        if (files.Length == 0) return;
        _vm.ShowReaction("\U0001f4cc");
        _ = _vm.ActivatedFileAction.ActionCallback(files);
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        var pos = Position;
        _vm?.SavePosition(pos.X, pos.Y);
        _vm?.Cleanup();
        base.OnClosing(e);
    }
}
