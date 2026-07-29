# Taskato 整窗工作区开发计划

> 2026-07-28：该 WPF 视觉重构方案已由 `plan_WebShellMigration_20260728_1717.md` 接替。当前实现作为 WebView2 迁移期间的旧界面回退入口，不再继续扩展其视觉层。

## 1. 计划状态

- [x] 用户确认采用整体重构方案 C：左侧模块导航 + 页面内多栏工作区。
- [x] HTML 交互原型已完成并通过设计 QA。
- [x] 视觉基准：`docs/previews/preview_TaskatoWorkspace_20260728_1421/`。
- [x] WPF 业务代码已开始修改。
- [x] 用户已明确回复“开始实现”，当前进入阶段化开发。

## 2. 目标

把当前以 `560 × 780` 小窗体和多个 `ShowDialog()` 子窗体组成的 Taskato，重构为可缩放、可最大化、可全屏使用的完整桌面应用：

1. 主窗口默认采用大尺寸工作区，保留最小化、最大化、关闭到托盘等既有桌面行为。
2. 左侧导航统一承载“任务、每日总结、历史记录、设置”。
3. 任务页使用任务树、任务执行区、详情/番茄钟三栏结构。
4. 每日总结、历史记录和设置成为主窗口内部的完整页面，不再默认弹出独立大窗体。
5. 保留确认、任务完成、首次引导和番茄钟 Toast 等真正适合用弹窗表达的流程。
6. 保留现有 SQLite 数据和设置文件，数据库迁移只允许向前兼容的增量变更。

## 3. 已确认的产品规则

- [x] 任务支持最多五层嵌套。
- [x] 子任务完成可级联完成父任务，父任务成果进入每日总结。
- [x] “补录完成”统一改为“补充任务”。
- [x] 完成后又取消的记录保留在历史/日志中，但不出现在每日总结。
- [x] 同一天完成、取消、再次完成时，每日总结只显示最后一次仍然有效的完成。
- [x] 已完成后删除任务不等同于取消完成，标题快照仍可保留在历史总结中。
- [x] 子任务输入框拥有独立焦点，不能被主任务输入框抢走。

## 4. 推荐架构

### 4.1 主窗口职责

保留 `Views/MainWindow.xaml` 作为唯一应用壳层，不另外创建第二个主窗口。它只负责：

- 自定义标题栏和窗口缩放。
- 左侧导航与当前页面容器。
- 关闭到托盘、最大化/还原和窗口状态保存。
- 少量只能由 View 完成的焦点、拖拽和视觉行为。

业务页面均改成 `UserControl`，通过 `ContentControl` 或数据模板显示。

### 4.2 ViewModel 组合

新增 `ShellViewModel`，组合而不是复制现有业务逻辑：

- `TaskWorkspace`：先复用现有 `MainViewModel`，避免同时重写任务树和番茄钟。
- `DailySummary`：复用并扩展 `DailySummaryViewModel`。
- `History`：复用并扩展 `HistoryViewModel`。
- `Settings`：新增 `SettingsViewModel`，逐步承接原 `SettingsWindow.xaml.cs` 的可绑定状态。

新增 `AppSection` 枚举和 `NavigateCommand`。`ShellViewModel.NavigateAsync()` 负责：

1. 切换当前页面。
2. 页面激活时刷新所需数据。
3. 保存最后访问页面。
4. 接收托盘或首次引导传入的目标设置分区。

### 4.3 页面生命周期

不引入复杂导航框架。需要刷新的页面实现轻量方法：

- `TaskWorkspace.LoadTodayTasksAsync()`。
- `DailySummaryViewModel.LoadSummaryAsync()`。
- `HistoryViewModel.SearchAsync()`。
- `SettingsViewModel.ReloadFromSettings()`。

导航时只刷新目标页面，避免启动时同时访问所有数据源。

### 4.4 视图与业务边界

- 数据查询、任务状态、统计、设置持久化放在 ViewModel/Service。
- 窗口拖动、聚焦、滚动定位、文件选择器、颜色选择器和媒体试听可以保留在 View 的代码后台。
- 任务完成、取消、移动、删除等业务仍由 `DatabaseService` 统一执行，不能在页面代码后台直接拼接数据库操作。

