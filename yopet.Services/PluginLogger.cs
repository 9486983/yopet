using yopet.Sdk;

namespace yopet.Services;

/// <summary>插件日志实现 —— 包装 LoggerService</summary>
public class PluginLogger : IPluginLogger
{
    private readonly LoggerService _inner;

    public PluginLogger(LoggerService inner) => _inner = inner;

    public string LogPath
    {
        get => _inner.LogDir;
        set => _inner.LogDir = value;
    }

    public void Debug<T>(string message) =>
        _ = _inner.WriteAsync("DEBUG", typeof(T).Name, message);

    public void Info<T>(string message) =>
        _ = _inner.WriteAsync("INFO", typeof(T).Name, message);

    public void Warn<T>(string message) =>
        _ = _inner.WriteAsync("WARN", typeof(T).Name, message);

    public void Error<T>(string message, Exception? ex = null) =>
        _ = _inner.WriteAsync("ERROR", typeof(T).Name, message, ex);
}
