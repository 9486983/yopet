using System.Reflection;
using System.Runtime.Loader;
using yopet.Sdk;

namespace yopet.Services.PluginHosting;

/// <summary>
/// 插件生命周期管理器 —— 负责插件的加载、初始化、清理与热重载编排。
///
/// 设计要点：
///   - 每个插件运行在独立的可卸载 <see cref="AssemblyLoadContext"/> 中，是热重载的前提；
///   - 热重载流程严格按序：清理旧插件 → 注销旧事件 → 重置宿主状态 → 等待程序集卸载
///     （释放 dll 文件锁）→ 重新扫描加载 → 初始化；
///   - 线程安全：插件集合以锁保护，公开只读快照。
/// </summary>
public sealed class PluginLifecycleManager
{
    private readonly List<LoadedPlugin> _plugins = new();
    private readonly object _lock = new();
    private readonly Action<string>? _log;

    public PluginLifecycleManager(Action<string>? log = null)
    {
        _log = log;
    }

    /// <summary>当前已加载的插件对象（只读快照，线程安全）</summary>
    public IReadOnlyList<IPlugin> Plugins
    {
        get
        {
            lock (_lock)
            {
                return _plugins.Select(p => p.Plugin).ToList();
            }
        }
    }

    /// <summary>当前已加载的插件实例（含加载上下文信息，线程安全）</summary>
    public IReadOnlyList<LoadedPlugin> LoadedPlugins
    {
        get
        {
            lock (_lock)
            {
                return _plugins.ToList();
            }
        }
    }

    /// <summary>从目录扫描并加载所有插件（同一 dll 内的多个插件类型会全部加载）</summary>
    public void LoadFromDirectory(string directory)
    {
        if (!Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
            return;
        }

        foreach (var dllPath in Directory.GetFiles(directory, "*.dll"))
        {
            try
            {
                var context = new AssemblyLoadContext(
                    Path.GetFileNameWithoutExtension(dllPath), isCollectible: true);
                var assembly = context.LoadFromAssemblyPath(dllPath);

                foreach (var type in assembly.GetTypes())
                {
                    if (!typeof(IPlugin).IsAssignableFrom(type) || type.IsAbstract || !type.IsClass)
                        continue;

                    if (Activator.CreateInstance(type) is IPlugin plugin)
                    {
                        lock (_lock)
                        {
                            _plugins.Add(new LoadedPlugin(plugin, dllPath, context));
                        }
                        _log?.Invoke($"已加载插件 [{plugin.Name}] <- {Path.GetFileName(dllPath)}");
                    }
                }
            }
            catch (Exception ex)
            {
                _log?.Invoke($"加载插件失败 [{dllPath}]: {ex.Message}");
            }
        }
    }

    /// <summary>初始化所有插件（单个插件初始化失败不影响其余）</summary>
    public async Task InitializeAllAsync(IPluginHost host)
    {
        foreach (var loaded in LoadedPlugins)
        {
            try
            {
                await loaded.Plugin.InitializeAsync(host);
            }
            catch (Exception ex)
            {
                _log?.Invoke($"初始化插件失败 [{loaded.Name}]: {ex.Message}");
            }
        }
    }

    /// <summary>清理所有插件（应用退出时调用）</summary>
    public async Task CleanupAllAsync()
    {
        foreach (var loaded in LoadedPlugins)
        {
            await loaded.CleanupAsync();
        }
    }

    /// <summary>
    /// 热重载：清理并卸载全部插件后，重新扫描目录并初始化。
    /// </summary>
    /// <param name="host">插件宿主（若实现 <see cref="IPluginStateResetter"/> 则一并重置宿主状态）</param>
    /// <param name="directory">插件目录（重新扫描）</param>
    public async Task ReloadAllAsync(IPluginHost host, string directory)
    {
        List<LoadedPlugin> oldPlugins;
        lock (_lock)
        {
            oldPlugins = _plugins.ToList();
            _plugins.Clear();
        }

        // 1) 逐个清理旧插件并注销其注册的事件（避免事件池残留导致"重复注册"冲突提示）
        foreach (var loaded in oldPlugins)
        {
            await loaded.CleanupAsync();
            host.Events.UnregisterAll(loaded.Name);
            _log?.Invoke($"已清理插件 [{loaded.Name}]");
        }

        // 2) 重置宿主残留状态（动作列表、激活态、会话等）
        if (host is IPluginStateResetter resetter)
        {
            resetter.ResetPluginState();
        }

        // 3) 等待旧程序集卸载完成（释放 dll 文件锁，供新版文件替换）
        foreach (var loaded in oldPlugins)
        {
            if (!loaded.UnloadAndWait())
            {
                _log?.Invoke($"警告：插件 [{loaded.Name}] 程序集未在超时内卸载，可能有引用泄漏");
            }
        }

        // 4) 重新扫描加载并初始化
        LoadFromDirectory(directory);
        await InitializeAllAsync(host);
        _log?.Invoke($"热重载完成，共加载 {LoadedPlugins.Count} 个插件");
    }
}
