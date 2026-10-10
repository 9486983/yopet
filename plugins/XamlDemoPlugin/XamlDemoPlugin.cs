using Lang.Avalonia;
using yopet.Sdk;
using XamlDemoPlugin.Views;

namespace XamlDemoPlugin;

/// <summary>
/// XAML 自定义页面示例插件 —— 演示插件 dll 内嵌编译型 .axaml，
/// 通过 <see cref="IPluginHost.ShowCustomViewAsync"/> 在宿主弹窗中显示。
///
/// 使用方式与约束见 docs/plugin-custom-ui-guideline.md：
///   - csproj 必须显式引用 Avalonia 12.0.3（XamlIl 编译目标不随 yopet.Sdk 传递）；
///   - 页面由插件实例化（工厂），宿主仅负责展示；
///   - 点击弹窗外部关闭。
/// </summary>
[Plugin("XAML 示例", Version = "1.0.0",
    Description = "演示插件 dll 内嵌编译型 XAML 自定义页面（Avalonia 12.0.3）")]
public class XamlDemoPlugin : PluginBase
{
    /// <summary>取当前语言的插件词条；读取失败回退默认值</summary>
    private static string T(string key, string fallback)
    {
        try
        {
            var value = I18nManager.Instance.GetResource($"Localization.XamlDemoPlugin.{key}");
            return string.IsNullOrWhiteSpace(value) ? fallback : value;
        }
        catch
        {
            return fallback;
        }
    }

    public override string Name => T("Name", "XAML 示例");

    public override Task InitializeAsync(IPluginHost host)
    {
        host.RegisterAction(new PluginAction
        {
            Name = T("OpenAction", "打开 XAML 面板"),
            Emoji = "🧩",
            Description = T("OpenActionDesc", "在宿主弹窗中显示插件内嵌的编译型 XAML 页面"),
            Target = ActionTarget.ContextMenu,
            Callback = async () => await host.ShowCustomViewAsync(PageFactory.CreatePanel()),
        });

        return Task.CompletedTask;
    }
}
