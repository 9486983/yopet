namespace yopet.Services.PluginHosting;

/// <summary>
/// 插件宿主状态重置抽象 —— 热重载前由宿主实现，清空插件注册的残留状态
/// （动作列表、激活态、会话等），避免新旧插件实例状态串扰。
/// </summary>
public interface IPluginStateResetter
{
    /// <summary>清空插件在宿主上注册的所有状态</summary>
    void ResetPluginState();
}
