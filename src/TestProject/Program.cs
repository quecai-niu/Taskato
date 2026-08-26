using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using SQLite;
using Taskato.Models;
using Taskato.Models.BridgeDtos;
using Taskato.Services;
using Taskato.Services.WebBridge;
using Taskato.ViewModels;

internal static class Program
{
    [STAThread]
    private static async Task Main()
    {
        // Bridge test（桥接测试）仍会验证 WPF 原生主题资源；创建最小 Application 容器，
        // 但不启动旧版工作区窗口，保持测试与当前 WebView2 宿主边界一致。
        if (Application.Current is null)
            _ = new Application();

        RunCurrentTaskVisibilityTests();
        RunPomodoroElapsedTimeTests();
        RunPomodoroPersistenceTests();
        RunWindowPlacementTests();
        RunWindowStatePersistenceTests();
        RunFeishuResponseTests();

        var testDirectory = Path.Combine(Path.GetTempPath(), "TaskatoTests");
        Directory.CreateDirectory(testDirectory);
        var databasePath = Path.Combine(testDirectory, $"task-tree-{Guid.NewGuid():N}.db");
        DatabaseService? database = null;

        try
        {
            await RunFeishuNotificationCountTestsAsync(testDirectory);
            await RunPomodoroCompletionCaptureTestsAsync(testDirectory);
            await RunLegacyPomodoroProgressMigrationTestsAsync(testDirectory);
            database = new DatabaseService(databasePath);
            await database.InitializeAsync();
            await RunFocusAnalyticsTestsAsync(database);
            await RunFocusTimerSlotStabilityTestsAsync(database);
            await RunNotificationCoordinatorTestsAsync(database, testDirectory);

            var root = await AddTaskAsync(database, "发布新版本");
            var phase = await database.AddChildTaskAsync(root, "登录模块")
                ?? throw new InvalidOperationException("创建阶段任务失败。");
            var leafOne = await database.AddChildTaskAsync(phase, "完成登录页面")
                ?? throw new InvalidOperationException("创建第一个叶子任务失败。");
            var leafTwo = await database.AddChildTaskAsync(phase, "调整按钮")
                ?? throw new InvalidOperationException("创建第二个叶子任务失败。");

            var firstResult = await database.CompleteTaskWithHierarchyValidationAsync(
                leafOne,
                DateTime.Today.AddHours(9),
                25,
                CompletionTrigger.Manual);
            Assert(firstResult.Succeeded, "第一个叶子任务应完成成功。");
            Assert(firstResult.CompletedTaskIds.Count == 1, "部分完成时不应提前完成父任务。");

            var secondResult = await database.CompleteTaskWithHierarchyValidationAsync(
                leafTwo,
                DateTime.Today.AddHours(10),
                15,
                CompletionTrigger.Manual);
            Assert(secondResult.Succeeded, "最后一个叶子任务应完成成功。");
            Assert(secondResult.CompletedTaskIds.Count == 1, "完成叶子任务只能完成当前任务。");

            var afterLeavesCompleted = await database.GetTaskTreeItemsAsync();
            Assert(!afterLeavesCompleted.Single(task => task.Id == phase.Id).IsCompleted &&
                   !afterLeavesCompleted.Single(task => task.Id == root.Id).IsCompleted,
                "全部叶子完成后，所有父任务仍应等待用户手动确认。");

            var phaseCompletion = await database.CompleteTaskWithHierarchyValidationAsync(
                afterLeavesCompleted.Single(task => task.Id == phase.Id),
                DateTime.Today.AddHours(10).AddMinutes(5),
                null,
                CompletionTrigger.Manual);
            Assert(phaseCompletion.Succeeded && phaseCompletion.CompletedTaskIds.SequenceEqual(new[] { phase.Id }),
                "全部叶子完成后应允许手动完成当前父任务。");
            Assert(!(await database.GetTaskTreeItemsAsync()).Single(task => task.Id == root.Id).IsCompleted,
                "手动完成中间父任务不得继续完成顶级祖先。");

            var rootCompletion = await database.CompleteTaskWithHierarchyValidationAsync(
                root,
                DateTime.Today.AddHours(10).AddMinutes(10),
                null,
                CompletionTrigger.Manual);
            Assert(rootCompletion.Succeeded && rootCompletion.CompletedTaskIds.SequenceEqual(new[] { root.Id }),
                "全部后代叶子完成后应允许手动完成顶级任务。");

            var firstCompletionRecords = await database.GetCompletionRecordsOnAsync(DateTime.Today);
            Assert(firstCompletionRecords.Count == 4, "每日总结应包含两个叶子和两个手动确认的父级成果。");
            Assert(firstCompletionRecords.Count(record => record.CompletionKind == CompletionKind.LeafTask) == 2,
                "叶子完成记录数量错误。");
            Assert(firstCompletionRecords.Count(record => record.CompletionKind != CompletionKind.LeafTask) == 2,
                "父级成果记录数量错误。");
            Assert(firstCompletionRecords.All(record => record.TriggerType == CompletionTrigger.Manual),
                "叶子和父任务的完成记录都必须来自用户手动确认。");
            Assert(firstCompletionRecords.Sum(record => record.DurationMinutes ?? 0) == 40,
                "父级成果不应重复累计耗时。");

            await database.ReopenTaskWithHierarchyRulesAsync(leafTwo);
            var reopenedTasks = await database.GetTaskTreeItemsAsync();
            Assert(!reopenedTasks.Single(task => task.Id == leafTwo.Id).IsCompleted, "叶子任务应重新打开。");
            Assert(!reopenedTasks.Single(task => task.Id == phase.Id).IsCompleted, "阶段任务应重新打开。");
            Assert(!reopenedTasks.Single(task => task.Id == root.Id).IsCompleted, "顶级任务应重新打开。");
            Assert((await database.GetCompletionRecordsOnAsync(DateTime.Today)).Count == 4,
                "重新打开不应删除历史完成记录。");

            var reopenedSummary = new DailySummaryViewModel(database);
            await reopenedSummary.LoadSummaryAsync();
            Assert(reopenedSummary.CompletedCount == 1,
                "每日总结应隐藏已取消完成的叶子任务及重新打开的父级成果。");
            Assert(reopenedSummary.CompletionGroups
                    .SelectMany(group => group.Records)
                    .Single()
                    .TaskId == leafOne.Id,
                "每日总结应保留仍然有效的叶子完成记录。");

            leafTwo = reopenedTasks.Single(task => task.Id == leafTwo.Id);
            await database.CompleteTaskWithHierarchyValidationAsync(
                leafTwo,
                DateTime.Today.AddHours(11),
                10,
                CompletionTrigger.Manual);
            var afterLeafRecompleted = await database.GetTaskTreeItemsAsync();
            await database.CompleteTaskWithHierarchyValidationAsync(
                afterLeafRecompleted.Single(task => task.Id == phase.Id),
                DateTime.Today.AddHours(11).AddMinutes(5),
                null,
                CompletionTrigger.Manual);
            await database.CompleteTaskWithHierarchyValidationAsync(
                afterLeafRecompleted.Single(task => task.Id == root.Id),
                DateTime.Today.AddHours(11).AddMinutes(10),
                null,
                CompletionTrigger.Manual);
            Assert((await database.GetCompletionRecordsOnAsync(DateTime.Today)).Count == 7,
                "再次手动完成应分别新增叶子与父级完成记录。");

            var recompletedSummary = new DailySummaryViewModel(database);
            await recompletedSummary.LoadSummaryAsync();
            Assert(recompletedSummary.CompletedCount == 4,
                "同日再次完成后，每日总结只应展示最后一次有效完成及其他有效成果。");

            phase = (await database.GetTaskTreeItemsAsync()).Single(task => task.Id == phase.Id);
            var addedAfterCompletion = await database.AddChildTaskAsync(phase, "补充回归测试");
            Assert(addedAfterCompletion is not null, "已完成父任务应允许新增子任务。");
            var afterAddTasks = await database.GetTaskTreeItemsAsync();
            Assert(!afterAddTasks.Single(task => task.Id == phase.Id).IsCompleted,
                "新增未完成子任务后阶段任务应重新打开。");
            Assert(!afterAddTasks.Single(task => task.Id == root.Id).IsCompleted,
                "新增未完成子任务后祖先任务应重新打开。");

            var blockedParentCompletion = await database.CompleteTaskWithHierarchyValidationAsync(
                afterAddTasks.Single(task => task.Id == phase.Id),
                DateTime.Today.AddHours(12),
                null,
                CompletionTrigger.Manual);
            Assert(!blockedParentCompletion.Succeeded &&
                   blockedParentCompletion.UncompletedDescendantCount == 1,
                "父任务存在未完成叶子时必须禁止直接完成。");

            var levelThree = await database.AddChildTaskAsync(addedAfterCompletion!, "三级")
                ?? throw new InvalidOperationException("创建第三级失败。");
            var levelFour = await database.AddChildTaskAsync(levelThree, "四级")
                ?? throw new InvalidOperationException("创建第四级失败。");
            var overLimitTask = await database.AddChildTaskAsync(levelFour, "超过五层");
            Assert(overLimitTask is null, "任务树必须限制为最多五层。");

            var movableRoot = await AddTaskAsync(database, "待移动任务");
            var invalidMove = await database.MoveTaskAsync(movableRoot.Id, levelFour.Id);
            Assert(!invalidMove.Succeeded, "移动任务后超过五层时必须拒绝操作。");

            var removableParent = await AddTaskAsync(database, "待删除父任务");
            var promotedChild = await database.AddChildTaskAsync(removableParent, "需要提升的子任务")
                ?? throw new InvalidOperationException("创建待提升子任务失败。");
            await database.DeleteTaskAsync(removableParent, TaskDeleteMode.PromoteChildren);
            var afterPromoteTasks = await database.GetTaskTreeItemsAsync();
            Assert(afterPromoteTasks.All(task => task.Id != removableParent.Id), "父任务应被删除。");
            Assert(afterPromoteTasks.Single(task => task.Id == promotedChild.Id).ParentId is null,
                "直属子任务应提升为顶级任务。");

            var structureParent = await AddTaskAsync(database, "结构变更父任务");
            var finishedChild = await database.AddChildTaskAsync(structureParent, "已完成子任务")
                ?? throw new InvalidOperationException("创建已完成子任务失败。");
            var removedPendingChild = await database.AddChildTaskAsync(structureParent, "待删除子任务")
                ?? throw new InvalidOperationException("创建待删除子任务失败。");
            await database.CompleteTaskWithHierarchyValidationAsync(
                finishedChild,
                DateTime.Today.AddHours(13),
                null,
                CompletionTrigger.Manual);
            await database.DeleteTaskAsync(removedPendingChild, TaskDeleteMode.DeleteBranch);
            var afterStructureDelete = await database.GetTaskTreeItemsAsync();
            Assert(!afterStructureDelete.Single(task => task.Id == structureParent.Id).IsCompleted,
                "删除最后一个未完成子任务后，父任务仍应等待手动完成。");
            var structureParentCompletion = await database.CompleteTaskWithHierarchyValidationAsync(
                afterStructureDelete.Single(task => task.Id == structureParent.Id),
                DateTime.Today.AddHours(13).AddMinutes(5),
                null,
                CompletionTrigger.Manual);
            Assert(structureParentCompletion.Succeeded,
                "结构变化后全部叶子完成时，应允许手动完成父任务。");

            var incompleteMovedTask = await AddTaskAsync(database, "移动进来的未完成任务");
            var moveIntoCompletedParent = await database.MoveTaskAsync(
                incompleteMovedTask.Id,
                structureParent.Id);
            Assert(moveIntoCompletedParent.Succeeded, "合法的层级移动应成功。");
            var afterIncompleteMove = await database.GetTaskTreeItemsAsync();
            Assert(!afterIncompleteMove.Single(task => task.Id == structureParent.Id).IsCompleted,
                "移入未完成子任务后，已完成父任务应重新打开。");

            var independentRoot = await AddTaskAsync(database, "独立确认顶级任务");
            var independentParent = await database.AddChildTaskAsync(independentRoot, "独立确认中间父任务")
                ?? throw new InvalidOperationException("创建独立确认父任务失败。");
            var independentLeaf = await database.AddChildTaskAsync(independentParent, "独立确认叶子任务")
                ?? throw new InvalidOperationException("创建独立确认叶子任务失败。");
            await database.CompleteTaskWithHierarchyValidationAsync(
                independentLeaf,
                DateTime.Today.AddHours(14),
                null,
                CompletionTrigger.Manual);
            await database.CompleteTaskWithHierarchyValidationAsync(
                independentParent,
                DateTime.Today.AddHours(14).AddMinutes(5),
                null,
                CompletionTrigger.Manual);
            await database.CompleteTaskWithHierarchyValidationAsync(
                independentRoot,
                DateTime.Today.AddHours(14).AddMinutes(10),
                null,
                CompletionTrigger.Manual);
            await database.ReopenTaskWithHierarchyRulesAsync(independentParent);
            var afterParentReopened = await database.GetTaskTreeItemsAsync();
            Assert(!afterParentReopened.Single(task => task.Id == independentParent.Id).IsCompleted,
                "取消中间父任务时应重新打开当前父任务。");
            Assert(afterParentReopened.Single(task => task.Id == independentRoot.Id).IsCompleted,
                "取消中间父任务不得清除顶级任务独立确认的完成状态。");

            root = afterIncompleteMove.Single(task => task.Id == root.Id);
            root.Title = "发布新版本（已改名）";
            root.Notes = "发布前检查安装、升级和回滚路径。";
            await database.UpdateTaskAsync(root);
            var preservedRecord = (await database.GetCompletionRecordsOnAsync(DateTime.Today))
                .First(record => record.TaskId == root.Id);
            Assert(preservedRecord.TitleSnapshot == "发布新版本",
                "历史完成记录必须保留完成时标题快照。");
            var activityTypes = (await database.SearchActivityRecordsAsync(
                    DateTime.Today.AddDays(-1),
                    DateTime.Today,
                    string.Empty,
                    "all",
                    sortDescending: true))
                .Select(record => record.ActivityType)
                .ToHashSet();
            Assert(activityTypes.Contains(TaskActivityType.Created), "活动日志必须记录任务创建。");
            Assert(activityTypes.Contains(TaskActivityType.Updated), "活动日志必须记录标题和笔记编辑。");
            Assert(activityTypes.Contains(TaskActivityType.Moved), "活动日志必须记录层级移动。");
            Assert(activityTypes.Contains(TaskActivityType.Completed), "活动日志必须记录手动完成的叶子及父级成果。");
            Assert(activityTypes.Contains(TaskActivityType.Reopened), "活动日志必须记录取消完成。");
            Assert(activityTypes.Contains(TaskActivityType.Deleted), "活动日志必须记录删除。");

            var legacyCompletedAt = DateTime.Today.AddDays(-1).AddHours(18);
            var legacyTask = new TaskItem
            {
                Title = "升级前已经完成的任务",
                CreatedAt = legacyCompletedAt.AddHours(-1),
                IsCompleted = true,
                CompletedAt = legacyCompletedAt,
                LastModifiedAt = legacyCompletedAt
            };
            await database.AddTaskAsync(legacyTask);
            await database.CloseAsync();

            database = new DatabaseService(databasePath);
            await database.InitializeAsync();
            var migratedTask = (await database.GetTaskTreeItemsAsync())
                .Single(task => task.Id == root.Id);
            var expectedMigratedContent = "发布新版本（已改名）\n发布前检查安装、升级和回滚路径。";
            Assert(migratedTask.Title == expectedMigratedContent && string.IsNullOrEmpty(migratedTask.Notes),
                "旧 Notes 必须无损合并进唯一任务内容并在迁移后清空。");
            var legacyRecords = await database.GetCompletionRecordsOnAsync(legacyCompletedAt);
            Assert(legacyRecords.Any(record =>
                    record.TaskId == legacyTask.Id &&
                    record.TriggerType == CompletionTrigger.LegacyMigration),
                "升级后必须把旧的已完成任务迁移为历史完成记录。");
            var activityCountAfterMigration = (await database.SearchActivityRecordsAsync(
                new DateTime(2000, 1, 1),
                DateTime.Today,
                string.Empty,
                "all",
                sortDescending: true)).Count;
            await database.InitializeAsync();
            var contentAfterRepeatedMigration = (await database.GetTaskTreeItemsAsync())
                .Single(task => task.Id == root.Id);
            Assert(contentAfterRepeatedMigration.Title == expectedMigratedContent,
                "重复初始化不得再次追加已经迁移的任务内容。");
            var activityCountAfterRepeatedMigration = (await database.SearchActivityRecordsAsync(
                new DateTime(2000, 1, 1),
                DateTime.Today,
                string.Empty,
                "all",
                sortDescending: true)).Count;
            Assert(activityCountAfterRepeatedMigration == activityCountAfterMigration,
                "重复初始化不得生成重复活动记录。");

            var backupSettingsPath = Path.Combine(testDirectory, $"backup-settings-{Guid.NewGuid():N}.json");
            var backupPomodoroStatePath = Path.Combine(testDirectory, "pomodoro-state.json");
            var backupArchivePath = Path.Combine(testDirectory, $"backup-{Guid.NewGuid():N}.taskato-backup");
            var restoreDirectory = Path.Combine(testDirectory, $"restore-{Guid.NewGuid():N}");
            try
            {
                var backupSettings = new SettingsService(backupSettingsPath);
                backupSettings.Config.WorkMinutes = 42;
                backupSettings.Save();
                new PomodoroStateStore(backupPomodoroStatePath).Save(new[]
                {
                    new PomodoroService(42, 5).CreateSnapshot("timer-0")
                });
                var backupService = new DataBackupService(database, backupSettings);
                await backupService.CreateArchiveAsync(backupArchivePath);
                Assert(File.Exists(backupArchivePath), "数据备份应创建归档文件。");

                Directory.CreateDirectory(restoreDirectory);
                await DataBackupService.StageRestoreAsync(backupArchivePath, restoreDirectory);
                DataBackupService.ApplyPendingRestore(restoreDirectory);
                var restoredDatabasePath = Path.Combine(restoreDirectory, "taskato.db");
                var restoredDatabase = new DatabaseService(restoredDatabasePath);
                await restoredDatabase.InitializeAsync();
                Assert((await restoredDatabase.GetTaskTreeItemsAsync()).Count > 0,
                    "恢复后的数据库应保留任务数据。");
                await restoredDatabase.CloseAsync();
                Assert(File.Exists(Path.Combine(restoreDirectory, "settings.json")),
                    "恢复应包含设置文件。");
                Assert(File.Exists(Path.Combine(restoreDirectory, "pomodoro-state.json")),
                    "恢复应包含番茄钟运行状态文件。");
            }
            finally
            {
                if (File.Exists(backupSettingsPath)) File.Delete(backupSettingsPath);
                if (File.Exists(backupPomodoroStatePath)) File.Delete(backupPomodoroStatePath);
                if (File.Exists(backupArchivePath)) File.Delete(backupArchivePath);
                if (Directory.Exists(restoreDirectory)) Directory.Delete(restoreDirectory, recursive: true);
            }

            var bridgeSettingsPath = Path.Combine(testDirectory, $"bridge-settings-{Guid.NewGuid():N}.json");
            try
            {
                var settings = new SettingsService(bridgeSettingsPath);
                var pomodoro = new PomodoroService(settings.Config.WorkMinutes, settings.Config.RestMinutes);
                var mainViewModel = new MainViewModel(
                    database,
                    pomodoro,
                    settings,
                    new FeishuService(settings));
                var bridgeContext = new WorkspaceContextService();
                using var bridge = new WebBridgeService(
                    database,
                    mainViewModel,
                    settings,
                    bridgeContext);

                var ready = await bridge.ProcessRequestAsync(new WebBridgeRequest
                {
                    Id = "ready-1",
                    Method = "app.ready"
                });
                Assert(ready.Ok && ready.Id == "ready-1", "桥接握手必须回传请求 id。");

                var contextUpdate = await bridge.ProcessRequestAsync(new WebBridgeRequest
                {
                    Id = "context-1",
                    Method = "workspace.context.set",
                    Params = JsonSerializer.SerializeToElement(new
                    {
                        activePage = "tasks",
                        taskViewMode = "create",
                        selectedTaskId = root.Id
                    })
                });
                Assert(contextUpdate.Ok &&
                       bridgeContext.Current == new WorkspaceContextSnapshot("tasks", "create", root.Id),
                    "桥接必须只通过白名单方法同步当前页面和任务视图上下文。");

                var unknown = await bridge.ProcessRequestAsync(new WebBridgeRequest
                {
                    Id = "unknown-1",
                    Method = "system.runAnything"
                });
                Assert(!unknown.Ok && unknown.Error?.Code == "method_not_found",
                    "未知桥接方法必须返回结构化白名单错误。");

                var invalid = await bridge.ProcessRequestAsync(new WebBridgeRequest
                {
                    Id = "invalid-1",
                    Method = "tasks.add",
                    Params = JsonSerializer.SerializeToElement(new { title = "" })
                });
                Assert(!invalid.Ok && invalid.Error?.Code == "invalid_params",
                    "无效任务参数必须返回结构化参数错误。");

                var malformedUpdate = await bridge.ProcessRequestAsync(new WebBridgeRequest
                {
                    Id = "invalid-2",
                    Method = "tasks.update",
                    Params = JsonSerializer.SerializeToElement(new { taskId = root.Id, priority = "high" })
                });
                Assert(!malformedUpdate.Ok && malformedUpdate.Error?.Code == "invalid_params",
                    "错误参数类型不能泄漏为内部异常。");

                var taskList = await bridge.ProcessRequestAsync(new WebBridgeRequest
                {
                    Id = "tasks-1",
                    Method = "tasks.list"
                });
                Assert(taskList.Ok && taskList.Result is not null, "任务查询桥接必须返回 DTO 快照。");
                var taskListJson = JsonSerializer.SerializeToElement(taskList.Result);
                Assert(taskListJson.TryGetProperty("tasks", out _) &&
                       taskListJson.TryGetProperty("detailTasks", out _),
                    "任务桥接必须分别返回左侧可见任务和详情完整任务。");
                Assert(!JsonSerializer.Serialize(taskList.Result).Contains("\"Notes\"", StringComparison.Ordinal),
                    "网页任务契约不得再暴露第二份 Notes 内容。");

                var summaryProgressAt = DateTime.Today.AddHours(16);
                await database.AddProgressRecordAsync(
                    root.Id,
                    "验证每日总结总投入",
                    summaryProgressAt.AddMinutes(-17),
                    summaryProgressAt,
                    17,
                    17,
                    "manual",
                    TaskProgressSource.Manual);
                var summaryResponse = await bridge.ProcessRequestAsync(new WebBridgeRequest
                {
                    Id = "summary-duration-total",
                    Method = "summary.get",
                    Params = JsonSerializer.SerializeToElement(new
                    {
                        date = DateTime.Today.ToString("yyyy-MM-dd")
                    })
                });
                Assert(summaryResponse.Ok && summaryResponse.Result is not null,
                    "每日总结桥接必须返回总投入统计。");
                var summaryJson = JsonSerializer.SerializeToElement(summaryResponse.Result);
                var taskDuration = summaryJson.GetProperty("taskDurationMinutes").GetInt32();
                var progressDuration = summaryJson.GetProperty("progressDurationMinutes").GetInt32();
                var totalDuration = summaryJson.GetProperty("totalDurationMinutes").GetInt32();
                Assert(progressDuration == 17 &&
                       totalDuration == taskDuration + progressDuration,
                    "每日总结总投入时长必须等于任务耗时与推进耗时之和。");

                var taskCountBeforeAdd = (await database.GetTaskTreeItemsAsync()).Count;
                var validAdd = await bridge.ProcessRequestAsync(new WebBridgeRequest
                {
                    Id = "task-add-visible-composer",
                    Method = "tasks.add",
                    Params = JsonSerializer.SerializeToElement(new { title = "常驻入口任务", priority = 3 })
                });
                var tasksAfterVisibleAdd = await database.GetTaskTreeItemsAsync();
                Assert(validAdd.Ok && tasksAfterVisibleAdd.Count == taskCountBeforeAdd + 1 &&
                       tasksAfterVisibleAdd.Single(task => task.Title == "常驻入口任务").Priority == 3,
                    "常驻新增入口必须真正写入顶级任务及其所选优先级。");

                var initialWorkMinutes = pomodoro.WorkMinutes;
                var increaseTimer = await bridge.ProcessRequestAsync(new WebBridgeRequest
                {
                    Id = "pomodoro-increase",
                    Method = "pomodoro.command",
                    Params = JsonSerializer.SerializeToElement(new { command = "increase", timerId = "timer-0" })
                });
                Assert(increaseTimer.Ok && pomodoro.WorkMinutes == initialWorkMinutes + 5 &&
                       settings.Config.WorkMinutes == initialWorkMinutes + 5,
                    "网页番茄钟增时命令必须更新运行时并持久化主计时器时长。");

                var globalSearch = await bridge.ProcessRequestAsync(new WebBridgeRequest
                {
                    Id = "search-1",
                    Method = "search.global",
                    Params = JsonSerializer.SerializeToElement(new { keyword = "回滚" })
                });
                Assert(globalSearch.Ok, "全局搜索必须能够匹配任务内容。");

                var timeline = await bridge.ProcessRequestAsync(new WebBridgeRequest
                {
                    Id = "history-1",
                    Method = "history.events",
                    Params = JsonSerializer.SerializeToElement(new
                    {
                        startDate = DateTime.Today.AddDays(-2).ToString("yyyy-MM-dd"),
                        endDate = DateTime.Today.ToString("yyyy-MM-dd"),
                        eventFilter = "reopened"
                    })
                });
                Assert(timeline.Ok &&
                       timeline.Result is IEnumerable<TaskActivityBridgeDto> timelineRows &&
                       timelineRows.Any(item => item.EventType == "reopened"),
                    "取消完成必须能在历史时间线中查询到。");

                var focusAnalytics = await bridge.ProcessRequestAsync(new WebBridgeRequest
                {
                    Id = "focus-analytics-day",
                    Method = "focus.analytics",
                    Params = JsonSerializer.SerializeToElement(new
                    {
                        period = "day",
                        anchorDate = DateTime.Today.ToString("yyyy-MM-dd")
                    })
                });
                Assert(focusAnalytics.Ok &&
                       focusAnalytics.Result is FocusAnalyticsBridgeDto focusResult &&
                       focusResult.Cells.Count == 48,
                    "专注统计 Bridge 必须返回 48 个日半小时格子。");

                var appearance = await bridge.ProcessRequestAsync(new WebBridgeRequest
                {
                    Id = "settings-appearance",
                    Method = "settings.save",
                    Params = JsonSerializer.SerializeToElement(new { appearanceMode = "light" })
                });
                Assert(appearance.Ok && settings.Config.AppearanceMode == "light",
                    "日间模式必须通过 C# 设置桥接持久化。");

                var notificationCounts = await bridge.ProcessRequestAsync(new WebBridgeRequest
                {
                    Id = "settings-feishu-counts",
                    Method = "settings.save",
                    Params = JsonSerializer.SerializeToElement(new
                    {
                        showMainWindowOnTimerNotification = true,
                        feishuWorkNotificationCount = -2,
                        feishuRestHalfwayNotificationCount = 3,
                        feishuRestCompletedNotificationCount = 28,
                        feishuTestNotificationCount = 0
                    })
                });
                Assert(notificationCounts.Ok &&
                       settings.Config.ShowMainWindowOnTimerNotification &&
                       settings.Config.FeishuWorkNotificationCount == 0 &&
                       settings.Config.FeishuRestHalfwayNotificationCount == 3 &&
                       settings.Config.FeishuRestCompletedNotificationCount == 20 &&
                       settings.Config.FeishuTestNotificationCount == 1,
                    "飞书通知次数必须由 C# 桥接约束到各自允许范围。");
                var toastDelay = await bridge.ProcessRequestAsync(new WebBridgeRequest
                {
                    Id = "settings-toast-delay",
                    Method = "settings.save",
                    Params = JsonSerializer.SerializeToElement(new { toastActionDelaySeconds = 4.5 })
                });
                Assert(toastDelay.Ok && settings.Config.ToastActionDelaySeconds == 4.5,
                    "Toast 操作延迟必须通过手动保存写入配置。");
                var reloadedNotificationSettings = new SettingsService(bridgeSettingsPath);
                Assert(reloadedNotificationSettings.Config.ShowMainWindowOnTimerNotification &&
                       reloadedNotificationSettings.Config.ToastActionDelaySeconds == 4.5,
                    "重新加载配置时必须保留强提示开关和 Toast 操作延迟。");

                var invalidAppearance = await bridge.ProcessRequestAsync(new WebBridgeRequest
                {
                    Id = "settings-appearance-invalid",
                    Method = "settings.save",
                    Params = JsonSerializer.SerializeToElement(new { appearanceMode = "unknown" })
                });
                Assert(!invalidAppearance.Ok && invalidAppearance.Error?.Code == "invalid_appearance",
                    "未知外观模式必须返回结构化校验错误。");

            }
            finally
            {
                if (File.Exists(bridgeSettingsPath)) File.Delete(bridgeSettingsPath);
            }

            Console.WriteLine("PASS: WebView2 宿主、任务树、父任务手动完成、只追加活动日志、桥接契约、层级限制和删除提升测试全部通过。");
        }
        finally
        {
            if (database is not null)
                await database.CloseAsync();
            if (File.Exists(databasePath))
                File.Delete(databasePath);
        }
    }

