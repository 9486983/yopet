using System.Globalization;
using Lang.Avalonia;
using yopet.Sdk;

namespace yopet.Services;

/// <summary>本地化实现 —— 包装共享 I18nManager，附带固定 key 前缀。</summary>
public sealed class Localizer : ILocalizer
{
    private readonly string _prefix;

    public Localizer(string prefix = "") => _prefix = prefix.Trim('.');

    public ILocalizer For(string prefix)
    {
        prefix = prefix.Trim('.');
        return new Localizer(string.IsNullOrEmpty(_prefix) ? prefix : $"{_prefix}.{prefix}");
    }

    public string Get(string key) =>
        I18nManager.Instance.GetResource(string.IsNullOrEmpty(_prefix) ? key : $"{_prefix}.{key}");

    public void SetCulture(string cultureName)
    {
        if (string.IsNullOrWhiteSpace(cultureName)) return;
        I18nManager.Instance.Culture = new CultureInfo(cultureName);
    }
}
