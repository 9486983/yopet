using System.Collections.Concurrent;

namespace yopet.Sdk;

/// <summary>
/// 多步会话 —— 表示一个持续到显式结束的工作流。
/// 插件通过 <see cref="IPluginHost.StartSession"/> 创建，
/// 此后宿主会自动锁定当前动作为默认操作，后续拖放不再弹出径向菜单。
/// </summary>
public interface ISession
{
    /// <summary>会话标题（显示在气泡 / 右键菜单中）</summary>
    string Title { get; }

    /// <summary>当前状态描述（如 "已处理 3 张图片"），宿主会展示在气泡中</summary>
    string Status { get; set; }

    /// <summary>
    /// 进度 0.0 ~ 1.0，-1 表示不确定进度（显示旋转进度环）。
    /// 宿主根据此值决定进度环的样式：确定值显示弧形进度，-1 显示无限旋转。
    /// </summary>
    double Progress { get; set; }

    /// <summary>会话是否活跃（<see cref="Complete"/> 或 <see cref="Cancel"/> 后为 false）</summary>
    bool IsActive { get; }

    /// <summary>
    /// 会话共享状态 —— 插件在多次回调间传递数据。
    /// 线程安全（ConcurrentDictionary），可在 <see cref="IPluginHost.RunWithAnimation"/> 等异步回调中安全读写。
    /// </summary>
    ConcurrentDictionary<string, object> Context { get; }

    /// <summary>完成会话（正常结束），自动恢复待机</summary>
    void Complete();

    /// <summary>取消会话，自动恢复待机</summary>
    void Cancel();

    /// <summary>会话完成时触发</summary>
    event Action<ISession>? OnCompleted;

    /// <summary>会话取消时触发</summary>
    event Action<ISession>? OnCancelled;
}
