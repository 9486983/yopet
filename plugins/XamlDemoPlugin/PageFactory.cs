using Avalonia.Controls;
using XamlDemoPlugin.Views;

namespace XamlDemoPlugin;

/// <summary>
/// 页面工厂 —— 插件把页面实例交给宿主显示的标准通道。
/// 宿主只接收已实例化的 <see cref="Control"/>，不做资源加载。
/// </summary>
public static class PageFactory
{
    public static Control CreatePanel() => new DemoPanel();
}