## 5. 文件规划

### 5.1 新增文件

- [x] `src/Taskato/Models/AppSection.cs`：主导航页面枚举。
- [x] `src/Taskato/ViewModels/ShellViewModel.cs`：页面组合、导航和激活刷新。
- [x] `src/Taskato/ViewModels/SettingsViewModel.cs`：可绑定的设置状态与保存命令。
- [x] `src/Taskato/Views/Pages/TaskWorkspaceView.xaml(.cs)`：任务多栏工作区。
- [x] `src/Taskato/Views/Pages/DailySummaryView.xaml(.cs)`：每日总结页面。
- [x] `src/Taskato/Views/Pages/HistoryView.xaml(.cs)`：历史页面。
- [x] `src/Taskato/Views/Pages/SettingsView.xaml(.cs)`：设置页面。
- [ ] `src/Taskato/Views/Controls/AppNavigationRail.xaml(.cs)`：可收起左侧导航。
- [ ] `src/Taskato/Views/Controls/TaskInspectorView.xaml(.cs)`：选中任务详情与操作。
- [ ] `src/Taskato/Views/Controls/PomodoroInspectorView.xaml(.cs)`：右侧番茄钟区域。
- [ ] `src/Taskato/Themes/WorkspaceColors.xaml`：整窗背景、文字和语义状态色。
- [x] `src/Taskato/Themes/WorkspaceControls.xaml`：导航、列表、输入、开关和按钮样式。
- [x] `src/Taskato/Services/PomodoroNotificationCoordinator.cs`：统一注册番茄钟 Toast 与飞书通知，移出主窗口代码后台。

历史事件时间线属于数据能力增强，单独隔离：

- [ ] `src/Taskato/Models/TaskActivityRecord.cs`：新增/编辑/移动/完成/取消/删除事件快照。
- [ ] `src/Taskato/Models/TaskActivityType.cs`：事件类型枚举。
- [ ] `src/Taskato/Services/DatabaseService.Activity.cs`：事件写入、查询和幂等迁移。

### 5.2 修改文件

- [x] `src/Taskato/App.xaml`：合并新资源字典，保留现有主题动态资源键。
- [x] `src/Taskato/App.xaml.cs`：创建 `ShellViewModel`，调整托盘导航和启动加载。
- [x] `src/Taskato/Views/MainWindow.xaml`：替换为整窗壳层，默认 `1280 × 820`，最小 `960 × 640`。
- [x] `src/Taskato/Views/MainWindow.xaml.cs`：只保留窗口、焦点和视图事件；适配 Shell DataContext。
- [ ] `src/Taskato/ViewModels/MainViewModel.cs`：增加选中任务、页面刷新和内嵌详情所需状态；移除打开大子窗体命令。
- [x] `src/Taskato/ViewModels/DailySummaryViewModel.cs`：补充任务文案、有效完成过滤和页面激活刷新。
- [ ] `src/Taskato/ViewModels/HistoryViewModel.cs`：适配页面式筛选和事件时间线。
- [ ] `src/Taskato/Services/DatabaseService.cs`：只增加兼容表初始化，不改写现有表。
- [ ] `src/Taskato/Services/DatabaseService.TaskTree.cs`：在同一事务边界记录任务活动。
- [ ] `src/Taskato/Services/SettingsService.cs`：保存窗口边界、窗口状态、导航收起状态和最后访问页面。
- [ ] `src/TestProject/Program.cs`：补充总结过滤、活动记录和迁移回归测试。
- [x] `README.md`：更新整窗工作区和导航说明。
- [x] `docs/handoff_TaskTree_20260724_1336.md`：修正“取消完成仍显示在每日总结”的旧说明。

### 5.3 最后再移除或降级的文件

以下窗口在对应页面通过验收之前不得删除：

- [ ] `DailySummaryWindow.xaml(.cs)`。
- [ ] `HistoryWindow.xaml(.cs)`。
- [ ] `SettingsWindow.xaml(.cs)`。
- [ ] `TaskDetailWindow.xaml(.cs)`。