    private static void RunPomodoroElapsedTimeTests()
    {
        var now = new DateTimeOffset(2026, 7, 29, 9, 0, 0, TimeSpan.Zero);
        var service = new PomodoroService(1, 2, () => now);
        var workCompleted = 0;
        var restCompleted = 0;
        var halfway = 0;
        service.WorkCompleted += () => workCompleted++;
        service.RestCompleted += () => restCompleted++;
        service.RestHalfway += () => halfway++;

        service.StartWork();
        now = now.AddSeconds(61);
        service.RefreshElapsedTime();
        Assert(workCompleted == 1, "休眠跨过截止时间后应只完成一次工作计时。");
        service.RefreshElapsedTime();
        Assert(workCompleted == 1, "重复同步不应重复发送工作完成通知。");
        service.TogglePause();
        Assert(service.CurrentState == PomodoroService.PomodoroState.CompletionPending,
            "截止瞬间点击暂停不得把等待处理状态错误改成暂停。");

        service.StartRest();
        now = now.AddSeconds(61);
        service.RefreshElapsedTime();
        Assert(halfway == 1, "跨过休息中点时应发送一次过半事件。");
        service.TogglePause();
        now = now.AddMinutes(10);
        service.RefreshElapsedTime();
        Assert(restCompleted == 0, "暂停期间不应消耗剩余时间。");
        service.TogglePause();
        now = now.AddSeconds(60);
        service.RefreshElapsedTime();
        Assert(restCompleted == 1, "恢复后应延续休息状态并按剩余时间完成。");

        var completedCycles = 0;
        var longRunningClock = new DateTimeOffset(2026, 7, 29, 0, 0, 0, TimeSpan.Zero);
        var longRunning = new PomodoroService(1, 1, () => longRunningClock);
        longRunning.WorkCompleted += () => completedCycles++;
        for (var cycle = 0; cycle < 1440; cycle++)
        {
            longRunning.StartWork();
            longRunningClock = longRunningClock.AddMinutes(1);
            longRunning.RefreshElapsedTime();
        }
        Assert(completedCycles == 1440,
            "模拟连续 24 小时的计时循环时，每轮只能产生一次完成事件。");

        var earlyClock = new DateTimeOffset(2026, 7, 29, 14, 0, 0, TimeSpan.Zero);
        var earlyService = new PomodoroService(5, 1, () => earlyClock);
        PomodoroWorkCompletion? earlyCompletion = null;
        earlyService.WorkCompletionRequested += completion => earlyCompletion = completion;
        earlyService.StartWork();
        earlyClock = earlyClock.AddSeconds(20);
        earlyService.RefreshElapsedTime();
        earlyService.TogglePause();
        earlyClock = earlyClock.AddMinutes(5);
        earlyService.TogglePause();
        earlyClock = earlyClock.AddSeconds(10);
        earlyService.CompleteWorkEarly();
        Assert(earlyService.CurrentState == PomodoroService.PomodoroState.CompletionPending &&
               earlyCompletion is { FocusedSeconds: 30, EndReason: "completed_early" },
            "提前完成应进入等待处理，并排除暂停时间后冻结有效专注秒数。");
        Assert(earlyService.CreateSnapshot("timer-0").CompletedFocusSeconds == 30,
            "等待处理快照必须保存提前完成的有效专注秒数。");

        var stoppedCompletionCount = 0;
        var stoppedService = new PomodoroService(5, 1, () => earlyClock);
        stoppedService.WorkCompletionRequested += _ => stoppedCompletionCount++;
        stoppedService.StartWork();
        earlyClock = earlyClock.AddSeconds(15);
        stoppedService.Stop();
        Assert(stoppedCompletionCount == 0 &&
               stoppedService.CurrentState == PomodoroService.PomodoroState.Idle,
            "停止应放弃本轮，不得进入完成记录流程。");
    }

