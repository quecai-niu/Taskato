using System.Text.Json;
using Taskato.Models;
using Taskato.Models.BridgeDtos;

namespace Taskato.Services.WebBridge
{
    public sealed partial class WebBridgeService
    {
        private async Task<object> GetProgressRecordsAsync(JsonElement parameters)
        {
            var taskId = RequiredInt(parameters, "taskId");
            if (await _database.GetTaskByIdAsync(taskId) is null)
                throw new BridgeRequestException("task_not_found", "任务不存在。");

            var ownRecords = await _database.GetProgressRecordsForTaskAsync(taskId, false);
            var allRecords = await _database.GetProgressRecordsForTaskAsync(taskId, true);
            var includeDescendants = OptionalBool(parameters, "includeDescendants") ?? false;
            var tasks = await _database.GetTaskTreeItemsAsync();
            var existingTaskIds = tasks.Select(task => task.Id).ToHashSet();
            var selectedRecords = includeDescendants ? allRecords : ownRecords;

            return new
            {
                records = selectedRecords.Select(record => ToProgressDto(record, existingTaskIds)).ToList(),
                ownCount = ownRecords.Count,
                allCount = allRecords.Count,
                ownDurationMinutes = ownRecords.Sum(record => record.DurationMinutes),
                allDurationMinutes = allRecords.Sum(record => record.DurationMinutes),
                lastProgressAt = allRecords.Count == 0
                    ? (DateTime?)null
                    : allRecords.Max(record => record.CompletedAt)
            };
        }

        private async Task<object> CreateProgressRecordAsync(JsonElement parameters)
        {
            var taskId = OptionalInt(parameters, "taskId");
            var completedAt = ParseProgressDateTime(parameters, "completedAt", DateTime.Now);
            if (completedAt > DateTime.Now.AddMinutes(1))
                throw new BridgeRequestException("future_progress", "推进时间不能晚于当前时间。");
            var durationMinutes = OptionalInt(parameters, "durationMinutes") ?? 0;
            if (durationMinutes is < 1 or > 1440)
                throw new BridgeRequestException("invalid_duration", "推进时长应为 1–1440 分钟。");

            var record = await _database.AddProgressRecordAsync(
                taskId,
                OptionalString(parameters, "note"),
                completedAt.AddMinutes(-durationMinutes),
                completedAt,
                durationMinutes,
                durationMinutes,
                "manual",
                TaskProgressSource.Manual);
            var existingTaskIds = (await _database.GetTaskTreeItemsAsync())
                .Select(task => task.Id)
                .ToHashSet();
            return ToProgressDto(record, existingTaskIds);
        }

        private async Task<object> UpdateProgressRecordAsync(JsonElement parameters)
        {
            var recordId = RequiredInt(parameters, "recordId");
            var record = await _database.GetProgressRecordByIdAsync(recordId)
                ?? throw new BridgeRequestException("progress_not_found", "推进记录不存在。");
            var durationMinutes = OptionalInt(parameters, "durationMinutes") ?? record.DurationMinutes;
            if (durationMinutes is < 1 or > 1440)
                throw new BridgeRequestException("invalid_duration", "推进时长应为 1–1440 分钟。");
            var note = parameters.ValueKind == JsonValueKind.Object &&
                       parameters.TryGetProperty("note", out _)
                ? OptionalString(parameters, "note") ?? string.Empty
                : record.Note;
            var taskId = parameters.ValueKind == JsonValueKind.Object &&
                         parameters.TryGetProperty("taskId", out _)
                ? OptionalInt(parameters, "taskId")
                : record.TaskId;

            var updated = await _database.UpdateProgressRecordAsync(
                recordId,
                taskId,
                note,
                durationMinutes);
            var existingTaskIds = (await _database.GetTaskTreeItemsAsync())
                .Select(task => task.Id)
                .ToHashSet();
            return ToProgressDto(updated, existingTaskIds);
        }

        private async Task<object> DeleteProgressRecordAsync(JsonElement parameters)
        {
            var deleted = await _database.DeleteProgressRecordAsync(
                RequiredInt(parameters, "recordId"));
            if (!deleted)
                throw new BridgeRequestException("progress_not_found", "推进记录不存在。");
            return new { deleted = true };
        }

        private async Task<object> DeleteProgressRecordsAsync(JsonElement parameters)
        {
            var recordIds = RequiredProgressRecordIds(parameters);
            try
            {
                var deletedCount = await _database.DeleteProgressRecordsAsync(recordIds);
                return new { deletedCount };
            }
            catch (InvalidOperationException exception)
            {
                throw new BridgeRequestException("progress_not_found", exception.Message);
            }
        }

        private static IReadOnlyList<int> RequiredProgressRecordIds(JsonElement parameters)
        {
            if (parameters.ValueKind != JsonValueKind.Object ||
                !parameters.TryGetProperty("recordIds", out var value) ||
                value.ValueKind != JsonValueKind.Array)
            {
                throw new BridgeRequestException("invalid_params", "缺少参数：recordIds");
            }

            var recordIds = new List<int>();
            foreach (var item in value.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Number ||
                    !item.TryGetInt32(out var recordId) ||
                    recordId <= 0)
                {
                    throw new BridgeRequestException(
                        "invalid_params",
                        "recordIds 必须是正整数数组。");
                }
                recordIds.Add(recordId);
            }

            var distinctRecordIds = recordIds.Distinct().ToArray();
            if (distinctRecordIds.Length == 0)
                throw new BridgeRequestException("invalid_params", "至少选择一条推进记录。");
            return distinctRecordIds;
        }

        private static DateTime ParseProgressDateTime(
            JsonElement parameters,
            string name,
            DateTime fallback)
        {
            var value = OptionalString(parameters, name);
            return DateTime.TryParse(value, out var parsed) ? parsed : fallback;
        }

        private static TaskProgressBridgeDto ToProgressDto(
            TaskProgressRecord record,
            IReadOnlySet<int> existingTaskIds)
        {
            var reasonDisplay = record.EndReason switch
            {
                "completed_early" => "提前完成",
                "completed" => "自然结束",
                "manual" => "手动补录",
                "legacy_migration" => "历史迁移",
                _ => "已记录"
            };
            return new TaskProgressBridgeDto(
                record.Id,
                record.TaskId,
                string.IsNullOrWhiteSpace(record.TaskTitleSnapshot)
                    ? "无所属"
                    : record.TaskTitleSnapshot,
                string.IsNullOrWhiteSpace(record.TaskPathSnapshot)
                    ? "无所属"
                    : record.TaskPathSnapshot,
                record.Note,
                record.StartedAt,
                record.CompletedAt,
                record.CreatedAt,
                record.OriginalDurationMinutes,
                record.DurationMinutes,
                record.OriginalDurationMinutes > 0 &&
                record.OriginalDurationMinutes != record.DurationMinutes,
                record.EndReason,
                reasonDisplay,
                record.Source.ToString().ToLowerInvariant(),
                record.TaskId.HasValue && existingTaskIds.Contains(record.TaskId.Value));
        }
    }
}
