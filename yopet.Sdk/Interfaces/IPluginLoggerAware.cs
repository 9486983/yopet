namespace yopet.Sdk;

/// <summary>由插件宿主在初始化前注入插件专属日志器。</summary>
public interface IPluginLoggerAware
{
    void SetLogger(IPluginLogger logger);
}
