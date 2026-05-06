# Taskato 番茄钟功能增强计划 (最终确认版)

本计划将执行 UI 细节优化、时间同步修复、多组番茄钟支持及设置扩展。

## 用户复查确认

> [!IMPORTANT]
> 1. **状态标签**：仅修改形状为圆角矩形（CornerRadius="8"），保留现有的霓虹脉冲和水波纹特效。
> 2. **任务条色块**：将左侧颜色条改为圆角胶囊形状，并确保其弧度与任务卡片的主体弧度保持视觉一致。
> 3. **时间同步**：步进统一为 1 分钟，下限 1 分钟。

## 待办事项 (TODO)

- [ ] 扩展 `SettingsConfig` 以包含新功能的开关。
- [ ] **[UI]** 修改 `App.xaml` 中的 `PomodoroStatusTagStyle` 形状（CornerRadius="8"）。
- [ ] **[UI]** 修改 `MainWindow.xaml` 中的任务条色块为圆角样式。
- [ ] **[BugFix]** 同步主页与设置的时间调节逻辑（步进 1min，下限 1min）。
- [ ] 在 `ToastWindow` 中实现弹窗计时器逻辑。
- [ ] 重构番茄钟逻辑以支持多组计时器。
- [ ] 更新 `MainWindow.xaml` 以展示多组计时器。
- [ ] 更新 `SettingsWindow.xaml` 添加功能开关。

## 方案细节

### 1. 设置系统扩展
- **文件**: `SettingsService.cs`
- **属性**: `EnableToastTimer`, `EnableMultiplePomodoros`。

### 2. UI 样式微调
- **状态标签**: 修改 `App.xaml` -> `PomodoroStatusTagStyle`。将 `ripple` 和 `mainBd` 的 `CornerRadius` 从 `100` 改为 `8`。
- **任务条色块**: 
  - 修改 `MainWindow.xaml` -> `taskCard` 内部的左侧 `Border`。
  - 增加 `CornerRadius="2"` (或更大) 和微小的 `Margin` (如 `4,8,0,8`)，使其呈现出带有圆角的垂直长条感，且不紧贴边缘。

### 3. 时间逻辑同步
- **MainViewModel**: `IncreaseTimeCommand`/`DecreaseTimeCommand` 步进改为 1。
- **SettingsWindow**: 同步点击事件逻辑。

### 4. 弹窗计时器与多组计时器
- **ToastWindow**: 开启时启动 `DispatcherTimer`。
- **多组逻辑**: 在 `MainViewModel` 中引入 `AdditionalTimers` 集合。

## 验证计划

- `dotnet build` 编译检查。
- 启动程序验证 UI 形状。
- 开启多组计时器测试独立性。
