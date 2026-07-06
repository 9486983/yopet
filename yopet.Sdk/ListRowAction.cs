namespace yopet.Sdk;

/// <summary>列表行操作按钮类型</summary>
public enum ListRowActionType
{
    /// <summary>普通按钮（默认）</summary>
    Button,
    /// <summary>下拉菜单按钮（点击展开多个子操作）</summary>
    Dropdown,
}

/// <summary>列表行操作按钮 —— 显示在 Action 列的每一行中</summary>
public class ListRowAction
{
    /// <summary>按钮文字</summary>
    public string Label { get; set; } = "";

    /// <summary>按钮 Emoji 图标</summary>
    public string Emoji { get; set; } = "";

    /// <summary>悬停提示</summary>
    public string? Tooltip { get; set; }

    /// <summary>按钮类型</summary>
    public ListRowActionType Type { get; set; } = ListRowActionType.Button;

    /// <summary>子操作列表（仅 Dropdown 模式生效）</summary>
    public List<ListRowAction>? Children { get; set; }

    /// <summary>点击回调，参数为当前行数据</summary>
    public Func<Dictionary<string, string>, Task>? Callback { get; set; }
}
