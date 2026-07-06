namespace PinTopPlugin;

/// <summary>Overlay 样式策略接口</summary>
internal interface IOverlayStyle
{
    /// <summary>是否有持久化 overlay 窗口（false = 仅闪烁，无窗口）</summary>
    bool IsPersistent { get; }

    /// <summary>应用样式到目标窗口（在 pump 线程调用）</summary>
    IntPtr[]? Apply(IntPtr targetHwnd, OverlayConfig cfg);

    /// <summary>更新 overlay 位置（在 pump 线程调用）</summary>
    void Update(IntPtr targetHwnd, IntPtr[] overlays, OverlayConfig cfg);

    /// <summary>移除 overlay（在 pump 线程调用）</summary>
    void Remove(IntPtr[]? overlays);
}

/// <summary>Overlay 配置（由 PinTopPlugin 从 IPluginHost 读取）</summary>
internal class OverlayConfig
{
    public uint ColorBgr { get; set; } = 0x00D7FF;   // BGR
    public byte Alpha { get; set; } = 140;
    public int Thickness { get; set; } = 3;
    public int Radius { get; set; } = 8;
    public string StyleName { get; set; } = "border";
    public string ClassName { get; set; } = "";
    public IntPtr Brush { get; set; } = IntPtr.Zero;
}
