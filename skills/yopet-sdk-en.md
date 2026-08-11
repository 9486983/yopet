# yopet Plugin SDK Guide

> ## ⚠️ Cross-Platform Requirement (Mandatory)
>
> yopet is a cross-platform desktop application (must compile and run on Windows / macOS / Linux). When developing or modifying plugins, you MUST follow these rules:
>
> 1. **No bare platform-specific API calls**: never directly use the Windows Registry, P/Invoke into `user32.dll` / `gdi32.dll`, or Windows-only executables/commands such as `powershell`, `cmd.exe`, `explorer.exe`, `notepad.exe`, `shutdown`, `powercfg`, or hard-coded Windows paths.
> 2. **Prefer cross-platform solutions**: use the Avalonia built-in `Clipboard` API for clipboard; use `Process.Start` with per-platform branching for opening files/directories/URLs (`open` on macOS, `explorer.exe` on Windows, etc.).
> 3. **Guard any platform-specific API**: when a platform-specific API is truly required, wrap it with `OperatingSystem.IsWindows()` / `OperatingSystem.IsMacOS()` and provide a graceful fallback on other platforms (return false, show "not available", or skip the feature) — never let plugin initialization fail.
> 4. **Cross-platform paths & processes**: always use `Path.Combine`; never hard-code `\`, `C:\`, `python.exe`, etc.; executable detection must cover per-platform names (e.g. `python` / `python3` / `py`).
> 5. **Degrade gracefully instead of relying on exceptions**: exceptions thrown by plugin `InitializeAsync` are caught and skipped by the host, but you should degrade explicitly in code rather than relying on exceptions as a fallback.

---

Use this guide when developing, modifying, or understanding plugins for the yopet desktop pet application. It covers all core interfaces and classes in the `yopet.Sdk` namespace.

---

## 1. Project Structure Overview

```
yopet.Sdk/              ← NuGet reference: yopet.Core
├── IPlugin.cs           Plugin entry interface
├── PluginBase.cs        Plugin base class (recommended to inherit)
├── PluginAttribute.cs   Metadata attribute
├── IPluginHost.cs       Host interaction interface (core API)
├── IPluginLogger.cs     Logging interface
├── IPluginScheduler.cs  Scheduled task interface
├── ISession.cs          Multi-step session interface
├── PluginAction.cs      Action descriptor
├── PluginConfig*.cs     Configuration system
├── PetAnimation.cs      Animation enum
├── ThoughtMessage.cs    Bubble message
├── ListDialog*.cs       List dialog
└── ListColumn.cs / ListRowAction.cs / ListToolbarAction.cs
```

**Dependency chain**: Plugin project references `yopet.Sdk` → `yopet.Sdk` internally references `yopet.Core` (only uses `ItemType`, `PetDefinition`, etc.).

---

## 2. Quick Start — Minimal Plugin

### 2.1 Project File (.csproj)

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

### 2.2 Plugin Body

```csharp
using yopet.Sdk;

namespace MyPlugin;

[Plugin("My Plugin", Version = "1.0.0", Description = "An example plugin")]
public class MyPlugin : PluginBase
{
    // PluginBase automatically reads Description from the [Plugin] attribute
    public override string Name => "My Plugin";

    public override async Task InitializeAsync(IPluginHost host)
    {
        await base.InitializeAsync(host);  // saves host to this.Host

        // Register a context menu action
        host.RegisterAction(new PluginAction
        {
            Name = "Say Hello",
            Emoji = "👋",
            Group = "🙋 My Plugin",       // group in the context menu
            Target = ActionTarget.ContextMenu,
            Callback = async () =>
            {
                host.ShowThought("👋 Hello!", "This is your first plugin!");
            },
        });

        host.Log("My Plugin loaded");
    }

    public override Task CleanupAsync()
    {
        // Clean up resources (called when the main app exits)
        return base.CleanupAsync();
    }
}
```

### 2.3 Build & Deploy

```bash
# Build
dotnet build -c Release

