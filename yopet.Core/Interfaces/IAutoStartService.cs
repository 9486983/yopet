namespace yopet.Core.Interfaces;

/// <summary>开机自启动管理</summary>
public interface IAutoStartService
{
    bool IsEnabled { get; }
    void Enable();
    void Disable();
}
