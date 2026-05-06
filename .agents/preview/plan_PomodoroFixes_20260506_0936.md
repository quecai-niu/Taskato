# 番茄钟功能修复与 UI 优化

本方案旨在解决用户提出的番茄钟步进过慢、附加计时器按钮失效、键盘自动聚焦失效以及任务卡片颜色标签形状异常的问题。

## 待办事项 (TODO)

- [ ] **[Pomodoro]** 将主页番茄钟的步进值改回 5 分钟（单次点击加减 5 分钟，保持设置页面的 1 分钟精度）。
- [ ] **[UI]** 修复 `JellyIconBtn` 样式，增加对播放 (▶)、暂停 (⏸)、停止 (⏹) 符号的支持，使附加计时器按钮可见并可用。
- [ ] **[Logic]** 实现主窗口键盘监听，任意字符键入时自动聚焦到任务输入框。
- [ ] **[UI]** 优化任务卡片颜色标签（Floating Pill），调整边距以避免被父容器圆角裁剪导致的“锥子”畸变。

## 方案细节

### 1. 时间步进调整 (MainViewModel.cs)
- 修改 `IncreaseTimeCommand` 和 `DecreaseTimeCommand` 的逻辑，将步进值从 `1` 改为 `5`。
- 设置页面保留 1 分钟精度，确保灵活性的同时提升主页操作效率。

### 2. JellyIconBtn 增强 (App.xaml)
- 在 `JellyIconBtn` 的 `ControlTemplate.Triggers` 中新增 DataTrigger 或 Trigger。
- 为 `Content="▶"`, `Content="⏸"`, `Content="⏹"` 分别定义矢量 Path 数据。

### 3. 键盘自动聚焦 (MainWindow.xaml.cs)
- 重写 `OnPreviewKeyDown` 方法。
- 判断当前焦点不在 TextBox/PasswordBox 且按下的是普通字符键（字母、数字、符号）时，将焦点转移到 `TaskInput` 并确保字符能被录入。

### 4. 颜色标签形状修正 (MainWindow.xaml)
- 调整 `taskCard` 内部颜色条的 `Margin`。
- 将 `Margin="10,10,0,10"` 改为 `Margin="12,12,0,12"`，确保其不与父容器 `CornerRadius="14"` 的圆角重叠，消除裁剪导致的尖头感。
- 确认 `VerticalAlignment="Stretch"` 下的高度计算正确。

## 验证计划

### 自动化测试
- `dotnet build` 确保无语法错误。

### 手动验证
- 点击主页加减按钮，确认每次增减 5 分钟。
- 添加附加番茄钟，确认其 ▶/⏸/⏹ 按钮显示正常且点击有响应。
- 在主界面直接敲击键盘，确认焦点自动跳转到输入框。
- 观察任务卡片左侧色条，确认其为上下圆润的长条形（Pill Shape）。
