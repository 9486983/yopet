namespace yopet.Core.Models;

/// <summary>
/// 动作的动态显示信息（用于语言切换等场景的即时刷新）。
/// 由插件在注册时提供，每次渲染时求值；对应字段为 null 时回退到
/// <see cref="PetActionConfig"/> 上的静态字符串。
/// </summary>
public sealed class LocalizedDisplay
{
    /// <summary>动态名称（每次渲染求值）</summary>
    public Func<string>? Name { get; init; }

    /// <summary>动态分组（每次渲染求值）</summary>
    public Func<string>? Group { get; init; }

    /// <summary>动态描述（每次渲染求值，tooltip 用）</summary>
    public Func<string>? Description { get; init; }

    /// <summary>便捷工厂</summary>
    public static LocalizedDisplay Of(
        Func<string>? name = null,
        Func<string>? group = null,
        Func<string>? description = null)
        => new() { Name = name, Group = group, Description = description };
}
