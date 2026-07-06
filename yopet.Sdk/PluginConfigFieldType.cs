namespace yopet.Sdk;

/// <summary>插件配置字段类型</summary>
public enum PluginConfigFieldType
{
    /// <summary>单行文本</summary>
    String,
    /// <summary>密码（输入掩码）</summary>
    Password,
    /// <summary>数字</summary>
    Number,
    /// <summary>开关</summary>
    Boolean,
    /// <summary>下拉选择</summary>
    Dropdown,
    /// <summary>文件选择路径（带浏览按钮）</summary>
    FilePath,
    /// <summary>文件夹选择路径（带浏览按钮）</summary>
    FolderPath,
    /// <summary>多行文本</summary>
    TextArea,
    /// <summary>Cron 表达式（下拉选常用模板）</summary>
    CronExpression,
    /// <summary>颜色选择</summary>
    Color,
}
