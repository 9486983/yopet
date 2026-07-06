using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using yopet.Core.Interfaces;
using yopet.Core.Models;

namespace yopet.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly IConfigService _configService;
    private readonly IDispatcherService _dispatcher;

    public Action<int>? NavigateCallback { get; set; }
    public Action<bool>? TogglePetCallback { get; set; }
    public Action? ToggleFullscreenCallback { get; set; }

    /// <summary>已加载的插件信息（只读展示用）</summary>
    public List<PluginInfo> LoadedPlugins { get; } = new();

    public MainViewModel(IConfigService configService, IDispatcherService dispatcher,
        Services.PluginLoader? pluginLoader = null)
    {
        _configService = configService;
        _dispatcher = dispatcher;
        _config = configService.Config;
        _animSpeed = configService.Config.AnimFrameDurationMs;
        _isDarkTheme = configService.Config.IsDarkTheme;
        _config.IsDarkTheme = _isDarkTheme;

        // 填充插件信息
        if (pluginLoader != null)
        {
            foreach (var p in pluginLoader.Plugins)
                LoadedPlugins.Add(new PluginInfo { Name = p.Name, Version = p.Version, Description = p.Description });
        }
    }

    [ObservableProperty]
    private int _selectedTabIndex;

    [ObservableProperty]
    private AppConfig _config;

    [ObservableProperty]
    private bool _isPetVisible;

    /// <summary>深色模式开关（即时生效）</summary>
    [ObservableProperty]
    private bool _isDarkTheme = true;

    partial void OnIsDarkThemeChanged(bool value)
    {
        Config.IsDarkTheme = value;
        _configService.Save();
        PetEvents.NotifyThemeChanged(value);
    }

    // ── 动画速度 ──

    [ObservableProperty]
    private double _animSpeed = 100.0;

    partial void OnAnimSpeedChanged(double value)
    {
        var rounded = (int)Math.Round(value);
        Config.AnimFrameDurationMs = rounded;
        _configService.Save();
        PetEvents.NotifyConfigSaved();
    }

    public bool IsHomeSelected => SelectedTabIndex == 0;
    public bool IsSettingsSelected => SelectedTabIndex == 1;

    partial void OnSelectedTabIndexChanged(int value)
    {
        _dispatcher.Post(() =>
        {
            OnPropertyChanged(nameof(IsHomeSelected));
            OnPropertyChanged(nameof(IsSettingsSelected));
        });
    }

    [RelayCommand]
    private void GoToHome() => NavigateCallback?.Invoke(0);

    [RelayCommand]
    private void GoToSettings() => NavigateCallback?.Invoke(1);

    [RelayCommand]
    private void ToggleFullscreen() => ToggleFullscreenCallback?.Invoke();

    [RelayCommand]
    private void SaveConfig()
    {
        _configService.Save();
        PetEvents.NotifyConfigSaved();
    }
}
