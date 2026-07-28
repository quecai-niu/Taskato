# Taskato 任务树与每日总结完成记录——代码逻辑讲解

## 1. 从数据结构开始

### `Models/TaskItem.cs`

1. `ParentId` 是可空整数：为空时任务是顶级任务，有值时指向同表中的父任务。
2. 数据库仍只保存一张 `TaskItems` 表，因此旧任务不需要搬表；迁移后其 `ParentId` 默认为空。
3. `Depth` 不写入数据库，只在加载任务树时计算；根任务为 `0`，第五层为 `4`。
4. `IsExpanded` 只表达当前界面是否展开该分支。
5. `NewChildTitle` 和 `IsAddingChild` 保存任务卡片内的快速新增状态。
6. `Children` 是运行时集合，数据库查询结果会根据 `ParentId` 重新组装到这里。
7. `CompletedLeafCount`、`TotalLeafCount` 保存递归统计结果。
8. `HasChildren` 与 `IsLeaf` 从 `Children` 推导，避免再保存一份可能失真的状态。
9. `CanAddChild` 使用 `Depth < 4`，所以第五层不再显示添加入口。
10. `IndentWidth` 把深度换算为主界面的左缩进。
11. `ProgressDisplay` 组合成 `已完成叶子数/叶子总数`。
12. `NotifyTreeStateChanged()` 在树组装完成后通知 WPF 重新读取这些推导属性。

### `Models/TaskCompletionRecord.cs`

1. `CompletionKind` 区分叶子任务、阶段父任务和顶级大任务。
2. `CompletionTrigger` 区分手动完成、自动级联、历史补录和旧数据迁移。
3. `TaskId` 指向当前任务；即使任务以后删除，记录本身仍然保留。
4. `TopLevelTaskId` 用于把同一个大任务下的记录分组。
5. `TitleSnapshot` 保存完成当时的任务标题。
6. `PathSnapshot` 保存完成当时从顶级任务到当前任务的完整路径。
7. `TopLevelTitleSnapshot` 保存分组标题，防止以后改名影响旧总结。
8. `DepthSnapshot` 保存当时层级，用于每日总结缩进。
9. `PrioritySnapshot` 让旧总结继续按完成当时的优先级统计。
10. `CompletionKind` 和 `TriggerType` 保存记录的业务语义。
11. `CompletedAt` 决定记录属于哪一天。
12. `DurationMinutes` 只写在叶子完成记录上，父级成果保持为空，避免重复累计耗时。
13. `CurrentTask` 是运行时引用；任务尚未删除时，可从每日总结双击打开详情。
14. `KindDisplay` 把枚举转换成“子任务完成”“阶段完成”“大任务完成”。

### `Models/TaskOperationResult.cs`

1. `TaskDeleteMode.DeleteBranch` 表示删除当前任务及全部后代。
2. `TaskDeleteMode.PromoteChildren` 表示只删除当前任务并提升直属子任务。
3. `TaskCompletionResult` 返回是否成功、剩余未完成叶子数和本次级联完成的任务编号。
4. `TaskMoveResult` 返回移动是否成功以及可直接展示给用户的失败原因。

## 2. 数据库初始化与兼容迁移

### `Services/DatabaseService.cs`

1. 构造函数仍默认使用程序目录下的 `Data/taskato.db`。
2. 可选的 `databasePath` 只给隔离测试使用，测试不会写入用户真实数据库。
3. `InitializeAsync()` 先创建或确认 `TaskItems` 和 `TaskCompletionRecords` 两张表。
4. 通过 `pragma_table_info` 检查旧数据库是否已有 `ParentId`。
5. 没有该列时执行 `ALTER TABLE TaskItems ADD COLUMN ParentId INTEGER`。
6. 随后创建 `ParentId` 索引，加快查找直属子任务。
7. 最后调用 `MigrateLegacyCompletionRecordsAsync()`。
8. 迁移方法遍历旧版已完成任务，为尚无对应记录的任务补一条不可变完成记录。
9. 对比 `TaskId + CompletedAt` 保证每次启动不会重复迁移。
10. `CloseAsync()` 让隔离测试关闭 SQLite 连接后安全删除临时文件。

