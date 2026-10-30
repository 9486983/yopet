using yopet.Sdk;

namespace yopet.Services;

/// <summary>插件日志实现 —— 包装共享 LoggerService，附带固定来源名称。</summary>
public class PluginLogger : IPluginLogger
{
    private readonly LoggerService _inner;
    private readonly string _source;

    public PluginLogger(LoggerService inner, string source = "Host")
    {
        _inner = inner;
        _source = source;
    }

    public string LogPath
    {
        get => _inner.LogDir;
        set => _inner.LogDir = value;
    }

    public IPluginLogger For(string source) => new PluginLogger(_inner, source);

    public void Debug(string message) =>
        _ = _inner.WriteAsync("DEBUG", _source, message);

    public void Info(string message) =>
        _ = _inner.WriteAsync("INFO", _source, message);

    public void Warn(string message) =>
        _ = _inner.WriteAsync("WARN", _source, message);

    public void Error(string message, Exception? ex = null) =>
        _ = _inner.WriteAsync("ERROR", _source, message, ex);

}
