using static PinTopPlugin.Win32Native;
using static PinTopPlugin.Win32Const;

namespace PinTopPlugin;

/// <summary>半透明描边样式 —— 单窗口 + SetWindowRgn 圆角（稳定）</summary>
internal class BorderStyle : IOverlayStyle
{
    public bool IsPersistent => true;

    public IntPtr[]? Apply(IntPtr targetHwnd, OverlayConfig cfg)
    {
        if (string.IsNullOrEmpty(cfg.ClassName)) return null;

        var hi = GetModuleHandle(null);
        int ex = WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TOPMOST;
        var hwnd = CreateWindowExW(ex, cfg.ClassName, null, WS_POPUP,
            0, 0, 10, 10, IntPtr.Zero, IntPtr.Zero, hi, IntPtr.Zero);
        if (hwnd == IntPtr.Zero) return null;
        SetLayeredWindowAttributes(hwnd, 0, cfg.Alpha, LWA_ALPHA);
        return new[] { hwnd };
    }

    public void Update(IntPtr targetHwnd, IntPtr[] overlays, OverlayConfig cfg)
    {
        if (overlays.Length == 0 || overlays[0] == IntPtr.Zero) return;
        if (!GetWindowRect(targetHwnd, out var r)) return;

        int t = cfg.Thickness;
        int rad = cfg.Radius;
        int x = r.Left - t, y = r.Top - t;
        int w = r.Right - r.Left + 2 * t, h = r.Bottom - r.Top + 2 * t;
        if (w <= 0 || h <= 0) return;

        var hwnd = overlays[0];

        if (rad > 0 && w > 2 * rad && h > 2 * rad)
        {
            var outerRgn = CreateRoundRectRgn(0, 0, w, h, rad, rad);
            var innerRgn = CreateRoundRectRgn(t, t, w - t, h - t,
                Math.Max(0, rad - t), Math.Max(0, rad - t));
            CombineRgn(outerRgn, outerRgn, innerRgn, RGN_DIFF);
            SetWindowRgn(hwnd, outerRgn, false);
        }
        else
        {
            var outerRgn = CreateRectRgn(0, 0, w, h);
            var innerRgn = CreateRectRgn(t, t, w - t, h - t);
            CombineRgn(outerRgn, outerRgn, innerRgn, RGN_DIFF);
            SetWindowRgn(hwnd, outerRgn, false);
        }

        SetWindowPos(hwnd, HWND_TOPMOST, x, y, w, h, SWP_NOACTIVATE | SWP_SHOWWINDOW);
    }

    public void Remove(IntPtr[]? overlays)
    {
        if (overlays == null) return;
        foreach (var h in overlays)
            if (h != IntPtr.Zero) DestroyWindow(h);
    }
}
