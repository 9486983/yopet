# yopet — Desktop Pet Companion 🐱

A Windows desktop pet companion app built with **Avalonia UI** (.NET 9). Your pet sits on top of other windows, responds to clicks, performs spritesheet animations, monitors AI assistant activity, delivers health reminders, and supports a hot-loadable plugin system.

---

## ✨ Features

### 🐱 Petdex Pet System
- **Spritesheet Animation** — WebP/PNG spritesheet with 8×9 grid (192×208 px per frame), decoded via SkiaSharp
- **Petdex Integration** — Scans `~/.codex/pets/` + `~/.petdex/pets/` for installed pets
- **Petdex Dialog** — Browse all installed pets, click to switch, or install via `npx petdex install <name>`
- **Thumbnail Cache** — Extracts first frame to `~/.petdex/thumbs/` on first load for instant preview
- **Blank Frame Filtering** — Automatically skips trailing blank frames per row
- **Adjustable Speed** — 30–300 ms/frame via settings slider, applies instantly
- **Auto Random Animation** — Switches to a non-idle row every ~30s, returns after 2s
- **Interaction** — Click to wave, double-click to open petdex, right-click for action menu

### 💬 AI Assistant Monitoring
- **Event File Polling** — Monitors `~/.petdex/events/*.json`
- **Thought Bubbles** — Displays AI response content (first 120 chars) above the pet

### 🧘 Health Reminders
| Type | Default Interval | Sample Message |
|------|-----------------|----------------|
| Sitting | 55 min | "Get up and stretch～your tail is growing into the chair! 🐱" |
| Eye Strain | 25 min | "Look out the window～staring at the screen too long turns you into a panda 🐼" |
| Hydration | 40 min | "Time to hydrate! Your body is shouting 'I'm thirsty～' 💧" |

- Cross-day auto-reset
- Custom intervals via plugin settings (15–120 min)

### 🪟 Window Features
| Feature | Implementation |
|---------|---------------|
| Always on Top | `Topmost=True` + re-pin every 3s |
| Transparent Background | `TransparencyLevelHint="Transparent"` |
| Borderless | `WindowDecorations="None"` |
| Position Memory | Saved to `~/.petdex/config.json`, restored on launch |
| Auto-start | Windows registry `HKCU\...\Run` |
| Pet-first Window | Starts with pet only; settings window opens on demand |

### 🔌 Plugin System
Hot-loadable plugin system with a rich SDK. Plugins are loaded from `plugins/` directory at startup.

| Plugin | Description |
|--------|-------------|
| 📌 **PinTopPlugin** | Ctrl+Alt+T to toggle window always-on-top with customizable overlay borders |
| 👾 **AgentHooksPlugin** | Unified monitoring for AI assistants (Reasonix, Claude Code) — tracks responses, commands, file changes |
| 🔑 **DeepSeekPlugin** | Query DeepSeek API balance, usage stats, and cache hit rate; supports scheduled auto-query |
| 📁 **FileUtilityPlugin** | File details, MD5 hash, ZIP compression, open in explorer/notepad, copy path |
| 🧘 **HealthReminder** | Sitting/eye/drink reminders with configurable intervals (now implemented as a plugin) |
| 🐍 **PythonScriptPlugin** | Mount `.py` scripts by drag-and-drop, run/edit/delete/cron-schedule them |
| 🖼️ **SessionDemoPlugin** | Demo of session workflow — drag a folder, then drag images for auto-collection |
| ⚙️ **SettingPlugin** | Settings hub — auto-start (cross-platform), pet animation speed, dark mode, plugin list |

### ⚙️ Settings (SettingPlugin)
The legacy settings window was removed — settings now live in the **SettingPlugin** (right-click pet → ⚙️ Settings):

| Setting | Implementation |
|---------|----------------|
| 🚀 Auto-start | Cross-platform (Windows registry / macOS LaunchAgents / Linux autostart) |
| 🐱 Animation speed | 30–300 ms/frame slider |
| 🎨 Dark mode | Theme toggle |
| 🧩 Plugin list | Browse loaded plugins |

---

## 🏗️ Architecture