迁移完成后前三者改为兼容入口或删除；`TaskDetailWindow` 是否保留为独立深度编辑窗口，在任务详情面板验收后决定。

## 6. 实施阶段

### 阶段 0：先修正已确认的总结规则（已完成）

- [x] 把每日总结按钮、弹窗标题、确认按钮和提示中的“补录完成”统一改为“补充任务”。
- [x] 只在每日总结展示层过滤失效完成记录，不删除 `TaskCompletionRecord`。
- [x] 当前任务仍存在时，仅保留 `IsCompleted == true` 且 `CompletedAt` 与记录时间一致的记录。
- [x] 当前任务已删除时保留标题快照记录。
- [x] 验证完成 → 取消后叶子任务和自动重新打开的父级成果都从总结消失。
- [x] 验证同日再次完成后只显示最后一次有效记录。

验证证据（2026-07-28）：`dotnet build Taskato.sln -c Release` 成功；`dotnet run --project src/TestProject/TestProject.csproj -c Release` 通过全部任务树回归断言，包括取消完成过滤与同日再次完成仅保留有效记录。

验收门槛：数据库历史数量不变，每日总结统计、分组、复制和导出结果同步过滤。

### 阶段 1：建立视觉令牌和应用壳层（代码完成，待真实窗口 QA）

- [x] 从现有 `App.xaml` 提取整窗控件与导航样式。
- [x] 保留 `ThemePrimaryBrush` 等动态资源，确保现有主题设置继续生效。
- [x] 建立 `ShellViewModel`、`AppSection` 和左侧导航。
- [x] 主窗口支持普通、最大化和还原；双击标题栏行为保持不变。
- [x] 保存并安全恢复窗口尺寸与位置；恢复时把窗口限制在当前可用屏幕范围内。
- [x] 保留关闭到托盘和托盘重新唤起行为。

验收门槛：空页面壳层可在四个模块间切换，托盘与窗口状态无回归。

验证证据（2026-07-28）：修复跨资源字典使用 `StaticResource` 导致首次布局取得 `DependencyProperty.UnsetValue` 的启动异常；自动化冒烟测试已升级为在 STA 线程真实执行 `MainWindow.Show()` 与 `UpdateLayout()`。默认 Debug `clean → build` 成功，完整应用启动后窗口句柄与标题正常、进程保持响应。

### 阶段 2：迁移任务页

- [x] 把现有任务列表迁入 `TaskWorkspaceView`，不改数据库接口。
- [ ] 左栏显示树形任务、完成状态、优先级和父级进度。
- [ ] 中栏显示选中任务、子任务列表、快速新增和任务笔记/说明。
- [ ] 右栏显示任务详情与最近状态；窗口不足约 `1180` 像素时改为可呼出的详情区域。
- [x] 增加 `SelectedTask`，单击选择，双击可继续保留原详情窗口作为过渡入口。
- [x] 保留主任务输入、Shift+Enter 换行、Tab 切换优先级和子任务独立焦点。
- [ ] 所有完成、取消、删除、移动和新增行为继续通过现有命令执行。

验收门槛：现有任务树测试全部通过；真实窗口中五层任务、长标题和空列表不裁切。

### 阶段 3：迁移番茄钟与通知

- [x] 右侧面板绑定当前番茄钟，支持开始、暂停、继续、停止。
- [x] 多番茄钟模式保留，放在右侧面板的紧凑切换区域。
- [x] 把 `MainWindow.xaml.cs` 中重复的事件订阅移到 `PomodoroNotificationCoordinator`。
- [x] 防止页面切换、窗口隐藏/显示或集合变化造成重复订阅和重复 Toast。
- [x] 保留飞书工作完成、休息完成和休息过半通知。

验收门槛：主番茄钟和新增计时器各触发一次通知；隐藏到托盘后计时不中断。

### 阶段 4：迁移每日总结页

