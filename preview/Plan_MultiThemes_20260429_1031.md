# Taskato 多主题系统实现计划

本计划旨在建立一套支持“昼夜模式+护眼多变体”的 WPF 资源架构，实现五种预设方案的热切换。

## 1. 资源架构重构 [关键步骤]
我们将把 `App.xaml` 中散乱的颜色定义提取到专门的主题资源文件夹中。

### [NEW] 目录结构
- `Resources/Themes/`
  - `Theme.Base.xaml` (存放通用的样式，如 Button、Border 模板)
  - `Theme.Dark.xaml` (默认深色配色)
  - `Theme.SoftSand.xaml` (柔沙暖白)
  - `Theme.NordicGrey.xaml` (静谧灰)
  - `Theme.MutedSage.xaml` (淡茶绿)
  - `Theme.Vintage.xaml` (复古卷轴)

## 2. 核心代码改动

### [MODIFY] [App.xaml](file:///d:/Work/My/LearningMaterials/DotNET/WPF/code/Taskato/src/Taskato/App.xaml)
- 移除硬编码的背景和文字颜色背景。
- 修改为 `MergedDictionaries` 加载默认主题。

### [NEW] ThemeService.cs
- 编写专门的主题切换 Helper，负责：
  1. 查找并替换当前活动的 `ResourceDictionary`。
  2. 触发全局 UI 刷新。
  3. 与 `SettingsService` 联动，确保用户选中的主题在下次启动时自动加载。

### [MODIFY] [SettingsWindow.xaml](file:///d:/Work/My/LearningMaterials/DotNET/WPF/code/Taskato/src/Taskato/Views/SettingsWindow.xaml)
- 增加主题模式选择器（五个色块图标或下拉框）。

## 3. 设计规范对齐
- **键名对齐**: 所有主题必须包含 `RegionBackgroundBrush`, `CardBackgroundBrush`, `TextMainBrush`, `BorderSubtleBrush` 等标准键。
- **透明度适配**: 针对不同背景亮度，微调 `JellyIconBtn` 的 Opacity 初始值，确保在亮色下依然通透。

## 4. 验证计划
- [ ] 切换主题时，检查主界面、番茄钟区域、任务列表是否同步更新。
- [ ] 验证在亮色主题下，原本的紫色主题色是否依然清晰可见（特别是白色文字在紫色背景上的平衡）。
- [ ] 确认退出程序再打开，主题是否记忆成功。