    private static async Task RunPomodoroCompletionCaptureTestsAsync(string testDirectory)
    {
        var databasePath = Path.Combine(
            testDirectory,
            $"pomodoro-completion-{Guid.NewGuid():N}.db");
        var database = new DatabaseService(databasePath);
        try
        {
            await database.InitializeAsync();
            var group = await AddTaskAsync(database, "完成记录任务组");
            var existingChild = await database.AddChildTaskAsync(group, "已有子任务")
                ?? throw new InvalidOperationException("创建完成记录测试子任务失败。");
            var context = new WorkspaceContextService();
            var completionService = new PomodoroCompletionService(database, context);
            context.Update("tasks", "detail", existingChild.Id);

            var destination = await completionService.GetDestinationAsync();
            Assert(destination.TaskId == existingChild.Id && destination.DisplayName == existingChild.Title,
                "番茄钟推进应精确归属于当前打开的任意层级任务。");

            var completedAt = new DateTimeOffset(2026, 8, 24, 10, 30, 0, TimeSpan.FromHours(8));
            var saved = await completionService.SaveAsync(
                new PomodoroWorkCompletion(completedAt, 380, "completed_early"),
                "整理番茄钟完成记录");
            Assert(saved.CreatedRecord.TaskId == existingChild.Id &&
                   saved.CreatedRecord.DurationMinutes == 6 &&
                   saved.CreatedRecord.OriginalDurationMinutes == 6 &&
                   saved.CreatedRecord.Note == "整理番茄钟完成记录",
                "番茄钟应创建当前任务的推进记录，并按规则取整耗时。");
            var savedChild = await database.GetTaskByIdAsync(existingChild.Id);
            Assert(savedChild is { IsCompleted: false, Status: TaskWorkStatus.InProgress },
                "第一条推进应把未完成任务改为进行中，但不得自动完成任务。");

            context.Update("tasks", "create", existingChild.Id);
            var blankResult = await completionService.SaveAsync(
                new PomodoroWorkCompletion(completedAt, 20, "completed"),
                "   ");
            Assert(blankResult.CreatedRecord.TaskId is null &&
                   blankResult.CreatedRecord.Note == string.Empty &&
                   blankResult.CreatedRecord.DurationMinutes == 1 &&
                   (await completionService.GetDestinationAsync()).TaskId is null,
                "新增页面中的空内容仍应保存无所属推进，正数耗时至少记一分钟。");

            var ungrouped = await completionService.SaveAsync(
                new PomodoroWorkCompletion(completedAt, 20, "completed"),
                "新增页面完成的记录");
            Assert(ungrouped.CreatedRecord.TaskId is null &&
                   ungrouped.CreatedRecord.Note == "新增页面完成的记录" &&
                   ungrouped.CreatedRecord.DurationMinutes == 1,
                "新增页面中的非空内容应保存无所属推进，且不创建任务。");

            var taskCount = (await database.GetTaskTreeItemsAsync()).Count;
            Assert(taskCount == 2,
                "推进记录不得伪装成已完成子任务或改变任务树数量。");
            var dayProgress = await database.GetProgressRecordsForDateAsync(completedAt.Date);
            Assert(dayProgress.Count == 3,
                "空内容、非空内容和已归属番茄钟都必须进入当日推进记录。");

            context.Update("tasks", "detail", int.MaxValue);
            Assert((await completionService.GetDestinationAsync()).TaskId is null,
                "失效的任务上下文必须安全回退到无所属。");
        }
        finally
        {
            await database.CloseAsync();
            if (File.Exists(databasePath)) File.Delete(databasePath);
        }
    }

