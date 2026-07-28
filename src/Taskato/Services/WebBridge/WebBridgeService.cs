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
    public sealed class WebBridgeService : IDisposable
    {
        private readonly DatabaseService _database;
        private readonly MainViewModel _taskWorkspace;
        private readonly SettingsService _settings;
        private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web)
        {
            Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
        };
        private CoreWebView2? _webView;
        private System.Windows.Media.MediaPlayer? _previewMediaPlayer;
        private bool _disposed;

        public event Action<string>? WindowCommandRequested;

        public WebBridgeService(DatabaseService database, MainViewModel taskWorkspace, SettingsService settings)
        {
            _database = database;
            _taskWorkspace = taskWorkspace;
            _settings = settings;

            _database.OnDataChanged += HandleDataChanged;
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
                "tasks.list" => await GetTasksAsync(),
                "tasks.parentCandidates" => await GetParentCandidatesAsync(request.Params),
                "tasks.add" => await AddTaskAsync(request.Params),
                "tasks.update" => await UpdateTaskAsync(request.Params),
                "tasks.toggle" => await ToggleTaskAsync(request.Params),
                "tasks.completeDialog" => await CompleteTaskWithNativeDialogAsync(request.Params),
                "tasks.delete" => await DeleteTaskAsync(request.Params),
                "tasks.move" => await MoveTaskAsync(request.Params),
                "summary.get" => await GetSummaryAsync(request.Params),
                "summary.candidates" => await GetSummaryCandidatesAsync(),
                "summary.backfill" => await BackfillTaskAsync(request.Params),
                "summary.copy" => await CopySummaryAsync(request.Params),
                "summary.export" => await ExportSummaryAsync(request.Params),
                "history.search" => await SearchHistoryAsync(request.Params),
                "history.events" => await GetHistoryEventsAsync(request.Params),
                "history.export" => await ExportHistoryAsync(request.Params),
                "settings.get" => GetSettings(),
                "settings.save" => SaveSettings(request.Params),
                "settings.chooseSound" => ChooseCustomSound(),
                "settings.previewSound" => PreviewSound(request.Params),
                "app.openDataFolder" => OpenDataFolder(),
                "pomodoro.list" => GetTimers(),
                "pomodoro.command" => ExecuteTimerCommand(request.Params),
                "window.minimize" or "window.maximize" or "window.close" or "window.drag" => ExecuteWindowCommand(request.Method),
                _ => throw new BridgeRequestException("method_not_found", $"不支持的方法：{request.Method}")
            };
        }

        private async Task<object> GetTasksAsync()
        {
            var tasks = PrepareTaskTree(await _database.GetTaskTreeItemsAsync());
            return new
            {
                tasks = tasks.Select(ToTaskDto).ToList(),
                selectedTaskId = _taskWorkspace.SelectedTask?.Id,
                maximumDepth = DatabaseService.MaximumTaskDepth
            };
        }

        private async Task<object> AddTaskAsync(JsonElement parameters)
        {
            var title = RequiredString(parameters, "title").Trim();
            if (title.Length > 500) throw new BridgeRequestException("invalid_title", "任务标题不能超过 500 个字符。");
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
                    ? titleElement.GetString()?.Trim()
                    : null;
                if (string.IsNullOrWhiteSpace(title) || title.Length > 500)
                    throw new BridgeRequestException("invalid_title", "任务标题不能为空且不能超过 500 个字符。");
                task.Title = title;
            }
            if (parameters.TryGetProperty("priority", out var priorityElement))
            {
                if (!priorityElement.TryGetInt32(out var priority))
                    throw new BridgeRequestException("invalid_params", "priority 必须是整数。");
                task.Priority = Math.Clamp(priority, 0, 3);
            }
            await _database.UpdateTaskAsync(task);
            return ToTaskDto(task);
        }

        private async Task<object> ToggleTaskAsync(JsonElement parameters)
        {
            var task = await FindTaskAsync(parameters);
            var desiredCompleted = OptionalBool(parameters, "completed") ?? !task.IsCompleted;
            if (desiredCompleted == task.IsCompleted)
                return new { completed = task.IsCompleted };
            if (!desiredCompleted)
            {
                await _database.ReopenTaskWithAncestorsAsync(task);
                return new { completed = false };
            }

            var duration = OptionalInt(parameters, "durationMinutes");
            if (duration is <= 0)
                throw new BridgeRequestException("invalid_duration", "耗时必须是大于 0 的分钟数。");
            var result = await _database.CompleteTaskWithCascadeAsync(
                task, DateTime.Now, duration, CompletionTrigger.Manual);
            if (!result.Succeeded)
                throw new BridgeRequestException("parent_has_open_leaves", "父任务不能直接完成，请先完成所有叶子任务。");
            return new { completed = true, completedTaskIds = result.CompletedTaskIds };
        }

        private async Task<object> CompleteTaskWithNativeDialogAsync(JsonElement parameters)
        {
            var task = await FindTaskAsync(parameters);
            if (task.IsCompleted) return new { completed = true, cancelled = false };
            if (!Taskato.Views.TaskCompletionDialog.TryShowForCompletion(
                    System.Windows.Application.Current.MainWindow,
                    task,
                    out var durationMinutes))
                return new { completed = false, cancelled = true };

            var result = await _database.CompleteTaskWithCascadeAsync(
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

        private async Task<object> GetSummaryAsync(JsonElement parameters)
        {
            var date = OptionalDate(parameters, "date") ?? DateTime.Today;
            var viewModel = new DailySummaryViewModel(_database) { CurrentDate = date };
            await viewModel.LoadSummaryAsync();
            return new
            {
                date = date.ToString("yyyy-MM-dd"),
                viewModel.SummaryLine,
                viewModel.CompletedCount,
                viewModel.LeafCompletedCount,
                viewModel.ParentAchievementCount,
                viewModel.HighPriorityCompleted,
                viewModel.TotalDurationMinutes,
                viewModel.TotalDurationDisplay,
                groups = viewModel.CompletionGroups.Select(group => new
                {
                    group.TopLevelTaskId,
                    group.Title,
                    records = group.Records.Select(record => new
                    {
                        record.TaskId,
                        record.TitleSnapshot,
                        record.DepthSnapshot,
                        record.PrioritySnapshot,
                        record.KindDisplay,
                        record.CompletedAt,
                        record.DurationDisplay,
                        exists = record.CurrentTask is not null
                    })
                })
            };
        }

        private async Task<object> GetSummaryCandidatesAsync()
        {
            var candidateIds = (await _database.GetUncompletedLeafTasksAsync()).Select(task => task.Id).ToHashSet();
            var tasks = PrepareTaskTree(await _database.GetTaskTreeItemsAsync());
            return tasks.Where(task => candidateIds.Contains(task.Id)).Select(ToTaskDto).ToList();
        }

        private async Task<object> BackfillTaskAsync(JsonElement parameters)
        {
            var task = await FindTaskAsync(parameters);
            if (task.IsCompleted)
                throw new BridgeRequestException("already_completed", "任务已经完成，无需重复补充。");
            var date = OptionalDate(parameters, "date") ?? DateTime.Today;
            if (date.Date > DateTime.Today)
                throw new BridgeRequestException("future_date", "不能为未来日期补充任务。");
            var completedAt = date.Date.Add(OptionalTime(parameters, "time") ?? DateTime.Now.TimeOfDay);
            var duration = OptionalInt(parameters, "durationMinutes");
            if (duration is <= 0)
                throw new BridgeRequestException("invalid_duration", "耗时必须是大于 0 的分钟数。");
            var result = await _database.CompleteTaskWithCascadeAsync(
                task, completedAt, duration, CompletionTrigger.Backfill);
            if (!result.Succeeded)
                throw new BridgeRequestException("invalid_task", "只能补充未完成的叶子任务。");
            return new { completedTaskIds = result.CompletedTaskIds };
        }

        private async Task<object> CopySummaryAsync(JsonElement parameters)
        {
            var text = await BuildSummaryTextAsync(OptionalDate(parameters, "date") ?? DateTime.Today);
            System.Windows.Clipboard.SetText(text);
            return new { copied = true };
        }

        private async Task<object> ExportSummaryAsync(JsonElement parameters)
        {
            var date = OptionalDate(parameters, "date") ?? DateTime.Today;
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "文本文件|*.txt",
                DefaultExt = ".txt",
                FileName = $"Taskato_每日总结_{date:yyyyMMdd}"
            };
            if (dialog.ShowDialog() != true) return new { exported = false };
            await File.WriteAllTextAsync(dialog.FileName, await BuildSummaryTextAsync(date), new UTF8Encoding(true));
            return new { exported = true };
        }

        private async Task<string> BuildSummaryTextAsync(DateTime date)
        {
            var summary = (JsonElement)JsonSerializer.SerializeToElement(await GetSummaryAsync(
                JsonSerializer.SerializeToElement(new { date = date.ToString("yyyy-MM-dd") })), _jsonOptions);
            var builder = new StringBuilder();
            builder.AppendLine($"{date:yyyy-MM-dd} 每日总结");
            builder.AppendLine(summary.GetProperty("summaryLine").GetString());
            foreach (var group in summary.GetProperty("groups").EnumerateArray())
            {
                builder.AppendLine();
                builder.AppendLine($"[{group.GetProperty("title").GetString()}]");
                foreach (var record in group.GetProperty("records").EnumerateArray())
                    builder.AppendLine($"- {record.GetProperty("titleSnapshot").GetString()}（{record.GetProperty("kindDisplay").GetString()}）");
            }
            return builder.ToString();
        }

        private async Task<object> SearchHistoryAsync(JsonElement parameters)
        {
            var start = OptionalDate(parameters, "startDate") ?? DateTime.Today.AddMonths(-1);
            var end = OptionalDate(parameters, "endDate") ?? DateTime.Today;
            var tasks = await _database.SearchTasksAsync(
                start, end,
                OptionalString(parameters, "keyword") ?? string.Empty,
                OptionalInt(parameters, "statusFilter") ?? 0,
                OptionalString(parameters, "sortField") ?? "CreatedAt",
                OptionalBool(parameters, "sortDesc") ?? true,
                OptionalBool(parameters, "priorityFirst") ?? false);
            return tasks.Select(ToTaskDto).ToList();
        }

        private async Task<object> GetHistoryEventsAsync(JsonElement parameters)
        {
            var start = OptionalDate(parameters, "startDate") ?? DateTime.Today.AddMonths(-1);
            var end = OptionalDate(parameters, "endDate") ?? DateTime.Today;
            if (end < start)
                throw new BridgeRequestException("invalid_date_range", "结束日期不能早于开始日期。");

            var keyword = OptionalString(parameters, "keyword") ?? string.Empty;
            var eventFilter = OptionalString(parameters, "eventFilter") ?? "all";
            var dayEnd = end.Date.AddDays(1);
            var tasks = await _database.GetTaskTreeItemsAsync();
            var records = await _database.GetCompletionRecordsBetweenAsync(start, end);
            var allRecordsUntilEnd = await _database.GetCompletionRecordsBetweenAsync(new DateTime(2000, 1, 1), end);
            var events = new List<TaskActivityBridgeDto>();

            events.AddRange(tasks
                .Where(task => task.CreatedAt >= start.Date && task.CreatedAt < dayEnd)
                .Select(task => new TaskActivityBridgeDto(
                    $"created-{task.Id}", task.Id, task.Title, "created", "创建任务",
                    task.CreatedAt, task.ParentId.HasValue ? "创建为子任务" : "创建为顶级任务",
                    "neutral", true)));

            events.AddRange(records.Select(record => new TaskActivityBridgeDto(
                $"completed-{record.Id}", record.TaskId, record.TitleSnapshot, "completed",
                record.CompletionKind switch
                {
                    CompletionKind.TopLevelAchievement => "大任务完成",
                    CompletionKind.ParentMilestone => "父级阶段完成",
                    _ => "完成任务"
                },
                record.CompletedAt,
                record.CurrentTask is null
                    ? "任务随后已删除；完成事实仍保留"
                    : record.DurationMinutes is > 0
                        ? $"记录专注 {record.DurationMinutes} 分钟"
                        : record.TriggerType == CompletionTrigger.AutomaticCascade
                            ? "由全部子任务完成自动达成"
                            : "完成记录",
                "success", record.CurrentTask is not null)));

            // 完成记录不可变；任务当前已恢复为未完成时，用最后修改时间投影一条取消完成事件。
            // 它只进入历史时间线，不参与每日总结的有效完成统计。
            var completedTaskIds = allRecordsUntilEnd.Select(record => record.TaskId).ToHashSet();
            events.AddRange(tasks
                .Where(task => !task.IsCompleted && completedTaskIds.Contains(task.Id))
                .Where(task => task.LastModifiedAt >= start.Date && task.LastModifiedAt < dayEnd)
                .Select(task => new TaskActivityBridgeDto(
                    $"reopened-{task.Id}-{task.LastModifiedAt.Ticks}", task.Id, task.Title,
                    "reopened", "取消完成", task.LastModifiedAt,
                    "保留在历史中，不计入每日总结", "warning", true)));

            var filteredEvents = events
                .Where(item => eventFilter == "all" || item.EventType == eventFilter)
                .Where(item => string.IsNullOrWhiteSpace(keyword) ||
                    item.Title.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                    item.Detail.Contains(keyword, StringComparison.OrdinalIgnoreCase));
            return (OptionalBool(parameters, "sortDesc") ?? true
                    ? filteredEvents.OrderByDescending(item => item.OccurredAt)
                    : filteredEvents.OrderBy(item => item.OccurredAt))
                .ToList();
        }

        private async Task<object> ExportHistoryAsync(JsonElement parameters)
        {
            var events = (IEnumerable<TaskActivityBridgeDto>)await GetHistoryEventsAsync(parameters);
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "CSV 文件|*.csv",
                DefaultExt = ".csv",
                FileName = $"Taskato_历史任务_{DateTime.Now:yyyyMMdd_HHmmss}"
            };
            if (dialog.ShowDialog() != true) return new { exported = false };

            var csv = new StringBuilder("发生时间,事件,任务标题,说明\r\n");
            foreach (var item in events)
                csv.AppendLine($"\"{item.OccurredAt:yyyy-MM-dd HH:mm:ss}\",\"{item.EventLabel}\",\"{item.Title.Replace("\"", "\"\"")}\",\"{item.Detail.Replace("\"", "\"\"")}\"");
            await File.WriteAllTextAsync(dialog.FileName, csv.ToString(), new UTF8Encoding(true));
            return new { exported = true };
        }

        private object GetSettings()
        {
            var c = _settings.Config;
            return new
            {
                c.WorkMinutes, c.RestMinutes, c.ThemePrimaryColor, c.ThemeSecondaryColor,
                c.AutoStartNextPomodoro, c.EnableToastTimer, c.ToastActionDelaySeconds,
                c.EnableMultiplePomodoros, c.NotificationSoundChoice, c.CustomSoundPath,
                c.FeishuEnabled, c.FeishuWebhookUrl, c.FeishuNotifyOnWork, c.FeishuNotifyOnRest,
                c.FeishuRepeatEnabled, c.FeishuRestHalfwayEnabled, c.AutoShowDailySummary,
                c.UseLegacyWorkspace,
                autoStartEnabled = AutoStartService.IsAutoStartEnabled()
            };
        }

        private object SaveSettings(JsonElement parameters)
        {
            var c = _settings.Config;
            c.WorkMinutes = ClampedOptionalInt(parameters, "workMinutes", c.WorkMinutes, 1, 240);
            c.RestMinutes = ClampedOptionalInt(parameters, "restMinutes", c.RestMinutes, 1, 120);
            c.AutoStartNextPomodoro = OptionalBool(parameters, "autoStartNextPomodoro") ?? c.AutoStartNextPomodoro;
            c.EnableToastTimer = OptionalBool(parameters, "enableToastTimer") ?? c.EnableToastTimer;
            var toastDelay = OptionalDouble(parameters, "toastActionDelaySeconds") ?? c.ToastActionDelaySeconds;
            c.ToastActionDelaySeconds = Math.Round(Math.Clamp(toastDelay, 0, 5) * 2) / 2;
            c.EnableMultiplePomodoros = OptionalBool(parameters, "enableMultiplePomodoros") ?? c.EnableMultiplePomodoros;
            c.NotificationSoundChoice = ClampedOptionalInt(parameters, "notificationSoundChoice", c.NotificationSoundChoice, 0, 5);
            c.CustomSoundPath = OptionalString(parameters, "customSoundPath") ?? c.CustomSoundPath;
            c.FeishuEnabled = OptionalBool(parameters, "feishuEnabled") ?? c.FeishuEnabled;
            c.FeishuWebhookUrl = OptionalString(parameters, "feishuWebhookUrl") ?? c.FeishuWebhookUrl;
            c.FeishuNotifyOnWork = OptionalBool(parameters, "feishuNotifyOnWork") ?? c.FeishuNotifyOnWork;
            c.FeishuNotifyOnRest = OptionalBool(parameters, "feishuNotifyOnRest") ?? c.FeishuNotifyOnRest;
            c.FeishuRepeatEnabled = OptionalBool(parameters, "feishuRepeatEnabled") ?? c.FeishuRepeatEnabled;
            c.FeishuRestHalfwayEnabled = OptionalBool(parameters, "feishuRestHalfwayEnabled") ?? c.FeishuRestHalfwayEnabled;
            c.AutoShowDailySummary = OptionalBool(parameters, "autoShowDailySummary") ?? c.AutoShowDailySummary;
            c.UseLegacyWorkspace = OptionalBool(parameters, "useLegacyWorkspace") ?? c.UseLegacyWorkspace;
            c.ThemePrimaryColor = ValidatedColor(parameters, "themePrimaryColor", c.ThemePrimaryColor);
            c.ThemeSecondaryColor = ValidatedColor(parameters, "themeSecondaryColor", c.ThemeSecondaryColor);
            if (OptionalBool(parameters, "autoStartEnabled") is { } autoStartEnabled)
                AutoStartService.SetAutoStart(autoStartEnabled);
            _settings.Save();
            _taskWorkspace.ApplySettingsFromConfig();
            ApplyNativeTheme(c.ThemePrimaryColor, c.ThemeSecondaryColor);
            PostEvent("settings.changed", GetSettings());
            return GetSettings();
        }

        private static void ApplyNativeTheme(string primaryValue, string secondaryValue)
        {
            var primary = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(primaryValue);
            var secondary = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(secondaryValue);
            var resources = System.Windows.Application.Current.Resources;
            resources["ThemePrimaryColor"] = primary;
            resources["ThemeSecondaryColor"] = secondary;
            resources["ThemePrimaryBrush"] = new System.Windows.Media.SolidColorBrush(primary);
            resources["ThemeGradientBrush"] = new System.Windows.Media.LinearGradientBrush(primary, secondary, 135);
        }

        private object ChooseCustomSound()
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "音频文件|*.wav;*.mp3;*.wma|所有文件|*.*",
                Title = "选择自定义音效"
            };
            if (dialog.ShowDialog() != true) return new { selected = false, path = _settings.Config.CustomSoundPath };
            _settings.Config.CustomSoundPath = dialog.FileName;
            _settings.Config.NotificationSoundChoice = 5;
            _settings.Save();
            return new { selected = true, path = dialog.FileName };
        }

        private object PreviewSound(JsonElement parameters)
        {
            var soundChoice = ClampedOptionalInt(parameters, "notificationSoundChoice", _settings.Config.NotificationSoundChoice, 0, 5);
            var customSoundPath = OptionalString(parameters, "customSoundPath") ?? _settings.Config.CustomSoundPath;
            var paths = new Dictionary<int, string>
            {
                [1] = @"C:\Windows\Media\Windows Notify.wav",
                [2] = @"C:\Windows\Media\Windows Ding.wav",
                [3] = @"C:\Windows\Media\Windows Background.wav",
                [4] = @"C:\Windows\Media\chimes.wav",
                [5] = customSoundPath
            };
            if (!paths.TryGetValue(soundChoice, out var path) || !File.Exists(path))
                return new { played = false };

            if (path.EndsWith(".wav", StringComparison.OrdinalIgnoreCase))
                new System.Media.SoundPlayer(path).Play();
            else
            {
                _previewMediaPlayer?.Close();
                _previewMediaPlayer = new System.Windows.Media.MediaPlayer();
                _previewMediaPlayer.MediaEnded += (_, _) =>
                {
                    _previewMediaPlayer?.Close();
                    _previewMediaPlayer = null;
                };
                _previewMediaPlayer.Open(new Uri(path, UriKind.Absolute));
                _previewMediaPlayer.Play();
            }
            return new { played = true };
        }

        private object OpenDataFolder()
        {
            var path = Path.GetDirectoryName(_database.DatabasePath)!;
            Process.Start(new ProcessStartInfo("explorer.exe", path) { UseShellExecute = true });
            return new { opened = true };
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
                case "rest": ExecuteTimerCommand(timer.EarlyRestCommand, "只有工作计时中才能提前休息。"); break;
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
            task.Id, task.Title, task.ParentId, task.Depth, task.IsCompleted, task.Priority,
            task.HasChildren, task.CanAddChild, task.CompletedLeafCount, task.TotalLeafCount,
            task.CreatedAt, task.CompletedAt, task.CompletionDurationMinutes);

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
            timer.WorkMinutes, timer.RestMinutes, timer.IsStarted,
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
            element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.TryGetInt32(out var result) ? result : null;
        private static bool? OptionalBool(JsonElement element, string name) =>
            element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False ? value.GetBoolean() : null;
        private static double? OptionalDouble(JsonElement element, string name) =>
            element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.TryGetDouble(out var result) ? result : null;
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
            if (_webView is not null) _webView.WebMessageReceived -= WebView_WebMessageReceived;
            _taskWorkspace.AllTimers.CollectionChanged -= Timers_CollectionChanged;
            foreach (var timer in _taskWorkspace.AllTimers) timer.PropertyChanged -= Timer_PropertyChanged;
            _previewMediaPlayer?.Close();
            _previewMediaPlayer = null;
        }
    }
}
