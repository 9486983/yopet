# yopet Plugin SDK 开发指南

> ## ⚠️ 跨平台约束（强制要求）
>
> yopet 是跨平台桌面应用（Windows / macOS / Linux 均需可编译、可运行）。开发或修改插件时必须遵守以下规则：
>
> 1. **禁止裸调用平台专属 API**：不得直接使用注册表、`user32.dll` / `gdi32.dll` 等 P/Invoke、`powershell`、`cmd.exe`、`explorer.exe`、`notepad.exe`、`shutdown` / `powercfg` 等 Windows 专属命令、可执行文件或路径。
> 2. **优先使用跨平台方案**：剪贴板优先用 Avalonia 内置 `Clipboard` API；打开文件/目录/URL 用 `Process.Start` + 按平台分支的命令（macOS 用 `open`，Windows 用 `explorer.exe` 等）。
> 3. **平台 API 必须守卫**：确需平台专属 API 时，必须用 `OperatingSystem.IsWindows()` / `OperatingSystem.IsMacOS()` 守卫包裹，并提供非目标平台的降级实现（返回 false、提示不可用或跳过功能），禁止让整个插件初始化失败。
> 4. **路径与进程跨平台**：路径一律用 `Path.Combine`，禁止硬编码 `\`、`C:\`、`python.exe` 等；可执行文件探测需覆盖各平台命名（如 `python` / `python3` / `py`）。
> 5. **优雅降级而非依赖异常**：插件 `InitializeAsync` 抛出的异常虽会被宿主捕获并跳过，但平台不可用时应在代码内显式降级，而不是靠异常兜底。

---

当你需要开发、修改或理解 yopet 桌面宠物应用的插件时使用。本文档涵盖 `yopet.Sdk` 命名空间下所有核心接口和类的使用方法。

---

## 1. 项目结构速览

```
yopet.Sdk/              ← NuGet 引用：yopet.Core
├── IPlugin.cs           插件入口接口
├── PluginBase.cs        插件基类（推荐继承）
├── PluginAttribute.cs   元数据特性
├── IPluginHost.cs       宿主交互接口（核心 API）
├── IPluginLogger.cs     日志接口
├── IPluginScheduler.cs  定时任务接口
├── ISession.cs          多步会话接口
├── PluginAction.cs      动作描述符
├── PluginConfig*.cs     配置系统
├── PetAnimation.cs      动画枚举
├── ThoughtMessage.cs    气泡消息
├── ListDialog*.cs       列表弹窗
└── ListColumn.cs / ListRowAction.cs / ListToolbarAction.cs
```

**依赖链**: 插件项目引用 `yopet.Sdk` → `yopet.Sdk` 内部引用 `yopet.Core`（仅使用 `ItemType`、`PetDefinition` 等模型）。

---

## 2. 快速开始 — 最小插件

### 2.1 项目文件 (.csproj)

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\yopet.Sdk\yopet.Sdk.csproj" />
  </ItemGroup>
</Project>
```

### 2.2 插件主体

```csharp
using yopet.Sdk;

namespace MyPlugin;

[Plugin("我的插件", Version = "1.0.0", Description = "一个示例插件")]
public class MyPlugin : PluginBase
{
    // PluginBase 自动从 [Plugin] 特性读取 Description
    public override string Name => "我的插件";

    public override async Task InitializeAsync(IPluginHost host)
    {
        await base.InitializeAsync(host);  // 保存 host 到 this.Host

        // 注册右键菜单动作
        host.RegisterAction(new PluginAction
        {
            Name = "打招呼",
            Emoji = "👋",
            Group = "🙋 我的插件",       // 右键菜单中的分组
            Target = ActionTarget.ContextMenu,
            Callback = async () =>
            {
                host.ShowThought("👋 你好！", "这是你的第一个插件！");
            },
        });

        host.Log("我的插件已加载");
    }

    public override Task CleanupAsync()
    {
        // 清理资源（主程序退出时调用）
        return base.CleanupAsync();
    }
}
```

### 2.3 构建与部署

