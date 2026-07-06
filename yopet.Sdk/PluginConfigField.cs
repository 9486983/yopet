namespace yopet.Sdk;

/// <summary>插件配置字段定义</summary>
public class PluginConfigField
{
    /// <summary>配置键名（用于 <see cref="IPluginHost.GetConfig"/> / <see cref="IPluginHost.SetConfig"/>）</summary>
    public string Key { get; set; } = "";

    /// <summary>显示名称</summary>
    public string Label { get; set; } = "";

    /// <summary>字段类型</summary>
    public PluginConfigFieldType Type { get; set; } = PluginConfigFieldType.String;

    /// <summary>是否为必填项</summary>
    public bool IsRequired { get; set; }

    /// <summary>输入提示文字</summary>
    public string? Placeholder { get; set; }

    /// <summary>默认值</summary>
    public string? DefaultValue { get; set; }

    /// <summary>说明文字（显示在字段下方）</summary>
    public string? Description { get; set; }

    /// <summary>下拉选项（Dropdown 类型使用）</summary>
    public List<PluginConfigOption>? Options { get; set; }

    /// <summary>最小值（Number 类型使用）</summary>
    public double? MinValue { get; set; }

    /// <summary>最大值（Number 类型使用）</summary>
    public double? MaxValue { get; set; }

    // ── FilePath / FolderPath ──

    /// <summary>文件选择器过滤（如 "图片文件|*.png;*.jpg|所有文件|*.*"）</summary>
    public string? FileFilter { get; set; }

    // ── CronExpression ──

    /// <summary>常用 cron 模板列表（显示在下拉中供快速选择）</summary>
    public List<PluginConfigOption>? CronPresets { get; set; }

    // ── TextArea ──

    /// <summary>文本区域行数（默认 3）</summary>
    public int TextAreaRows { get; set; } = 3;
}