- [x] 将 `DailySummaryWindow` 内容迁入 `DailySummaryView`。
- [x] 导航进入页面时加载当前日期，前后日期切换继续可用。
- [x] 实现原型中的有效完成、父级成果和专注时长信息层级。
- [x] “补充任务”继续使用任务完成对话流程，不重复实现完成业务。
- [x] 复制和导出使用与页面一致的过滤后数据。
- [ ] 点击仍存在的任务可选中并导航回任务页详情；已删除任务只展示快照。

验收门槛：阶段 0 的所有过滤用例和原有补充任务流程均通过。

### 阶段 5：迁移设置页

- [ ] 将设置窗体主体迁入 `SettingsView`，保留左侧分区导航。
- [ ] 主题、番茄钟、系统、每日总结、提示音和飞书设置全部可操作。
- [x] 把已迁移设置的纯状态和保存逻辑放入 `SettingsViewModel`。
- [ ] 文件选择、声音试听、滚动定位等平台 UI 行为保留在代码后台。
- [x] 托盘“设置”和首次飞书引导直接导航到设置页。

验收门槛：修改后重启应用，所有设置与旧版本设置文件兼容并正确恢复。

### 阶段 6：迁移历史页并增加事件时间线

本阶段分成两个可独立验收的子阶段，避免数据迁移阻塞整窗 UI：

- [ ] 6A：先把现有任务历史查询、筛选、排序、导出和删除能力迁入 `HistoryView`。
- [ ] 6B：新增只追加的 `TaskActivityRecord`，记录新增、编辑、移动、完成、取消、删除等事件。
- [ ] 完成事件与 `TaskCompletionRecord` 在同一业务操作中产生，但使用来源标识避免重复展示。
- [ ] 对旧 `TaskCompletionRecord` 做幂等事件回填；无法推导的旧取消/编辑事件不伪造。
- [ ] 历史页面明确显示“日志从升级后开始完整记录”的边界。
- [ ] 每日总结不读取取消事件，只依据有效完成记录。

验收门槛：取消完成在历史页可见，在每日总结不可见；重复启动迁移不会产生重复事件。

### 阶段 7：收口旧窗口和启动流程

- [ ] 移除 `MainViewModel` 中打开每日总结、历史和设置窗口的 `ShowDialog()` 路径。
- [x] 将托盘和自动昨日总结入口改为 Shell 导航。
- [x] 清理主窗口中已经迁移到页面/协调器的代码后台逻辑。
- [x] 确认真正的短流程弹窗继续保留：确认、任务完成、首次引导、Toast。
- [ ] 页面功能对齐后再删除旧大窗体，避免提前失去可回退实现。

验收门槛：常规使用过程中只存在一个主窗口；不会打开重复的总结、历史或设置窗口。

### 阶段 8：完整验证与文档同步

- [x] 运行 `dotnet build Taskato.sln -c Release`。
- [x] 运行 `dotnet run --project src/TestProject/TestProject.csproj -c Release`。
- [ ] 在 1440 × 1024、1100 × 800 和最小 960 × 640 三种窗口尺寸做截图检查。
- [ ] 检查导航活动态、文本裁切、滚动区域、长任务标题、五层嵌套和空状态。
- [ ] 检查关闭到托盘、托盘恢复、最大化、跨屏恢复和应用退出。
- [ ] 检查任务新增、子任务输入焦点、完成/取消、父级级联、移动和删除。
- [ ] 检查番茄钟、多计时器、Toast、飞书通知和设置持久化。
- [ ] 检查每日总结复制/导出与页面统计一致。
- [ ] 检查历史记录迁移、查询、导出和重复启动幂等性。
- [x] 更新 README、任务树交接文档；当前仓库未维护 `docs/REPO_MAP.md` 与 `docs/project-memory.md`，本轮不额外创建。

## 7. 数据结构与兼容策略

### 7.1 设置文件新增字段

计划在 `SettingsConfig` 增加可空或带默认值字段：

- `WindowWidth`、`WindowHeight`。
- `WindowLeft`、`WindowTop`。
- `IsWindowMaximized`。
- `IsNavigationCollapsed`。
- `LastAppSection`。

旧 JSON 缺少这些字段时由默认值自动补齐，不要求手工迁移。