```bash
# 构建
dotnet build -c Release

# 将输出的 .dll 复制到 yopet 输出目录的 plugins/ 下
# 例如: yopet/bin/Release/net9.0/win-x64/publish/plugins/
```

启动 yopet 后，插件自动从 `plugins/` 目录加载。右键宠物即可看到插件注册的动作。

---

## 3. 核心 API 详解

### 3.1 注册动作 (右键菜单 + 径向菜单)

`PluginAction` 支持两种出现位置：

| Target | 出现位置 | 回调参数 |
|--------|---------|---------|
| `ActionTarget.ContextMenu` | 右键宠物菜单 | `Callback: Func<Task>` |
| `ActionTarget.RadialMenu` | 拖放文件时弹出的径向菜单 | `FileCallback: Func<string[], Task>` |

**右键菜单示例 (ContextMenu)**:
```csharp
host.RegisterAction(new PluginAction
{
    Name = "查询余额",
    Emoji = "💰",
    Description = "查询 API 余额",
    Group = "🔑 我的服务",     // 同名 Group 会自动折叠
    Target = ActionTarget.ContextMenu,
    Callback = async () => { /* ... */ },
});
```

**径向菜单示例 (文件拖放)**:
```csharp
host.RegisterAction(new PluginAction
{
    Name = "压缩为 ZIP",
    Emoji = "📦",
    Target = ActionTarget.RadialMenu,
    AcceptType = ItemType.Both,       // File / Folder / Both
    FileExtensions = new[] { ".txt", ".md" },  // 文件过滤
    CanActivate = true,               // 允许设为默认操作
    FileCallback = async (paths) =>
    {
        // paths[0] 是被拖放的文件/文件夹路径
        await host.RunWithAnimation(PetAnimation.Running, async token =>
        {
            // 执行耗时操作...
        });
    },
});
```

### 3.2 显示气泡消息

```csharp
// 直接显示（新消息会覆盖当前气泡）
host.ShowThought("📌 已置顶", "记事本");

// 排队显示（前一条播完再播下一条，不打断）
host.EnqueueThought(new ThoughtMessage
{
    Title = "💬 第一条",
    Text = "重要消息",
    DurationMs = 8000,  // 默认 5000ms
});

// 清空队列
host.ClearThoughtQueue();

// 隐藏气泡
host.StopAnimation();  // 同时恢复待机动画
```

### 3.3 宠物动画控制

```csharp
// 触发一个短暂的反应动画（自动恢复待机）
host.ShowReaction("💰", PetAnimation.Jump);

// 持续动画（长时间任务时使用）
host.StartAnimation(PetAnimation.Running);

// 恢复待机
host.StopAnimation();

// 推荐：RunWithAnimation — 执行异步委托，自动管理动画 + 可取消 + 恢复待机
await host.RunWithAnimation(PetAnimation.Running, async token =>
{
    for (int i = 0; i < 10; i++)
    {
        token.ThrowIfCancellationRequested();  // 用户点击进度环取消时抛出
        await Task.Delay(1000, token);
    }
});

// 多动画轮换（长时间任务在不同动画间切换，避免单调）
await host.RunWithAnimation(
    new[] { PetAnimation.Running, PetAnimation.Wave, PetAnimation.Jump },
    async token =>
    {
        // 执行逻辑...
    });
```

**动画枚举**:

| 值 | 行号 | 含义 | 使用场景 |
|-----|------|------|---------|
| `Idle` | 0 | 待机/呼吸 | 默认状态 |
| `RunningRight` | 1 | 向右跑 | 活跃、正面反馈 |
| `RunningLeft` | 2 | 向左跑 | 返回、取消 |
| `Wave` | 3 | 挥手 | 打招呼、响应 |
| `Jump` | 4 | 跳跃 | 惊喜、成功 |
| `Failed` | 5 | 失败/沮丧 | 错误反馈 |
| `Waiting` | 6 | 等待 | 空闲等待 |
| `Running` | 7 | 忙碌工作 | 处理中、查询 |
| `Review` | 8 | 审查代码 | 阅读、分析 |

