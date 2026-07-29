using SQLite;
using Taskato.Models;

namespace Taskato.Services
{
    public partial class DatabaseService
    {
        private const string ActivityMigrationBoundaryKey = "activity-migration-boundary:v1";

        private async Task InitializeActivityRecordsAsync()
        {
            await _db.CreateTableAsync<TaskActivityRecord>();

            // 旧版本没有活动日志，只能可靠回填创建与完成事实；取消、编辑和移动不能推测。
            await _db.RunInTransactionAsync(connection =>
            {
                var existingKeys = connection.Table<TaskActivityRecord>()
                    .Select(record => record.SourceKey)
                    .ToHashSet(StringComparer.Ordinal);
                var tasks = connection.Table<TaskItem>().ToList();

                foreach (var task in tasks)
                {
                    var sourceKey = CreatedActivitySourceKey(task.Id);
                    if (existingKeys.Contains(sourceKey)) continue;

                    InsertActivityRecord(
                        connection,
                        task,
                        TaskActivityType.Created,
                        task.ParentId.HasValue ? "创建为子任务" : "创建为顶级任务",
                        task.CreatedAt,
                        sourceKey,
                        isLegacyBackfill: true,
                        depthSnapshot: GetDepth(tasks, task.Id));
                    existingKeys.Add(sourceKey);
                }

                foreach (var completion in connection.Table<TaskCompletionRecord>().ToList())
                {
                    var sourceKey = CompletionActivitySourceKey(completion.Id);
                    if (existingKeys.Contains(sourceKey)) continue;

                    connection.Insert(new TaskActivityRecord
                    {
                        TaskId = completion.TaskId,
                        ActivityType = TaskActivityType.Completed,
                        TitleSnapshot = completion.TitleSnapshot,
                        DetailSnapshot = CompletionDetail(completion),
                        ParentIdSnapshot = null,
                        DepthSnapshot = completion.DepthSnapshot,
                        OccurredAt = completion.CompletedAt,
                        CompletionRecordId = completion.Id,
                        SourceKey = sourceKey,
                        IsLegacyBackfill = true
                    });
                    existingKeys.Add(sourceKey);
                }

                if (!existingKeys.Contains(ActivityMigrationBoundaryKey))
                {
                    connection.Insert(new TaskActivityRecord
                    {
                        TaskId = null,
                        ActivityType = TaskActivityType.MigrationBoundary,
                        TitleSnapshot = "活动日志升级完成",
                        DetailSnapshot = "从此时间开始完整记录任务创建、编辑、移动、完成、取消完成和删除。",
                        OccurredAt = DateTime.Now,
                        SourceKey = ActivityMigrationBoundaryKey
                    });
                }
            });
        }

        public async Task<List<TaskActivityRecord>> SearchActivityRecordsAsync(
            DateTime startDate,
            DateTime endDate,
            string keyword,
            string eventFilter,
            bool sortDescending)
        {
            var dayEnd = endDate.Date.AddDays(1);
            var records = await _db.Table<TaskActivityRecord>()
                .Where(record => record.OccurredAt >= startDate.Date && record.OccurredAt < dayEnd)
                .ToListAsync();

            if (!string.IsNullOrWhiteSpace(keyword))
            {
                records = records.Where(record =>
                    record.TitleSnapshot.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                    record.DetailSnapshot.Contains(keyword, StringComparison.OrdinalIgnoreCase)).ToList();
            }

            if (!string.IsNullOrWhiteSpace(eventFilter) &&
                !eventFilter.Equals("all", StringComparison.OrdinalIgnoreCase) &&
                Enum.TryParse<TaskActivityType>(eventFilter, true, out var activityType))
            {
                records = records.Where(record => record.ActivityType == activityType).ToList();
            }

            return sortDescending
                ? records.OrderByDescending(record => record.OccurredAt).ThenByDescending(record => record.Id).ToList()
                : records.OrderBy(record => record.OccurredAt).ThenBy(record => record.Id).ToList();
        }

        private static void InsertActivityRecord(
            SQLiteConnection connection,
            TaskItem task,
            TaskActivityType activityType,
            string detail,
            DateTime occurredAt,
            string? sourceKey = null,
            int? completionRecordId = null,
            bool isLegacyBackfill = false,
            int? parentIdSnapshot = null,
            int? depthSnapshot = null)
        {
            connection.Insert(new TaskActivityRecord
            {
                TaskId = task.Id,
                ActivityType = activityType,
                TitleSnapshot = task.Title,
                DetailSnapshot = detail,
                ParentIdSnapshot = parentIdSnapshot ?? task.ParentId,
                DepthSnapshot = depthSnapshot ?? 0,
                OccurredAt = occurredAt,
                CompletionRecordId = completionRecordId,
                SourceKey = sourceKey ?? $"live:{Guid.NewGuid():N}",
                IsLegacyBackfill = isLegacyBackfill
            });
        }

        private static string CreatedActivitySourceKey(int taskId) => $"task-created:{taskId}";
        private static string CompletionActivitySourceKey(int completionRecordId) => $"completion:{completionRecordId}";

        private static string CompletionDetail(TaskCompletionRecord record) => record.CompletionKind switch
        {
            CompletionKind.TopLevelAchievement => "手动确认大任务完成",
            CompletionKind.ParentMilestone => "手动确认父级阶段完成",
            _ when record.DurationMinutes is > 0 => $"完成任务，记录专注 {record.DurationMinutes} 分钟",
            _ => "完成任务"
        };
    }
}