    private static async Task RunLegacyPomodoroProgressMigrationTestsAsync(string testDirectory)
    {
        var databasePath = Path.Combine(
            testDirectory,
            $"legacy-pomodoro-progress-{Guid.NewGuid():N}.db");
        var database = new DatabaseService(databasePath);
        try
        {
            await database.InitializeAsync();
            var group = await AddTaskAsync(database, "旧版任务组");
            _ = await database.AddChildTaskAsync(group, "真实子任务")
                ?? throw new InvalidOperationException("创建迁移测试真实子任务失败。");
            var completedAt = DateTime.Today.AddHours(11);
            await database.CloseAsync();

            var legacyRecordTask = new TaskItem
            {
                ParentId = group.Id,
                Title = "旧版番茄钟自动记录",
                CreatedAt = completedAt,
                LastModifiedAt = completedAt,
                IsCompleted = true,
                Status = TaskWorkStatus.Completed,
                CompletedAt = completedAt,
                CompletionDurationMinutes = 25
            };
            using (var connection = new SQLiteConnection(databasePath))
            {
                connection.Insert(legacyRecordTask);
                var completion = new TaskCompletionRecord
                {
                    TaskId = legacyRecordTask.Id,
                    TopLevelTaskId = group.Id,
                    TitleSnapshot = legacyRecordTask.Title,
                    PathSnapshot = $"{group.Title} > {legacyRecordTask.Title}",
                    TopLevelTitleSnapshot = group.Title,
                    DepthSnapshot = 1,
                    CompletionKind = CompletionKind.LeafTask,
                    TriggerType = CompletionTrigger.Manual,
                    CompletedAt = completedAt,
                    DurationMinutes = 25
                };
                connection.Insert(completion);
                connection.Insert(new TaskActivityRecord
                {
                    TaskId = legacyRecordTask.Id,
                    ActivityType = TaskActivityType.Created,
                    TitleSnapshot = legacyRecordTask.Title,
                    DetailSnapshot = $"由番茄钟完成记录创建为“{group.Title}”的子任务",
                    ParentIdSnapshot = group.Id,
                    DepthSnapshot = 1,
                    OccurredAt = completedAt,
                    CompletionRecordId = completion.Id,
                    SourceKey = $"legacy-pomodoro:{legacyRecordTask.Id}"
                });
            }

            database = new DatabaseService(databasePath);
            await database.InitializeAsync();
            var tasks = await database.GetTaskTreeItemsAsync();
            Assert(tasks.All(task => task.Id != legacyRecordTask.Id) &&
                   tasks.Any(task => task.Title == "真实子任务"),
                "迁移只能移除明确标记的旧版番茄钟任务，不能误删真实子任务。");
            var records = await database.GetProgressRecordsForDateAsync(completedAt.Date);
            Assert(records.Count == 1 &&
                   records[0].LegacyTaskId == legacyRecordTask.Id &&
                   records[0].TaskId == group.Id &&
                   records[0].Note == "旧版番茄钟自动记录" &&
                   records[0].DurationMinutes == 25,
                "可靠识别的旧版番茄钟任务必须无损转换为所属父任务的推进记录。");

            await database.CloseAsync();
            database = new DatabaseService(databasePath);
            await database.InitializeAsync();
            Assert((await database.GetProgressRecordsForDateAsync(completedAt.Date)).Count == 1,
                "旧版番茄钟迁移必须幂等，重复启动不能生成重复推进记录。");
        }
        finally
        {
            await database.CloseAsync();
            if (File.Exists(databasePath)) File.Delete(databasePath);
        }
    }

