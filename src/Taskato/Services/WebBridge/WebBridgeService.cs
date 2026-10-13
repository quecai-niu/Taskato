using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Web.WebView2.Core;
using Taskato.Models;
using Taskato.Models.BridgeDtos;
using Taskato.ViewModels;

namespace Taskato.Services.WebBridge
{
    /// <summary>
    /// WebView2 与 C# 业务层之间的唯一入口。网页只能调用这里显式列出的白名单方法，
    /// 从而避免把数据库连接、ViewModel 或任意 CLR 方法暴露给脚本。
    /// </summary>
    public sealed partial class WebBridgeService : IDisposable
    {
        private readonly DatabaseService _database;
        private readonly MainViewModel _taskWorkspace;
        private readonly SettingsService _settings;
        private readonly DataBackupService _dataBackup;
        private readonly WorkspaceContextService _workspaceContext;
        private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web)
        {
            Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
        };
        private CoreWebView2? _webView;
        private System.Windows.Media.MediaPlayer? _previewMediaPlayer;
        private bool _disposed;

        public event Action<string>? WindowCommandRequested;

        public WebBridgeService(
            DatabaseService database,
            MainViewModel taskWorkspace,
            SettingsService settings,
            WorkspaceContextService? workspaceContext = null)
        {
            _database = database;
            _taskWorkspace = taskWorkspace;
            _settings = settings;
            _dataBackup = new DataBackupService(database, settings);
            _workspaceContext = workspaceContext ?? new WorkspaceContextService();

            _database.OnDataChanged += HandleDataChanged;
            _taskWorkspace.FocusTrackingChanged += HandleFocusTrackingChanged;
            _taskWorkspace.AllTimers.CollectionChanged += Timers_CollectionChanged;
            foreach (var timer in _taskWorkspace.AllTimers) RegisterTimer(timer);
        }

        public void Attach(CoreWebView2 webView)
        {
            if (_webView == webView) return;
            if (_webView is not null)
                _webView.WebMessageReceived -= WebView_WebMessageReceived;

            _webView = webView;
            _webView.WebMessageReceived += WebView_WebMessageReceived;
        }

        public void NotifyWindowStateChanged(bool isMaximized) =>
            PostEvent("window.stateChanged", new { isMaximized });

        public void NavigateTo(string page) => PostEvent("app.navigate", new { page });

