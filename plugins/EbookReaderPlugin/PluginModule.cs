using System.Runtime.CompilerServices;
using System.Runtime.Loader;

namespace EbookReaderPlugin;

/// <summary>
/// 插件宿主使用独立的可卸载 ALC 加载插件 dll，不会自动到插件目录寻找第三方依赖。
/// 这里注册一个解析器，把 plugins/deps 下的第三方程序集按需加载进来。
/// </summary>
internal static class PluginModule
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        try
        {
            var assembly = typeof(PluginModule).Assembly;
            var context = AssemblyLoadContext.GetLoadContext(assembly);
            var baseDirectory = Path.GetDirectoryName(assembly.Location);
            if (context is null || string.IsNullOrEmpty(baseDirectory))
                return;

            var dependencyDirectory = Path.Combine(baseDirectory, "deps");
            if (!Directory.Exists(dependencyDirectory))
                return;

            context.Resolving += (loadContext, assemblyName) =>
            {
                if (string.IsNullOrWhiteSpace(assemblyName.Name))
                    return null;

                var candidate = Path.Combine(dependencyDirectory, assemblyName.Name + ".dll");
                return File.Exists(candidate)
                    ? loadContext.LoadFromAssemblyPath(candidate)
                    : null;
            };
        }
        catch
        {
            // 依赖解析失败时保持安静，具体功能调用会给出更明确的错误。
        }
    }
}
