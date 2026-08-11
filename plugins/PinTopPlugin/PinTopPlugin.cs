using System.Runtime.InteropServices;
using System.Text;
using Lang.Avalonia;
using yopet.Core.Models;
using yopet.Sdk;
using static PinTopPlugin.Win32Native;
using static PinTopPlugin.Win32Const;

namespace PinTopPlugin;

[Plugin("窗口置顶器", Version = "1.1.0", Description = "Ctrl+Alt+T 置顶/取消置顶当前窗口，带半透明边框指示")]
public class PinTopPlugin : PluginBase
{
    private IPluginHost? _host;
    private Thread? _pump;
    private IntPtr _msgWnd;
    private int _hotKeyId;
    private readonly HashSet<IntPtr> _pinned = new();
    private readonly OverlayManager _overlayMgr;
    private OverlayConfig _overlayCfg = new();
    private ListDialogConfig? _activeListConfig;
    private WndProcDelegate? _wndProc;
    private WndProcDelegate? _overlayWndProc;

    /// <summary>取当前语言的插件词条</summary>
    private static string T(string key) =>
        I18nManager.Instance.GetResource($"Localization.PinTopPlugin.{key}");

    public override string Name => T("Name");

    public PinTopPlugin()
    {
        _overlayMgr = new OverlayManager(_pinned);
    }

    public override Task InitializeAsync(IPluginHost host)
    {
        _host = host;
        host.RegisterConfig(new PluginConfigSection { Key = "pintop", Title = T("Name"), Emoji = "📌",
            Fields = new() {
                new() { Key = "pt_enabled", Label = T("EnabledLabel"), Type = PluginConfigFieldType.Boolean, DefaultValue = "true" },
                new() { Key = "pt_modifiers", Label = T("ModifiersLabel"), Type = PluginConfigFieldType.String, DefaultValue = "Ctrl+Alt" },
                new() { Key = "pt_key", Label = T("MainKeyLabel"), Type = PluginConfigFieldType.String, DefaultValue = "T" },
                new() { Key = "pt_border_enabled", Label = T("BorderEnabledLabel"), Type = PluginConfigFieldType.Boolean, DefaultValue = "true" },
                new() { Key = "pt_border_style", Label = T("BorderStyleLabel"), Type = PluginConfigFieldType.Dropdown, DefaultValue = "border",
                    Options = new() {
                        new() { Label = T("StyleBorder"), Value = "border" },
                        new() { Label = T("StyleCorner"), Value = "corner" },
                        new() { Label = T("StyleFlash"), Value = "flash" },
                    } },
                new() { Key = "pt_border_color", Label = T("BorderColorLabel"), Type = PluginConfigFieldType.String, DefaultValue = "#FFD700" },
                new() { Key = "pt_border_alpha", Label = T("BorderAlphaLabel"), Type = PluginConfigFieldType.Number, DefaultValue = "140", MinValue = 0, MaxValue = 255 },
                new() { Key = "pt_border_thickness", Label = T("BorderThicknessLabel"), Type = PluginConfigFieldType.Number, DefaultValue = "3", MinValue = 1, MaxValue = 20 },
                new() { Key = "pt_border_radius", Label = T("BorderRadiusLabel"), Type = PluginConfigFieldType.Number, DefaultValue = "8", MinValue = 0, MaxValue = 30 },
            }}, Name);
        host.RegisterAction(new PluginAction { Name = T("SettingsAction"), Emoji = "📌", Group = T("Group"), Target = ActionTarget.ContextMenu,
            Display = LocalizedDisplay.Of(name: () => T("SettingsAction"), group: () => T("Group")),
            Callback = () => { host.ShowConfigDialog("pintop"); return Task.CompletedTask; } });

        host.RegisterAction(new PluginAction
        {
            Name = T("PinnedWindows"),
            Emoji = "📌",
            Group = T("Group"),
            Display = LocalizedDisplay.Of(name: () => T("PinnedWindows"), group: () => T("Group")),
            Target = ActionTarget.ContextMenu,
            Callback = () => ShowPinnedList(host),
        });

        LoadOverlaySettings();
        host.ConfigValueChanged += OnConfigChanged;

        if (host.GetConfig("pt_enabled") != "false")
        {
            var mod = ParseMod(host.GetConfig("pt_modifiers") ?? "Ctrl+Alt");
            var key = ParseKey(host.GetConfig("pt_key") ?? "T");
            StartPump(mod, key);
        }
        host.Log("窗口置顶器插件已加载");
        return Task.CompletedTask;
    }

    public override Task CleanupAsync()
    {
        if (_host != null) _host.ConfigValueChanged -= OnConfigChanged;

        if (_msgWnd != IntPtr.Zero)
            KillTimer(_msgWnd, FOCUS_TIMER_ID);

        _overlayMgr.Cleanup();

        foreach (var h in _pinned.ToList())
            SetWindowPos(h, HWND_NOTOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE);
        _pinned.Clear();

        if (_pump?.ManagedThreadId > 0)
            PostThreadMessage((uint)_pump.ManagedThreadId, 0x0012, IntPtr.Zero, IntPtr.Zero);
        return base.CleanupAsync();
    }

