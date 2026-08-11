# yopet — 桌面电子宠物 🐱

基于 **Avalonia UI** (.NET 9) 的 Windows 桌面宠物应用。宠物悬浮在所有窗口之上，响应点击交互，播放精灵图逐帧动画，监测 AI 助手活动，定时推送健康提醒，并支持热加载插件系统。

---

## ✨ 功能特性

### 🐱 Petdex 宠物系统
- **精灵图动画** — 8×9 网格 (192×208 px/帧) 的 WebP/PNG 精灵图，SkiaSharp 解码渲染
- **Petdex 集成** — 扫描 `~/.codex/pets/` + `~/.petdex/pets/` 目录下的已安装宠物
- **宠物图鉴** — 弹窗展示所有已安装宠物，一键切换或通过 `npx petdex install <name>` 安装
- **缩略图缓存** — 首次加载提取第一帧保存到 `~/.petdex/thumbs/`，下次即时预览
- **空白帧过滤** — 自动检测并跳过尾部空白帧，每行只播放有效帧
- **速度可调** — 30~300ms/帧，设置页滑块调节，即时生效
- **自动随机动画** — 每约 30 秒随机切换到非 idle 行，2 秒后恢复
- **交互反馈** — 单击挥手、双击打开图鉴、右键弹出动作菜单

### 💬 AI 助手活动监测
- **事件文件轮询** — 监控 `~/.petdex/events/*.json`
- **对话气泡** — 宠物上方弹出显示 AI 响应内容（前 120 字）

### 🧘 健康提醒
| 类型 | 默认间隔 | 提示语示例 |
|------|---------|-----------|
| 久坐 | 55 分钟 | "起来活动一下吧～坐太久尾巴要长在椅子上啦！🐱" |
| 用眼 | 25 分钟 | "看看窗外吧～一直盯着屏幕，眼睛会变成熊猫眼的🐼" |
| 喝水 | 40 分钟 | "喝水时间到！你的身体正在喊「我好渴啊～」💧" |

- 跨天自动重置
- 间隔通过插件设置自定义（15~120 分钟）

### 🪟 窗口特性
| 特性 | 实现方式 |
|------|---------|
| 置顶悬浮 | `Topmost=True` + 每 3 秒重设防沉底 |
| 透明背景 | `TransparencyLevelHint="Transparent"` |
| 无边框 | `WindowDecorations="None"` |
| 位置记忆 | 保存到 `~/.petdex/config.json`，启动时恢复 |
| 开机自启 | Windows 注册表 `HKCU\...\Run` |
| 宠物为主窗口 | 启动仅显示宠物，设置窗按需打开 |

### 🔌 插件系统
支持热加载的插件系统，提供丰富的 SDK。插件在启动时从 `plugins/` 目录加载。

| 插件 | 说明 |
|------|------|
| 📌 **PinTopPlugin** | Ctrl+Alt+T 置顶/取消置顶窗口，带可定制的半透明边框指示 |
| 👾 **AgentHooksPlugin** | 统一管理 AI 助手监测（Reasonix、Claude Code 等），追踪响应/命令/文件变更 |
| 🔑 **DeepSeekPlugin** | 查询 DeepSeek API 余额和使用量，支持定时自动查询 |
| 📁 **FileUtilityPlugin** | 文件详情、MD5 哈希、ZIP 压缩、资源管理器定位、记事本打开、复制路径 |
| 🧘 **HealthReminder** | 久坐/用眼/喝水定时提醒（现已实现为插件） |
| 🐍 **PythonScriptPlugin** | 拖入 .py 文件挂载脚本，支持运行/编辑/删除/Cron 定时执行 |
| 🖼️ **SessionDemoPlugin** | 会话工作流示例：拖入文件夹启动会话，再拖图片自动整理 |
| ⚙️ **SettingPlugin** | 设置中心 —— 开机自启（跨平台）、动画速度、深色模式、插件列表 |

### ⚙️ 设置功能（SettingPlugin）
原设置窗口已移除，设置功能现由 **SettingPlugin** 提供（右键宠物 → ⚙️ 设置）：

| 设置项 | 实现 |
|--------|------|
| 🚀 开机自启 | 跨平台（Windows 注册表 / macOS LaunchAgents / Linux autostart） |
| 🐱 动画速度 | 30–300 ms/帧 滑块 |
| 🎨 深色模式 | 主题切换 |
| 🌐 语言 | zh-CN / en-US 下拉，即时生效 |
| 🧩 插件列表 | 浏览已加载插件 |

### 🌐 多语言（i18n）

基于 **Lang.Avalonia**（MIT）。所有界面文本集中在 `yopet/I18n/` 词条库：

| 文件 | 语言 |
|------|------|
| `I18n/zh-CN.json` | 简体中文（默认） |
| `I18n/en-US.json` | English |

