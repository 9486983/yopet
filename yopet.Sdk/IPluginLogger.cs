namespace yopet.Sdk;

/// <summary>插件日志接口 —— 所有日志方法集中在此对象中</summary>
public interface IPluginLogger
{
    /// <summary>日志目录路径（默认 .yopet/logs/，可修改）</summary>
    string LogPath { get; set; }

    /// <summary>输出调试日志</summary>
    void Debug<T>(string message);

    /// <summary>输出信息日志</summary>
    void Info<T>(string message);

    /// <summary>输出警告日志</summary>
    void Warn<T>(string message);

    /// <summary>输出错误日志（含异常）</summary>
    void Error<T>(string message, Exception? ex = null);
}
