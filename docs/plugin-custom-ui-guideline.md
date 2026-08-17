# 插件自定义 UI（dll 内嵌 XAML）开发指南

> 状态：可行性已验证（spike 通过）· 更新时间：2026-08-17 · 适用 Avalonia **12.0.3**
> 相关文档：`docs/hover-radial-menu.md`、`docs/avalonia-animation-libs.md`

## 一、结论与验证依据

**插件 dll 可以内嵌编译型 XAML（.axaml）构建自定义页面，且与热重载（collectible AssemblyLoadContext）完全兼容。** 已通过 spike 实证：

| 验证项 | 结果 |
|---|---|
| collectible ALC 加载插件 dll（热重载同款） | ✅ |
| 编译型 .axaml 页面实例化（`x:Class` + `InitializeComponent`） | ✅ |
| 控件树构建 / DataContext 绑定生效 | ✅ |
| 工厂方法把页面实例交给宿主 | ✅ |
| 页面引用释放后 ALC 完整卸载（热重载可行） | ✅ |

**关键前提**：插件必须显式引用 `Avalonia 12.0.3`（见 §四.1）。

## 二、架构总览

```
插件 dll（collectible ALC）
├── Views/MyPanel.axaml        → 编译为 UserControl 派生类
├── MyPanel.axaml.cs           → InitializeComponent()
├── PageFactory.cs             → public static Control CreatePage() => new MyPanel();
└── MyPlugin.cs                → host.ShowCustomViewAsync(PageFactory.CreatePage())
                                    │
                                    ▼
宿主（主程序，默认 ALC）
├── IPluginHost.ShowCustomViewAsync(Control, title)   ← SDK 新增
├── PluginHostImpl（回调 OnShowCustomView）
└── PetWindow.ShowDialog(control)                     ← 现有弹窗承载
```

要点：**页面实例由插件自己创建（走 `Load(object)` 机制，天然兼容 collectible ALC），宿主只负责展示**；宿主**不**按 `avares://` Uri 加载插件资源（那条路在热加载下不可行，见 §五）。

## 三、SDK 需要修改的清单

### 1. `yopet.Sdk/IPluginHost.cs` — 新增一个方法

```csharp
// ── 插件自定义页面 ──

/// <summary>
/// 在宿主弹窗中显示插件自定义页面（编译型 XAML 或代码构建的 Control）。
/// 页面实例由插件创建，宿主仅负责展示；调用方需确保页面被关闭且无外部引用后
/// 可被热重载卸载（见 docs/plugin-custom-ui-guideline.md）。
/// </summary>
/// <param name="view">插件构建的页面实例（建议由工厂方法创建）</param>
/// <param name="title">可选标题（页面自带标题时传 null）</param>
Task ShowCustomViewAsync(Control view, string? title = null);
```

- `Control` 类型来自 Avalonia——`yopet.Sdk` 经 `Lang.Avalonia` 传递引用 Avalonia 12.0.3，**SDK csproj 无需新增包引用**。
- 返回值 `Task`：约定"页面被关闭后完成"，便于插件等待用户操作结果；当前实现可先 `Task.CompletedTask`。

### 2. `yopet.Services/PluginHostImpl.cs` — 实现

```csharp
/// <summary>显示插件自定义页面的 UI 回调（由 App 层设置，传入已实例化的 Control）</summary>
public Func<Control, string?, Task>? OnShowCustomView { get; set; }

public Task ShowCustomViewAsync(Control view, string? title = null)
    => OnShowCustomView != null ? OnShowCustomView(view, title) : Task.CompletedTask;
```

### 3. `yopet/App.axaml.cs` — 接线到现有弹窗

```csharp
pluginHost.OnShowCustomView = (view, title) =>
{
    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
    {
        var popup = PetWindow.ShowDialogOn(petWindow, view); // 复用 DialogPopup
        // 关闭交互（如点外部/Esc/页面内按钮）以最终实现为准；
        // 关闭时必须释放 view 引用，否则热重载卸载会超时（现有 UnloadAndWait 警告）
    });
    return Task.CompletedTask;
};
```

### 4. 可选：关闭约定

若希望 `ShowCustomViewAsync` 的 Task 在页面关闭时完成，宿主侧用 `TaskCompletionSource` + Popup 关闭事件（`popup.Closed` 或自定义关闭按钮）。本期文档不做强制要求，实现时二选一即可。

## 四、插件开发者使用步骤