### 3.4 配置系统

**注册配置定义** (在 `InitializeAsync` 中调用):

```csharp
host.RegisterConfig(new PluginConfigSection
{
    Title = "我的插件配置",
    Emoji = "⚙️",
    Groups = new()   // 可选分组
    {
        new PluginConfigGroup
        {
            Title = "基本设置",
            Emoji = "🖥️",
            FieldKeys = { "my_name", "my_count" },
        },
    },
    Fields = new()
    {
        new()
        {
            Key = "my_name",
            Label = "名称",
            Type = PluginConfigFieldType.String,
            DefaultValue = "默认值",
            Description = "请输入名称",
        },
        new()
        {
            Key = "my_count",
            Label = "次数",
            Type = PluginConfigFieldType.Number,
            DefaultValue = "10",
            MinValue = 1,
            MaxValue = 100,
        },
        new()
        {
            Key = "my_enabled",
            Label = "启用",
            Type = PluginConfigFieldType.Boolean,
            DefaultValue = "true",
        },
        new()
        {
            Key = "my_mode",
            Label = "模式",
            Type = PluginConfigFieldType.Dropdown,
            DefaultValue = "auto",
            Options = new()
            {
                new() { Label = "自动", Value = "auto" },
                new() { Label = "手动", Value = "manual" },
            },
        },
        new()
        {
            Key = "my_path",
            Label = "文件路径",
            Type = PluginConfigFieldType.FilePath,
            Placeholder = "选择文件...",
        },
        new()
        {
            Key = "my_notes",
            Label = "备注",
            Type = PluginConfigFieldType.TextArea,
            TextAreaRows = 4,
        },
    },
    // 保存前校验
    Validate = values =>
    {
        var errors = new List<string>();
        if (string.IsNullOrEmpty(values.GetValueOrDefault("my_name")))
            errors.Add("名称不能为空");
        return errors;
    },
}, Name);
```

**读写配置值**:

```csharp
// 读取
var enabled = host.GetConfig("my_enabled");    // 返回 string?，默认值由 DefaultValue 指定
var count = int.TryParse(host.GetConfig("my_count"), out var n) ? n : 10;

// 写入
host.SetConfig("my_key", "my_value");

// 监听配置变更
host.ConfigValueChanged += (_, key) =>
{
    if (key == "my_enabled")
        RestartSomething();
};
```

**可用的字段类型**:

| 类型 | 用途 | 额外属性 |
|------|------|---------|
| `String` | 单行文本 | Placeholder |
| `Password` | 密码输入 | - |
| `Number` | 数字 | MinValue, MaxValue |
| `Boolean` | 开关 | - |
| `Dropdown` | 下拉选择 | Options |
| `FilePath` | 文件选择 | FileFilter, Placeholder |
| `FolderPath` | 文件夹选择 | Placeholder |
| `TextArea` | 多行文本 | TextAreaRows |
| `CronExpression` | Cron 表达式 | CronPresets |
| `Color` | 颜色选择 | - |

### 3.5 打开配置弹窗

```csharp
// 在右键菜单回调或其他地方调用
host.ShowConfigDialog("我的插件配置");  // 参数为注册时的 Title
```

### 3.6 弹窗交互

**输入框**:
```csharp
var input = await host.ShowInputDialog("标题", "占位符", "初始值");
if (input == null) return;  // 用户取消
// 使用 input
```

**确认框**:
```csharp
var confirmed = await host.ShowConfirmDialog("确认删除", "确定要删除吗？");
if (confirmed) { /* 执行删除 */ }
```

### 3.7 列表弹窗

强大的数据列表组件，支持表格和卡片网格两种布局。

