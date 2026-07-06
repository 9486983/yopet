using static PinTopPlugin.Win32Native;
using static PinTopPlugin.Win32Const;

namespace PinTopPlugin;

/// <summary>四角图钉样式 —— 每角一个方块</summary>
internal class CornerStyle : IOverlayStyle
{
    public bool IsPersistent => true;

    public IntPtr[]? Apply(IntPtr targetHwnd, OverlayConfig cfg)
    {
        if (string.IsNullOrEmpty(cfg.ClassName)) return null;

        var hi = GetModuleHandle(null);
        int ex = WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TOPMOST;
        var overlays = new IntPtr[4];
        for (int i = 0; i < 4; i++)
        {
            var hwnd = CreateWindowExW(ex, cfg.ClassName, null, WS_POPUP,
                0, 0, 10, 10, IntPtr.Zero, IntPtr.Zero, hi, IntPtr.Zero);
            if (hwnd == IntPtr.Zero) { CleanupPartial(overlays, i); return null; }
            SetLayeredWindowAttributes(hwnd, 0, cfg.Alpha, LWA_ALPHA);
            overlays[i] = hwnd;
        }
        return overlays;
    }

    public void Update(IntPtr targetHwnd, IntPtr[] overlays, OverlayConfig cfg)
    {
        if (!GetWindowRect(targetHwnd, out var r)) return;
        int t = cfg.Thickness;
        int x = r.Left, y = r.Top;
        int w = r.Right - r.Left, h = r.Bottom - r.Top;
        if (w <= 0 || h <= 0) return;

        int cornerLen = Math.Max(8, t * 5);
        uint flags = SWP_NOACTIVATE | SWP_SHOWWINDOW;
        SetWindowPos(overlays[0], HWND_TOPMOST, x - t, y - t, cornerLen, cornerLen, flags);
        SetWindowPos(overlays[1], HWND_TOPMOST, x + w + t - cornerLen, y - t, cornerLen, cornerLen, flags);
        SetWindowPos(overlays[2], HWND_TOPMOST, x - t, y + h + t - cornerLen, cornerLen, cornerLen, flags);
        SetWindowPos(overlays[3], HWND_TOPMOST, x + w + t - cornerLen, y + h + t - cornerLen, cornerLen, cornerLen, flags);
    }

    public void Remove(IntPtr[]? overlays)
    {
        if (overlays == null) return;
        foreach (var h in overlays)
            if (h != IntPtr.Zero) DestroyWindow(h);
    }

    private static void CleanupPartial(IntPtr[] overlays, int count)
    {
        for (int j = 0; j < count; j++)
            if (overlays[j] != IntPtr.Zero) DestroyWindow(overlays[j]);
    }
}