# Copy the output .dll to the plugins/ directory under the yopet output folder
# e.g.: yopet/bin/Release/net9.0/win-x64/publish/plugins/
```

Once yopet starts, plugins are automatically loaded from the `plugins/` directory. Right-click the pet to see the registered actions.

---

## 3. Core API Reference

### 3.1 Registering Actions (Context Menu + Radial Menu)

`PluginAction` supports two locations:

| Target | Location | Callback |
|--------|----------|----------|
| `ActionTarget.ContextMenu` | Right-click pet menu | `Callback: Func<Task>` |
| `ActionTarget.RadialMenu` | Radial menu on file drop | `FileCallback: Func<string[], Task>` |

**Context Menu Example**:
```csharp
host.RegisterAction(new PluginAction
{
    Name = "Check Balance",
    Emoji = "💰",
    Description = "Query API balance",
    Group = "🔑 My Service",     // Same Group name auto-collapses
    Target = ActionTarget.ContextMenu,
    Callback = async () => { /* ... */ },
});
```

**Radial Menu Example (file drop)**:
```csharp
host.RegisterAction(new PluginAction
{
    Name = "Compress to ZIP",
    Emoji = "📦",
    Target = ActionTarget.RadialMenu,
    AcceptType = ItemType.Both,       // File / Folder / Both
    FileExtensions = new[] { ".txt", ".md" },  // file filter
    CanActivate = true,               // allow setting as default action
    FileCallback = async (paths) =>
    {
        // paths[0] is the dropped file/folder path
        await host.RunWithAnimation(PetAnimation.Running, async token =>
        {
            // perform time-consuming operation...
        });
    },
});
```

### 3.2 Showing Bubble Messages

```csharp
// Direct display (new message overwrites the current bubble)
host.ShowThought("📌 Pinned", "Notepad");

// Queue display (plays one at a time, doesn't interrupt)
host.EnqueueThought(new ThoughtMessage
{
    Title = "💬 First Message",
    Text = "Important message",
    DurationMs = 8000,  // default 5000ms
});

// Clear the queue
host.ClearThoughtQueue();

// Hide bubble
host.StopAnimation();  // also restores idle animation
```

### 3.3 Pet Animation Control

```csharp
// Trigger a brief reaction animation (auto-restores idle)
host.ShowReaction("💰", PetAnimation.Jump);

// Continuous animation (for long-running tasks)
host.StartAnimation(PetAnimation.Running);

// Restore idle
host.StopAnimation();

// Recommended: RunWithAnimation — execute async delegate, auto-manages animation + cancellable + restores idle
await host.RunWithAnimation(PetAnimation.Running, async token =>
{
    for (int i = 0; i < 10; i++)
    {
        token.ThrowIfCancellationRequested();  // throws when user clicks the progress ring to cancel
        await Task.Delay(1000, token);
    }
});

// Multi-animation cycling (switches between animations for long tasks)
await host.RunWithAnimation(
    new[] { PetAnimation.Running, PetAnimation.Wave, PetAnimation.Jump },
    async token =>
    {
        // execute logic...
    });
```

**Animation Enum**:

| Value | Row | Meaning | Use Case |
|-------|-----|---------|----------|
| `Idle` | 0 | Idle / breathing | Default state |
| `RunningRight` | 1 | Running right | Active, positive feedback |
| `RunningLeft` | 2 | Running left | Return, cancel |
| `Wave` | 3 | Waving | Greeting, response |
| `Jump` | 4 | Jumping | Surprise, success |
| `Failed` | 5 | Failed / upset | Error feedback |
| `Waiting` | 6 | Waiting | Idle waiting |
| `Running` | 7 | Busy working | Processing, querying |
| `Review` | 8 | Reviewing code | Reading, analyzing |

### 3.4 Configuration System

**Register config definition** (call in `InitializeAsync`):

```csharp
host.RegisterConfig(new PluginConfigSection
{
    Title = "My Plugin Settings",
    Emoji = "⚙️",
    Groups = new()   // optional grouping
    {
        new PluginConfigGroup
        {
            Title = "Basic Settings",
            Emoji = "🖥️",
            FieldKeys = { "my_name", "my_count" },
        },
    },
    Fields = new()
    {
        new()
        {
            Key = "my_name",
            Label = "Name",
            Type = PluginConfigFieldType.String,
            DefaultValue = "default",
            Description = "Enter your name",
        },
        new()
        {
            Key = "my_count",
            Label = "Count",
            Type = PluginConfigFieldType.Number,
            DefaultValue = "10",
            MinValue = 1,
            MaxValue = 100,
        },
        new()
        {
            Key = "my_enabled",
            Label = "Enabled",
            Type = PluginConfigFieldType.Boolean,
            DefaultValue = "true",
        },
        new()
        {
            Key = "my_mode",
            Label = "Mode",
            Type = PluginConfigFieldType.Dropdown,
            DefaultValue = "auto",
            Options = new()
            {
                new() { Label = "Auto", Value = "auto" },
                new() { Label = "Manual", Value = "manual" },
            },
        },
        new()
        {
            Key = "my_path",
            Label = "File Path",
            Type = PluginConfigFieldType.FilePath,
            Placeholder = "Select a file...",
        },
        new()
        {
            Key = "my_notes",
            Label = "Notes",
            Type = PluginConfigFieldType.TextArea,
            TextAreaRows = 4,
        },
    },
    // Pre-save validation
    Validate = values =>
    {
        var errors = new List<string>();
        if (string.IsNullOrEmpty(values.GetValueOrDefault("my_name")))
            errors.Add("Name cannot be empty");
        return errors;
    },
}, Name);
```

**Reading & writing config values**:

```csharp
// Read
var enabled = host.GetConfig("my_enabled");    // returns string?, defaultValue from DefaultValue
var count = int.TryParse(host.GetConfig("my_count"), out var n) ? n : 10;