    private static void RunCurrentTaskVisibilityTests()
    {
        var today = new DateTime(2026, 7, 30);
        var oldCompletedParent = new TaskItem
        {
            Id = 1,
            Title = "历史父任务",
            CreatedAt = today.AddDays(-5),
            CompletedAt = today.AddDays(-3),
            IsCompleted = true
        };
        var currentOpenChild = new TaskItem
        {
            Id = 2,
            ParentId = 1,
            Title = "遗留未完成子任务",
            CreatedAt = today.AddDays(-4),
            IsCompleted = false
        };
        var unrelatedOldCompletedChild = new TaskItem
        {
            Id = 3,
            ParentId = 1,
            Title = "无关历史完成子任务",
            CreatedAt = today.AddDays(-4),
            CompletedAt = today.AddDays(-2),
            IsCompleted = true
        };
        var createdToday = new TaskItem
        {
            Id = 4,
            Title = "今天创建并完成",
            CreatedAt = today.AddHours(8),
            CompletedAt = today.AddHours(9),
            IsCompleted = true
        };
        var completedToday = new TaskItem
        {
            Id = 5,
            Title = "历史创建今天完成",
            CreatedAt = today.AddDays(-2),
            CompletedAt = today.AddHours(10),
            IsCompleted = true
        };
        var oldCompletedRoot = new TaskItem
        {
            Id = 6,
            Title = "纯历史完成任务",
            CreatedAt = today.AddDays(-3),
            CompletedAt = today.AddDays(-2),
            IsCompleted = true
        };

        var visibleIds = CurrentTaskVisibility.GetVisibleTaskIds(
            new[]
            {
                oldCompletedParent,
                currentOpenChild,
                unrelatedOldCompletedChild,
                createdToday,
                completedToday,
                oldCompletedRoot
            },
            today);

        Assert(visibleIds.SetEquals(new[] { 1, 2, 4, 5 }),
            "当前任务规则应保留未完成任务、今日任务及必要祖先，并隐藏无关历史完成分支。");
    }