```
yopet.sln
├── yopet.Core/           Domain models + interfaces (zero UI dependency)
│   ├── Models/           PetDefinition, AppConfig, events
│   └── Interfaces/       IConfigService, IPetdexService, etc.
├── yopet.Sdk/            Plugin SDK (IPlugin, PluginBase, host interfaces)
├── yopet.Services/       Service implementations
│   ├── ConfigService.cs          JSON config persistence
│   ├── PetdexService.cs          Petdex scanning/loading
│   ├── ActivityMonitor.cs        AI event monitoring
│   ├── PluginLoader.cs           Plugin discovery & loading
│   ├── PluginHostImpl.cs         Plugin host implementation (incl. host settings API)
│   └── CronSchedulerService.cs   Cron/scheduled task engine
├── yopet.ViewModels/     MVVM ViewModels
│   └── PetViewModel.cs           Pet logic + interaction
└── yopet/                Avalonia UI layer
    ├── App.axaml/.cs             App entry + DI container
    ├── PetWindow.axaml/.cs       Pet overlay window (transparent, always-on-top)
    ├── Controls/                 Custom controls (SpritesheetView)
    ├── Views/                    Dialog pages (Petdex, plugin config, etc.)
    └── Styles/Themes/            Dark & Light themes
```

**Dependency chain**: `Core ← Services ← ViewModels ← App`

---

## 🛠️ Tech Stack

| Component | Version |
|-----------|---------|
| .NET | 9.0 |
| Avalonia | 12.0.3 (FluentTheme) |
| CommunityToolkit.Mvvm | 8.4.0 (source generators) |
| SkiaSharp | 3.119.4-preview.1.1 |
| Target Platform | Windows (x64) · macOS (osx-x64/osx-arm64) · Linux |

---

## 🚀 Build & Run

```bash
# Build
dotnet build yopet.sln

# Run
dotnet run --project yopet\yopet.csproj
```

### MSI Installer
Powershell script `installer/pack.ps1` generates an MSI installer using WiX Toolset:

```powershell
# Ensure WiX is installed, then run:
.\installer\pack.ps1
```

---

## 🐾 Installing Pets

```bash
# Browse available pets
# Visit https://petdex.crafter.run

# Install a pet
npx petdex install kirby
npx petdex install boba

# Or enter the command directly in the petdex dialog input box
```

---

## 📦 Spritesheet Specification

| Property | Standard Value |
|----------|---------------|
| Total Size | 1536 × 1872 px |
| Grid | 8 columns × 9 rows |
| Frame Size | 192 × 208 px |
| Format | WebP or PNG |
| Blank Frames | Trailing blanks auto-skipped |

**Animation Row Legend:**

| Row | State | Description |
|-----|-------|-------------|
| 0 | idle | Idle / breathing |
| 1 | running-right | Running right |
| 2 | running-left | Running left |
| 3 | waving | Waving |
| 4 | jumping | Jumping |
| 5 | failed | Failure / upset |
| 6 | waiting | Waiting |
| 7 | running | Busy working |
| 8 | review | Reviewing code |

---

## ⚙️ Configuration

**App config** — `%APPDATA%\yopet\config.json`

```json
{
  "WindowWidth": 420,
  "WindowHeight": 768,
  "PetName": "yopet",
  "CurrentPetId": "petdex:kirby",
  "PetWindowX": 1200,
  "PetWindowY": 100,
  "IsDarkTheme": true,
  "EnableAutoStart": false,
  "AnimFrameDurationMs": 100.0
}
```

**Other paths:**

| Path | Purpose |
|------|---------|
| `~/.codex/pets/<slug>/` | Petdex pet install dir (Codex) |
| `~/.petdex/pets/<slug>/` | Petdex pet install dir (Petdex CLI) |
| `~/.petdex/events/` | AI assistant event files |
| `~/.petdex/thumbs/` | Pet thumbnail cache |
| `~/.petdex/telemetry.json` | Petdex telemetry |

---

## ⌨️ Interaction

| Action | Effect |
|--------|--------|
| Click pet | Pet waves back (switches to waving animation row) |
| Double-click pet | Opens petdex dialog |
| Right-click pet | Opens action menu (switch pet / plugin actions / settings / exit) |
| Drag pet | Drag the pet window anywhere |
| Enter (petdex input) | Executes `npx petdex install` |
| 🔄 (petdex) | Refresh pet list |

---

## 📄 License

MIT License — see [LICENSE](LICENSE) for details.

---

## 🙏 Credits

- **Petdex** — Pet sharing ecosystem by [Crafter](https://crafter.run)
- **Avalonia** — Cross-platform UI framework
- **SkiaSharp** — 2D graphics library
- **CommunityToolkit.Mvvm** — MVVM source generators
