using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;
using Lang.Avalonia;
using Lang.Avalonia.Json;
using Microsoft.Extensions.DependencyInjection;
using System.Globalization;
using System.Runtime.CompilerServices;
using yopet.Core.Interfaces;
using yopet.Sdk;
using yopet.Services;
using yopet.ViewModels;
using yopet.UI;

namespace yopet;

public partial class App : Application
{
    public static PetViewModel? PetViewModel { get; private set; }

    private ServiceProvider? _serviceProvider;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        ScrollViewer.LoadedEvent.AddClassHandler<ScrollViewer>((scroll, _) => AttachScrollBehavior(scroll));

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // ── 构建 DI 容器 ──
            var services = new ServiceCollection();

            // 基础设施（Singleton）
            services.AddSingleton<IConfigService, ConfigService>();
            services.AddSingleton<IDispatcherService, AvaloniaDispatcherService>();
            services.AddSingleton<IPetdexService, PetdexService>();
            services.AddSingleton<IPluginLogger>(_ => new PluginLogger(
                new LoggerService(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "plugins", "logs")),
                "Host"));
            services.AddSingleton<ILocalizer>(_ => new Localizer());
            services.AddSingleton<ISchedulerService, SchedulerService>();
            services.AddSingleton<IPluginScheduler, PluginScheduler>();
            services.AddSingleton<PluginHostImpl>();
            services.AddSingleton<IPluginHost>(sp => sp.GetRequiredService<PluginHostImpl>());
            services.AddSingleton<PluginLoader>();

            // ViewModels（Singleton）
            _serviceProvider = services.BuildServiceProvider();

            // ── 从容器解析服务 ──
            var configService = _serviceProvider.GetRequiredService<IConfigService>();
            var dispatcher = _serviceProvider.GetRequiredService<IDispatcherService>();
            var petdexService = _serviceProvider.GetRequiredService<IPetdexService>();
            var localizer = _serviceProvider.GetRequiredService<ILocalizer>();
            Localization.Current = localizer;
            var pluginHost = _serviceProvider.GetRequiredService<PluginHostImpl>();
            var pluginLoader = _serviceProvider.GetRequiredService<PluginLoader>();
            var scheduler = _serviceProvider.GetRequiredService<ISchedulerService>();
            var config = configService.Config;

            // ── 注册多语言资源（JSON 文件位于输出目录 I18n/） ──
            try
            {
                var culture = string.IsNullOrWhiteSpace(config.Language)
                    ? new CultureInfo("zh-CN")
                    : new CultureInfo(config.Language);
                I18nManager.Instance.Register(new JsonLangPlugin(), culture, out var i18nError);
                if (!string.IsNullOrWhiteSpace(i18nError))
                    System.Diagnostics.Debug.WriteLine($"[I18n] 注册失败: {i18nError}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[I18n] 注册异常: {ex.Message}");
            }

            // ── 加载主题资源 ──
            var savedTheme = config.PluginValues.TryGetValue("st_theme", out var themeId)
                ? themeId
                : null;
            ArcUi.ApplyTheme(savedTheme ?? (config.IsDarkTheme ? "dark" : "light"));
            ArcUi.ApplyLanguage(config.Language);

            // ── 监听主题切换 ──
            Core.Models.PetEvents.ThemeChanged += isDark =>
                Avalonia.Threading.Dispatcher.UIThread.Post(() => ArcUi.ApplyAppearance(isDark));

            pluginHost.LanguageChanged += (_, cultureName) =>
                Avalonia.Threading.Dispatcher.UIThread.Post(() => ArcUi.ApplyLanguage(cultureName));

            // ── 加载并初始化插件 ──
            pluginLoader.LoadFromDirectory(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "plugins"));
            var pluginInitTask = pluginLoader.InitializeAllAsync(pluginHost);

            // ── 启动全局定时任务调度器（主程序与所有插件共用，Quartz.NET） ──
            scheduler.Start();
            desktop.Exit += (_, _) =>
            {
                scheduler.Stop();
                _serviceProvider?.Dispose();
            };

            // ── 宠物窗口 ──
            var petVm = new PetViewModel(configService, dispatcher, petdexService,
                pluginHost.PluginActions,
                pluginHost.FileActions,
                pluginHost.Events);
            PetViewModel = petVm;
            petVm.RefreshActionsCallback = () =>
            {
                petVm.RefreshPluginActions(pluginHost.PluginActions);
                petVm.RefreshFileActions(pluginHost.FileActions);
            };
            _ = RefreshPluginsWhenReadyAsync(pluginInitTask, petVm, pluginHost);
            var petWindow = new PetWindow
            {
                DataContext = petVm,
                Position = new PixelPoint((int)config.PetWindowX, (int)config.PetWindowY),
            };

            // ── 连接插件气泡回调 ──
            pluginHost.OnShowThought = (title, text) =>
                petVm.ShowFileDropInfo(title, text);
            pluginHost.OnShowQueuedThought = (title, text) =>
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    petVm.ThoughtAssistant = PetViewModel.FormatBubbleTitle(title);
                    petVm.ThoughtText = text;
                    petVm.IsShowingThought = true;
                });
            pluginHost.OnHideThought = () =>
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    petVm.IsShowingThought = false);
            pluginHost.OnShowReaction = emoji =>
                petVm.ShowReaction(emoji);
            pluginHost.OnStartAnimation = anim =>
                petVm.AnimCurrentRow = (int)anim;
            pluginHost.OnStopAnimation = () =>
                petVm.AnimCurrentRow = 0;
            pluginHost.OnTaskRunningChanged = running =>
                petVm.IsTaskRunning = running;
            petVm.CancelTaskCallback = () =>
                pluginHost.CancelCurrentTask();

            // ── 热重载插件（右键菜单触发：清理→卸载→重载→初始化→刷新 UI 动作） ──
            petVm.ReloadPluginsCallback = async () =>
            {
                try
                {
                    await pluginHost.ReloadAllAsync();
                    petVm.RefreshPluginActions(pluginHost.PluginActions);
                    petVm.RefreshFileActions(pluginHost.FileActions);
                    petVm.ShowFileDropInfo(
                        localizer.Get("Localization.PetWindow.ReloadPluginsDone"),
                        string.Format(localizer.Get("Localization.PetWindow.ReloadPluginsDoneMsg"),
                            pluginHost.PluginActions.Count, pluginHost.FileActions.Count));
                }
                catch (Exception ex)
                {
                    petVm.ShowFileDropInfo(
                        localizer.Get("Localization.PetWindow.ReloadPluginsFailed"),
                        ex.Message);
                }
            };

            // ── 连接会话事件 ──
            pluginHost.OnSessionStarted = session =>
                Avalonia.Threading.Dispatcher.UIThread.Post(() => petVm.OnSessionStarted(session));
            pluginHost.OnSessionEnded = () =>
                Avalonia.Threading.Dispatcher.UIThread.Post(() => petVm.OnSessionEnded());
            petVm.EndSessionCallback = () =>
                pluginHost.CurrentSession?.Cancel();

            // ── 插件事件池冲突提示（重复注册/多插件共存时展示给用户，仅注册阶段触发一次） ──
            pluginHost.Events.ConflictDetected += (_, args) =>
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    System.Diagnostics.Debug.WriteLine($"[EventPool] {args.Message}");
                    if (App.PetViewModel is { } vm)
                        vm.ShowFileDropInfo("⚠️ 插件事件冲突", args.Message);
                });

            // ── 连接插件配置弹窗 ──
            pluginHost.OnShowPluginConfig = async section =>
            {
                try
                {
                    await Views.PluginConfigDialog.ShowAsync(petWindow, section,
                        key => configService.GetPluginValue(key),
                        values => pluginHost.SavePluginConfig(values));
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[ConfigDialog] Error: {ex}");
                    // 出错时尝试用简单方式显示
                    pluginHost.ShowThought(localizer.Get("Localization.PetWindow.ConfigLoadFailed"), ex.Message);
                }
            };

            // ── 连接剪贴板 ──
            petVm.ClipboardSetText = text =>
            {
                try
                {
                    var psi = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "powershell",
                        Arguments = $"-Command \"Set-Clipboard -Value '{text.Replace("'", "''")}'\"",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                    };
                    System.Diagnostics.Process.Start(psi);
                }
                catch { }
            };

            // ── 连接插件输入框回调 ──
            pluginHost.OnShowInputDialog = async (title, placeholder, initial) =>
                await Views.InputDialog.ShowAsync(petWindow, title, placeholder, initial);

            // ── 连接插件确认框回调 ──
            pluginHost.OnShowConfirmDialog = async (title, text) =>
                await Views.ConfirmDialog.ShowAsync(petWindow, title, text);

            // ── 连接插件选项/输入对话框回调 ──
            pluginHost.OnShowPromptDialog = config =>
                Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(
                    () => Views.PromptDialog.ShowAsync(petWindow, config));

            pluginHost.OnShowDialog = dialog =>
            {
                var popup = PetWindow.ShowDialogOn(
                    petWindow,
                    dialog.View,
                    dialog.Options.ShowClose,
                    dialog.Options.AlignToBubbleBottom,
                    dialog.Options.BottomInset);

                popup.Closed += (_, _) => dialog.Cancel();
                return () => { if (popup.IsVisible) popup.Close(); };
            };

            // ── 连接插件列表弹窗回调 ──
            pluginHost.OnShowListDialog = async config =>
                await Views.ListDialog.ShowAsync(petWindow, config);

            // ── 连接插件自定义页面弹窗（插件 dll 内嵌 XAML，点击弹窗外部关闭） ──
            pluginHost.OnShowCustomView = (view, _) =>
            {
                PetWindow.ShowDialogOn(petWindow, view);
                return Task.CompletedTask;
            };

            desktop.MainWindow = petWindow;
            petWindow.Show();

        }

        base.OnFrameworkInitializationCompleted();
    }

    private static readonly ConditionalWeakTable<ScrollViewer, object> ScrollBehaviors = new();

    private static void AttachScrollBehavior(ScrollViewer scroll)
    {
        if (ScrollBehaviors.TryGetValue(scroll, out _)) return;
        ScrollBehaviors.Add(scroll, new object());

        var timer = new Avalonia.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(700),
        };

        void SetBars(double opacity, bool hitTest)
        {
            foreach (var bar in scroll.GetVisualDescendants().OfType<Avalonia.Controls.Primitives.ScrollBar>())
            {
                bar.Opacity = opacity;
                bar.IsHitTestVisible = hitTest;
            }
        }

        void Hide()
        {
            timer.Stop();
            SetBars(0, false);
        }

        void Show()
        {
            SetBars(1, true);
            timer.Stop();
            timer.Start();
        }

        timer.Tick += (_, _) => Hide();
        scroll.AllowAutoHide = false;
        scroll.ScrollChanged += (_, e) =>
        {
            if (!scroll.IsPointerOver) return;
            if (e.OffsetDelta.X == 0 && e.OffsetDelta.Y == 0) return;
            Show();
        };
        scroll.PointerExited += (_, _) => Hide();
        Avalonia.Threading.Dispatcher.UIThread.Post(Hide, Avalonia.Threading.DispatcherPriority.Loaded);
    }

    private static async Task RefreshPluginsWhenReadyAsync(
        Task initTask, PetViewModel petVm, PluginHostImpl pluginHost)
    {
        try { await initTask; }
        catch { }

        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            petVm.RefreshPluginActions(pluginHost.PluginActions);
            petVm.RefreshFileActions(pluginHost.FileActions);


        });
    }

}
