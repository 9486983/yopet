using System.Reflection;
using System.Runtime.Loader;
using yopet.Sdk;

namespace yopet.Services;

/// <summary>
/// 插件加载器 —— 从 plugins/ 目录扫描并加载外部 DLL
/// </summary>
public class PluginLoader
{
    private readonly List<IPlugin> _plugins = new();

    /// <summary>已加载的插件列表</summary>
    public IReadOnlyList<IPlugin> Plugins => _plugins;

    /// <summary>从指定目录加载所有插件</summary>
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
                    if (typeof(IPlugin).IsAssignableFrom(type) && !type.IsAbstract && type.IsClass)
                    {
                        if (Activator.CreateInstance(type) is IPlugin plugin)
                        {
                            _plugins.Add(plugin);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"加载插件失败 [{dllPath}]: {ex.Message}");
            }
        }
    }

    /// <summary>初始化所有插件</summary>
    public async Task InitializeAllAsync(IPluginHost host)
    {
        foreach (var plugin in _plugins)
        {
            try
            {
                await plugin.InitializeAsync(host);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"初始化插件失败 [{plugin.Name}]: {ex.Message}");
            }
        }
    }

    /// <summary>清理所有插件</summary>
    public async Task CleanupAllAsync()
    {
        foreach (var plugin in _plugins)
        {
            try { await plugin.CleanupAsync(); }
            catch { }
        }
    }
}
