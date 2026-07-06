using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using yopet.Core.Models;
using yopet.Sdk;
using yopet.ViewModels;
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

    public PetWindow()
    {
        InitializeComponent();
        var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        t.Tick += (_, _) => { if (IsVisible) Topmost = true; };
        t.Start();
    }

    /// <summary>气泡定位目标（供弹窗 Popup 定位用）</summary>
    public Control BubbleTarget => PetBody;

    /// <summary>在 DialogPopup 中显示内容，返回控制权</summary>
    public Popup ShowDialog(Control content)
    {
        DialogContent.Content = content;
        DialogPopup.IsOpen = true;
        return DialogPopup;
    }

    public static Popup ShowDialogOn(Window owner, Control content)
    {
        if (owner is PetWindow pw)
            return pw.ShowDialog(content);
        return null!; // 非 PetWindow 时不应调用
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
        try { if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return; }
        catch { return; }
        var now = DateTime.Now;
        if ((now - _lastClick).TotalMilliseconds < 350)
        {
            _lastClick = DateTime.MinValue;
            OpenPetdex();
        }
        else
        {
            _lastClick = now;
            Dispatcher.UIThread.Post(async () =>
            {
                await Task.Delay(400);
                if ((DateTime.Now - _lastClick).TotalMilliseconds >= 380)
                    _vm?.SingleClickCommand.Execute(null);
            });
        }
    }

    // ── 右键菜单 ──
    private static SolidColorBrush ThemeBrush(string key, uint fallback = 0xFFFFFFFF)
    {
        if (Application.Current?.TryFindResource(key, out var v) == true && v is Color c)
            return new SolidColorBrush(c);
        return new SolidColorBrush(Color.Parse($"#{fallback:X8}"));
    }

    private void OnPetContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (_vm == null) return;
        var menu = new ContextMenu();

        // 会话中 → 显示结束会话（不论是否激活，因为会话可能来自右键菜单动作）
        if (_vm.IsSessionActive)
        {
            var session = _vm.CurrentSession;
            var title = session?.Title ?? "会话";
            var status = session?.Status ?? "";
            var statusSuffix = string.IsNullOrEmpty(status) ? "" : $"（{status}）";
            var endItem = new MenuItem
            {
                Header = $"⏹️ 结束{title}{statusSuffix}",
                FontSize = 13,
                Foreground = ThemeBrush("TextPrimary"),
            };
            endItem.Click += (_, _) => _vm.EndSessionCallback?.Invoke();
            menu.Items.Add(endItem);
            menu.Items.Add(new Separator());
        }
        // 普通激活（无会话）→ 显示解锁
        else if (_vm.IsActivated)
        {
            var n = _vm.ActivatedFileAction?.Name ?? "";
            var unlockItem = new MenuItem
            {
                Header = "\U0001f513 解锁「" + n + "」",
                FontSize = 13, Foreground = ThemeBrush("TextPrimary"),
            };
            unlockItem.Click += (_, _) => _vm.DeactivateAction();
            menu.Items.Add(unlockItem);
            menu.Items.Add(new Separator());
        }

        var dexItem = new MenuItem
        {
            Header = "\U0001f4d6 宠物图鉴",
            FontSize = 13, Foreground = ThemeBrush("TextPrimary"),
        };
        dexItem.Click += (_, _) => OpenPetdex();
        menu.Items.Add(dexItem);
        menu.Items.Add(new Separator());

        var groups = _vm.Actions.GroupBy(a => string.IsNullOrEmpty(a.Group) ? "" : a.Group).ToList();
        foreach (var group in groups)
        {
            if (string.IsNullOrEmpty(group.Key))
            {
                foreach (var a in group) menu.Items.Add(BuildMenuItem(a));
            }
            else
            {
                var sub = new MenuItem { Header = group.Key, FontSize = 13, Foreground = ThemeBrush("TextPrimary") };
                foreach (var a in group) sub.Items.Add(BuildMenuItem(a));
                menu.Items.Add(sub);
            }
        }
        menu.Items.Add(new Separator());

        var settingsItem = new MenuItem
        {
            Header = "⚙️ 打开设置", FontSize = 13, Foreground = ThemeBrush("TextPrimary"),
        };
        settingsItem.Click += (_, _) =>
        {
            var w = App.SettingsWindow;
            if (w != null) { w.Show(); w.WindowState = WindowState.Normal; w.Activate(); }
        };
        menu.Items.Add(settingsItem);
        menu.Items.Add(new Separator());

        var closeItem = new MenuItem
        {
            Header = "✕ 关闭宠物", FontSize = 13,
            Foreground = new SolidColorBrush(Color.Parse("#E81123")),
        };
        closeItem.Click += (_, _) => Close();
        menu.Items.Add(closeItem);

        menu.Open(sender as Control);
        e.Handled = true;
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
            Title = "Petdex 宠物",
            Emoji = "🎮",
            CardTextKey = "name",
            CardFallbackEmoji = "🎮",
            CardImageProvider = row => row.GetValueOrDefault("thumb_path"),
            Items = items,
        };

        config.ToolbarActions.Add(new ListToolbarAction
        {
            Emoji = "⏬",
            Label = "Install",
            Callback = async () =>
            {
                var petName = await InputDialog.ShowAsync(this, "安装宠物", "宠物名...");
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

        _vm?.ShowFileDropInfo("⏳ 安装中", $"正在安装 {petName}...");

        var exitTask = proc.WaitForExitAsync();
        var completed = await Task.WhenAny(exitTask, Task.Delay(120_000));
        if (completed != exitTask)
        {
            proc.Kill();
            _vm?.ShowFileDropInfo("⏱️ 超时", $"安装 {petName} 超时（120秒）");
            return;
        }

        if (proc.ExitCode == 0)
        {
            _vm?.ShowFileDropInfo("✅ 安装成功", $"{petName} 已安装");
            _vm?.ReloadPetdexPets();
            config.NotifyDataChanged();
        }
        else
        {
            var err = output.ToString().Trim();
            if (err.Length > 100) err = err[..100] + "...";
            _vm?.ShowFileDropInfo("❌ 安装失败", err);
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

    private MenuItem BuildMenuItem(PetActionConfig a)
    {
        var item = new MenuItem
        {
            Header = $"{a.Emoji} {a.Name}",
            FontSize = 13, Foreground = ThemeBrush("TextPrimary"),
        };
        ToolTip.SetTip(item, a.Description);
        var cap = a;
        item.Click += (_, _) => _vm?.PerformActionCommand.Execute(cap);
        return item;
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        var pos = Position;
        _vm?.SavePosition(pos.X, pos.Y);
        _vm?.Cleanup();
        base.OnClosing(e);
    }
}