    // ── 配置 ──

    private void LoadOverlaySettings()
    {
        var host = _host;
        if (host == null) return;

        var hex = (host.GetConfig("pt_border_color") ?? "#FFD700").TrimStart('#');
        if (uint.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out var rgb))
        {
            var r = (rgb >> 16) & 0xFF;
            var g = (rgb >> 8) & 0xFF;
            var b = rgb & 0xFF;
            _overlayCfg.ColorBgr = (b << 16) | (g << 8) | r;
        }
        if (byte.TryParse(host.GetConfig("pt_border_alpha") ?? "140", out var a)) _overlayCfg.Alpha = a;
        if (int.TryParse(host.GetConfig("pt_border_thickness") ?? "3", out var t) && t >= 1) _overlayCfg.Thickness = t;
        if (int.TryParse(host.GetConfig("pt_border_radius") ?? "8", out var radius) && radius >= 0) _overlayCfg.Radius = radius;
    }

    private bool IsBorderEnabled() => (_host?.GetConfig("pt_border_enabled") ?? "true") != "false";
    private string BorderStyleName() => _host?.GetConfig("pt_border_style") ?? "border";

    private void OnConfigChanged(object? sender, string key)
    {
        if (key is not ("pt_border_enabled" or "pt_border_style" or "pt_border_color"
            or "pt_border_alpha" or "pt_border_thickness" or "pt_border_radius")) return;

        LoadOverlaySettings();
        if (_msgWnd != IntPtr.Zero)
            PostMessage(_msgWnd, WM_REFRESH_OVERLAYS, IntPtr.Zero, IntPtr.Zero);
    }

    // ── 消息泵 ──

    private void StartPump(uint mod, uint key)
    {
        _hotKeyId = GetHashCode();
        _wndProc = WndProc;
        _overlayWndProc = OverlayWndProc;
        var cn = $"PinTop_{_hotKeyId}";
        _overlayMgr.OverlayClassName = $"PinTopOverlay_{_hotKeyId}";
        var hi = GetModuleHandle(null);
        var wndProcPtr = Marshal.GetFunctionPointerForDelegate(_wndProc);
        var overlayWndProcPtr = Marshal.GetFunctionPointerForDelegate(_overlayWndProc);

        _pump = new Thread(() =>
        {
            try
            {
                var wc = new WNDCLASSW { lpfnWndProc = wndProcPtr, hInstance = hi, lpszClassName = cn };
                RegisterClassW(ref wc);
                _msgWnd = CreateWindowExW(0, cn, "", 0, 0, 0, 0, 0, IntPtr.Zero, IntPtr.Zero, hi, IntPtr.Zero);
                if (_msgWnd == IntPtr.Zero) return;
                RegisterHotKey(_msgWnd, _hotKeyId, mod, key);

                // 注册 overlay 窗口类
                var overlayClass = new WNDCLASSW
                {
                    lpfnWndProc = overlayWndProcPtr,
                    hInstance = hi,
                    lpszClassName = _overlayMgr.OverlayClassName,
                };
                RegisterClassW(ref overlayClass);

                // 注册事件钩子
                _overlayMgr.RegisterEventHandlers();

                // 初始化 overlay 系统
                _overlayCfg.ClassName = _overlayMgr.OverlayClassName;
                _overlayCfg.StyleName = BorderStyleName();
                _overlayMgr.RefreshAll(_overlayCfg, IsBorderEnabled() ? BorderStyleName() : "none", _msgWnd);

                // 启动聚焦轮询（每 200ms 检查前台窗口是否为置顶窗口）
                SetTimer(_msgWnd, FOCUS_TIMER_ID, 200, IntPtr.Zero);

                while (GetMessage(out var m, IntPtr.Zero, 0, 0) != 0)
                {
                    try { TranslateMessage(ref m); DispatchMessage(ref m); }
                    catch { }
                }
            }
            catch { }
        }) { IsBackground = true, Name = "PinTop" };
        _pump.Start();
    }

    private IntPtr WndProc(IntPtr h, int m, IntPtr w, IntPtr l)
    {
        if (m == WM_HOTKEY && (int)w == _hotKeyId)
        {
            try { OnHotKey(); }
            catch { }
            return IntPtr.Zero;
        }
        if (m == WM_REMOVE_OVERLAY)
        {
            try { _overlayMgr.Remove(w); }
            catch { }
            return IntPtr.Zero;
        }
        if (m == WM_UNPIN)
        {
            try
            {
                SetWindowPos(w, HWND_NOTOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE);
                _pinned.Remove(w);
                _overlayMgr.Remove(w);
                _host?.ShowThought(T("UnpinnedTitle"), GetTitle(w));
                _activeListConfig?.NotifyDataChanged();
            }
            catch { }
            return IntPtr.Zero;
        }
        if (m == WM_REFRESH_OVERLAYS)
        {
            try
            {
                var styleName = BorderStyleName();
                var enabled = IsBorderEnabled();
                _overlayCfg.StyleName = styleName;
                _overlayCfg.ClassName = _overlayMgr.OverlayClassName;
                _overlayMgr.RefreshAll(_overlayCfg, enabled ? styleName : "none", _msgWnd);
            }
            catch { }
            return IntPtr.Zero;
        }
        if (m == WM_TIMER && w == FOCUS_TIMER_ID)
        {
            try
            {
                // 前台窗口若是置顶窗口，提到 TOPMOST 组最前
                var fg = GetForegroundWindow();
                if (fg != IntPtr.Zero && _pinned.Contains(fg))
                    SetWindowPos(fg, HWND_TOP, 0, 0, 0, 0,
                        SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
            }
            catch { }
            return IntPtr.Zero;
        }
        return DefWindowProcW(h, m, w, l);
    }

    private IntPtr OverlayWndProc(IntPtr h, int m, IntPtr w, IntPtr l)
    {
        if (m == WM_ERASEBKGND)
        {
            try
            {
                var dc = w;
                if (GetClientRect(h, out var rc) && _overlayCfg.Brush != IntPtr.Zero)
                    FillRect(dc, ref rc, _overlayCfg.Brush);
            }
            catch { }
            return new IntPtr(1);
        }
        return DefWindowProcW(h, m, w, l);
    }

    // ── 热键 ──

    private void OnHotKey()
    {
        var hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return;

        var host = _host;
        if (host == null) return;

        if (_pinned.Contains(hwnd))
        {
            // 移除 TOPMOST 样式（不移动窗口，减少 DWM 干扰）
            int ex = GetWindowLong(hwnd, GWL_EXSTYLE);
            SetWindowLong(hwnd, GWL_EXSTYLE, ex & ~WS_EX_TOPMOST);

            _pinned.Remove(hwnd);
            _overlayMgr.Remove(hwnd);
            host.ShowThought(T("UnpinnedTitle"), GetTitle(hwnd));
        }
        else
        {
            if (!SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE))
            {
                host.ShowThought(T("PinFailTitle"), string.Format(T("PinFailMsg"), GetTitle(hwnd)));
                return;
            }
            _pinned.Add(hwnd);
            if (IsBorderEnabled())
            {
                _overlayCfg.ClassName = _overlayMgr.OverlayClassName;
                _overlayMgr.Create(hwnd);
            }
            host.ShowThought(T("PinnedTitle"), GetTitle(hwnd));
        }
        _activeListConfig?.NotifyDataChanged();
    }

    // ── 工具 ──

    private static string GetTitle(IntPtr h)
    {
        var sb = new StringBuilder(256);
        GetWindowText(h, sb, sb.Capacity);
        return sb.ToString();
    }

    static uint ParseMod(string s)
    {
        uint m = 0;
        foreach (var p in s.Split('+', StringSplitOptions.TrimEntries))
            m |= p.ToLowerInvariant() switch { "ctrl" => 0x0002u, "alt" => 0x0001u, "shift" => 0x0004u, "win" => 0x0008u, _ => 0 };
        return m;
    }

    static uint ParseKey(string s)
    {
        s = s.Trim().ToUpperInvariant();
        if (s.Length == 1 && s[0] >= 'A' && s[0] <= 'Z') return (uint)s[0];
        return 0x54u;
    }

    // ── 列表弹窗 ──

    private async Task ShowPinnedList(IPluginHost host)
    {
        if (_pinned.Count == 0)
        {
            host.ShowThought(T("PinnedListTitle"), T("EmptyListMsg"));
            return;
        }

        if (_activeListConfig != null) return;

        var config = new ListDialogConfig
        {
            Title = T("PinnedWindows"),
            Emoji = "📌",
            DataSource = () =>
            {
                var valid = _pinned.Where(IsWindow).ToList();
                foreach (var h in _pinned.Except(valid).ToList())
                {
                    _pinned.Remove(h);
                    if (_msgWnd != IntPtr.Zero)
                        PostMessage(_msgWnd, WM_REMOVE_OVERLAY, h, IntPtr.Zero);
                }
                return Task.FromResult(valid.Select(h => new Dictionary<string, string>
                {
                    { "hwnd", h.ToString() },
                    { "title", GetTitle(h) },
                }).ToList());
            },
            Columns = new()
            {
                new() { Key = "title", Header = T("ColTitle") },
                new()
                {
                    Key = "actions", Header = "",
                    Type = ListColumnType.Action,
                    RowActions = new()
                    {
                        new()
                        {
                            Label = T("UnpinAction"), Emoji = "🔓",
                            Callback = row =>
                            {
                                if (IntPtr.TryParse(row["hwnd"], out var h) && _msgWnd != IntPtr.Zero)
                                    PostMessage(_msgWnd, WM_UNPIN, h, IntPtr.Zero);
                                return Task.CompletedTask;
                            },
                        },
                    },
                },
            },
        };

        _activeListConfig = config;
        await host.ShowListDialog(config);
        _activeListConfig = null;
    }
}