    private static void RunPomodoroPersistenceTests()
    {
        var testRoot = Path.Combine(Path.GetTempPath(), $"Taskato-Pomodoro-{Guid.NewGuid():N}");
        Directory.CreateDirectory(testRoot);
        var statePath = Path.Combine(testRoot, "pomodoro-state.json");
        try
        {
            var now = new DateTimeOffset(2026, 7, 29, 9, 0, 0, TimeSpan.Zero);
            var store = new PomodoroStateStore(statePath);
            var main = new PomodoroService(1, 5, () => now);
            var additional = new PomodoroService(2, 5, () => now);
            main.StartWork();
            additional.StartRest();
            store.Save(new[]
            {
                main.CreateSnapshot("timer-0"),
                additional.CreateSnapshot("timer-1")
            });

            var saved = store.Load();
            Assert(saved.Count == 2 && saved.Select(snapshot => snapshot.SlotId)
                    .SequenceEqual(new[] { "timer-0", "timer-1" }),
                "主番茄钟和附加番茄钟必须分别保存。");
            Assert(!File.Exists(statePath + ".tmp"), "原子保存完成后不应遗留临时文件。");

            now = now.AddSeconds(20);
            var resumed = new PomodoroService(25, 5, () => now);
            resumed.Restore(saved.Single(snapshot => snapshot.SlotId == "timer-0"));
            var resumedSnapshot = resumed.CreateSnapshot("timer-0");
            Assert(resumed.CurrentState == PomodoroService.PomodoroState.Working &&
                   resumedSnapshot.RemainingSeconds == 40,
                "软件关闭期间仍应按绝对截止时间消耗运行中计时。");

            resumed.TogglePause();
            var pausedSnapshot = resumed.CreateSnapshot("timer-0");
            now = now.AddHours(3);
            var pausedRestore = new PomodoroService(25, 5, () => now);
            pausedRestore.Restore(pausedSnapshot);
            Assert(pausedRestore.CurrentState == PomodoroService.PomodoroState.Paused &&
                   pausedRestore.CreateSnapshot("timer-0").RemainingSeconds == 40,
                "暂停后关闭软件不应继续消耗剩余时间。");

            var expiredClock = new DateTimeOffset(2026, 7, 29, 10, 0, 0, TimeSpan.Zero);
            var expiring = new PomodoroService(1, 5, () => expiredClock);
            expiring.StartWork();
            var runningSnapshot = expiring.CreateSnapshot("timer-0");
            expiredClock = expiredClock.AddSeconds(75);
            var expired = new PomodoroService(25, 5, () => expiredClock);
            var recoveredNotifications = 0;
            expired.WorkCompleted += () => recoveredNotifications++;
            expired.Restore(runningSnapshot);
            Assert(expired.CurrentState == PomodoroService.PomodoroState.CompletionPending &&
                   expired.CompletionElapsedSeconds == 15 && expired.IsRecoveredCompletion &&
                   recoveredNotifications == 1,
                "关闭期间到期后应进入等待处理，并从原截止时间计算已过去时长。");
            expired.RefreshElapsedTime();
            Assert(recoveredNotifications == 1, "恢复后的界面刷新不得重复触发完成提醒。");

            var pendingSnapshot = expired.CreateSnapshot("timer-0");
            expiredClock = expiredClock.AddSeconds(15);
            var pendingRestore = new PomodoroService(25, 5, () => expiredClock);
            var pendingNotifications = 0;
            pendingRestore.WorkCompleted += () => pendingNotifications++;
            pendingRestore.Restore(pendingSnapshot);
            Assert(pendingRestore.CompletionElapsedSeconds == 30 && pendingNotifications == 1,
                "等待处理状态再次启动时应延续超时计时，并只恢复一次提醒。");

            File.WriteAllText(statePath, "{损坏的 JSON");
            Assert(store.Load().Count == 0, "损坏的状态文件必须安全降级为空状态。");
            File.WriteAllText(statePath, "{\"Version\":1,\"Timers\":null}");
            Assert(store.Load().Count == 0, "计时器列表为 null 时必须安全降级为空状态。");
            File.WriteAllText(statePath, "{\"Version\":1,\"Timers\":[null]}");
            Assert(store.Load().Count == 0, "计时器列表中的 null 快照必须被安全过滤。");
        }
        finally
        {
            if (Directory.Exists(testRoot)) Directory.Delete(testRoot, recursive: true);
        }
    }

    private static void RunWindowPlacementTests()
    {
        var screens = new[]
        {
            new WindowPlacementCalculator.WorkArea(0, 0, 1920, 1040, IsPrimary: true),
            new WindowPlacementCalculator.WorkArea(1920, 0, 2560, 1400)
        };

        var secondScreen = WindowPlacementCalculator.Calculate(
            1280, 820, 2200, 120, screens);
        Assert(secondScreen.Left == 2200 && secondScreen.Top == 120,
            "仍与第二屏相交的窗口应恢复到原显示器。");

        var oversized = WindowPlacementCalculator.Calculate(
            6000, 3000, 2100, -300, screens);
        Assert(oversized.Width == 2560 && oversized.Height == 1400 && oversized.Top == 0,
            "超出工作区的窗口尺寸和坐标必须约束在目标屏幕内。");

        var disconnected = WindowPlacementCalculator.Calculate(
            1280, 820, 7000, 100, screens);
        Assert(disconnected.Left == 3200 && disconnected.Top == 100,
            "屏幕拔除后应移动到距离原窗口最近的剩余工作区边缘。");

        var firstLaunch = WindowPlacementCalculator.Calculate(
            1280, 820, null, null, screens);
        Assert(firstLaunch.Left == 320 && firstLaunch.Top == 110,
            "首次启动应在主屏工作区居中。");
    }

