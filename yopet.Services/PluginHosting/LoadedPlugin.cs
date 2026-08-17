using System.Diagnostics;
using System.Reflection;
using System.Runtime.Loader;
using yopet.Sdk;

namespace yopet.Services.PluginHosting;

/// <summary>
/// 已加载的插件实例 —— 领域对象：聚合插件对象、来源 dll 路径与可卸载的加载上下文，
/// 并封装插件的清理与程序集卸载操作（热重载的基础单元）。
/// </summary>
public sealed class LoadedPlugin
{
    private readonly AssemblyLoadContext _alc;

    /// <summary>插件对象</summary>
    public IPlugin Plugin { get; }

    /// <summary>插件 dll 来源路径</summary>
    public string DllPath { get; }

    /// <summary>插件名</summary>
    public string Name => Plugin.Name;

    /// <summary>插件版本</summary>
    public string Version => Plugin.Version;

    /// <summary>插件描述</summary>
    public string Description => Plugin.Description;

    internal LoadedPlugin(IPlugin plugin, string dllPath, AssemblyLoadContext alc)
    {
        Plugin = plugin;
        DllPath = dllPath;
        _alc = alc;
    }

    /// <summary>
    /// 调用插件清理钩子。单个插件清理异常不影响后续清理流程。
    /// </summary>
    public async Task CleanupAsync()
    {
        try
        {
            await Plugin.CleanupAsync();
        }
        catch
        {
            // 插件清理失败不阻断热重载流程
        }
    }

    /// <summary>
    /// 卸载插件程序集并等待卸载完成（释放 dll 文件锁，供重新加载替换文件）。
    /// </summary>
    /// <param name="timeoutMs">卸载等待超时（毫秒）</param>
    /// <returns>true=在超时内卸载成功；false=仍有引用泄漏</returns>
    public bool UnloadAndWait(int timeoutMs = 5000)
    {
        // trackResurrection=true：WeakReference 在终结后仍能反映 ALC 存活，用于精确检测卸载完成
        var weakRef = new WeakReference(_alc, trackResurrection: true);
        _alc.Unload();

        var sw = Stopwatch.StartNew();
        while (weakRef.IsAlive && sw.ElapsedMilliseconds < timeoutMs)
        {
            Thread.Sleep(50);
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }
        return !weakRef.IsAlive;
    }
}
