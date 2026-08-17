# 悬浮环绕菜单（Hover Radial Menu）开发方案

> 状态：规划中（暂不实现） · 更新：2026-08-12
> 决策记录：暂不实现，先制定方案；悬浮时菜单优先（CPU 气泡让位）；元素径向排列朝向宠物中心。

## 一、目标与效果

鼠标悬浮宠物时，宠物周围径向展开一圈**圆角 + 圆弧倒梯形**元素，每个元素对应一个插件动作（emoji + 名称）；点击执行动作，鼠标离开收起。悬浮期间 CPU 气泡**让位**（不显示）。

## 二、效果规格

| 项 | 规格 |
|---|---|
| 触发 | 鼠标进入宠物窗口（`EventNames.PetHoverEntered`）；离开收起（`PetHoverExited`） |
| 元素形状 | 圆角 + 上边圆弧的倒梯形（`Path` 绘制，上宽约 56px / 下宽约 36px / 高约 80px） |
| 朝向 | **径向排列**：元素顶部指向宠物中心，整体如花瓣/光芒 |
| 数量 | 插件动作数，均布环绕，上限 8（超出缩小半径/字号） |
| 动画 | 逐个弹出（缩放+透明度，参考 `FileRadialMenu.AnimateItemsIn`）+ 悬浮浮动 |
| 高亮 | 鼠标移入：放大 + 变色（参考 `FileRadialMenu.HighlightNearest`） |
| 点击 | 执行对应插件动作（`PetActionConfig.ActionCallback`，与右键菜单同源） |
| 隐藏 | 鼠标离开宠物或菜单区域 → 收起动画 |

## 三、技术架构

**新增**
- `yopet/Views/HoverRadialMenu.cs`：悬浮环绕菜单窗口（透明 Topmost、锚定宠物中心、圆环布局、径向旋转、命中检测——整体骨架改造自 `FileRadialMenu`）
- 倒梯形元素：`Path` + `StreamGeometry`（几何静态缓存，所有元素复用同一形状）

**复用（零改动）**
- 事件池 `PetHoverEntered / PetHoverExited` 触发
- `PluginHostImpl.PluginActions`（`List<PetActionConfig>`）作为数据源——与右键菜单同一批动作
- `FileRadialMenu` 的窗口模式、入场/浮动动画、最近距离命中算法

**改动（主程序小改）**
- `PetWindow`：悬浮进入时创建并显示 `HoverRadialMenu`（锚定宠物屏幕中心）；离开时收起
- 气泡让位：主程序暴露"悬浮菜单已打开"状态（建议 `PetViewModel` 属性 → 经 `PluginHostImpl` 暴露给插件）；Usage 插件 `OnHoverEntered` 检测到菜单打开则**不弹 CPU 气泡**

## 四、数据流

```
鼠标进入宠物（PetWindow.PointerEntered）
  → PetViewModel.OnPetHoverEntered()
      ├─ ① 显示 HoverRadialMenu（数据 = PluginHostImpl.PluginActions）
      └─ ② Publish(PetHoverEntered)
            → Usage 插件：检测菜单已打开 → 跳过气泡
点击倒梯形元素 → action.ActionCallback()（等同右键菜单点击）
鼠标离开 → 收起菜单；Publish(PetHoverExited)
```

## 五、倒梯形形状实现要点

```
M 8,10
Q 36,-6 64,10      ← 上边外凸圆弧
L 72,56
Q 72,72 56,72      ← 右下圆角
L 16,72
Q 8,72 8,56        ← 左下圆角
Z
```

用 `StreamGeometry` 一次性构建并缓存；元素内部用 `Path`（形状）+ `StackPanel`（emoji + 名称）叠加，或 `Path` 作为 Border 背景。

## 六、径向布局算法

- N 个元素：`θᵢ = -90° + i × (360°/N)`（自正上方顺时针）
- 位置：`cx + R·cos θᵢ`、`cy + R·sin θᵢ`（R ≈ 95px，可调）
- **朝向中心**：元素旋转角 = `θᵢ + 90°`（使元素竖轴指向宠物中心）
- 注意：径向排列下，下方元素的 emoji/文字会倒置——这是"朝向中心"的固有代价，已确认接受

## 七、实施步骤（建议顺序）

1. **形状先行**：倒梯形 `Path` 几何 + 单个元素控件（纯样式，独立验证视觉）
2. **菜单窗口**：`HoverRadialMenu`（改造 `FileRadialMenu`：数据源换 `PetActionConfig`、形状换 Path、加径向旋转）
3. **接线**：`PetWindow` 悬浮事件显示/收起菜单、锚定位置
4. **气泡让位**：暴露菜单状态 → Usage 插件检查跳过
5. **动画打磨**：弹出/收起/浮动节奏
6. **验证**：构建 + 手动悬浮/点击/离开；命中用最近距离算法

## 八、风险与应对

| 风险 | 应对 |
|---|---|
| 径向文字倒置可读性 | 已确认接受；可后续加"文字反补偿"选项 |
| 元素多时重叠 | 上限 8 + 动态半径/字号 |
| 与右键菜单重复 | 同一数据源，无冲突，点选更快捷 |
| 透明窗口命中/层级 | `FileRadialMenu` 已验证同类模式 |
| 与 CPU 气泡时序竞争 | 菜单状态优先，Usage 插件跳过 |
