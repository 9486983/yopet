# Avalonia 动画相关库清单

> 用途：悬浮环绕菜单（Hover Radial Menu）开发时评估可用的动画/图形库
> 查询时间：2026-08-12 · 项目 Avalonia 版本：**12.1.1**
> ⚠️ 选库前先看版本：**11.x 的库不兼容 Avalonia 12**，优先选 12.x；11.x 需等作者升级或自行 fork

## 一、通用动画 / 布局动画

| 库 | 最新版本 | Avalonia 12 兼容 | NuGet 链接 | 说明 |
|---|---|---|---|---|
| **Avalonia 内置动画** | 随框架 | ✅ 原生 | —（框架自带） | `Transitions`、关键帧、`CubicEaseOut` 等 Easing、`DispatcherTimer`。FileRadialMenu 已用它实现弹出/浮动/高亮，环绕菜单可直接复用，零依赖 |
| **WCKYWCKF.Avalonia.Animations** | 12.0.1 | ✅ 12.x | https://www.nuget.org/packages/WCKYWCKF.Avalonia.Animations | 中文作者，主要用于**布局动画**（元素展开/收起过渡） |
| **AnimationRx.Avalonia** | 3.0.0 | ⚠️ 需验证 | https://www.nuget.org/packages/AnimationRx.Avalonia | Reactive 风格动画（WPF 移植），下载量小 |
| **ConnectedAnimation.Avalonia** | 1.0.0 | ⚠️ 需验证 | https://www.nuget.org/packages/ConnectedAnimation.Avalonia | UWP 风格"连接动画"（元素在页面间移动过渡） |

## 二、动图播放（GIF / WebP / Lottie）

| 库 | 最新版本 | Avalonia 12 兼容 | NuGet 链接 | 说明 |
|---|---|---|---|---|
| **AnimatedImage.Avalonia** | 2.1.4 | ❌ 11.x | https://www.nuget.org/packages/AnimatedImage.Avalonia | GIF/PNG/WebP 动图，6.3万下载，最流行但未升 12 |
| **Avalonia.Labs.AnimatedImage** | 12.0.2 | ✅ 官方 Labs | https://www.nuget.org/packages/Avalonia.Labs.AnimatedImage | 官方 Labs 出品，适配 Avalonia 12 |
| **AnimationImage.Avalonia** | 3.1.1 | ⚠️ 需验证 | https://www.nuget.org/packages/AnimationImage.Avalonia | 中文作者，Lottie/GIF/WebP 极简播放 |
| **WebPAnimationControl.Avalonia** | 1.0.0 | ⚠️ 需验证 | https://www.nuget.org/packages/WebPAnimationControl.Avalonia | 仅 WebP 动图 |

## 三、Lottie（设计师动画 JSON）

| 库 | 最新版本 | Avalonia 12 兼容 | NuGet 链接 | 说明 |
|---|---|---|---|---|
| **Avalonia.Labs.Lottie** | 11.3.1 | ❌ 11.x | https://www.nuget.org/packages/Avalonia.Labs.Lottie | Lottie 播放器，5.2万下载，官方 Labs，但仅 11.x |
| **Avalonia.Skia.Lottie** | 11.0.0 | ❌ 11.x | https://www.nuget.org/packages/Avalonia.Skia.Lottie | Lottie 播放（SkiaSharp） |
| **Lottie.WithImages** | 12.0.0 | ✅ 12.x | https://www.nuget.org/packages/Lottie.WithImages | 版本号对齐 12，可尝试 |

## 四、SVG（图标 / 矢量素材）

| 库 | 最新版本 | Avalonia 12 兼容 | NuGet 链接 | 说明 |
|---|---|---|---|---|
| **Avalonia.Svg.Skia** | 11.3.0 | ❌ 11.x | https://www.nuget.org/packages/Avalonia.Svg.Skia | 131万下载，最主流，但 11.x |
| **Avalonia.Svg** | 11.3.0 | ❌ 11.x | https://www.nuget.org/packages/Avalonia.Svg | 89万下载，同上 |
| **Svg.Controls.Avalonia** | 12.0.0.15 | ✅ 12.x | https://www.nuget.org/packages/Svg.Controls.Avalonia | 38万下载，版本对齐 12 |

## 五、加载指示器 / 特效

| 库 | 最新版本 | Avalonia 12 兼容 | NuGet 链接 | 说明 |
|---|---|---|---|---|
| **Optris.LoadingIndicators.Avalonia** | 12.0.4 | ✅ 12.x | https://www.nuget.org/packages/Optris.LoadingIndicators.Avalonia | 加载指示器集合（Avalonia 12 fork） |
| **LoadingIndicators.Avalonia** | 11.0.11.1 | ❌ 11.x | https://www.nuget.org/packages/LoadingIndicators.Avalonia | 原版，5.8万下载，11.x |
| **SimpleShimmer.Avalonia** | 1.0.1 | ⚠️ 需验证 | https://www.nuget.org/packages/SimpleShimmer.Avalonia | 微光闪烁（加载占位） |
| **Egolds.Avalonia.Xaml.Interactions.Animated** | 11.2.3 | ❌ 11.x | https://www.nuget.org/packages/Egolds.Avalonia.Xaml.Interactions.Animated | ScrollViewer 平滑滚动动画 |
| **Toasty.Avalonia** | 2.0.0 | ✅ 12.x | https://www.nuget.org/packages/Toasty.Avalonia | Toast 通知浮层（含动画） |

## 六、完整 UI 库（自带动画系统，引入成本高）

| 库 | 链接 | 说明 |
|---|---|---|
| **FluentAvalonia** | https://www.nuget.org/packages/FluentAvalonia | Fluent 风格控件，含流畅动画 |
| **SukiUI** | https://www.nuget.org/packages/SukiUI | 现代 UI 主题库 |
| **Ursa.Avalonia** | https://www.nuget.org/packages/Ursa.Avalonia | 扩展控件库 |

> 环绕菜单场景大概率用不上这些，仅作记录。

## 七、针对环绕菜单的选型建议

1. **默认用内置动画**——倒梯形弹出、径向展开、高亮、浮动，FileRadialMenu 已证明内置动画足够，零依赖零风险
2. 想要更顺滑的**展开/收起布局过渡** → `WCKYWCKF.Avalonia.Animations`（12.0.1）
3. 想要**图标用 SVG 素材** → `Svg.Controls.Avalonia`（12.0.0.15）
4. 想要**宠物特效动图** → `Avalonia.Labs.AnimatedImage`（12.0.2，官方）
