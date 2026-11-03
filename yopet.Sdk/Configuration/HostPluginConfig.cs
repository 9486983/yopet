namespace yopet.Sdk;

/// <summary>
/// 基于 <see cref="IPluginHost"/> 的配置实现，仅用于未使用依赖注入的旧插件回退。
/// </summary>
internal sealed class HostPluginConfig(IPluginHost host) : IPluginConfig
{
    public string? Get(string key) => host.GetConfig(key);

    public void Set(string key, string value) => host.SetConfig(key, value);

    public void SetMany(IEnumerable<KeyValuePair<string, string?>> values)
    {
        var dict = new Dictionary<string, string?>();
        foreach (var (key, value) in values)
            dict[key] = value;
        host.SaveConfigValues(dict);
    }
}
