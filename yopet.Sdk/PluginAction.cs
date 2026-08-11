using yopet.Core.Models;

namespace yopet.Sdk;

/// <summary>动作出现的位置</summary>
public enum ActionTarget
{
    /// <summary>右键菜单</summary>
    ContextMenu,
    /// <summary>文件拖放径向菜单</summary>
    RadialMenu,
}

/// <summary>
/// 插件动作描述符 —— 插件通过此对象注册功能，取代零散的字符串参数。
/// </summary>
public class PluginAction
{
    /// <summary>显示名称（右键菜单项 / 径向菜单选项）</summary>
    public string Name { get; set; } = "";

    /// <summary>Emoji 图标</summary>
    public string Emoji { get; set; } = "";

    /// <summary>功能描述（鼠标悬停提示）</summary>
    public string Description { get; set; } = "";

    /// <summary>右键菜单分组名（只有 Target=ContextMenu 时生效）</summary>
    public string Group { get; set; } = "";

    /// <summary>
    /// 动态显示信息（语言切换即时刷新；为 null 时使用上面的静态字符串）。
    /// </summary>
    public LocalizedDisplay? Display { get; set; }

    /// <summary>出现位置</summary>
    public ActionTarget Target { get; set; } = ActionTarget.ContextMenu;

    /// <summary>文件扩展名过滤（只有 Target=RadialMenu 时生效，null/空=不过滤）</summary>
    /// <example>new[] { ".txt", ".md", ".csv" }</example>
    public string[]? FileExtensions { get; set; }

    /// <summary>接受的项目类型（只有 Target=RadialMenu 时生效）</summary>
    public ItemType AcceptType { get; set; } = ItemType.Both;

    /// <summary>右键菜单点击回调（Target=ContextMenu 时使用）</summary>
    public Func<Task>? Callback { get; set; }

    /// <summary>文件拖放回调（Target=RadialMenu 时使用，参数为文件路径数组）</summary>
    public Func<string[], Task>? FileCallback { get; set; }

    /// <summary>是否可被设为默认拖放操作（激活后拖文件直接执行，不弹出菜单）</summary>
    public bool CanActivate { get; set; }
}
