using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace XamlDemoPlugin.Views;

/// <summary>
/// 插件 dll 内嵌的编译型 XAML 页面。
/// InitializeComponent 由 Avalonia 的 XamlIl 编译器生成，
/// 走 <c>AvaloniaXamlLoader.Load(object)</c>（基于实例类型程序集），
/// 因此在热重载的 collectible AssemblyLoadContext 下同样可用。
/// </summary>
public partial class DemoPanel : UserControl
{
    public DemoPanel()
    {
        InitializeComponent();
        DataContext = new DemoViewModel();
    }

    private void OnButtonClick(object? sender, RoutedEventArgs e)
    {
        var vm = (DemoViewModel)DataContext!;
        vm.Message = string.IsNullOrWhiteSpace(Input.Text)
            ? "你还没输入内容～"
            : $"你输入了：{Input.Text}";
    }
}

/// <summary>演示页面 ViewModel（手写 INotifyPropertyChanged，运行时绑定用）</summary>
public sealed class DemoViewModel : INotifyPropertyChanged
{
    private string _message = "等待输入…";

    public string Message
    {
        get => _message;
        set
        {
            _message = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Message)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
