namespace yopet.Sdk;

/// <summary>插件配置分区 —— 插件在初始化时通过 <see cref="IPluginHost.RegisterConfig"/> 注册</summary>
public class PluginConfigSection
{
    /// <summary>
    /// 稳定标识（建议用英文 id，如 "setting"、"deepseek"）。
    /// 用于 <see cref="IPluginHost.ShowConfigDialog"/> 定位，不受语言切换影响。
    /// </summary>
    public string Key { get; set; } = "";

    /// <summary>分区标题（如 "DeepSeek API 配置"）</summary>
    public string Title { get; set; } = "";

    /// <summary>标题前的 Emoji 图标</summary>
    public string? Emoji { get; set; }

    /// <summary>配置字段列表</summary>
    public List<PluginConfigField> Fields { get; set; } = new();

    /// <summary>字段分组（可选。不设置时所有字段平铺展示）</summary>
    public List<PluginConfigGroup>? Groups { get; set; }

    /// <summary>保存前校验回调。返回空列表=通过；返回错误消息列表=校验失败</summary>
    public Func<Dictionary<string, string?>, List<string>>? Validate { get; set; }

    /// <summary>注册该配置的插件名称（由宿主在注册时自动填充）</summary>
    public string? PluginName { get; set; }

    /// <summary>是否从统一设置页隐藏（内部编辑表单等可设为 true）</summary>
    public bool HideFromSettings { get; set; }
}
