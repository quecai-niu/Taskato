using System.IO;
using System.Text;
using System.Text.Json;
using Taskato.Models;
using Taskato.Models.BridgeDtos;
using Taskato.ViewModels;

namespace Taskato.Services.WebBridge
{
    /// <summary>
    /// 每日总结与活动历史协议。集中维护统计口径和导出格式，避免与任务写操作互相耦合。
    /// </summary>
    public sealed partial class WebBridgeService
    {
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
            var result = await _database.CompleteTaskWithHierarchyValidationAsync(
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
            var records = await _database.SearchActivityRecordsAsync(
                start, end, keyword, eventFilter, OptionalBool(parameters, "sortDesc") ?? true);
            var existingTaskIds = (await _database.GetTaskTreeItemsAsync()).Select(task => task.Id).ToHashSet();
            return records.Select(record => ToActivityDto(record, existingTaskIds)).ToList();
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
    }
}
