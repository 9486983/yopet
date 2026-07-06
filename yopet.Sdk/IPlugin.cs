namespace yopet.Sdk;

/// <summary>插件接口 —— 所有插件必须实现此接口</summary>
public interface IPlugin
{
    /// <summary>插件名称</summary>
    string Name { get; }

    /// <summary>版本号</summary>
    string Version { get; }

    /// <summary>描述</summary>
    string Description { get; }

    /// <summary>初始化（主程序启动时调用）</summary>
    Task InitializeAsync(IPluginHost host);

    /// <summary>清理（主程序退出时调用）</summary>
    Task CleanupAsync();
}
