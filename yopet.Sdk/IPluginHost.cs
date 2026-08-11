using yopet.Core.Models;

namespace yopet.Sdk;

/// <summary>插件宿主接口 —— 插件通过此接口与主程序交互</summary>
public interface IPluginHost
{    // ── 日志（独立对象，所有日志方法放这里） ──
    IPluginLogger Logger { get; }

    // ── 定时任务（独立对象，所有调度方法放这里） ──
    IPluginScheduler Scheduler { get; }

    // ── 会话多步工作流 ──

    /// <summary>当前活跃会话（无则为 null）</summary>
    ISession? CurrentSession { get; }

    /// <summary>
    /// 启动一个多步会话，同时自动锁定与此会话同名的动作为默认拖放操作。
    /// 后续拖入文件将不再弹出径向菜单，直接路由到该动作的回调。
    /// 插件在回调内通过 <see cref="ISession.Context"/> 在多次调用间共享状态。
    /// </summary>
    /// <param name="title">会话标题，需与 <see cref="PluginAction.Name"/> 一致以便自动激活</param>
    /// <returns>会话实例，线程安全</returns>
    ISession StartSession(string title);

    // ── 插件配置定义 ──

    /// <summary>注册插件配置定义（初始化时调用）</summary>
    void RegisterConfig(PluginConfigSection config, string? pluginName = null);

    /// <summary>打开插件配置弹窗（由插件在右键菜单回调中调用）</summary>
    void ShowConfigDialog(string sectionTitle);

    /// <summary>当用户修改并保存插件配置后触发，参数为变更的配置键名</summary>
    event EventHandler<string>? ConfigValueChanged;
    /// <summary>注册一个动作（右键菜单 / 径向菜单均可）</summary>
    void RegisterAction(PluginAction action);

    /// <summary>动态更新已注册动作的显示名称（右键菜单下次打开时生效）</summary>
    void UpdateActionName(string currentName, string newName);

    /// <summary>在宠物气泡中显示文字（标题 + 内容）</summary>
    void ShowThought(string title, string text);

    /// <summary>排队显示气泡消息，前一条播完再播下一条，不打断</summary>
    void EnqueueThought(ThoughtMessage message);

    /// <summary>清空未播的消息队列</summary>
    void ClearThoughtQueue();

    /// <summary>显示宠物反应 emoji（短暂弹出在宠物上方）</summary>
    void ShowReaction(string emoji, PetAnimation animation = PetAnimation.Wave);

    /// <summary>开始持续动画（长时间任务时展示状态，如"思考中"）</summary>
    void StartAnimation(PetAnimation animation);

    /// <summary>结束持续动画，恢复待机</summary>
    void StopAnimation();

    /// <summary>在指定动画状态下执行异步委托，执行完毕后自动恢复待机</summary>
    Task RunWithAnimation(PetAnimation animation, Func<CancellationToken, Task> action);

    /// <summary>在多个动画间轮换执行异步委托（适用于长时间任务），支持取消</summary>
    Task RunWithAnimation(IEnumerable<PetAnimation> animations, Func<CancellationToken, Task> action);

    /// <summary>取消当前正在执行的 RunWithAnimation 任务</summary>
    void CancelCurrentTask();

    /// <summary>弹出输入框让用户输入文本（返回 null 表示取消）</summary>
    Task<string?> ShowInputDialog(string title, string placeholder, string? initialValue = null);

    /// <summary>弹出确认对话框（气泡样式），返回 true=确认 false=取消</summary>
    Task<bool> ShowConfirmDialog(string title, string text);

    /// <summary>弹出列表弹窗（气泡样式），支持自定义数据、操作列、编辑、工具栏</summary>
    Task ShowListDialog(ListDialogConfig config);

    /// <summary>获取/设置插件配置值（保存在主程序配置中）</summary>
    string? GetConfig(string key);
    void SetConfig(string key, string value);

    /// <summary>输出日志（兼容旧版）</summary>
    void Log(string message);

    // ── 宿主设置（供设置类插件使用） ──

    /// <summary>获取当前宠物动画速度（ms/帧）</summary>
    double GetAnimationSpeedMs();

    /// <summary>设置宠物动画速度（ms/帧），立即生效并持久化</summary>
    void SetAnimationSpeedMs(double ms);

    /// <summary>获取当前是否深色主题</summary>
    bool GetDarkTheme();

    /// <summary>设置深色/浅色主题，立即生效并持久化</summary>
    void SetDarkTheme(bool isDark);

    /// <summary>获取当前界面语言（culture name，如 zh-CN）</summary>
    string GetLanguage();

    /// <summary>切换界面语言（culture name，如 zh-CN / en-US），立即生效并持久化</summary>
    void SetLanguage(string cultureName);

    /// <summary>已加载的插件信息列表（只读展示用）</summary>
    IReadOnlyList<PluginInfo> LoadedPlugins { get; }
}
