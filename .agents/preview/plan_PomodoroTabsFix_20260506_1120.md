# 多番茄钟真·选项卡模式重构方案

## 1. 目标与问题回顾
在此前的重构中，虽然完成了大体的架构调整，但由于未在 UI 层面（`MainWindow.xaml`）完全对应新 `ViewModel` 的属性名，导致了以下 Bug：
- **标签消失**：UI 绑定了旧属性 `PomodoroStatusText`，而新的 ViewModel 中该属性叫 `StatusText`，导致绑定失败。
- **无法开始/控制失效**：大圆环区域绑定的命令仍是 `StartPomodoroCommand`、`PausePomodoroCommand` 等，但实际应替换为 `StartWorkCommand`、`PauseCommand` 等。

本次方案将**彻底梳理数据模型与 UI 绑定的映射关系**，确保“狸猫换太子”后所有毛细血管完美接通。

## 2. 详细执行步骤

### [x] 步骤 1：补齐 `PomodoroTimerViewModel.cs` 的属性（对齐主 UI）
为了让大圆环 UI 无缝适配任何一个 `PomodoroTimerViewModel`，我们需要确保它包含主界面所需的全部状态属性。
- **新增属性**：`IsWorkingState` (bool)、`IsRestingState` (bool)、`IsPaused` (bool)。
- **新增命令**：`EarlyRestCommand`（提前休息），绑定到 `StartRest()`。
- **时间调节保存**：在构造函数中增加 `Action<int> onTimeAdjusted` 回调，确保用户在面板上点击 `+` `-` 时能同步保存设置。

### [x] 步骤 2：精简 `MainViewModel.cs`（剥离旧属性，建立集合）
将旧的、直接写在 `MainViewModel` 中的番茄钟字段彻底移除，以集合和当前选中项取而代之。
- **移除冗余字段**：`TimerDisplay`、`TimerProgress`、`PomodoroStatusText`、`IsTimerRunning`、`IsPaused`、`IsWorkingState`、`IsRestingState`。
- **移除旧命令**：`StartPomodoroCommand`、`PausePomodoroCommand` 等。
- **新增属性**：
  - `ObservableCollection<PomodoroTimerViewModel> AllTimers`：包含所有番茄钟（主 + 附加）。
  - `PomodoroTimerViewModel CurrentActiveTimer`：记录当前选中的标签页。
- **初始化**：在构造函数中创建一个名为“主番茄钟”的实例，存入 `AllTimers[0]`，并设为 `CurrentActiveTimer`。

### [x] 步骤 3：重写 `MainWindow.xaml` 绑定（核心修复区）
这是解决上一版所有 Bug 的关键。
- **外层包裹**：给番茄钟大圆环区域的外部（`<StackPanel>` 内）套上一层 `<Border DataContext="{Binding CurrentActiveTimer}">`，将数据上下文切换为当前选中的番茄钟。
- **精细更新绑定属性（关键！）**：
  - `PomodoroStatusText` -> `StatusText`
  - `IsTimerRunning` -> `IsStarted`
  - `StartPomodoroCommand` -> `StartWorkCommand`
  - `PausePomodoroCommand` -> `PauseCommand`
  - `StopPomodoroCommand` -> `StopCommand`
  - `TimerProgress` -> `Progress`
- **清理底部面板**：删除底部原本用于显示 `SelectedAdditionalTimer` 的那一坨多余的 `ContentControl`。
- **修改列表**：将水平 Tab 切换器 `ListBox` 的 `ItemsSource` 从 `AdditionalTimers` 改为 `AllTimers`，`SelectedItem` 改为 `CurrentActiveTimer`。

### [x] 步骤 4：更新 `MainWindow.xaml.cs` 与弹窗逻辑
- 删除原来的 `vm.OnWorkCompleted` 和 `vm.OnRestCompleted` 订阅（因为这两个事件已从 `MainViewModel` 中移除）。
- 遍历 `vm.AllTimers` 注册事件，并通过监听 `vm.AllTimers.CollectionChanged`，确保无论是主番茄钟还是后来添加的子番茄钟，时间到了都能准确调用自身的 `StartRest()` 或 `StartWork()`，互不干扰。

## 3. 验证标准
1. UI 大圆环依然漂亮，右上角状态标签文字恢复正常。
2. 开始、暂停、提前休息按钮完美响应。
3. 点击附加组标签，大圆环的数字瞬间切换，并且各自的倒计时相互独立运行。
4. 任何番茄钟计时结束，桌面弹窗准时出现。

---

**是否确认执行该方案？**
