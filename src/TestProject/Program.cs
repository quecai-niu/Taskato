using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Taskato.Models;
using Taskato.Models.BridgeDtos;
using Taskato.Services;
using Taskato.Services.WebBridge;
using Taskato.ViewModels;
using Taskato.Views;

internal static class Program
{
    [STAThread]
    private static async Task Main()
    {
        RunWorkspaceXamlSmokeTest();
        RunPomodoroElapsedTimeTests();
        RunWindowPlacementTests();

        var testDirectory = Path.Combine(Path.GetTempPath(), "TaskatoTests");
        Directory.CreateDirectory(testDirectory);
        var databasePath = Path.Combine(testDirectory, $"task-tree-{Guid.NewGuid():N}.db");
        DatabaseService? database = null;

        try
        {
            database = new DatabaseService(databasePath);
            await database.InitializeAsync();
            await RunNotificationCoordinatorTestsAsync(database, testDirectory);

            var root = await AddTaskAsync(database, "发布新版本");
            var phase = await database.AddChildTaskAsync(root, "登录模块")
                ?? throw new InvalidOperationException("创建阶段任务失败。");
            var leafOne = await database.AddChildTaskAsync(phase, "完成登录页面")
                ?? throw new InvalidOperationException("创建第一个叶子任务失败。");
            var leafTwo = await database.AddChildTaskAsync(phase, "调整按钮")
                ?? throw new InvalidOperationException("创建第二个叶子任务失败。");

            var firstResult = await database.CompleteTaskWithCascadeAsync(
                leafOne,
                DateTime.Today.AddHours(9),
                25,
                CompletionTrigger.Manual);
            Assert(firstResult.Succeeded, "第一个叶子任务应完成成功。");
            Assert(firstResult.CompletedTaskIds.Count == 1, "部分完成时不应提前完成父任务。");

            var secondResult = await database.CompleteTaskWithCascadeAsync(
                leafTwo,
                DateTime.Today.AddHours(10),
                15,
                CompletionTrigger.Manual);
            Assert(secondResult.Succeeded, "最后一个叶子任务应完成成功。");
            Assert(secondResult.CompletedTaskIds.Count == 3, "最后一个叶子任务应级联完成两级父任务。");

            var firstCompletionRecords = await database.GetCompletionRecordsOnAsync(DateTime.Today);
            Assert(firstCompletionRecords.Count == 4, "每日总结应包含两个叶子和两个父级成果。");
            Assert(firstCompletionRecords.Count(record => record.CompletionKind == CompletionKind.LeafTask) == 2,
                "叶子完成记录数量错误。");
            Assert(firstCompletionRecords.Count(record => record.CompletionKind != CompletionKind.LeafTask) == 2,
                "父级成果记录数量错误。");
            Assert(firstCompletionRecords.Sum(record => record.DurationMinutes ?? 0) == 40,
                "父级成果不应重复累计耗时。");

            await database.ReopenTaskWithAncestorsAsync(leafTwo);
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
            await database.CompleteTaskWithCascadeAsync(
                leafTwo,
                DateTime.Today.AddHours(11),
                10,
                CompletionTrigger.Manual);
            Assert((await database.GetCompletionRecordsOnAsync(DateTime.Today)).Count == 7,
                "再次完成应新增叶子与父级完成记录。");

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

            var blockedParentCompletion = await database.CompleteTaskWithCascadeAsync(
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
            await database.CompleteTaskWithCascadeAsync(
                finishedChild,
                DateTime.Today.AddHours(13),
                null,
                CompletionTrigger.Manual);
            await database.DeleteTaskAsync(removedPendingChild, TaskDeleteMode.DeleteBranch);
            var afterStructureDelete = await database.GetTaskTreeItemsAsync();
            Assert(afterStructureDelete.Single(task => task.Id == structureParent.Id).IsCompleted,
                "删除最后一个未完成子任务后，父任务应按剩余子任务重新计算为完成。");

            var incompleteMovedTask = await AddTaskAsync(database, "移动进来的未完成任务");
            var moveIntoCompletedParent = await database.MoveTaskAsync(
                incompleteMovedTask.Id,
                structureParent.Id);
            Assert(moveIntoCompletedParent.Succeeded, "合法的层级移动应成功。");
            var afterIncompleteMove = await database.GetTaskTreeItemsAsync();
            Assert(!afterIncompleteMove.Single(task => task.Id == structureParent.Id).IsCompleted,
                "移入未完成子任务后，已完成父任务应重新打开。");

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
            Assert(activityTypes.Contains(TaskActivityType.Completed), "活动日志必须记录完成及父级成果。");
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
            var activityCountAfterRepeatedMigration = (await database.SearchActivityRecordsAsync(
                new DateTime(2000, 1, 1),
                DateTime.Today,
                string.Empty,
                "all",
                sortDescending: true)).Count;
            Assert(activityCountAfterRepeatedMigration == activityCountAfterMigration,
                "重复初始化不得生成重复活动记录。");

            var backupSettingsPath = Path.Combine(testDirectory, $"backup-settings-{Guid.NewGuid():N}.json");
            var backupArchivePath = Path.Combine(testDirectory, $"backup-{Guid.NewGuid():N}.taskato-backup");
            var restoreDirectory = Path.Combine(testDirectory, $"restore-{Guid.NewGuid():N}");
            try
            {
                var backupSettings = new SettingsService(backupSettingsPath);
                backupSettings.Config.WorkMinutes = 42;
                backupSettings.Save();
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
            }
            finally
            {
                if (File.Exists(backupSettingsPath)) File.Delete(backupSettingsPath);
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
                using var bridge = new WebBridgeService(database, mainViewModel, settings);

                var ready = await bridge.ProcessRequestAsync(new WebBridgeRequest
                {
                    Id = "ready-1",
                    Method = "app.ready"
                });
                Assert(ready.Ok && ready.Id == "ready-1", "桥接握手必须回传请求 id。");

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

                var globalSearch = await bridge.ProcessRequestAsync(new WebBridgeRequest
                {
                    Id = "search-1",
                    Method = "search.global",
                    Params = JsonSerializer.SerializeToElement(new { keyword = "回滚" })
                });
                Assert(globalSearch.Ok, "全局搜索必须能够匹配任务笔记。");

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

                var appearance = await bridge.ProcessRequestAsync(new WebBridgeRequest
                {
                    Id = "settings-appearance",
                    Method = "settings.save",
                    Params = JsonSerializer.SerializeToElement(new { appearanceMode = "light" })
                });
                Assert(appearance.Ok && settings.Config.AppearanceMode == "light",
                    "日间模式必须通过 C# 设置桥接持久化。");

                var invalidAppearance = await bridge.ProcessRequestAsync(new WebBridgeRequest
                {
                    Id = "settings-appearance-invalid",
                    Method = "settings.save",
                    Params = JsonSerializer.SerializeToElement(new { appearanceMode = "unknown" })
                });
                Assert(!invalidAppearance.Ok && invalidAppearance.Error?.Code == "invalid_appearance",
                    "未知外观模式必须返回结构化校验错误。");

                string? requestedWindowCommand = null;
                bridge.WindowCommandRequested += command => requestedWindowCommand = command;
                var legacySwitch = await bridge.ProcessRequestAsync(new WebBridgeRequest
                {
                    Id = "window-legacy",
                    Method = "window.useLegacy"
                });
                Assert(legacySwitch.Ok && requestedWindowCommand == "useLegacy",
                    "设置页的立即切换操作必须到达原生窗口宿主。");
            }
            finally
            {
                if (File.Exists(bridgeSettingsPath)) File.Delete(bridgeSettingsPath);
            }

            Console.WriteLine("PASS: 整窗 XAML、任务树、级联完成、只追加活动日志、桥接契约、层级限制和删除提升测试全部通过。");
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

    private static async Task RunNotificationCoordinatorTestsAsync(
        DatabaseService database,
        string testDirectory)
    {
        var settingsPath = Path.Combine(
            testDirectory,
            $"notification-settings-{Guid.NewGuid():N}.json");
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
            using var coordinator = new PomodoroNotificationCoordinator(
                viewModel,
                _ => workNotifications++,
                _ => restNotifications++,
                _ => halfwayNotifications++);
            var timer = viewModel.AllTimers[0];

            timer.StartWork();
            now = now.AddMinutes(1);
            timer.RefreshElapsedTime();
            timer.RefreshElapsedTime();
            Assert(workNotifications == 1,
                "同一次工作完成只能由通知协调器分发一次。");

            timer.StartRest();
            now = now.AddMinutes(1);
            timer.RefreshElapsedTime();
            Assert(halfwayNotifications == 1,
                "同一次休息过半只能分发一次通知。");
            now = now.AddMinutes(1);
            timer.RefreshElapsedTime();
            Assert(restNotifications == 1,
                "同一次休息完成只能分发一次通知。");

            coordinator.Dispose();
            timer.StartWork();
            now = now.AddMinutes(1);
            timer.RefreshElapsedTime();
            Assert(workNotifications == 1,
                "通知协调器释放后必须解除全部计时器订阅。");
        }
        finally
        {
            if (File.Exists(settingsPath)) File.Delete(settingsPath);
        }

        await Task.CompletedTask;
    }

    private static void RunWorkspaceXamlSmokeTest()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var app = new Taskato.App();
                app.InitializeComponent();
                var window = new MainWindow
                {
                    IsAppShuttingDown = true,
                    ShowActivated = false,
                    ShowInTaskbar = false,
                    Opacity = 0
                };
                window.Show();
                window.UpdateLayout();
                window.Close();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null)
            throw new InvalidOperationException("整窗 XAML 实例化失败。", failure);
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

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
