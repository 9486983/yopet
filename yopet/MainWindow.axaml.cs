using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using yopet.ViewModels;

namespace yopet;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        // 窗口按钮
        MinimizeBtn.Click += (_, _) => WindowState = WindowState.Minimized;
        MaximizeBtn.Click += (_, _) =>
            WindowState = WindowState == WindowState.FullScreen
                ? WindowState.Normal
                : WindowState.FullScreen;
        // 关闭时隐藏而非退出（宠物窗才是主窗口）
        CloseBtn.Click += (_, _) => Hide();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is not MainViewModel vm) return;
        vm.NavigateCallback = index => vm.SelectedTabIndex = index;
    }

    private void TitleBar_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            BeginMoveDrag(e);
    }
}
