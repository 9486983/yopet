namespace yopet.Sdk;

/// <summary>列表弹窗配置 —— 插件通过此对象定义列表的数据、列、工具栏</summary>
public class ListDialogConfig
{
    /// <summary>布局模式：Table（默认）或 CardGrid</summary>
    public ListDialogLayoutMode LayoutMode { get; set; } = ListDialogLayoutMode.Table;

    /// <summary>弹窗标题</summary>
    public string Title { get; set; } = "";

    /// <summary>标题前的 Emoji 图标</summary>
    public string? Emoji { get; set; }

    /// <summary>静态数据源（行数据列表，每行为键值对字典）</summary>
    public List<Dictionary<string, string>>? Items { get; set; }

    /// <summary>异步数据源回调（与 Items 互斥，优先级更高）</summary>
    public Func<Task<List<Dictionary<string, string>>>>? DataSource { get; set; }

    /// <summary>列定义（Table 模式必填；CardGrid 模式使用第一列 Key 作卡片文字标签）</summary>
    public List<ListColumn> Columns { get; set; } = new();

    /// <summary>工具栏按钮列表</summary>
    public List<ListToolbarAction> ToolbarActions { get; set; } = new();

    // ── CardGrid 模式属性 ──

    /// <summary>卡片文字标签的 Key（默认取 Columns[0].Key）</summary>
    public string CardTextKey { get; set; } = "";

    /// <summary>无图片时的回退 Emoji</summary>
    public string CardFallbackEmoji { get; set; } = "📄";

    /// <summary>
    /// 返回每张卡片封面图片的路径。每行调用一次。
    /// 返回 null/空 = 显示回退 Emoji。
    /// </summary>
    public Func<Dictionary<string, string>, string?>? CardImageProvider { get; set; }

    /// <summary>
    /// 卡片点击回调。返回 true → 关闭弹窗并返回该行数据；
    /// 返回 false → 保持弹窗打开。
    /// 未设置时，点击卡片自动关闭弹窗。
    /// </summary>
    public Func<Dictionary<string, string>, Task<bool>>? OnCardClick { get; set; }

    /// <summary>数据变更时触发，列表弹窗订阅后自动刷新</summary>
    public event EventHandler? DataChanged;

    /// <summary>通知列表弹窗数据已变更（可从任意线程调用）</summary>
    public void NotifyDataChanged() => DataChanged?.Invoke(this, EventArgs.Empty);
}
