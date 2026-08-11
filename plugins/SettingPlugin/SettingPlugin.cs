using System.Diagnostics;
using Lang.Avalonia;
using yopet.Core.Models;
using yopet.Sdk;

namespace SettingPlugin;

[Plugin("设置", Version = "1.0.0",
    Description = "开机自启、动画速度、深色模式与插件列表管理")]
public class SettingPlugin : PluginBase
{
    private const string KeyAutoStart = "st_auto_start";
    private const string KeyAnimSpeed = "st_anim_speed";
    private const string KeyDarkTheme = "st_dark_theme";
    private const string KeyLanguage = "st_language";

    private IPluginHost? _host;

    /// <summary>取当前语言的插件词条</summary>
    private static string T(string key) =>
        I18nManager.Instance.GetResource($"Localization.SettingPlugin.{key}");

    public override string Name => T("Name");

    public override string Description => T("Description");

    public override async Task InitializeAsync(IPluginHost host)
    {
        _host = host;
        await base.InitializeAsync(host);

        // ── 配置表单 ──
        host.RegisterConfig(new PluginConfigSection
        {
            Key = "setting",
            Title = T("Name"),
            Emoji = "⚙️",
            Fields = new()
            {
                new()
                {
                    Key = KeyAutoStart,
                    Label = T("AutoStartLabel"),
                    Type = PluginConfigFieldType.Boolean,
                    DefaultValue = AutoStartHelper.IsEnabled() ? "true" : "false",
                    Description = T("AutoStartDesc"),
                },
                new()
                {
                    Key = KeyAnimSpeed,
                    Label = T("AnimSpeedLabel"),
                    Type = PluginConfigFieldType.Number,
                    DefaultValue = host.GetAnimationSpeedMs().ToString("0"),
                    MinValue = 30, MaxValue = 300,
                    Description = T("AnimSpeedDesc"),
                },
                new()
                {
                    Key = KeyDarkTheme,
                    Label = T("DarkThemeLabel"),
                    Type = PluginConfigFieldType.Boolean,
                    DefaultValue = host.GetDarkTheme() ? "true" : "false",
                    Description = T("DarkThemeDesc"),
                },
                new()
                {
                    Key = KeyLanguage,
                    Label = T("LanguageLabel"),
                    Type = PluginConfigFieldType.Dropdown,
                    DefaultValue = host.GetLanguage(),
                    Options = new()
                    {
                        new() { Label = "简体中文", Value = "zh-CN" },
                        new() { Label = "English", Value = "en-US" },
                    },
                    Description = T("LanguageDesc"),
                },
            },
        }, Name);

        // ── 首次同步主程序当前值到插件配置（未设置过时） ──
        if (host.GetConfig(KeyAutoStart) == null)
            host.SetConfig(KeyAutoStart, AutoStartHelper.IsEnabled() ? "true" : "false");
        if (host.GetConfig(KeyAnimSpeed) == null)
            host.SetConfig(KeyAnimSpeed, host.GetAnimationSpeedMs().ToString("0"));
        if (host.GetConfig(KeyDarkTheme) == null)
            host.SetConfig(KeyDarkTheme, host.GetDarkTheme() ? "true" : "false");
        if (host.GetConfig(KeyLanguage) == null)
            host.SetConfig(KeyLanguage, host.GetLanguage());

        // ── 右键菜单入口 ──
        host.RegisterAction(new PluginAction
        {
            Name = T("Name"),
            Emoji = "⚙️",
            Group = T("Group"),
            Display = LocalizedDisplay.Of(name: () => T("Name"), group: () => T("Group")),
            Target = ActionTarget.ContextMenu,
            Callback = () => { host.ShowConfigDialog("setting"); return Task.CompletedTask; },
        });

        host.RegisterAction(new PluginAction
        {
            Name = T("PluginList"),
            Emoji = "🧩",
            Group = T("Group"),
            Display = LocalizedDisplay.Of(name: () => T("PluginList"), group: () => T("Group")),
            Target = ActionTarget.ContextMenu,
            Callback = () => ShowPluginListAsync(host),
        });

        // ── 配置变更即时生效 ──
        host.ConfigValueChanged += OnConfigChanged;

        host.Log("设置插件已加载");
    }

    private async Task ShowPluginListAsync(IPluginHost host)
    {
        var config = new ListDialogConfig
        {
            Title = T("PluginList"),
            Emoji = "🧩",
            Columns = new()
            {
                new() { Key = "name", Header = T("ColPlugin") },
                new() { Key = "desc", Header = T("ColDesc") },
            },
            DataSource = () => Task.FromResult(
                host.LoadedPlugins.Select(p => new Dictionary<string, string>
                {
                    ["name"] = $"{p.Name}  v{p.Version}",
                    ["desc"] = p.Description,
                }).ToList()),
        };
        await host.ShowListDialog(config);
    }