## 3. 新增子任务

### `DatabaseService.TaskTree.cs` 的 `AddChildTaskAsync`

1. 空标题立即返回，不产生数据库写入。
2. 所有操作进入 SQLite 事务。
3. 事务内重新读取父任务，避免界面对象已经过期。
4. `GetDepth(parent) >= 4` 时拒绝新增，严格限制为五层。
5. 新任务的 `ParentId` 设置为父任务编号。
6. 默认继承父任务优先级，创建时间和修改时间使用当前时间。
7. `GetNextSiblingOrder()` 把任务放到同级列表末尾。
8. 插入子任务后调用 `ReopenTaskAndAncestors()`。
9. 如果父任务或祖先已经完成，它们会恢复为未完成，但历史完成记录不会删除。
10. 事务成功后触发 `OnDataChanged`，主界面自动重载任务树。

## 4. 完成叶子任务并逐级完成父任务

### `CompleteTaskWithCascadeAsync`

1. 方法接收任务、完成时间、可选耗时和触发来源。
2. 事务内重新读取整棵任务树和目标任务。
3. 如果目标仍有直属子任务，方法拒绝直接完成父任务。
4. `CountUncompletedLeaves()` 递归计算还剩多少叶子任务，供界面提示。
5. 叶子任务合法时调用 `MarkTaskCompleted()`。
6. `MarkTaskCompleted()` 同时写当前状态和一条完成记录。
7. 叶子任务记录实际耗时；父任务记录不写耗时。
8. 接着读取当前任务的 `ParentId`。
9. 每次循环查找父任务的全部直属子任务。
10. 只要存在一个未完成直属子任务，就停止向上级联。
11. 全部直属子任务完成且父任务尚未完成时，自动完成父任务并写完成记录。
12. 再把检查位置提升到父任务的父任务。
13. 因此完成最后一个叶子任务时，可以在同一事务中连续完成多级父任务。
14. 所有写入成功后才触发一次全局刷新，界面不会看到中间状态。

### `CreateCompletionRecord`

1. `GetAncestorChain()` 从当前任务一直向上查到顶级任务。
2. 祖先链第一项就是分组所需的顶级任务。
3. 无子任务的当前任务标记为 `LeafTask`。
4. 有子任务且没有父级的任务标记为 `TopLevelAchievement`。
5. 其余父任务标记为 `ParentMilestone`。
6. 标题、路径、顶级标题、层级、优先级和时间在这里一次性保存为快照。

## 5. 取消完成但保留历史

### `ReopenTaskWithAncestorsAsync`

1. 事务内找到要重开的叶子任务。
2. `ReopenTaskAndAncestors()` 从当前任务开始循环。
3. 每一级把 `IsCompleted` 改为 `false`。
4. 清空当前状态用的 `CompletedAt` 和 `CompletionDurationMinutes`。
5. 更新修改时间后继续处理父级。
6. 该方法不删除 `TaskCompletionRecords`，所以操作历史仍保留；每日总结会过滤这次已经失效的完成。
7. 以后再次完成同一任务时，会生成一组新的完成记录。

## 6. 移动和删除后的状态重算

### `MoveTaskAsync`

1. 先验证目标任务和目标父任务都存在。
2. `GetDescendantIds()` 找出全部后代。
3. 目标父任务是自己或后代时拒绝移动，防止形成环。
4. `GetSubtreeHeight()` 计算待移动子树高度。
5. 目标深度加子树高度达到五层以外时拒绝移动。
6. 验证通过后更新 `ParentId` 和同级排序号。
7. `RecalculateAncestorStates()` 分别重新计算旧父级链和新父级链。
8. 移入未完成任务时，已完成的新父级链会重新打开。
9. 移走最后一个未完成任务后，如果旧父级的剩余子任务全部完成，旧父级会自动完成并记录成果。

