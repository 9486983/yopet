namespace yopet.Sdk;

/// <summary>列表列的类型</summary>
public enum ListColumnType
{
    /// <summary>纯文本显示</summary>
    Text,
    /// <summary>可编辑（点击后变为输入框）</summary>
    Editable,
    /// <summary>操作按钮列</summary>
    Action,
}

/// <summary>列表列定义</summary>
public class ListColumn
{
    /// <summary>数据键名（对应行字典中的 Key）</summary>
    public string Key { get; set; } = "";

    /// <summary>列标题</summary>
    public string Header { get; set; } = "";

    /// <summary>列类型</summary>
    public ListColumnType Type { get; set; } = ListColumnType.Text;

    /// <summary>列宽（像素），double.NaN 表示自动</summary>
    public double Width { get; set; } = double.NaN;

    /// <summary>操作按钮列表（仅 Action 列生效）</summary>
    public List<ListRowAction>? RowActions { get; set; }

    /// <summary>编辑保存回调（仅 Editable 列生效），参数为整行数据 + 新值</summary>
    public Func<Dictionary<string, string>, string, Task>? OnCellEdit { get; set; }
}