**表格模式**:
```csharp
host.RegisterAction(new PluginAction
{
    Name = "脚本列表",
    Emoji = "📜",
    Group = "🐍 我的分组",
    Target = ActionTarget.ContextMenu,
    Callback = async () =>
    {
        var config = new ListDialogConfig
        {
            Title = "脚本管理器",
            Emoji = "🐍",
            LayoutMode = ListDialogLayoutMode.Table,

            // 动态数据源（每次刷新重新获取数据）
            DataSource = () => Task.FromResult(new List<Dictionary<string, string>>
            {
                new() { ["name"] = "hello.py", ["desc"] = "Hello World", ["status"] = "🟢" },
                new() { ["name"] = "backup.py", ["desc"] = "备份工具", ["status"] = "🔴" },
            }),

            Columns = new()
            {
                new() { Key = "name", Header = "名称", Width = 120 },
                new() { Key = "desc", Header = "描述", Width = double.NaN }, // 自动宽度
                new() { Key = "status", Header = "状态", Width = 70 },
                new()
                {
                    Key = "actions",
                    Header = "操作",
                    Type = ListColumnType.Action,
                    RowActions = new()
                    {
                        new()
                        {
                            Label = "运行",
                            Emoji = "▶",
                            Callback = row =>
                            {
                                // row["name"] 获取当前行数据
                                return Task.CompletedTask;
                            },
                        },
                        new()
                        {
                            Label = "更多",
                            Emoji = "⚙️",
                            Type = ListRowActionType.Dropdown,  // 下拉菜单
                            Children = new()
                            {
                                new() { Label = "编辑", Emoji = "📝", Callback = async row => { } },
                                new() { Label = "删除", Emoji = "🗑", Callback = async row => { } },
                            },
                        },
                    },
                },
            },

            // 工具栏
            ToolbarActions = new()
            {
                new() { Label = "刷新", Emoji = "🔄", Callback = async () => config.NotifyDataChanged() },
            },
        };

        await host.ShowListDialog(config);
    },
});
```

**卡片网格模式** (适合图片展示，如宠物图鉴):
```csharp
var config = new ListDialogConfig
{
    Title = "宠物列表",
    Emoji = "🐱",
    LayoutMode = ListDialogLayoutMode.CardGrid,
    CardFallbackEmoji = "🐱",
    CardImageProvider = row => row.GetValueOrDefault("thumbnail"),
    OnCardClick = async row =>
    {
        // 选中某卡片
        return true;  // true=关闭弹窗并选中, false=保持打开
    },
    DataSource = () => Task.FromResult(items),
};
```

**数据变更通知**: 从任何线程调用 `config.NotifyDataChanged()` 即可刷新列表。

### 3.8 日志

```csharp
// 通过 IPluginLogger (由 IPluginHost.Logger 提供)
host.Logger.Debug<MyPlugin>("调试信息");
host.Logger.Info<MyPlugin>("普通信息");
host.Logger.Warn<MyPlugin>("警告");
host.Logger.Error<MyPlugin>("错误", exception);

// 简单日志（输出到 Debug 窗口 + LogEmitted 事件）
host.Log("简单消息");

// 日志目录：默认 .yopet/logs/，可通过 LogPath 修改
```

### 3.9 定时任务

**Cron 表达式**:
```csharp
host.Scheduler.Register(
    jobId: "my_backup",           // 唯一标识
    cronExpression: "0 0 * * *",  // 每小时执行
    callback: async () =>
    {
        host.ShowThought("⏰ 定时任务", "执行备份...");
    },
    description: "⏰ 每小时备份");
```

**间隔执行** (秒级):
```csharp
host.Scheduler.RegisterInterval(
    jobId: "my_heartbeat",
    intervalSeconds: 30,   // 每30秒
    callback: async () => { /* ... */ },
    description: "💓 心跳");
```

**管理**:
```csharp
host.Scheduler.Pause("my_backup");     // 暂停
host.Scheduler.Resume("my_backup");    // 恢复
host.Scheduler.Unregister("my_backup");// 移除

// 查看所有任务
var jobs = host.Scheduler.GetJobs();
foreach (var (id, desc, running) in jobs)
    Console.WriteLine($"{id}: {desc} ({(running ? "运行中" : "已暂停")})");
```

### 3.10 多步会话工作流

会话（Session）实现"拖入文件夹 → 启动会话 → 后续拖入文件自动路由到同一动作"的工作流。

