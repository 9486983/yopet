using System.Runtime.InteropServices;
using static PinTopPlugin.Win32Native;
using static PinTopPlugin.Win32Const;

namespace PinTopPlugin;

/// <summary>系统级闪烁样式 —— 仅调用 FlashWindowEx，无持久窗口</summary>
internal class FlashStyle : IOverlayStyle
{
    public bool IsPersistent => false;

    public IntPtr[]? Apply(IntPtr targetHwnd, OverlayConfig cfg)
    {
        var fi = new FLASHWINFO
        {
            cbSize = (uint)Marshal.SizeOf<FLASHWINFO>(),
            hwnd = targetHwnd,
            dwFlags = FLASHW_CAPTION | FLASHW_TIMERNOFG,
            uCount = 5,
            dwTimeout = 0,
        };
        FlashWindowEx(ref fi);
        return null;
    }

    public void Update(IntPtr targetHwnd, IntPtr[] overlays, OverlayConfig cfg) { }

    public void Remove(IntPtr[]? overlays) { }
}
