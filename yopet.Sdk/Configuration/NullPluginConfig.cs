namespace yopet.Sdk;

/// <summary>无操作配置实现，用于未使用依赖注入的旧插件回退。</summary>
public sealed class NullPluginConfig : IPluginConfig
{
    public static readonly NullPluginConfig Instance = new();

    private NullPluginConfig()
    {
    }

    public string? Get(string key) => null;

    public void Set(string key, string value)
    {
    }

    public void SetMany(IEnumerable<KeyValuePair<string, string?>> values)
    {
    }
}