    private void OnConfigChanged(object? sender, string key)
    {
        var host = _host;
        if (host == null) return;

        switch (key)
        {
            case KeyAutoStart:
                if (host.GetConfig(KeyAutoStart) == "true")
                    AutoStartHelper.Enable();
                else
                    AutoStartHelper.Disable();
                break;

            case KeyAnimSpeed:
                if (double.TryParse(host.GetConfig(KeyAnimSpeed), out var ms))
                    host.SetAnimationSpeedMs(ms);
                break;

            case KeyDarkTheme:
                host.SetDarkTheme(host.GetConfig(KeyDarkTheme) == "true");
                break;

            case KeyLanguage:
                var lang = host.GetConfig(KeyLanguage);
                if (!string.IsNullOrWhiteSpace(lang))
                    host.SetLanguage(lang);
                break;
        }
    }

    public override Task CleanupAsync()
    {
        if (_host != null) _host.ConfigValueChanged -= OnConfigChanged;
        return base.CleanupAsync();
    }
}

/// <summary>
/// 开机自启动 —— 跨平台实现：
/// Windows 注册表 HKCU\...\Run / macOS ~/Library/LaunchAgents plist / Linux ~/.config/autostart
/// </summary>
internal static class AutoStartHelper
{
    private const string EntryName = "yopet";

    private static string ExecutablePath =>
        Environment.ProcessPath ?? Environment.GetCommandLineArgs()[0];

    public static bool IsEnabled()
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                using var key = Microsoft.Win32.Registry.CurrentUser
                    .OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
                var value = key?.GetValue(EntryName) as string;
                return !string.IsNullOrEmpty(value);
            }
            if (OperatingSystem.IsMacOS())
                return File.Exists(MacPlistPath);
            if (OperatingSystem.IsLinux())
                return File.Exists(LinuxDesktopPath);
        }
        catch { /* 读取失败按未启用处理 */ }
        return false;
    }

    public static void Enable()
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                using var key = Microsoft.Win32.Registry.CurrentUser
                    .OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", writable: true);
                key?.SetValue(EntryName, $"\"{ExecutablePath}\"");
            }
            else if (OperatingSystem.IsMacOS())
            {
                Directory.CreateDirectory(Path.GetDirectoryName(MacPlistPath)!);
                File.WriteAllText(MacPlistPath, BuildPlist());
            }
            else if (OperatingSystem.IsLinux())
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LinuxDesktopPath)!);
                File.WriteAllText(LinuxDesktopPath,
                    "[Desktop Entry]\n" +
                    "Type=Application\n" +
                    "Name=yopet\n" +
                    $"Exec={ExecutablePath}\n" +
                    "X-GNOME-Autostart-enabled=true\n");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"设置自启动失败: {ex.Message}");
        }
    }

    public static void Disable()
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                using var key = Microsoft.Win32.Registry.CurrentUser
                    .OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", writable: true);
                key?.DeleteValue(EntryName, throwOnMissingValue: false);
            }
            else if (OperatingSystem.IsMacOS() && File.Exists(MacPlistPath))
            {
                File.Delete(MacPlistPath);
            }
            else if (OperatingSystem.IsLinux() && File.Exists(LinuxDesktopPath))
            {
                File.Delete(LinuxDesktopPath);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"关闭自启动失败: {ex.Message}");
        }
    }

    private static string MacPlistPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        "Library", "LaunchAgents", "com.yopet.app.plist");

    private static string LinuxDesktopPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".config", "autostart", "yopet.desktop");

    private static string BuildPlist() =>
        "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n" +
        "<!DOCTYPE plist PUBLIC \"-//Apple//DTD PLIST 1.0//EN\" " +
        "\"http://www.apple.com/DTDs/PropertyList-1.0.dtd\">\n" +
        "<plist version=\"1.0\">\n" +
        "<dict>\n" +
        "    <key>Label</key>\n" +
        "    <string>com.yopet.app</string>\n" +
        "    <key>ProgramArguments</key>\n" +
        "    <array>\n" +
        $"        <string>{ExecutablePath}</string>\n" +
        "    </array>\n" +
        "    <key>RunAtLoad</key>\n" +
        "    <true/>\n" +
        "</dict>\n" +
        "</plist>\n";
}