```csharp
host.RegisterAction(new PluginAction
{
    Name = "图片整理",
    Emoji = "🖼️",
    Target = ActionTarget.RadialMenu,
    AcceptType = ItemType.Both,
    FileCallback = async (paths) =>
    {
        var session = host.CurrentSession;

        if (session?.IsActive == true)
        {
            // 已有活跃会话：后续拖入
            await ProcessFiles(paths, session, host);
            return;
        }

        // 首次拖入：启动会话（自动锁定同名动作为默认操作）
        session = host.StartSession("图片整理");
        session.Context["count"] = 0;
        session.Context["outputDir"] = Path.Combine(paths[0], "_output");
        session.Status = "已就绪，拖入图片开始整理";
        host.ShowThought("📋 会话已启动", "拖入图片文件...");
    },
});
```

**ISession API**:
```csharp
// 状态与进度
session.Status = "已处理 3 张图片";     // 显示在气泡中
session.Progress = 0.5;               // 0.0~1.0 确定进度
session.Progress = -1;                // 不确定进度（旋转进度环）

// 共享状态（线程安全）
session.Context["key"] = value;
var val = session.Context["key"];

// 结束会话
session.Complete();    // 正常完成
session.Cancel();      // 取消

// 事件
session.OnCompleted += s => { };
session.OnCancelled += s => { };
```

---

## 4. [Plugin] 特性

```csharp
[Plugin("插件显示名", Version = "1.0.0", Description = "功能描述")]
public class MyPlugin : PluginBase { }
```

特性属性会被 `PluginBase.Description` 自动读取。Version 和 Description 可选。

---

## 5. 完整示例参考

项目内置了多个插件可作参考：

| 插件文件 | 主要特性 |
|---------|---------|
| `plugins/PinTopPlugin/` | 热键注册、配置系统、Overlay 窗口、列表弹窗 |
| `plugins/DeepSeekPlugin/` | 定时任务、HTTP 调用、配置持久化、消息队列 |
| `plugins/FileUtilityPlugin/` | 径向菜单、文件操作、RunWithAnimation 动画 |
| `plugins/PythonScriptPlugin/` | 列表弹窗（含下拉操作）、配置分组、定时任务、输入框交互 |
| `plugins/WebAnalyzerPlugin/` | 输入框、确认框、文件保存、API 调用 |
| `plugins/SessionDemoPlugin/` | 会话工作流完整示例 |
| `plugins/AgentHooksPlugin/` | 动态配置开关、列表弹窗数据刷新、多 Provider 管理 |
| `plugins/HealthReminder/` | 定时任务、配置监听、随机消息 |

---

## 6. 最佳实践

1. **使用 `RunWithAnimation`** — 代替手动 `StartAnimation`/`StopAnimation`，自动管理动画 + 可取消 + 异常恢复
2. **配置默认值** — 为 `PluginConfigField.DefaultValue` 设置合理默认值，用户不修改也能正常工作
3. **配置监听** — 订阅 `ConfigValueChanged` 让配置变更即时生效，无需重启
4. **使用 `EnqueueThought`** — 避免多条消息相互覆盖，保证用户看到所有重要信息
5. **会话 Context 线程安全** — `ISession.Context` 是 `ConcurrentDictionary`，可在异步回调中安全读写
6. **检查 `token.ThrowIfCancellationRequested()`** — 在 `RunWithAnimation` 的循环中定期检查，支持用户取消
7. **插件路径** — 插件 DLL 位于 `AppDomain.CurrentDomain.BaseDirectory/plugins/` 下

---

## 7. 注意事项

- 插件运行在主进程内，异常会拖垮整个应用 — **务必 catch 所有异常**
- 插件 DLL 使用 `AssemblyLoadContext(isCollectible: true)` 加载，理论上支持热重载
- 插件项目需要引用 `yopet.Sdk`，后者已引用 `yopet.Core`（`ItemType` 等模型）
- 每个插件的 `.csproj` 需以 `net9.0` 为目标框架
