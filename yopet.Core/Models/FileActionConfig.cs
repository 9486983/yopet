namespace yopet.Core.Models;

/// <summary>拖放项目类型</summary>
public enum ItemType { File, Folder, Both }

/// <summary>文件拖放动作配置（径向菜单选项）</summary>
public class FileActionConfig
{
    public string Name { get; set; } = "";
    public string Emoji { get; set; } = "";
    public string Description { get; set; } = "";

    /// <summary>文件扩展名过滤（null/空=不过滤）</summary>
    public string[]? FileExtensions { get; set; }

    /// <summary>接受的项目类型</summary>
    public ItemType AcceptType { get; set; } = ItemType.Both;

    /// <summary>执行回调，参数为拖放的文件路径列表</summary>
    public Func<string[], Task>? ActionCallback { get; set; }

    /// <summary>判断此动作是否匹配给定的文件扩展名和类型</summary>
    public bool Matches(HashSet<string> extensions, ItemType itemType)
    {
        // 类型过滤
        if (AcceptType != ItemType.Both && AcceptType != itemType)
            return false;
        // 没有扩展名限制 → 匹配所有
        if (FileExtensions == null || FileExtensions.Length == 0) return true;
        // 没有传入扩展名 → 不过滤
        if (extensions.Count == 0) return true;
        // 至少一个注册的扩展名匹配
        return FileExtensions.Any(ext => extensions.Contains(ext.ToLowerInvariant()));
    }
}
