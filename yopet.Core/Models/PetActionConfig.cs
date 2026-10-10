namespace yopet.Core.Models;

/// <summary>宠物动作配置</summary>
public class PetActionConfig
{
    public string Name { get; set; } = "";
    public string Emoji { get; set; } = "";
    public string Reaction { get; set; } = "";
    public string Description { get; set; } = "";

    /// <summary>分组名（用于右键菜单二级菜单，为空则不分组）</summary>
    public string Group { get; set; } = "";

    /// <summary>
    /// 动态显示信息（语言切换即时刷新；为 null 时使用上面的静态字符串）。
    /// </summary>
    public LocalizedDisplay? Display { get; set; }

    /// <summary>异步回调（取代 Reaction，执行具体逻辑后可通过 IPluginHost 显示结果）</summary>
    public Func<Task>? ActionCallback { get; set; }
}