- **切换语言**：右键宠物 → ⚙️ 设置 → 语言下拉（即时生效，菜单/动作就地刷新）
- key 遵循 `Localization.<模块>.<名称>`（如 `Localization.PetWindow.MenuPetdex`）；key 缺失时回退显示原始 key 便于发现
- **插件文本共用统一词条库**——插件词条并入同一 `I18n/*.json`（见 `skills/yopet-sdk.md` 第 8 章）

---

## 🏗️ 项目架构

```
yopet.sln
├── yopet.Core/           纯领域模型 + 接口（无 UI 依赖）
│   ├── Models/           PetDefinition、AppConfig、事件模型
│   └── Interfaces/       IConfigService、IPetdexService 等
├── yopet.Sdk/            插件 SDK（IPlugin、PluginBase、宿主接口）
├── yopet.Services/       服务实现层
│   ├── ConfigService.cs          JSON 配置持久化
│   ├── PetdexService.cs          Petdex 宠物扫描/加载
│   ├── ActivityMonitor.cs        AI 事件监控
│   ├── PluginLoader.cs           插件发现与加载
│   ├── PluginHostImpl.cs         插件宿主实现（含宿主设置 API）
│   └── CronSchedulerService.cs   Cron 定时任务引擎
├── yopet.ViewModels/     MVVM ViewModel 层
│   └── PetViewModel.cs           宠物逻辑 + 交互
└── yopet/                Avalonia 应用层
    ├── App.axaml/.cs             应用入口 + DI 容器
    ├── PetWindow.axaml/.cs       宠物悬浮窗（透明置顶）
    ├── Controls/                 自定义控件（SpritesheetView）
    ├── Views/                    对话框页面（Petdex、插件配置等）
    └── Styles/Themes/            深色 & 浅色主题
```

**依赖链**：`Core ← Services ← ViewModels ← App`

---

## 🛠️ 技术栈

| 组件 | 版本说明 |
|------|---------|
| .NET | 9.0 |
| Avalonia | 12.0.3 (FluentTheme) |
| CommunityToolkit.Mvvm | 8.4.0（源生成器） |
| SkiaSharp | 3.119.4-preview.1.1 |
| 目标平台 | Windows (x64) · macOS (osx-x64/osx-arm64) · Linux |

---

## 🚀 构建与运行

```bash
# 构建
dotnet build yopet.sln

# 运行
dotnet run --project yopet\yopet.csproj
```

### MSI 安装包
使用 `installer/pack.ps1` PowerShell 脚本通过 WiX Toolset 生成 MSI 安装包：

```powershell
# 确保已安装 WiX，然后执行：
.\installer\pack.ps1
```

---

## 🐾 安装宠物

```bash
# 浏览可用宠物
# 访问 https://petdex.crafter.run

# 安装宠物
npx petdex install kirby
npx petdex install boba

# 或在宠物图鉴的输入框中直接输入安装命令
```

---

## 📦 精灵图规格

| 属性 | 标准值 |
|------|--------|
| 总尺寸 | 1536 × 1872 px |
| 网格 | 8 列 × 9 行 |
| 单帧尺寸 | 192 × 208 px |
| 格式 | WebP 或 PNG |
| 空白帧 | 尾部空白自动跳过 |

**动画行对照：**

| 行 | 状态 | 说明 |
|-----|------|------|
| 0 | idle | 待机/呼吸 |
| 1 | running-right | 向右跑 |
| 2 | running-left | 向左跑 |
| 3 | waving | 挥手 |
| 4 | jumping | 跳跃 |
| 5 | failed | 失败/沮丧 |
| 6 | waiting | 等待 |
| 7 | running | 忙碌工作 |
| 8 | review | 审查代码 |

---

## ⚙️ 配置文件

**应用配置** — `%APPDATA%\yopet\config.json`

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

**其他路径：**

| 路径 | 用途 |
|------|------|
| `~/.codex/pets/<slug>/` | Petdex 宠物安装目录（Codex） |
| `~/.petdex/pets/<slug>/` | Petdex 宠物安装目录（Petdex CLI） |
| `~/.petdex/events/` | AI 助手事件文件 |
| `~/.petdex/thumbs/` | 宠物缩略图缓存 |
| `~/.petdex/telemetry.json` | Petdex 遥测 |

---

## ⌨️ 快捷键与交互

| 操作 | 效果 |
|------|------|
| 单击宠物 | 宠物挥手回应，切换到 waving 动画行 |
| 双击宠物 | 打开宠物图鉴 |
| 右键宠物 | 打开动作菜单（切换宠物 / 插件动作 / 设置 / 退出） |
| 拖动宠物 | 任意位置拖动宠物窗口 |
| Enter (图鉴输入框) | 执行 `npx petdex install` |
| 🔄 (图鉴) | 刷新宠物列表 |

---

## 📄 许可证

MIT 许可证 — 详见 [LICENSE](LICENSE) 文件。

---

## 🙏 致谢

- **Petdex** — 宠物共享生态，由 [Crafter](https://crafter.run) 提供
- **Avalonia** — 跨平台 UI 框架
- **SkiaSharp** — 2D 图形库
- **CommunityToolkit.Mvvm** — MVVM 源生成器
