namespace yopet.Sdk;

/// <summary>插件元数据特性</summary>
[AttributeUsage(AttributeTargets.Class)]
public class PluginAttribute : Attribute
{
    public string Name { get; }
    public string Version { get; set; } = "1.0.0";
    public string Description { get; set; } = "";

    public PluginAttribute(string name) => Name = name;
}