    private static void RunWindowStatePersistenceTests()
    {
        Assert(WindowStatePersistence.CaptureIsMaximized(false, WindowState.Maximized),
            "最大化时必须立即记录最大化状态。");
        Assert(!WindowStatePersistence.CaptureIsMaximized(true, WindowState.Normal),
            "还原为普通窗口时必须清除最大化状态。");
        Assert(WindowStatePersistence.CaptureIsMaximized(true, WindowState.Minimized),
            "最小化不得覆盖最近一次最大化状态。");
        Assert(WindowStatePersistence.ResolveActivationState(WindowState.Minimized, true) ==
               WindowState.Maximized,
            "从托盘恢复时应回到最近一次最大化状态。");
        Assert(WindowStatePersistence.ResolveActivationState(WindowState.Minimized, false) ==
               WindowState.Normal,
            "普通窗口最小化后应恢复为普通状态。");
    }

    private static void RunFeishuResponseTests()
    {
        var modernSuccess = FeishuService.InterpretResponse(
            HttpStatusCode.OK,
            "{\"code\":0,\"msg\":\"success\"}");
        Assert(modernSuccess.Sent, "新版飞书 code=0 响应应识别为发送成功。");

        var legacySuccess = FeishuService.InterpretResponse(
            HttpStatusCode.OK,
            "{\"StatusCode\":0,\"StatusMessage\":\"success\"}");
        Assert(legacySuccess.Sent, "旧版飞书 StatusCode=0 响应应识别为发送成功。");

        var rejected = FeishuService.InterpretResponse(
            HttpStatusCode.OK,
            "{\"code\":19024,\"msg\":\"Key Words Not Found\"}");
        Assert(!rejected.Sent && rejected.ErrorCode == "feishu_error" && rejected.Message.Contains("Key Words"),
            "飞书业务错误应保留可操作的错误说明。");

        var httpError = FeishuService.InterpretResponse(HttpStatusCode.NotFound, "{}");
        Assert(!httpError.Sent && httpError.ErrorCode == "http_error",
            "HTTP 错误应与飞书业务错误区分。");
    }

    private static async Task RunFeishuNotificationCountTestsAsync(string testDirectory)
    {
        var settingsPath = Path.Combine(
            testDirectory,
            $"feishu-count-settings-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(settingsPath, "{\"FeishuEnabled\":true}");
            var settings = new SettingsService(settingsPath);
            Assert(
                settings.Config.FeishuEnabled &&
                settings.Config.FeishuWorkNotificationCount == 0 &&
                settings.Config.FeishuRestHalfwayNotificationCount == 3 &&
                settings.Config.FeishuRestCompletedNotificationCount == 6 &&
                settings.Config.FeishuTestNotificationCount == 1,
                "旧配置缺少独立次数时应采用 0、3、6、1 的兼容默认值。");

            settings.Config.FeishuWebhookUrl = "https://open.feishu.cn/open-apis/bot/v2/hook/test";
            var handler = new CountingHttpMessageHandler();
            using var httpClient = new HttpClient(handler);
            var service = new FeishuService(settings, httpClient);

            await service.NotifyAsync("关闭提醒", "不应发送", 0, 0);
            Assert(handler.RequestCount == 0, "通知次数为 0 时不应请求飞书。");

            await service.NotifyAsync("休息过半", "发送三条", 3, 0);
            Assert(handler.RequestCount == 3, "休息过半配置为 3 时应发送三条消息。");

            await service.NotifyAsync("休息完成", "发送六条", 6, 0);
            Assert(handler.RequestCount == 9, "休息完成配置为 6 时应再发送六条消息。");

            var testResult = await service.TestAsync(settings.Config.FeishuWebhookUrl, 4, 0);
            Assert(testResult.Sent && handler.RequestCount == 13 && testResult.Message.Contains("4 条"),
                "手动测试应按独立次数发送并返回实际发送数量。");

            await service.SendRepeatedAsync("上限", "最多二十条", 99, 0);
            Assert(handler.RequestCount == 33,
                "飞书重复发送次数必须限制为最多二十条。");
        }
        finally
        {
            if (File.Exists(settingsPath)) File.Delete(settingsPath);
        }
    }

    private static async Task RunNotificationCoordinatorTestsAsync(
        DatabaseService database,
        string testDirectory)
    {
        var settingsPath = Path.Combine(
            testDirectory,
            $"notification-settings-{Guid.NewGuid():N}.json");
        var statePath = Path.Combine(
            testDirectory,
            $"notification-pomodoro-{Guid.NewGuid():N}.json");
        try
        {
            var now = new DateTimeOffset(2026, 7, 29, 9, 0, 0, TimeSpan.Zero);
            var timerService = new PomodoroService(1, 2, () => now);
            var settings = new SettingsService(settingsPath);
            var viewModel = new MainViewModel(
                database,
                timerService,
                settings,
                new FeishuService(settings));
            var workNotifications = 0;
            var restNotifications = 0;
            var halfwayNotifications = 0;
            var mainWindowActivations = 0;
            Assert(!settings.Config.ShowMainWindowOnTimerNotification,
                "提示时打开主界面必须默认关闭。");
            settings.Config.ShowMainWindowOnTimerNotification = true;
            using var coordinator = new PomodoroNotificationCoordinator(
                viewModel,
                _ => workNotifications++,
                _ => restNotifications++,
                _ => halfwayNotifications++,
                () => mainWindowActivations++);
            var timer = viewModel.AllTimers[0];

            timer.StartWork();
            now = now.AddMinutes(1);
            timer.RefreshElapsedTime();
            timer.RefreshElapsedTime();
            Assert(workNotifications == 1,
                "同一次工作完成只能由通知协调器分发一次。");
            Assert(mainWindowActivations == 1,
                "启用强提示后，工作完成必须同步请求激活主界面。");

            timer.StartRest();
            now = now.AddMinutes(1);
            timer.RefreshElapsedTime();
            Assert(halfwayNotifications == 1,
                "同一次休息过半只能分发一次通知。");
            Assert(mainWindowActivations == 1,
                "休息过半不应激活主界面。");
            now = now.AddMinutes(1);
            timer.RefreshElapsedTime();
            Assert(restNotifications == 1,
                "同一次休息完成只能分发一次通知。");
            Assert(mainWindowActivations == 2,
                "启用强提示后，休息完成必须同步请求激活主界面。");

            coordinator.Dispose();
            timer.StartWork();
            now = now.AddMinutes(1);
            timer.RefreshElapsedTime();
            Assert(workNotifications == 1,
                "通知协调器释放后必须解除全部计时器订阅。");
            Assert(mainWindowActivations == 2,
                "通知协调器释放后不得继续激活主界面。");

            var restoredSettings = new SettingsService(settingsPath);
            restoredSettings.Config.AdditionalTimersWorkMinutes = new List<int> { 2 };
            restoredSettings.Save();
            var stateStore = new PomodoroStateStore(statePath);
            var completedAt = DateTimeOffset.UtcNow.AddSeconds(-30);
            stateStore.Save(new[]
            {
                new PomodoroRuntimeSnapshot
                {
                    SlotId = "timer-0",
                    State = PomodoroService.PomodoroState.CompletionPending,
                    CompletedPhase = PomodoroService.PomodoroState.Working,
                    CompletedAtUtc = completedAt,
                    WorkMinutes = 1,
                    RestMinutes = 2,
                    TotalSeconds = 60
                },
                new PomodoroRuntimeSnapshot
                {
                    SlotId = "timer-1",
                    State = PomodoroService.PomodoroState.CompletionPending,
                    CompletedPhase = PomodoroService.PomodoroState.Resting,
                    CompletedAtUtc = completedAt,
                    WorkMinutes = 2,
                    RestMinutes = 2,
                    TotalSeconds = 120
                }
            });
            var restoredViewModel = new MainViewModel(
                database,
                new PomodoroService(1, 2),
                restoredSettings,
                new FeishuService(restoredSettings),
                stateStore);
            var restoredWorkNotifications = 0;
            var restoredRestNotifications = 0;
            using var restoredCoordinator = new PomodoroNotificationCoordinator(
                restoredViewModel,
                _ => restoredWorkNotifications++,
                _ => restoredRestNotifications++,
                _ => { });
            restoredViewModel.RestorePomodoroState();
            Assert(restoredViewModel.AllTimers.Count == 2 &&
                   restoredViewModel.AllTimers.All(timer =>
                       timer.CurrentState == PomodoroService.PomodoroState.CompletionPending),
                "主番茄钟和附加番茄钟必须分别恢复等待处理状态。");
            Assert(restoredViewModel.AllTimers.All(timer =>
                       timer.StatusText == "等待处理" && timer.TimerDisplay.StartsWith('+')),
                "恢复到期后网页和旧版界面都应显示等待状态及正向超时时间。");
            Assert(restoredWorkNotifications == 1 && restoredRestNotifications == 1,
                "恢复时每个到期计时器必须各分发一次对应提醒。");

            // 统计写入使用异步串行队列；测试结束前等待队列清空，避免数据库关闭时仍有后台写入锁。
            await viewModel.FlushFocusTrackingAsync();
            await restoredViewModel.FlushFocusTrackingAsync();
        }
        finally
        {
            if (File.Exists(settingsPath)) File.Delete(settingsPath);
            if (File.Exists(statePath)) File.Delete(statePath);
        }

        await Task.CompletedTask;
    }

