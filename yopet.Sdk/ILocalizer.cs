namespace yopet.Sdk;

/// <summary>本地化文本提供器 —— 通过依赖注入获取，避免直接访问静态 I18nManager。</summary>
public interface ILocalizer
{
    /// <summary>创建绑定固定 key 前缀的子本地化器。</summary>
    ILocalizer For(string prefix);

    /// <summary>读取 key 对应的本地化文本；缺失时回退为 key 本身。</summary>
    string Get(string key);

    /// <summary>切换当前语言（culture name，如 zh-CN / en-US）。</summary>
    void SetCulture(string cultureName);
}
