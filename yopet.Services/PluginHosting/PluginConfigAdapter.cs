using yopet.Core.Interfaces;
using yopet.Sdk;

namespace yopet.Services.PluginHosting;

/// <summary>
/// 将宿主 <see cref="IConfigService"/> 适配为插件可用的窄配置接口。
/// 键名保持原样，以兼容已有插件的配置数据。
/// </summary>
internal sealed class PluginConfigAdapter(IConfigService config) : IPluginConfig
{
    public string? Get(string key) => config.GetPluginValue(key);

    public void Set(string key, string value) => config.SetPluginValue(key, value);

    public void SetMany(IEnumerable<KeyValuePair<string, string?>> values)
        => config.SetPluginValuesBatch(values);
}
