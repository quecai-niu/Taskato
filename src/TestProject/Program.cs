using System;
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

        var testDirectory = Path.Combine(Path.GetTempPath(), "TaskatoTests");
        Directory.CreateDirectory(testDirectory);
        var databasePath = Path.Combine(testDirectory, $"task-tree-{Guid.NewGuid():N}.db");
        DatabaseService? database = null;

        try
        {
            database = new DatabaseService(databasePath);
            await database.InitializeAsync();

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
            await database.UpdateTaskAsync(root);
            var preservedRecord = (await database.GetCompletionRecordsOnAsync(DateTime.Today))
                .First(record => record.TaskId == root.Id);
            Assert(preservedRecord.TitleSnapshot == "发布新版本",
                "历史完成记录必须保留完成时标题快照。");

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
            }
            finally
            {
                if (File.Exists(bridgeSettingsPath)) File.Delete(bridgeSettingsPath);
            }

            Console.WriteLine("PASS: 整窗 XAML、任务树、级联完成、历史记录、桥接契约、层级限制和删除提升测试全部通过。");
        }
        finally
        {
            if (database is not null)
                await database.CloseAsync();
            if (File.Exists(databasePath))
                File.Delete(databasePath);
        }
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
