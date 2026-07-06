namespace yopet.Sdk;

/// <summary>下拉选项（用于 Dropdown 类型字段）</summary>
public class PluginConfigOption
{
    /// <summary>显示文字（展示在下拉列表中）</summary>
    public string Label { get; set; } = "";

    /// <summary>存储值（写入配置的值）</summary>
    public string Value { get; set; } = "";
}
