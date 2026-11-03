namespace yopet.Sdk;

/// <summary>
/// 插件配置读写接口 —— 对宿主配置服务的窄封装，只暴露插件需要的键值读写能力。
/// </summary>
public interface IPluginConfig
{
    /// <summary>读取指定配置键的值，不存在时返回 null。</summary>
    string? Get(string key);

    /// <summary>写入指定配置键的值并持久化。</summary>
    void Set(string key, string value);

    /// <summary>批量写入配置值；值为 null 表示删除该键。</summary>
    void SetMany(IEnumerable<KeyValuePair<string, string?>> values);
}
