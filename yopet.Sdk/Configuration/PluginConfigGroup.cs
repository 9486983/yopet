namespace yopet.Sdk;

/// <summary>插件配置字段分组 —— 将多个字段归为一组，分组展示</summary>
public class PluginConfigGroup
{
    /// <summary>分组标题</summary>
    public string Title { get; set; } = "";

    /// <summary>分组描述（显示在标题下方）</summary>
    public string? Description { get; set; }

    /// <summary>分组 Emoji 图标</summary>
    public string? Emoji { get; set; }

    /// <summary>分组内包含的字段 Key 列表（引用 PluginConfigSection.Fields）</summary>
    public List<string> FieldKeys { get; set; } = new();
}
