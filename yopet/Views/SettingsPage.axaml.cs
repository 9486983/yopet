using Avalonia.Controls;
using yopet.Services;

namespace yopet.Views;

public partial class SettingsPage : UserControl
{
    private bool _initialized;

    public SettingsPage()
    {
        InitializeComponent();

        // 页面加载完成后标记初始化完毕，避免首次绑定触发操作
        Loaded += (_, _) => _initialized = true;

        // 开机自启动开关：即时生效
        AutoStartToggle.IsCheckedChanged += (_, _) =>
        {
            if (!_initialized) return; // 忽略首次绑定
            if (AutoStartToggle.IsChecked == true)
                new AutoStartService().Enable();
            else
                new AutoStartService().Disable();
        };
    }
}