    private static async Task<TaskItem> AddTaskAsync(DatabaseService database, string title)
    {
        var task = new TaskItem
        {
            Title = title,
            CreatedAt = DateTime.Now,
            LastModifiedAt = DateTime.Now
        };
        await database.AddTaskAsync(task);
        return task;
    }

    private static async Task RunFocusAnalyticsTestsAsync(DatabaseService database)
    {
        var day = DateTime.Today.AddDays(-2);
        var slotId = $"analytics-{Guid.NewGuid():N}";
        var workStart = AtLocal(day.AddHours(9));

        await database.StartPomodoroPhaseAsync(
            slotId,
            "统计测试计时器",
            1001,
            "统计测试任务",
            PomodoroFocusPhase.Work,
            workStart,
            hasStartMarker: true);
        await database.PausePomodoroPhaseAsync(slotId, PomodoroFocusPhase.Work, workStart.AddMinutes(10));
        await database.ResumePomodoroPhaseAsync(
            slotId,
            "统计测试计时器",
            1001,
            "统计测试任务",
            PomodoroFocusPhase.Work,
            workStart.AddMinutes(20));
        await database.EndPomodoroPhaseAsync(
            slotId,
            PomodoroFocusPhase.Work,
            workStart.AddMinutes(35),
            "stopped");
        await database.StartPomodoroPhaseAsync(
            slotId,
            "统计测试计时器",
            1001,
            "统计测试任务",
            PomodoroFocusPhase.Rest,
            workStart.AddMinutes(35),
            hasStartMarker: false);
        await database.EndPomodoroPhaseAsync(
            slotId,
            PomodoroFocusPhase.Rest,
            workStart.AddMinutes(40),
            "completed");

        var pendingSlotId = $"analytics-pending-{Guid.NewGuid():N}";
        var pendingStart = AtLocal(day.AddHours(11));
        await database.StartPomodoroPhaseAsync(
            pendingSlotId,
            "统计测试计时器",
            null,
            string.Empty,
            PomodoroFocusPhase.Work,
            pendingStart,
            hasStartMarker: true);
        await database.ReconcilePomodoroPhaseAsync(
            pendingSlotId,
            "统计测试计时器",
            null,
            string.Empty,
            new PomodoroRuntimeSnapshot
            {
                State = PomodoroService.PomodoroState.CompletionPending,
                CompletedPhase = PomodoroService.PomodoroState.Working,
                CompletedAtUtc = pendingStart.AddMinutes(10)
            },
            AtLocal(day.AddHours(12)));

        var result = await database.GetPomodoroAnalyticsAsync("day", day, AtLocal(day.AddDays(1)));
        Assert(result.Cells.Count == 48, "日统计必须拆成 48 个半小时格子。");
        Assert(result.WorkSeconds == 2_100 && result.RestSeconds == 300 && result.TotalSeconds == 2_400,
            "统计必须包含有效工作与休息时长，但不包含暂停和完成等待。");
        Assert(result.WorkStartCount == 2, "启动标记只能统计工作阶段的真实启动。");
        Assert(result.ActiveDays == 1 && result.LongestSessionSeconds == 1_500,
            "统计摘要应按有效运行区间计算活跃天数和最长单次。");
        var workCells = result.Cells.Where(cell => cell.Label is "09:00" or "09:30").ToList();
        Assert(workCells.Sum(cell => cell.WorkSeconds) == 1_500 &&
               workCells.Sum(cell => cell.RestSeconds) == 300 &&
               workCells.Sum(cell => cell.WorkStarts.Count) == 1,
            "暂停后的恢复必须按半小时格切分有效工作时长，并保留一次启动标记。");

        var weekResult = await database.GetPomodoroAnalyticsAsync("week", day, AtLocal(day.AddDays(7)));
        Assert(weekResult.Cells.Count == 7 * 48 && weekResult.TotalSeconds == result.TotalSeconds,
            "周统计必须提供 7 天、每天 48 格，并汇总日统计时长。");
        var monthResult = await database.GetPomodoroAnalyticsAsync("month", day, AtLocal(day.AddDays(31)));
        Assert(monthResult.Cells.Count == DateTime.DaysInMonth(day.Year, day.Month),
            "月统计必须按当月实际天数生成日格子。");

        var boundaryDay = day.AddDays(-5);
        var boundarySlotId = $"analytics-boundary-{Guid.NewGuid():N}";
        var boundaryStart = AtLocal(boundaryDay.AddDays(1).AddMinutes(-10));
        await database.StartPomodoroPhaseAsync(
            boundarySlotId,
            "跨天测试计时器",
            null,
            string.Empty,
            PomodoroFocusPhase.Work,
            boundaryStart,
            hasStartMarker: true);
        await database.EndPomodoroPhaseAsync(
            boundarySlotId,
            PomodoroFocusPhase.Work,
            boundaryStart.AddMinutes(20),
            "completed");
        var beforeMidnight = await database.GetPomodoroAnalyticsAsync("day", boundaryDay, AtLocal(boundaryDay.AddDays(2)));
        var afterMidnight = await database.GetPomodoroAnalyticsAsync("day", boundaryDay.AddDays(1), AtLocal(boundaryDay.AddDays(2)));
        Assert(beforeMidnight.WorkSeconds == 600 && afterMidnight.WorkSeconds == 600,
            "跨午夜运行区间必须准确切分到前后两个日期。");
    }

    private static DateTimeOffset AtLocal(DateTime value)
    {
        var unspecified = DateTime.SpecifyKind(value, DateTimeKind.Unspecified);
        return new DateTimeOffset(unspecified, TimeZoneInfo.Local.GetUtcOffset(unspecified));
    }

    private static async Task RunFocusTimerSlotStabilityTestsAsync(DatabaseService database)
    {
        var settingsPath = Path.Combine(Path.GetTempPath(), "TaskatoTests", $"slot-settings-{Guid.NewGuid():N}.json");
        try
        {
            var settings = new SettingsService(settingsPath);
            settings.Config.AdditionalTimersWorkMinutes = new List<int> { 1, 1 };
            var viewModel = new MainViewModel(
                database,
                new PomodoroService(1, 1),
                settings,
                new FeishuService(settings));
            var removedTimer = viewModel.AllTimers[1];
            var activeTimer = viewModel.AllTimers[2];

            activeTimer.StartWork();
            viewModel.RemoveAdditionalTimerCommand.Execute(removedTimer);
            activeTimer.StopCommand.Execute(null);
            await viewModel.FlushFocusTrackingAsync();

            var result = await database.GetPomodoroAnalyticsAsync(
                "day",
                DateTime.Today,
                DateTimeOffset.Now.AddMinutes(1));
            Assert(result.TotalSeconds == 0,
                "删除附加计时器后，剩余计时器的稳定槽位必须仍能关闭原统计会话。");
        }
        finally
        {
            if (File.Exists(settingsPath)) File.Delete(settingsPath);
        }
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private sealed class CountingHttpMessageHandler : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"code\":0,\"msg\":\"success\"}")
            });
        }
    }
}