// Write
host.SetConfig("my_key", "my_value");

// Listen for config changes
host.ConfigValueChanged += (_, key) =>
{
    if (key == "my_enabled")
        RestartSomething();
};
```

**Available field types**:

| Type | Purpose | Extra Properties |
|------|---------|-----------------|
| `String` | Single-line text | Placeholder |
| `Password` | Password input | - |
| `Number` | Number | MinValue, MaxValue |
| `Boolean` | Toggle | - |
| `Dropdown` | Dropdown selection | Options |
| `FilePath` | File picker | FileFilter, Placeholder |
| `FolderPath` | Folder picker | Placeholder |
| `TextArea` | Multi-line text | TextAreaRows |
| `CronExpression` | Cron expression | CronPresets |
| `Color` | Color picker | - |

### 3.5 Opening the Config Dialog

```csharp
// Call from a context-menu callback or elsewhere
host.ShowConfigDialog("My Plugin Settings");  // parameter is the registered Title
```

### 3.6 Dialog Interactions

**Input dialog**:
```csharp
var input = await host.ShowInputDialog("Title", "Placeholder", "Initial value");
if (input == null) return;  // user cancelled
// use input
```

**Confirm dialog**:
```csharp
var confirmed = await host.ShowConfirmDialog("Confirm Delete", "Are you sure?");
if (confirmed) { /* perform delete */ }
```

### 3.7 List Dialog

A powerful data list component supporting both table and card grid layouts.

**Table mode**:
```csharp
host.RegisterAction(new PluginAction
{
    Name = "Script List",
    Emoji = "📜",
    Group = "🐍 My Group",
    Target = ActionTarget.ContextMenu,
    Callback = async () =>
    {
        var config = new ListDialogConfig
        {
            Title = "Script Manager",
            Emoji = "🐍",
            LayoutMode = ListDialogLayoutMode.Table,

            // Dynamic data source (refreshes on each open)
            DataSource = () => Task.FromResult(new List<Dictionary<string, string>>
            {
                new() { ["name"] = "hello.py", ["desc"] = "Hello World", ["status"] = "🟢" },
                new() { ["name"] = "backup.py", ["desc"] = "Backup tool", ["status"] = "🔴" },
            }),

            Columns = new()
            {
                new() { Key = "name", Header = "Name", Width = 120 },
                new() { Key = "desc", Header = "Description", Width = double.NaN }, // auto width
                new() { Key = "status", Header = "Status", Width = 70 },
                new()
                {
                    Key = "actions",
                    Header = "Actions",
                    Type = ListColumnType.Action,
                    RowActions = new()
                    {
                        new()
                        {
                            Label = "Run",
                            Emoji = "▶",
                            Callback = row =>
                            {
                                // row["name"] gets the current row data
                                return Task.CompletedTask;
                            },
                        },
                        new()
                        {
                            Label = "More",
                            Emoji = "⚙️",
                            Type = ListRowActionType.Dropdown,  // dropdown menu
                            Children = new()
                            {
                                new() { Label = "Edit", Emoji = "📝", Callback = async row => { } },
                                new() { Label = "Delete", Emoji = "🗑", Callback = async row => { } },
                            },
                        },
                    },
                },
            },

            // Toolbar
            ToolbarActions = new()
            {
                new() { Label = "Refresh", Emoji = "🔄", Callback = async () => config.NotifyDataChanged() },
            },
        };

        await host.ShowListDialog(config);
    },
});
```

**Card Grid mode** (great for image displays like pet gallery):
```csharp
var config = new ListDialogConfig
{
    Title = "Pet List",
    Emoji = "🐱",
    LayoutMode = ListDialogLayoutMode.CardGrid,
    CardFallbackEmoji = "🐱",
    CardImageProvider = row => row.GetValueOrDefault("thumbnail"),
    OnCardClick = async row =>
    {
        // card selected
        return true;  // true=close dialog and select, false=keep open
    },
    DataSource = () => Task.FromResult(items),
};
```

**Data change notification**: Call `config.NotifyDataChanged()` from any thread to refresh the list.

### 3.8 Logging

```csharp
// Via IPluginLogger (provided by IPluginHost.Logger)
host.Logger.Debug<MyPlugin>("Debug message");
host.Logger.Info<MyPlugin>("Info message");
host.Logger.Warn<MyPlugin>("Warning");
host.Logger.Error<MyPlugin>("Error", exception);

// Simple log (outputs to Debug window + LogEmitted event)
host.Log("Simple message");

