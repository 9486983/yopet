namespace yopet.Sdk;

/// <summary>工具栏按钮类型</summary>
public enum ListToolbarActionType
{
    /// <summary>普通按钮</summary>
    Button,
    /// <summary>下拉菜单按钮</summary>
    Dropdown,
}

/// <summary>列表工具栏动作定义</summary>
public class ListToolbarAction
{
    /// <summary>按钮文字</summary>
    public string Label { get; set; } = "";

    /// <summary>按钮 Emoji 图标</summary>
    public string Emoji { get; set; } = "";

    /// <summary>按钮类型</summary>
    public ListToolbarActionType Type { get; set; } = ListToolbarActionType.Button;

    /// <summary>点击回调（Button 类型使用）</summary>
    public Func<Task>? Callback { get; set; }

    /// <summary>下拉子项（Dropdown 类型使用）</summary>
    public List<ListToolbarAction>? Children { get; set; }
}