        private async void WebView_WebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            try
            {
                var request = JsonSerializer.Deserialize<WebBridgeRequest>(e.WebMessageAsJson, _jsonOptions);
                PostResponse(await ProcessRequestAsync(request));
            }
            catch (JsonException)
            {
                PostResponse(WebBridgeResponse.Failure(string.Empty, "invalid_json", "请求 JSON 无法解析。"));
            }
        }

        /// <summary>
        /// 执行一条已反序列化请求。公开此边界用于契约测试；返回值与 WebView2 实际响应完全一致。
        /// </summary>
        public async Task<WebBridgeResponse> ProcessRequestAsync(WebBridgeRequest? request)
        {
            try
            {
                if (request is null || string.IsNullOrWhiteSpace(request.Id) || string.IsNullOrWhiteSpace(request.Method))
                    throw new BridgeRequestException("invalid_request", "请求必须包含 id 和 method。");
                return WebBridgeResponse.Success(request.Id, await DispatchAsync(request));
            }
            catch (BridgeRequestException exception)
            {
                return WebBridgeResponse.Failure(request?.Id ?? string.Empty, exception.Code, exception.Message);
            }
            catch (Exception exception)
            {
                // 不把堆栈发送到网页，避免泄露本地路径；调试器仍可观察原始异常。
                Debug.WriteLine(exception);
                DiagnosticsService.Log("WebBridge 请求失败", exception, request?.Method);
                return WebBridgeResponse.Failure(request?.Id ?? string.Empty, "internal_error", "操作失败，请稍后重试。");
            }
        }

        private async Task<object?> DispatchAsync(WebBridgeRequest request)
        {
            return request.Method switch
            {
                "app.ready" => new
                {
                    protocolVersion = 1,
                    appVersion = System.Reflection.Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "1.0.0"
                },
                "workspace.context.set" => UpdateWorkspaceContext(request.Params),
                "tasks.list" => await GetTasksAsync(),
                "tasks.options" => await GetTaskOptionsAsync(),
                "tasks.parentCandidates" => await GetParentCandidatesAsync(request.Params),
                "tasks.add" => await AddTaskAsync(request.Params),
                "tasks.update" => await UpdateTaskAsync(request.Params),
                "tasks.toggle" => await ToggleTaskAsync(request.Params),
                "tasks.completeDialog" => await CompleteTaskWithNativeDialogAsync(request.Params),
                "tasks.delete" => await DeleteTaskAsync(request.Params),
                "tasks.images.list" => await ListTaskImagesAsync(request.Params),
                "tasks.images.add" => await AddTaskImageAsync(request.Params),
                "tasks.images.get" => await GetTaskImageAsync(request.Params),
                "tasks.images.delete" => await DeleteTaskImageAsync(request.Params),
                "tasks.move" => await MoveTaskAsync(request.Params),
                "tasks.reorder" => await ReorderTasksAsync(request.Params),
                "search.global" => await GlobalSearchAsync(request.Params),
                "summary.get" => await GetSummaryAsync(request.Params),
                "summary.candidates" => await GetSummaryCandidatesAsync(),
                "summary.backfill" => await BackfillTaskAsync(request.Params),
                "summary.copy" => await CopySummaryAsync(request.Params),
                "summary.export" => await ExportSummaryAsync(request.Params),
                "progress.list" => await GetProgressRecordsAsync(request.Params),
                "progress.create" => await CreateProgressRecordAsync(request.Params),
                "progress.update" => await UpdateProgressRecordAsync(request.Params),
                "progress.delete" => await DeleteProgressRecordAsync(request.Params),
                "progress.deleteBatch" => await DeleteProgressRecordsAsync(request.Params),
                "history.search" => await SearchHistoryAsync(request.Params),
                "history.events" => await GetHistoryEventsAsync(request.Params),
                "history.export" => await ExportHistoryAsync(request.Params),
                "focus.analytics" => await GetFocusAnalyticsAsync(request.Params),
                "settings.get" => GetSettings(),
                "settings.save" => SaveSettings(request.Params),
                "settings.chooseSound" => ChooseCustomSound(),
                "settings.previewSound" => PreviewSound(request.Params),
                "settings.testFeishu" => await TestFeishuAsync(request.Params),
                "settings.openFeishuGuide" => OpenFeishuGuide(),
                "app.openDataFolder" => OpenDataFolder(),
                "app.openLogsFolder" => OpenLogsFolder(),
                "app.backupData" => await BackupDataAsync(),
                "app.restoreData" => await StageDataRestoreAsync(),
                "pomodoro.list" => GetTimers(),
                "pomodoro.timeline" => await GetPomodoroTimelineAsync(request.Params),
                "pomodoro.command" => ExecuteTimerCommand(request.Params),
                "window.minimize" or "window.maximize" or "window.close" or "window.drag" or "window.state" => ExecuteWindowCommand(request.Method),
                _ => throw new BridgeRequestException("method_not_found", $"不支持的方法：{request.Method}")
            };
        }

        private async Task<object> GetTasksAsync()
        {
            var tasks = PrepareTaskTree(await _database.GetTaskTreeItemsAsync());
            var visibleTaskIds = CurrentTaskVisibility.GetVisibleTaskIds(tasks, DateTime.Today);
            var visibleTasks = tasks.Where(task => visibleTaskIds.Contains(task.Id)).ToList();
            var selectedTaskId = _taskWorkspace.SelectedTask?.Id;
            if (!selectedTaskId.HasValue || !visibleTaskIds.Contains(selectedTaskId.Value))
                selectedTaskId = visibleTasks.FirstOrDefault()?.Id;

            return new
            {
                tasks = visibleTasks.Select(ToTaskDto).ToList(),
                // 左侧树只展示当前任务；详情区仍需完整直属子任务，二者不能共用过滤后的集合。
                detailTasks = tasks.Select(ToTaskDto).ToList(),
                selectedTaskId,
                maximumDepth = DatabaseService.MaximumTaskDepth
            };
        }

        private async Task<object> GetTaskOptionsAsync()
        {
            var tasks = PrepareTaskTree(await _database.GetTaskTreeItemsAsync());
            return tasks.Select(ToTaskDto).ToList();
        }

        private object UpdateWorkspaceContext(JsonElement parameters)
        {
            var activePage = RequiredString(parameters, "activePage");
            var taskViewMode = OptionalString(parameters, "taskViewMode") ?? "detail";
            var selectedTaskId = OptionalInt(parameters, "selectedTaskId");
            var progressTargetTaskId = OptionalInt(parameters, "progressTargetTaskId");
            _workspaceContext.Update(
                activePage,
                taskViewMode,
                selectedTaskId,
                progressTargetTaskId);
            return _workspaceContext.Current;
        }

        private async Task<object> AddTaskAsync(JsonElement parameters)
        {
            var title = RequiredString(parameters, "title").Trim();
            if (title.Length > 5000) throw new BridgeRequestException("invalid_title", "任务内容不能超过 5000 个字符。");
            var priority = Math.Clamp(OptionalInt(parameters, "priority") ?? 0, 0, 3);
            var parentId = OptionalInt(parameters, "parentId");

            TaskItem? created;
            if (parentId.HasValue)
            {
                var parent = await _database.GetTaskByIdAsync(parentId.Value)
                    ?? throw new BridgeRequestException("parent_not_found", "父任务不存在。");
                created = await _database.AddChildTaskAsync(parent, title, priority);
                if (created is null)
                    throw new BridgeRequestException("maximum_depth", "无法新增子任务，请检查五层限制。");
            }
            else
            {
                created = new TaskItem
                {
                    Title = title,
                    Priority = priority,
                    CreatedAt = DateTime.Now,
                    LastModifiedAt = DateTime.Now
                };
                await _database.AddTaskAsync(created);
            }

            return ToTaskDto(created);
        }

        private async Task<object> GetParentCandidatesAsync(JsonElement parameters)
        {
            var candidates = await _database.GetValidParentCandidatesAsync(RequiredInt(parameters, "taskId"));
            return candidates.Select(ToTaskDto).ToList();
        }

        private async Task<object> UpdateTaskAsync(JsonElement parameters)
        {
            var task = await FindTaskAsync(parameters);
            if (parameters.TryGetProperty("title", out var titleElement))
            {
                var title = titleElement.ValueKind == JsonValueKind.String
                    ? titleElement.GetString()
                    : null;
                if (string.IsNullOrWhiteSpace(title) || title.Length > 5000)
                    throw new BridgeRequestException("invalid_title", "任务内容不能为空且不能超过 5000 个字符。");
                task.Title = title;
            }
            if (parameters.TryGetProperty("priority", out var priorityElement))
            {
                if (priorityElement.ValueKind != JsonValueKind.Number ||
                    !priorityElement.TryGetInt32(out var priority))
                    throw new BridgeRequestException("invalid_params", "priority 必须是整数。");
                task.Priority = Math.Clamp(priority, 0, 3);
            }
            await _database.UpdateTaskAsync(task);
            return ToTaskDto(task);
        }

        private async Task<object> GlobalSearchAsync(JsonElement parameters)
        {
            var keyword = RequiredString(parameters, "keyword").Trim();
            if (keyword.Length < 2)
                throw new BridgeRequestException("keyword_too_short", "请输入至少两个字符。");

            var allTasks = PrepareTaskTree(await _database.GetTaskTreeItemsAsync());
            var existingTaskIds = allTasks.Select(task => task.Id).ToHashSet();
            var tasks = allTasks
                .Where(task => task.Title.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                .Take(12)
                .Select(ToTaskDto)
                .ToList();
            var activities = (await _database.SearchActivityRecordsAsync(
                    new DateTime(2000, 1, 1),
                    DateTime.Today,
                    keyword,
                    "all",
                    sortDescending: true))
                .Take(12)
                .Select(record => ToActivityDto(record, existingTaskIds))
                .ToList();
            return new { tasks, activities };
        }

        private async Task<object> ToggleTaskAsync(JsonElement parameters)
        {
            var task = await FindTaskAsync(parameters);
            var desiredCompleted = OptionalBool(parameters, "completed") ?? !task.IsCompleted;
            if (desiredCompleted == task.IsCompleted)
                return new { completed = task.IsCompleted };
            if (!desiredCompleted)
            {
                await _database.ReopenTaskWithHierarchyRulesAsync(task);
                return new { completed = false };
            }

            var duration = OptionalInt(parameters, "durationMinutes");
            if (duration is <= 0)
                throw new BridgeRequestException("invalid_duration", "耗时必须是大于 0 的分钟数。");
            var result = await _database.CompleteTaskWithHierarchyValidationAsync(
                task, DateTime.Now, duration, CompletionTrigger.Manual);
            if (!result.Succeeded)
                throw new BridgeRequestException("parent_has_open_leaves", "父任务不能直接完成，请先完成所有叶子任务。");
            return new { completed = true, completedTaskIds = result.CompletedTaskIds };
        }

        private async Task<object> CompleteTaskWithNativeDialogAsync(JsonElement parameters)
        {
            var task = await FindTaskAsync(parameters);
            if (task.IsCompleted) return new { completed = true, cancelled = false };
            var hasChildren = (await _database.GetTaskTreeItemsAsync())
                .Any(candidate => candidate.ParentId == task.Id);
            if (!Taskato.Views.TaskCompletionDialog.TryShowForCompletion(
                    System.Windows.Application.Current.MainWindow,
                    task,
                    out var durationMinutes,
                    hasChildren))
                return new { completed = false, cancelled = true };

            var result = await _database.CompleteTaskWithHierarchyValidationAsync(
                task, DateTime.Now, durationMinutes, CompletionTrigger.Manual);
            if (!result.Succeeded)
                throw new BridgeRequestException("parent_has_open_leaves", "父任务不能直接完成，请先完成所有叶子任务。");
            return new { completed = true, cancelled = false, completedTaskIds = result.CompletedTaskIds };
        }

        private async Task<object> DeleteTaskAsync(JsonElement parameters)
        {
            var task = await FindTaskAsync(parameters);
            var modeText = OptionalString(parameters, "mode") ?? "deleteBranch";
            var mode = modeText.Equals("promoteChildren", StringComparison.OrdinalIgnoreCase)
                ? TaskDeleteMode.PromoteChildren
                : TaskDeleteMode.DeleteBranch;
            var affected = await _database.DeleteTaskAsync(task, mode);
            return new { affected };
        }

        private async Task<object> MoveTaskAsync(JsonElement parameters)
        {
            var taskId = RequiredInt(parameters, "taskId");
            var result = await _database.MoveTaskAsync(taskId, OptionalInt(parameters, "parentId"));
            if (!result.Succeeded)
                throw new BridgeRequestException("invalid_move", result.ErrorMessage);
            return new { moved = true };
        }

        private async Task<object> ReorderTasksAsync(JsonElement parameters)
        {
            if (parameters.ValueKind != JsonValueKind.Object ||
                !parameters.TryGetProperty("taskIds", out var taskIdsElement) ||
                taskIdsElement.ValueKind != JsonValueKind.Array)
                throw new BridgeRequestException("invalid_params", "taskIds 必须是任务编号数组。");

            var taskIds = new List<int>();
            foreach (var taskIdElement in taskIdsElement.EnumerateArray())
            {
                if (taskIdElement.ValueKind != JsonValueKind.Number ||
                    !taskIdElement.TryGetInt32(out var taskId) ||
                    taskId <= 0)
                    throw new BridgeRequestException("invalid_params", "taskIds 必须只包含有效任务编号。");
                taskIds.Add(taskId);
            }

            var result = await _database.ReorderSiblingTasksAsync(taskIds);
            if (!result.Succeeded)
                throw new BridgeRequestException("invalid_reorder", result.ErrorMessage);
            return new { reordered = true };
        }

        private object GetTimers() => new
        {
            activeId = TimerId(_taskWorkspace.CurrentActiveTimer),
            multipleEnabled = _settings.Config.EnableMultiplePomodoros,
            timers = _taskWorkspace.AllTimers.Select(ToTimerDto).ToList()
        };

        private object ExecuteTimerCommand(JsonElement parameters)
        {
            var command = RequiredString(parameters, "command");
            var timer = FindTimer(OptionalString(parameters, "timerId"));
            switch (command)
            {
                case "select": _taskWorkspace.CurrentActiveTimer = timer; break;
                case "start": ExecuteTimerCommand(timer.StartWorkCommand, "当前状态不能开始计时。"); break;
                case "pause": ExecuteTimerCommand(timer.PauseCommand, "当前状态不能暂停或继续。"); break;
                case "stop": ExecuteTimerCommand(timer.StopCommand, "当前状态不能停止计时。"); break;
                case "rest": ExecuteTimerCommand(timer.EarlyRestCommand, "只有工作计时中才能提前完成。"); break;
                case "increase": ExecuteTimerCommand(timer.IncreaseTimeCommand, "只能在计时器空闲时调整时长。"); break;
                case "decrease": ExecuteTimerCommand(timer.DecreaseTimeCommand, "只能在计时器空闲时调整时长。"); break;
                case "adjustRemaining":
                    var remainingSeconds = RequiredInt(parameters, "remainingSeconds");
                    if (!timer.CanAdjustRemaining)
                        throw new BridgeRequestException("invalid_timer_state", "只能在专注或休息倒计时运行时调整剩余时间。");
                    if (!timer.IsValidRemainingAdjustment(remainingSeconds))
                        throw new BridgeRequestException("invalid_duration", "剩余时间必须是允许的分钟档位或当前阶段的精确上限。");
                    if (!timer.TryAdjustRemainingSeconds(remainingSeconds))
                        throw new BridgeRequestException("invalid_timer_state", "计时阶段已经结束，无法继续调整剩余时间。");
                    break;
                case "add":
                    if (!_settings.Config.EnableMultiplePomodoros)
                        throw new BridgeRequestException("feature_disabled", "请先在设置中开启多组番茄钟。");
                    _taskWorkspace.AddAdditionalTimerCommand.Execute(null);
                    break;
                case "remove":
                    if (_taskWorkspace.AllTimers.IndexOf(timer) <= 0)
                        throw new BridgeRequestException("invalid_timer", "主番茄钟不能移除。");
                    _taskWorkspace.RemoveAdditionalTimerCommand.Execute(timer);
                    break;
                default: throw new BridgeRequestException("invalid_command", $"不支持的番茄钟命令：{command}");
            }
            PostEvent("pomodoro.changed", GetTimers());
            return GetTimers();
        }

        private object ExecuteWindowCommand(string method)
        {
            WindowCommandRequested?.Invoke(method["window.".Length..]);
            return new { accepted = true };
        }

        private async Task<TaskItem> FindTaskAsync(JsonElement parameters)
        {
            var task = await _database.GetTaskByIdAsync(RequiredInt(parameters, "taskId"));
            return task ?? throw new BridgeRequestException("task_not_found", "任务不存在。");
        }

        private TaskBridgeDto ToTaskDto(TaskItem task) => new(
            task.Id, task.Title, task.ParentId, task.Depth, task.IsCompleted,
            task.Status switch
            {
                TaskWorkStatus.Completed => "completed",
                TaskWorkStatus.InProgress => "inProgress",
                _ => "notStarted"
            },
            task.Priority, task.OrderIndex,
            task.HasChildren, task.CanAddChild, task.CompletedLeafCount, task.TotalLeafCount,
            task.CreatedAt, task.CompletedAt, task.CompletionDurationMinutes);

        private TaskActivityBridgeDto ToActivityDto(
            TaskActivityRecord record,
            IReadOnlySet<int>? existingTaskIds = null)
        {
            var (label, tone) = record.ActivityType switch
            {
                TaskActivityType.Created => ("创建任务", "neutral"),
                TaskActivityType.Updated => ("编辑任务", "neutral"),
                TaskActivityType.Moved => ("移动任务", "neutral"),
                TaskActivityType.Completed => ("完成任务", "success"),
                TaskActivityType.Reopened => ("取消完成", "warning"),
                TaskActivityType.Deleted => ("删除任务", "warning"),
                TaskActivityType.MigrationBoundary => ("日志边界", "neutral"),
                _ => ("任务活动", "neutral")
            };
            return new TaskActivityBridgeDto(
                $"activity-{record.Id}",
                record.TaskId,
                TaskItem.FirstLine(record.TitleSnapshot),
                record.ActivityType.ToString().ToLowerInvariant(),
                label,
                record.OccurredAt,
                record.DetailSnapshot,
                tone,
                record.TaskId.HasValue && (existingTaskIds?.Contains(record.TaskId.Value) ?? true),
                record.IsLegacyBackfill);
        }

        private static List<TaskItem> PrepareTaskTree(List<TaskItem> tasks)
        {
            var lookup = tasks.ToDictionary(task => task.Id);
            foreach (var task in tasks) task.Children.Clear();
            foreach (var task in tasks)
            {
                if (task.ParentId.HasValue && lookup.TryGetValue(task.ParentId.Value, out var parent))
                    parent.Children.Add(task);
            }
            foreach (var root in tasks.Where(task => !task.ParentId.HasValue || !lookup.ContainsKey(task.ParentId.Value)))
                CalculateTaskState(root, 0);
            return tasks;
        }

        private static (int Completed, int Total) CalculateTaskState(TaskItem task, int depth)
        {
            task.Depth = depth;
            if (task.Children.Count == 0)
            {
                task.CompletedLeafCount = task.IsCompleted ? 1 : 0;
                task.TotalLeafCount = 1;
            }
            else
            {
                var states = task.Children.Select(child => CalculateTaskState(child, depth + 1)).ToList();
                task.CompletedLeafCount = states.Sum(state => state.Completed);
                task.TotalLeafCount = states.Sum(state => state.Total);
            }
            task.NotifyTreeStateChanged();
            return (task.CompletedLeafCount, task.TotalLeafCount);
        }

        private TimerBridgeDto ToTimerDto(PomodoroTimerViewModel timer) => new(
            TimerId(timer), timer.TimerName, timer.TimerDisplay, timer.StatusText, timer.Progress,
            timer.WorkMinutes, timer.RestMinutes, timer.TimerPhase, timer.RemainingSeconds,
            timer.MinimumAdjustableSeconds, timer.MaximumAdjustableSeconds,
            timer.AdjustmentStepSeconds, timer.CanAdjustRemaining, timer.IsStarted,
            timer.StartWorkCommand.CanExecute(null), timer.PauseCommand.CanExecute(null),
            timer.StopCommand.CanExecute(null), timer.EarlyRestCommand.CanExecute(null));

        private static void ExecuteTimerCommand(System.Windows.Input.ICommand command, string errorMessage)
        {
            if (!command.CanExecute(null))
                throw new BridgeRequestException("invalid_timer_state", errorMessage);
            command.Execute(null);
        }

        private string TimerId(PomodoroTimerViewModel? timer) => timer is null
            ? string.Empty
            : $"timer-{_taskWorkspace.AllTimers.IndexOf(timer)}";

        private PomodoroTimerViewModel FindTimer(string? timerId)
        {
            if (timerId?.StartsWith("timer-") == true && int.TryParse(timerId[6..], out var index) &&
                index >= 0 && index < _taskWorkspace.AllTimers.Count)
                return _taskWorkspace.AllTimers[index];
            return _taskWorkspace.CurrentActiveTimer ?? _taskWorkspace.AllTimers.First();
        }

        private void HandleDataChanged() => PostEvent("tasks.changed", new { });
        private void HandleFocusTrackingChanged() => PostEvent("focus.changed", new { });

        private void Timers_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.OldItems is not null)
                foreach (PomodoroTimerViewModel timer in e.OldItems) timer.PropertyChanged -= Timer_PropertyChanged;
            if (e.NewItems is not null)
                foreach (PomodoroTimerViewModel timer in e.NewItems) RegisterTimer(timer);
            PostEvent("pomodoro.changed", GetTimers());
        }

        private void RegisterTimer(PomodoroTimerViewModel timer)
        {
            timer.PropertyChanged -= Timer_PropertyChanged;
            timer.PropertyChanged += Timer_PropertyChanged;
        }

        private void Timer_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(PomodoroTimerViewModel.TimerDisplay) or
                nameof(PomodoroTimerViewModel.StatusText) or nameof(PomodoroTimerViewModel.Progress))
                PostEvent("pomodoro.changed", GetTimers());
        }

        private void PostResponse(WebBridgeResponse response) => PostJson(response);
        private void PostEvent(string eventName, object? payload) => PostJson(new WebBridgeEvent(eventName, payload));

        private void PostJson(object value)
        {
            if (_webView is null || _disposed) return;
            var json = JsonSerializer.Serialize(value, _jsonOptions);
            var dispatcher = System.Windows.Application.Current.Dispatcher;
            void Post()
            {
                try { _webView?.PostWebMessageAsJson(json); }
                catch (InvalidOperationException) { /* 窗口关闭期间丢弃迟到事件。 */ }
            }
            if (dispatcher.CheckAccess()) Post();
            else _ = dispatcher.InvokeAsync(Post);
        }

        private static string RequiredString(JsonElement element, string name) =>
            OptionalString(element, name) is { Length: > 0 } value
                ? value
                : throw new BridgeRequestException("invalid_params", $"缺少参数：{name}");
        private static int RequiredInt(JsonElement element, string name) =>
            OptionalInt(element, name) ?? throw new BridgeRequestException("invalid_params", $"缺少参数：{name}");
        private static string? OptionalString(JsonElement element, string name) =>
            element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
        private static int? OptionalInt(JsonElement element, string name) =>
            element.ValueKind == JsonValueKind.Object &&
            element.TryGetProperty(name, out var value) &&
            value.ValueKind == JsonValueKind.Number &&
            value.TryGetInt32(out var result)
                ? result
                : null;
        private static bool? OptionalBool(JsonElement element, string name) =>
            element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False ? value.GetBoolean() : null;
        private static double? OptionalDouble(JsonElement element, string name) =>
            element.ValueKind == JsonValueKind.Object &&
            element.TryGetProperty(name, out var value) &&
            value.ValueKind == JsonValueKind.Number &&
            value.TryGetDouble(out var result)
                ? result
                : null;
        private static DateTime? OptionalDate(JsonElement element, string name) =>
            DateTime.TryParse(OptionalString(element, name), out var value) ? value.Date : null;
        private static TimeSpan? OptionalTime(JsonElement element, string name) =>
            TimeSpan.TryParse(OptionalString(element, name), out var value) ? value : null;
        private static int ClampedOptionalInt(JsonElement element, string name, int fallback, int minimum, int maximum) =>
            Math.Clamp(OptionalInt(element, name) ?? fallback, minimum, maximum);
        private static string ValidatedColor(JsonElement element, string name, string fallback)
        {
            var value = OptionalString(element, name);
            if (string.IsNullOrWhiteSpace(value)) return fallback;
            try { _ = System.Windows.Media.ColorConverter.ConvertFromString(value); return value; }
            catch { throw new BridgeRequestException("invalid_color", $"{name} 不是有效颜色。"); }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _database.OnDataChanged -= HandleDataChanged;
            _taskWorkspace.FocusTrackingChanged -= HandleFocusTrackingChanged;
            if (_webView is not null) _webView.WebMessageReceived -= WebView_WebMessageReceived;
            _taskWorkspace.AllTimers.CollectionChanged -= Timers_CollectionChanged;
            foreach (var timer in _taskWorkspace.AllTimers) timer.PropertyChanged -= Timer_PropertyChanged;
            _previewMediaPlayer?.Close();
            _previewMediaPlayer = null;
        }
    }
}