// Log directory: default .yopet/logs/, configurable via LogPath
```

### 3.9 Scheduled Tasks

**Cron expression**:
```csharp
host.Scheduler.Register(
    jobId: "my_backup",           // unique identifier
    cronExpression: "0 0 * * *",  // runs every hour
    callback: async () =>
    {
        host.ShowThought("⏰ Scheduled Task", "Running backup...");
    },
    description: "⏰ Hourly backup");
```

**Interval execution** (seconds precision):
```csharp
host.Scheduler.RegisterInterval(
    jobId: "my_heartbeat",
    intervalSeconds: 30,   // every 30 seconds
    callback: async () => { /* ... */ },
    description: "💓 Heartbeat");
```

**Management**:
```csharp
host.Scheduler.Pause("my_backup");     // pause
host.Scheduler.Resume("my_backup");    // resume
host.Scheduler.Unregister("my_backup");// remove

// View all tasks
var jobs = host.Scheduler.GetJobs();
foreach (var (id, desc, running) in jobs)
    Console.WriteLine($"{id}: {desc} ({(running ? "Running" : "Paused")})");
```

### 3.10 Multi-Step Session Workflow

A Session implements a "drop a folder → start a session → subsequent file drops route to the same action automatically" workflow.

```csharp
host.RegisterAction(new PluginAction
{
    Name = "Image Organizer",
    Emoji = "🖼️",
    Target = ActionTarget.RadialMenu,
    AcceptType = ItemType.Both,
    FileCallback = async (paths) =>
    {
        var session = host.CurrentSession;

        if (session?.IsActive == true)
        {
            // Active session exists: subsequent drops
            await ProcessFiles(paths, session, host);
            return;
        }

        // First drop: start a session (automatically locks the same-name action as default)
        session = host.StartSession("Image Organizer");
        session.Context["count"] = 0;
        session.Context["outputDir"] = Path.Combine(paths[0], "_output");
        session.Status = "Ready, drop images to organize";
        host.ShowThought("📋 Session Started", "Drop image files...");
    },
});
```

**ISession API**:
```csharp
// Status & progress
session.Status = "Processed 3 images";     // displayed in the bubble
session.Progress = 0.5;                   // 0.0~1.0 determinate progress
session.Progress = -1;                    // indeterminate progress (spinning ring)

// Shared state (thread-safe)
session.Context["key"] = value;
var val = session.Context["key"];

// End session
session.Complete();    // normal completion
session.Cancel();      // cancel

// Events
session.OnCompleted += s => { };
session.OnCancelled += s => { };
```

---

## 4. [Plugin] Attribute

```csharp
[Plugin("Plugin Display Name", Version = "1.0.0", Description = "Feature description")]
public class MyPlugin : PluginBase { }
```

Attribute properties are automatically read by `PluginBase.Description`. Version and Description are optional.

---

## 5. Reference: Built-in Plugins

The project includes several plugins for reference:

| Plugin File | Key Features |
|-------------|-------------|
| `plugins/PinTopPlugin/` | Hotkey registration, config system, Overlay window, list dialog |
| `plugins/DeepSeekPlugin/` | Scheduled tasks, HTTP calls, config persistence, message queue |
| `plugins/FileUtilityPlugin/` | Radial menu, file operations, RunWithAnimation |
| `plugins/PythonScriptPlugin/` | List dialog (with dropdown actions), config groups, scheduled tasks, input dialog |
| `plugins/SessionDemoPlugin/` | Complete session workflow example |
| `plugins/AgentHooksPlugin/` | Dynamic config toggles, list dialog data refresh, multi-Provider management |
| `plugins/HealthReminder/` | Scheduled tasks, config listening, randomized messages |

---

## 6. Best Practices

1. **Use `RunWithAnimation`** — instead of manual `StartAnimation`/`StopAnimation`; auto-manages animation + cancellability + exception recovery
2. **Set default values** — provide sensible `PluginConfigField.DefaultValue` so the plugin works without user modification
3. **Listen for config changes** — subscribe to `ConfigValueChanged` to apply changes immediately without restart
4. **Use `EnqueueThought`** — prevents multiple messages from overwriting each other; ensures users see all important information
5. **Session Context is thread-safe** — `ISession.Context` is a `ConcurrentDictionary`, safe to read/write from async callbacks
6. **Check `token.ThrowIfCancellationRequested()`** — periodically in `RunWithAnimation` loops to support user cancellation
7. **Plugin path** — plugin DLLs are located at `AppDomain.CurrentDomain.BaseDirectory/plugins/`

---

## 7. Important Notes

- Plugins run inside the main process — an unhandled exception will crash the whole app. **Always catch all exceptions.**
- Plugin DLLs are loaded via `AssemblyLoadContext(isCollectible: true)`, theoretically supporting hot-reload
- Plugin projects need to reference `yopet.Sdk`, which already references `yopet.Core` (for `ItemType` etc.)
- Each plugin's `.csproj` must target `net9.0`
