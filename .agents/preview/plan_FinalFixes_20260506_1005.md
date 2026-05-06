# 番茄钟功能增强与 UI 终极修复

本方案旨在解决附加计时器时间调节缺失、颜色标签形状异常以及提醒音缺失的问题。

## 待办事项 (TODO)

- [ ] **[Pomodoro]** 在 `PomodoroTimerViewModel` 中实现时间加减逻辑（步进 5 分钟）。
- [ ] **[UI]** 修正任务卡片颜色标签，改用 `Rectangle` 并优化边距，彻底解决“锥子”形状问题。
- [ ] **[UI]** 在 `MainWindow.xaml` 的附加计时器行中添加加减时间按钮。
- [ ] **[Sound]** 在 `ToastWindow` 中集成提示音播放逻辑。
- [ ] **[Sound]** 在 `desktop-notification` 技能的 `notify.ps1` 脚本中增加提示音。

## 方案细节

### 1. 附加计时器调节 (PomodoroTimerViewModel.cs)
- 新增 `IncreaseTimeCommand` 和 `DecreaseTimeCommand`。
- 修改 `MainWindow.xaml` 中的 `ItemsControl` 模板，在时间文本两侧加入微型按钮。

### 2. 颜色标签形状 (MainWindow.xaml)
- 将 `Border` 替换为 `Rectangle`。
- `Rectangle` 属性：`Width="4"`, `RadiusX="2"`, `RadiusY="2"`, `VerticalAlignment="Stretch"`, `Margin="14,14,0,14"`。
- 父容器 `taskCard`：取消 `ClipToBounds="True"`，防止阴影或圆角被切边。

### 3. 提示音实现 (C# & PowerShell)
- **C#**: 使用 `System.Media.SoundPlayer` 播放 `C:\Windows\Media\Windows Notify System Generic.wav`。
- **PowerShell**: 在 `notify.ps1` 中添加 `$player = New-Object System.Media.SoundPlayer("C:\Windows\Media\Windows Notify System Generic.wav"); $player.Play();`。

## 验证计划

- `dotnet build` 验证。
- 手动检查附加计时器调节功能。
- 确认任务卡片色条在各种长度下均保持圆润。
- 确认弹窗时伴随清脆的提示音。
