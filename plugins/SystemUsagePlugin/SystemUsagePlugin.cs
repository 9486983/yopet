using Lang.Avalonia;
using yopet.Sdk;

namespace SystemUsagePlugin;

/// <summary>
/// 系统资源监控插件 —— 鼠标悬浮在宠物上时，通过气泡组件（<see cref="IPluginHost.ShowThought"/>）
/// 展示 CPU 与运行内存占用（硬盘忽略），无需修改任何主程序 UI 代码。
///
/// 参考 RunCat365 的 usage 模块设计，做了跨平台处理（Windows / Linux / macOS）：
///   - 无需任何配置项；
///   - 通过统一插件事件池订阅 <see cref="EventNames.PetHoverEntered"/> / <see cref="EventNames.PetHoverExited"/>，
///     悬浮进入时启动周期刷新并展示气泡，离开时停止；
///   - 与其它插件同时注册悬浮事件时，由事件池做冲突提示。
/// </summary>
[Plugin("系统资源监控", Version = "1.0.0",
    Description = "鼠标悬浮在宠物上时展示 CPU 与内存占用（跨平台，无需配置）")]
public class SystemUsagePlugin : PluginBase
{
    /// <summary>取当前语言的插件词条</summary>
    private static string T(string key) =>
        I18nManager.Instance.GetResource($"Localization.SystemUsagePlugin.{key}");

    public override string Name => T("Name");

    private SystemUsageService? _service;
    private IPluginHost? _host;
    private System.Threading.Timer? _refreshTimer;

    /// <summary>悬浮期间气泡刷新间隔</summary>
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(1.5);

    public override Task InitializeAsync(IPluginHost host)
    {
        _host = host;
        _service = new SystemUsageService();

        // 订阅悬浮进入/离开事件（事件池统一注册，主程序只负责触发）
        host.Events.Register(Name, EventNames.PetHoverEntered, new Action(OnHoverEntered));
        host.Events.Register(Name, EventNames.PetHoverExited, new Action(OnHoverExited));

        return Task.CompletedTask;
    }

    public override Task CleanupAsync()
    {
        if (Host is { } host)
        {
            host.Events.Unregister(Name, EventNames.PetHoverEntered);
            host.Events.Unregister(Name, EventNames.PetHoverExited);
        }
        _refreshTimer?.Dispose();
        _refreshTimer = null;
        _service?.Dispose();
        _service = null;
        return Task.CompletedTask;
    }

    /// <summary>鼠标进入宠物：立即展示一次并启动周期刷新</summary>
    private void OnHoverEntered()
    {
        Refresh();
        _refreshTimer ??= new System.Threading.Timer(_ => Refresh(), null, RefreshInterval, RefreshInterval);
    }

    /// <summary>鼠标离开宠物：停止刷新（气泡由气泡组件按既有规则自动隐藏）</summary>
    private void OnHoverExited()
    {
        _refreshTimer?.Dispose();
        _refreshTimer = null;
    }

    /// <summary>采集系统占用并通过气泡组件展示</summary>
    private void Refresh()
    {
        if (_service == null || _host == null) return;

        try
        {
            var (cpu, memPercent, memTotal, memUsed) = _service.Read();
            _host.ShowThought(Name, string.Format(T("Format"),
                cpu.ToString("F1"),
                memPercent.ToString("F1"),
                FormatBytes(memUsed),
                FormatBytes(memTotal)));
        }
        catch
        {
            // 采集失败时静默跳过本次刷新
        }
    }

    /// <summary>字节格式化（参考 RunCat365 的 ByteFormatter）</summary>
    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var i = 0;
        double value = bytes;
        while (value >= 1024 && i < units.Length - 1)
        {
            value /= 1024;
            i++;
        }
        return $"{value:0.#} {units[i]}";
    }
}