### 1. 插件 csproj：必须显式引用 Avalonia 12.0.3

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <CopyLocalLockFileAssemblies>true</CopyLocalLockFileAssemblies>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\yopet.Sdk\yopet.Sdk.csproj" />
    <!-- 关键：显式引用 Avalonia（Lang.Avalonia 传递的 Avalonia exclude 了 Build 目标，
         .axaml 的 XamlIl 编译需要本包提供的 MSBuild 目标；版本必须与主程序一致 12.0.3） -->
    <PackageReference Include="Avalonia" Version="12.0.3" />
  </ItemGroup>
</Project>
```

> ⚠️ **不要**只靠 `yopet.Sdk` 传递的 Avalonia——`Lang.Avalonia` 的依赖声明 `exclude="Build,Analyzers"`，XAML 编译目标不会传递，`.axaml` 不会编译成资源，实例化会报 `No precompiled XAML found`。

### 2. 编写 .axaml 页面

`Views/MonitorPanel.axaml`：

```xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             x:Class="MyPlugin.Views.MonitorPanel"
             x:CompileBindings="False"
             Width="300">
  <StackPanel Spacing="8" Margin="12">
    <Border Background="#3319c8b9" CornerRadius="8" Padding="8">
      <TextBlock Text="我的插件页面" FontSize="15" FontWeight="Bold"/>
    </Border>
    <TextBlock Text="{Binding Status}"/>
    <Button Content="关闭" Click="OnCloseClick"/>
  </StackPanel>
</UserControl>
```

`Views/MonitorPanel.axaml.cs`：

```csharp
using Avalonia.Controls;

namespace MyPlugin.Views;

public partial class MonitorPanel : UserControl
{
    public MonitorPanel() => InitializeComponent();
}
```

### 3. 页面工厂（把实例交给宿主）

```csharp
using Avalonia.Controls;
using MyPlugin.Views;

namespace MyPlugin;

public static class PageFactory
{
    public static Control CreateMonitorPage() => new MonitorPanel();
}
```

### 4. 插件主体调用宿主通道

```csharp
public override Task InitializeAsync(IPluginHost host)
{
    // 例如右键动作：点击弹出插件页面
    host.RegisterAction(new PluginAction
    {
        Name = "打开监控面板",
        Emoji = "📊",
        Target = ActionTarget.ContextMenu,
        Callback = async () => await host.ShowCustomViewAsync(PageFactory.CreateMonitorPage()),
    });
    return Task.CompletedTask;
}
```

### 5. 绑定注意

- `.axaml` 里用 `{Binding}`（运行时绑定）时，给 `UserControl` 加 `x:CompileBindings="False"`（spike 采用方式）；
- 想用编译绑定（`x:CompileBindings="True"`）则必须给每个 `{Binding}` 提供 `x:DataType`，否则编译报 `AVLN2100`。

## 五、关键约束与注意事项

| 约束 | 说明 |
|---|---|
| **Avalonia 版本锁 12.0.3** | 12.1.x 的 XAML 编译/加载有回归（spike 实证：默认 ALC 也报 `No precompiled XAML found`）。**勿升级** |
| **插件与主程序 Avalonia 版本一致** | 插件 dll 引用 12.0.3，否则程序集绑定冲突（`Could not load Avalonia.Controls, Version=...`） |
| **页面由插件实例化** | 宿主**不要**按 `avares://插件程序集/...` Uri 加载（`AssetLoader` 无 collectible ALC 感知，`Assembly.Load` 全局找不到插件程序集）；插件代码内 `Load(object)` 机制天然可用 |
| **字符串 XAML 不可用** | Avalonia 12.x 无 `AvaloniaXamlLoader.Parse(string)` 公共 API |
| **热重载卸载** | 页面实例释放引用后 ALC 可卸载（spike ⑦ 通过）；宿主弹窗关闭时必须释放 `view` 引用，否则 `UnloadAndWait` 超时告警 |
| **样式/主题** | 页面内联样式（`Background`/`CornerRadius` 等）可直接写；跨程序集 `DynamicResource` 引用主程序主题色**未验证**，需要时另行 spike |
| **图标素材** | 可引入 `Svg.Controls.Avalonia`（12.0.0.15）等，见 `docs/avalonia-animation-libs.md` |

## 六、落地实施顺序（后续按需推进）

1. SDK：`IPluginHost.ShowCustomViewAsync` + `PluginHostImpl` 回调 + `App.axaml.cs` 接线（≈30 行）
2. 弹窗关闭交互与 `Task` 完成约定（`TaskCompletionSource`）
3. 用本项目一个插件（如 SystemUsagePlugin 的配置页）改造为 .axaml 页面，端到端验证
4. 按需补充：跨程序集主题资源桥接 spike