### 7.2 SQLite 新表

`TaskActivityRecords` 采用新增表，不修改或删除现有 `Tasks` 与 `TaskCompletionRecords`：

- 主键与任务 ID。
- 事件类型。
- 标题、父级和层级快照。
- 发生时间。
- 可读详情快照。
- 可空的完成记录来源 ID，用于幂等回填。

删除任务后活动记录继续存在，不依赖外键级联删除。

## 8. 风险与控制

### 高风险

1. `MainWindow.xaml.cs` 当前直接假设 DataContext 是 `MainViewModel`。
   - 控制：Shell 引入后统一改为 `ShellViewModel.TaskWorkspace`，并把通知订阅先迁出再拆页面。
2. `SettingsWindow.xaml.cs` 约 600 行且大量直接操作控件。
   - 控制：先迁移为可用的 UserControl，再逐块绑定 ViewModel，不进行一次性机械重写。
3. 新历史事件表涉及所有任务写操作。
   - 控制：放到 UI 迁移后的独立阶段；只做加表和事务内追加，不更改旧完成记录。

### 中风险

1. 多栏布局在较窄窗口可能裁切。
   - 控制：设置明确断点；右栏先隐藏/抽屉化，任务树保留最小宽度，中栏允许滚动。
2. 页面切换可能导致重复加载或事件订阅。
   - 控制：页面激活刷新与长期事件订阅分离；协调器实现一次性注册和释放。
3. 主题切换可能遗漏新页面资源。
   - 控制：新样式只引用现有动态主题键和新增语义资源，不在页面硬编码主题主色。

## 9. 回滚方案

- 每个阶段完成后单独构建、测试和截图，阶段间不混合提交。
- 在新页面通过验收前保留旧 `DailySummaryWindow`、`HistoryWindow` 和 `SettingsWindow`。
- 开发期间在 `App.xaml.cs` 保留一个仅用于迁移的壳层切换点，确认完成后再删除。
- SQLite 迁移只新增表；回滚旧程序时旧程序会忽略新表，原任务和完成记录不受影响。
- 设置文件新增字段使用默认值，旧版本反序列化时可忽略未知字段。
- 如果某个页面迁移失败，只回退对应导航目标，不回滚数据库与已验证的其他页面。

## 10. 推荐执行顺序

1. 阶段 0：总结规则和文案。
2. 阶段 1：壳层、导航、窗口状态。
3. 阶段 2：任务页。
4. 阶段 3：番茄钟与通知。
5. 阶段 4：每日总结。
6. 阶段 5：设置。
7. 阶段 6A：现有历史页迁移。
8. 阶段 6B：事件时间线与增量数据库迁移。
9. 阶段 7：清理旧窗口。
10. 阶段 8：完整验证与文档同步。

## 11. 当前下一步

- [x] 用户已确认本计划。
- [x] 用户明确回复“开始实现”，阶段 0 已完成。
- [x] 实施阶段 1：视觉令牌、应用壳层、导航与窗口状态（代码与 XAML 冒烟测试完成）。
- [ ] 继续完成任务页细节、完整设置迁移、历史事件时间线和真实窗口截图 QA。
- [ ] 每完成一个阶段，更新本文档勾选状态并报告实际验证证据。

## 2026-07-29 Web 工作区成熟度收口

本计划中未勾选的 WPF `UserControl` 文件项不再创建：后续确认的
`plan_WebShellMigration_20260728_1717.md` 已将整窗工作区实现改为 React + WebView2，
旧 WPF 页面仅作为故障回退保留。对应业务目标已在网页工作区实现，包括三栏任务页、
任务笔记、只追加活动日志、页面式总结/历史/设置和原生文件能力桥接。

验证证据：C# 回归程序通过；前端 3 个测试文件共 10 项测试通过；Release WebView2
隔离烟测人工验证创建任务、详情/笔记、全局搜索和关闭到托盘，发布流水线自动验证
React 根节点、本地桥接与错误状态。尚未完成的旧 WPF 控件项
保留原状态，以避免把“技术路线被替代”误标为“按原文件清单实现”。