### `DeleteTaskAsync`

1. “提升子任务”模式把每个直属子任务的 `ParentId` 改成当前任务的 `ParentId`。
2. “删除分支”模式先递归收集后代编号。
3. 后代按深度从深到浅删除，最后删除当前任务。
4. 两种模式都不删除完成记录。
5. 删除后重新计算原父级链，保证父级状态符合剩余子任务。

## 7. 主界面如何把数据库行变成任务树

### `MainViewModel.LoadTodayTasksAsync`

1. 从数据库加载任务行。
2. `BuildVisibleTaskTree()` 清空旧的运行时 `Children`。
3. 通过字典按 `ParentId` 把子任务放入父任务集合。
4. 没有有效父任务的记录作为根任务。
5. `CalculateTreeState()` 递归设置深度并累计叶子完成进度。
6. 递归使用 `HashSet` 防御异常循环数据。
7. `ContainsCurrentTask()` 保留未完成任务、今天创建任务、今天完成任务及其完整上下文分支。
8. `RefreshFlattenedTasks()` 按展开状态把树拍平成 `TodayTasks`。
9. WPF 仍绑定普通 `ListBox`，因此不需要重写整个列表控件。
10. `_collapsedTaskIds` 记录折叠分支，数据库刷新后不会意外全部展开。

### `ToggleTaskAsync`

1. 如果点击的是父任务，显示剩余叶子数并阻止直接完成。
2. 如果是未完成叶子，先打开耗时对话框。
3. 用户确认后调用统一的事务化级联完成方法。
4. 如果是已完成叶子，则调用重开方法并自动重开祖先。

### `MainWindow.xaml`

1. 卡片左边距绑定 `IndentWidth`，不同层级形成树形缩进。
2. `▾/▸` 按钮绑定展开命令。
3. 父任务标题右侧显示 `ProgressDisplay`。
4. `＋` 按钮只在前四层显示。
5. 展开的快速输入区双向绑定 `NewChildTitle`。
6. Enter 或“添加”按钮都调用同一个新增子任务方法。
7. 删除含子任务的节点时，代码后台提供“删除分支”“提升子任务”“取消”三种结果。

## 8. 详情窗口的完整树管理

### `TaskDetailWindow.xaml.cs`

1. 构造函数接收当前任务和同一个 `DatabaseService`。
2. `EditingTask` 只承载尚未保存的标题和优先级，避免输入过程污染原对象。
3. `LoadTreeManagementDataAsync()` 加载合法父任务候选。
4. 当前任务自身、全部后代以及会导致超过五层的目标不会出现在候选列表。
5. 选择新父任务后，保存按钮先调用 `MoveTaskAsync()` 验证并完成移动，再保存标题和优先级。
6. 移动验证失败时窗口保持打开并展示原因，尚未保存的文字修改也不会提前写入。
7. 详情页的状态按钮也调用统一完成/重开服务，不再直接修改完成字段。
8. 直属子任务区支持添加、打开详情和删除。
9. 删除子任务时同样提供删除分支或提升后代。
10. 到达第五层时隐藏添加子任务区域。

## 9. 每日总结如何生成

### `DailySummaryViewModel.LoadSummaryAsync`

1. 按当前日期查询 `TaskCompletionRecords`，并关联仍存在的当前任务状态。
2. 仍存在的任务只有在 `IsCompleted == true` 且 `CompletedAt` 与记录时间相同时才计入；已删除任务继续使用完成快照。
3. `CompletedCount` 是当天仍然有效的完成记录数。
4. `LeafCompletedCount` 只统计叶子完成。
5. `ParentAchievementCount` 统计阶段和顶级成果。
6. 高优先级统计使用完成时优先级快照。
7. 耗时只累加记录中的非空耗时，因此父级级联不会重复计算。
8. 记录按 `TopLevelTaskId + TopLevelTitleSnapshot` 分组。
9. 每组按完成时间和层级排列。
10. XAML 先显示大任务分组，再按 `DepthSnapshot` 缩进每条完成记录。

