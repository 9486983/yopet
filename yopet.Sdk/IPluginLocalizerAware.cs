namespace yopet.Sdk;

/// <summary>由插件宿主在初始化前注入本地化器。</summary>
public interface IPluginLocalizerAware
{
    void SetLocalizer(ILocalizer localizer);
}
