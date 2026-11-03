namespace yopet.Sdk;

/// <summary>插件日志接口 —— 所有日志方法集中在此对象中。</summary>
public interface IPluginLogger
{
    /// <summary>日志目录路径（默认 plugins/logs/，可修改）。</summary>
    string LogPath { get; set; }

    /// <summary>创建绑定固定来源名称的子日志器。</summary>
    IPluginLogger For(string source);

    void Debug(string message);

    void Info(string message);

    void Warn(string message);

    void Error(string message, Exception? ex = null);

}
