namespace yopet.Sdk;

/// <summary>插件事件池冲突提示参数 —— 宿主订阅 <see cref="PluginEventPool.ConflictDetected"/> 后可展示给用户</summary>
public sealed class PluginEventConflictEventArgs(string eventName, string pluginName, string message) : EventArgs
{
    /// <summary>冲突事件名（如 <see cref="EventNames.PetHover"/>）</summary>
    public string EventName { get; } = eventName;

    /// <summary>本次发起注册的插件</summary>
    public string PluginName { get; } = pluginName;

    /// <summary>冲突说明（可直接展示）</summary>
    public string Message { get; } = message;
}

/// <summary>
/// 插件事件池 —— 常用事件（如宠物悬浮提示）统一在此注册与管理，避免插件各自为政、事件难维护。
///
/// 规则很简单：
///   1. 按事件名归组、按插件名注册/注销（插件卸载时 <see cref="UnregisterAll"/> 一键清空）；
///   2. 同一事件允许多个插件共存（按注册顺序生效）；
///   3. 检测到「同名插件重复注册」或「多插件共存」时，通过 <see cref="ConflictDetected"/> 提示宿主。
///
/// 线程安全；对外执行类方法（<see cref="InvokeCombinedText"/>）在锁外调用用户委托。
/// </summary>
public sealed class PluginEventPool
{
    private readonly object _lock = new();
    private readonly Dictionary<string, List<(string PluginName, Delegate Handler)>> _events = new();

    /// <summary>冲突提示事件（重复注册、多插件共存）</summary>
    public event EventHandler<PluginEventConflictEventArgs>? ConflictDetected;

    /// <summary>
    /// 注册事件处理器。同一插件重复注册同名事件时，覆盖旧处理器并触发冲突提示。
    /// </summary>
    /// <typeparam name="T">处理器委托类型（如 <see cref="Func{TResult}"/>）</typeparam>
    /// <param name="pluginName">插件名</param>
    /// <param name="eventName">事件名（建议使用 <see cref="EventNames"/> 常量）</param>
    /// <param name="handler">处理器</param>
    public void Register<T>(string pluginName, string eventName, T handler) where T : Delegate
    {
        if (string.IsNullOrWhiteSpace(pluginName)) throw new ArgumentNullException(nameof(pluginName));
        if (string.IsNullOrWhiteSpace(eventName)) throw new ArgumentNullException(nameof(eventName));
        if (handler is null) throw new ArgumentNullException(nameof(handler));

        lock (_lock)
        {
            if (!_events.TryGetValue(eventName, out var list))
            {
                list = [];
                _events[eventName] = list;
            }

            // 同名插件重复注册 → 覆盖旧处理器 + 提示
            var idx = list.FindIndex(e => e.PluginName == pluginName);
            if (idx >= 0)
            {
                list[idx] = (pluginName, handler);
                Raise(eventName, pluginName, $"插件「{pluginName}」重复注册事件「{eventName}」，已覆盖旧处理器。");
                return;
            }

            list.Add((pluginName, handler));

            // 多插件共存 → 提示（不阻断，按注册顺序合并生效）
            if (list.Count > 1)
            {
                var others = string.Join("、", list
                    .Where(e => e.PluginName != pluginName)
                    .Select(e => e.PluginName)
                    .Distinct());
                Raise(eventName, pluginName, $"插件「{pluginName}」与「{others}」同时注册事件「{eventName}」，将按注册顺序合并生效。");
            }
        }
    }

    /// <summary>注销指定插件注册的指定事件</summary>
    public void Unregister(string pluginName, string eventName)
    {
        lock (_lock)
        {
            if (!_events.TryGetValue(eventName, out var list)) return;
            list.RemoveAll(e => e.PluginName == pluginName);
            if (list.Count == 0) _events.Remove(eventName);
        }
    }

    /// <summary>注销指定插件的全部事件（插件卸载/退出时调用）</summary>
    public void UnregisterAll(string pluginName)
    {
        lock (_lock)
        {
            var emptied = _events
                .Where(kv => kv.Value.RemoveAll(e => e.PluginName == pluginName) > 0 && kv.Value.Count == 0)
                .Select(kv => kv.Key)
                .ToList();
            foreach (var key in emptied) _events.Remove(key);
        }
    }

    /// <summary>获取事件处理器快照（按注册顺序，可安全在锁外调用）</summary>
    public IReadOnlyList<(string PluginName, Delegate Handler)> GetHandlers(string eventName)
    {
        lock (_lock)
        {
            return _events.TryGetValue(eventName, out var list) ? list.ToList() : [];
        }
    }

    /// <summary>
    /// 合并调用指定事件的所有 <see cref="Func{TResult}"/> 处理器并拼接文本（适用于悬浮提示等多文本合并场景）。
    /// 处理器在锁外按注册顺序调用，单个处理器异常不影响其余。
    /// </summary>
    public string InvokeCombinedText(string eventName, string separator = "\n")
    {
        var handlers = GetHandlers(eventName);
        var parts = new List<string>(handlers.Count);
        foreach (var (_, handler) in handlers)
        {
            if (handler is not Func<string> provider) continue;
            try
            {
                var text = provider();
                if (!string.IsNullOrWhiteSpace(text)) parts.Add(text);
            }
            catch
            {
                // 单个处理器失败不影响其他处理器
            }
        }
        return string.Join(separator, parts);
    }

    /// <summary>触发指定事件：按注册顺序调用所有 <see cref="Action"/> 订阅者，单个异常不影响其余。</summary>
    public void Publish(string eventName)
    {
        foreach (var (_, handler) in GetHandlers(eventName))
        {
            if (handler is not Action action) continue;
            try { action(); }
            catch { /* 单个订阅者失败不影响其他 */ }
        }
    }

    private void Raise(string eventName, string pluginName, string message)
        => ConflictDetected?.Invoke(this, new PluginEventConflictEventArgs(eventName, pluginName, message));
}

/// <summary>常用插件事件名常量 —— 新事件接入事件池时在此登记，避免魔法字符串</summary>
public static class EventNames
{
    /// <summary>宠物悬浮进入：鼠标进入宠物时触发，handler 类型 <see cref="Action"/>（插件在此启动悬浮展示）</summary>
    public const string PetHoverEntered = "pet.hover.entered";

    /// <summary>宠物悬浮离开：鼠标离开宠物时触发，handler 类型 <see cref="Action"/>（插件在此停止悬浮展示）</summary>
    public const string PetHoverExited = "pet.hover.exited";

    /// <summary>宠物单击：单击宠物时触发，handler 类型 <see cref="Action"/>（插件可响应单击）</summary>
    public const string PetClicked = "pet.clicked";

    /// <summary>宠物双击：双击宠物时触发，handler 类型 <see cref="Action"/>（插件可响应双击）</summary>
    public const string PetDoubleClicked = "pet.doubleclicked";
}
