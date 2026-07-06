using Microsoft.Win32;
using yopet.Core.Interfaces;

namespace yopet.Services;

/// <summary>
/// 开机自启动服务（Windows 注册表实现）
/// 写入 HKCU\Software\Microsoft\Windows\CurrentVersion\Run
/// </summary>
public class AutoStartService : IAutoStartService
{
    private const string RegistryPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string EntryName = "yopet";

    /// <summary>获取当前进程的可执行文件路径</summary>
    private static string ExecutablePath =>
        Environment.ProcessPath ?? Environment.GetCommandLineArgs()[0];

    public bool IsEnabled
    {
        get
        {
            if (!OperatingSystem.IsWindows()) return false;
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RegistryPath);
                var value = key?.GetValue(EntryName) as string;
                return !string.IsNullOrEmpty(value);
            }
            catch
            {
                return false;
            }
        }
    }

    public void Enable()
    {
        if (!OperatingSystem.IsWindows()) return;
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegistryPath, writable: true);
            key?.SetValue(EntryName, $"\"{ExecutablePath}\"");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"设置自启动失败: {ex.Message}");
        }
    }

    public void Disable()
    {
        if (!OperatingSystem.IsWindows()) return;
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegistryPath, writable: true);
            key?.DeleteValue(EntryName, throwOnMissingValue: false);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"关闭自启动失败: {ex.Message}");
        }
    }
}