### `BackfillTaskAsync`

1. 禁止为未来日期补充任务。
2. 候选列表只加载未完成叶子任务。
3. 用户选定叶子并填写耗时后，调用同一个级联完成方法。
4. 叶子和因此完成的所有父级使用相同补充时间。

### `BuildSummaryText`

1. 输出日期、星期和总结导语。
2. 输出完成记录、叶子任务、父级成果、高优先级成果和实际耗时。
3. 按顶级任务分组输出。
4. 每条记录按完成时层级添加空格缩进。
5. 导出的标题、路径和优先级全部来自快照。

## 10. 自动化验证逐项对应

### `TestProject/Program.cs`

1. 使用随机临时 SQLite 文件，不接触真实数据。
2. 创建“顶级任务 → 阶段 → 两个叶子”的任务树。
3. 完成第一个叶子，断言只产生一个完成结果。
4. 完成最后一个叶子，断言同时完成叶子、阶段和顶级任务。
5. 断言每日总结有两个叶子记录和两个父级成果。
6. 断言父级成果没有重复累计耗时。
7. 重开叶子，断言所有祖先恢复未完成且历史记录数量不变。
8. 再次完成，断言产生新记录。
9. 给已完成父级新增子任务，断言父级链重新打开。
10. 直接完成仍有未完成叶子的父任务，断言操作被拒绝。
11. 创建到第五层，再添加第六层，断言失败。
12. 移动后超过五层，断言失败。
13. 删除父任务并提升子任务，断言子任务成为顶级任务。
14. 删除最后一个未完成子任务，断言父级按剩余任务自动完成。
15. 向已完成父级移入未完成任务，断言父级重新打开。
16. 修改任务标题，断言旧完成记录的标题快照不变。
17. 模拟旧版本已完成任务并重启数据库服务，断言自动补建迁移记录。
18. 最后关闭连接并删除临时数据库。

## 11. 验证命令

```powershell
$testBuildRoot = Join-Path $env:TEMP 'Taskato-Codex-Tests'
dotnet run --project src\TestProject\TestProject.csproj -p:BaseOutputPath="$testBuildRoot\"

$buildRoot = Join-Path $env:TEMP 'Taskato-Codex-Build2'
dotnet build Taskato.sln -p:BaseOutputPath="$buildRoot\"
```

预期测试输出：

```text
PASS: 任务树、级联完成、历史记录、层级限制和删除提升测试全部通过。
```

## 12. 2026-07-28 Web 工作区迁移补充

主工作区现已迁移到 `src/Taskato.Web` 的 React + TypeScript 页面，WPF 仍是桌面宿主和业务事实来源：

1. `WebShellWindow` 通过 WebView2 虚拟域名加载打包后的静态资源，生产环境不启动 HTTP 服务。
2. `WebBridgeService` 只接受白名单 JSON 请求，任务、总结、历史、设置和番茄钟写操作继续进入原有 C# Service。
3. React 的顶级任务输入与子任务输入使用两个独立 state；浏览器交互烟测已确认子任务输入不会污染顶级输入。
4. 历史页从不可变完成记录和任务当前状态投影创建、完成、父级成果与取消完成事件；取消完成不改变每日总结的有效记录过滤规则。
5. `UseLegacyWorkspace=true` 或 WebView2 初始化错误页的“切换到旧版界面”可回退到本文件前文说明的 WPF 工作区。
6. `TestProject` 增加桥接握手、未知方法、无效参数、任务 DTO 和历史时间线契约测试。

新的验证顺序：

```powershell
cd src/Taskato.Web
npm test
npm run build
cd ../..
dotnet build Taskato.sln -c Release
dotnet run --project src/TestProject/TestProject.csproj -c Release
```
