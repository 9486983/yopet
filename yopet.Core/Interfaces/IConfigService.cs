using yopet.Core.Models;

namespace yopet.Core.Interfaces;

/// <summary>配置服务 —— 加载/保存应用配置</summary>
public interface IConfigService
{
    AppConfig Config { get; }
    void Save();

    /// <summary>插件读取配置值</summary>
    string? GetPluginValue(string key);

    /// <summary>插件写入配置值</summary>
    void SetPluginValue(string key, string value);

    /// <summary>批量设置插件配置值（只保存一次）</summary>
    void SetPluginValuesBatch(IEnumerable<KeyValuePair<string, string?>> values);
}
