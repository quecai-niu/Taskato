# Taskato 番茄钟功能增强计划 (Elite UI 版)

本计划将基于最新的“精英级”审美方案，执行 UI 细节优化、时间同步修复、多组番茄钟支持及设置扩展。

## 用户复查确认

> [!IMPORTANT]
> 1. **状态标签**：修改形状为圆角矩形（CornerRadius="10"），保留现有的霓虹脉冲和水波纹特效。
> 2. **任务条色块 (Elite Design)**：将左侧颜色条改为**内嵌浮动胶囊 (Floating Inset Pill)**。色块将不再贴边，而是带有上下左右的边距，并拥有极大的圆角半径以呼应主体弧度，同时具备外发光效果。
> 3. **时间同步**：主页与设置的步进统一为 1 分钟，下限 1 分钟，上限放宽至 240 分钟。

## 待办事项 (TODO)

- [x] 扩展 `SettingsConfig` 以包含新功能的开关。
- [x] **[UI]** 修改 `App.xaml` 中的 `PomodoroStatusTagStyle` 形状（CornerRadius="10"）。
- [x] **[UI]** 修改 `MainWindow.xaml` 中的任务条色块为内嵌浮动圆角样式。
- [x] **[BugFix]** 同步主页与设置的时间调节逻辑（步进 1min，下限 1min）。
- [x] 在 `ToastWindow` 中实现弹窗计时器逻辑。
- [x] 重构番茄钟逻辑以支持多组计时器。
- [x] 更新 `MainWindow.xaml` 以展示多组计时器。
- [x] 更新 `SettingsWindow.xaml` 添加功能开关。

## 方案细节

### 1. 设置系统扩展
- **文件**: `SettingsService.cs`
- **属性**: `EnableToastTimer`, `EnableMultiplePomodoros`。

### 2. UI 样式重塑 (基于 Elite 方案)
- **状态标签**: 修改 `App.xaml` -> `PomodoroStatusTagStyle`。
  - 将 `ripple` 和 `mainBd` 的 `CornerRadius` 改为 `10`。
  - 微调 `ripple` 动画的 `ScaleTransform` 以适配矩形。
- **任务条色块 (Floating Pill)**: 
  - 修改 `MainWindow.xaml` -> `taskCard` 内部布局。
  - 色块 `Border` 改为 `Width="6"`, `CornerRadius="100"`, `Margin="12,12,0,12"`。
  - 增加 `DropShadowEffect` 模拟预览中的霓虹感。

### 3. 时间逻辑同步
- **MainViewModel**: 修改 `IncreaseTimeCommand`/`DecreaseTimeCommand` 步进为 1。
- **SettingsWindow**: 修改按钮点击事件，移除 5 分钟步进逻辑，改为 1 分钟。

### 4. 弹窗计时器与多组计时器
- **ToastWindow**: 增加计时器逻辑。
- **多组逻辑**: 支持多实例 `PomodoroService` 或管理类。

## 验证计划

- `dotnet build` 编译检查。
- 启动程序验证 Elite UI 的实际表现（圆角、发光、间距）。
- 验证时间同步一致性。
