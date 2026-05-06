# Taskato 番茄钟功能增强计划 (更新版 v2)

本计划旨在完善 Taskato 的 UI 审美、修复时间同步 Bug、支持多组番茄钟及相关设置。

## 用户复查确认

> [!IMPORTANT]
> 1. **UI 预览**：已生成预览文件至 `.agents/preview/`。请从 **Neon Aura (A)**, **Frosted Glass (B)**, 或 **Gradient Flow (C)** 中选择一种样式。
> 2. **规则更新**：已更新 `BaseRules.md`，明确所有预览和计划文件均存放在 `.agents/preview/` 目录下。
> 3. **时间同步**：步进统一为 1 分钟，下限 1 分钟。

## 待办事项 (TODO)

- [ ] 扩展 `SettingsConfig` 以包含新功能的开关。
- [ ] **[UI]** 修改 `App.xaml` 中的 `PomodoroStatusTagStyle` 为选定的圆角矩形样式。
- [ ] **[UI]** 修改 `MainWindow.xaml` 中的任务条 (`taskCard`) 样式。
- [ ] **[BugFix]** 同步主页与设置的时间调节逻辑（步进 1min，下限 1min）。
- [ ] 在 `ToastWindow` 中实现弹窗计时器逻辑。
- [ ] 重构番茄钟逻辑以支持多组计时器。
- [ ] 更新 `MainWindow.xaml` 以展示多组计时器。
- [ ] 更新 `SettingsWindow.xaml` 添加功能开关。

## 方案细节

### 1. 设置系统扩展
- **文件**: `SettingsService.cs`
- **变更**: 添加 `EnableToastTimer` 和 `EnableMultiplePomodoros`。

### 2. UI 样式调整 (圆角矩形)
- **状态标签**: 修改 `App.xaml` -> `PomodoroStatusTagStyle`。
- **任务条**: 修改 `MainWindow.xaml` 中的 `taskCard` Border，改为圆角矩形。

### 3. 时间同步逻辑修复
- **主页**: 修改 `MainViewModel.cs` 中的 `IncreaseTimeCommand` 和 `DecreaseTimeCommand`（步进 1）。
- **设置**: 修改 `SettingsWindow.xaml.cs` 中的点击事件（步进 1）。

### 4. 规则与文件管理
- 更新 `BaseRules.md` 第 4 条。
- 将所有 `plan_*.md` 和 `preview_*.html` 移至 `.agents/preview/`。

## 验证计划

### 自动化测试
- 执行 `dotnet build` 确保编译通过。

### 手动测试
1. **样式验证**：启动程序查看 UI 变化。
2. **同步验证**：确认主页和设置的时间步进一致。
