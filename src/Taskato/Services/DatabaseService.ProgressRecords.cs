using SQLite;
using Taskato.Models;

namespace Taskato.Services
{
    public partial class DatabaseService
    {
        private async Task InitializeProgressRecordsAsync()
        {
            await _db.CreateTableAsync<TaskProgressRecord>();
            await _db.ExecuteAsync(
                "CREATE INDEX IF NOT EXISTS IX_TaskProgressRecords_TaskCompleted " +
                "ON TaskProgressRecords(TaskId, CompletedAt)");
            await _db.ExecuteAsync(
                "UPDATE TaskItems SET Status = ? WHERE IsCompleted = 1 AND Status <> ?",
                TaskWorkStatus.Completed,
                TaskWorkStatus.Completed);
            await MigrateLegacyPomodoroTasksAsync();
        }

        /// <summary>
        /// 旧版把番茄钟记录保存为已完成子任务。只有创建活动明确带有系统标记、任务没有子项且存在
        /// 对应完成记录时才迁移，避免把用户真实创建的已完成任务误删。
        /// </summary>
        private async Task MigrateLegacyPomodoroTasksAsync()
        {
            await _db.RunInTransactionAsync(connection =>
            {
                var tasks = connection.Table<TaskItem>().ToList();
                var taskById = tasks.ToDictionary(task => task.Id);
                var parentIds = tasks
                    .Where(task => task.ParentId.HasValue)
                    .Select(task => task.ParentId!.Value)
                    .ToHashSet();
                var createdActivities = connection.Table<TaskActivityRecord>()
                    .Where(record => record.ActivityType == TaskActivityType.Created)
                    .ToList()
                    .Where(record => record.TaskId.HasValue &&
                                     record.DetailSnapshot.StartsWith(
                                         "由番茄钟完成记录创建为",
                                         StringComparison.Ordinal))
                    .ToList();

                foreach (var activity in createdActivities)
                {
                    var legacyTaskId = activity.TaskId!.Value;
                    if (!taskById.TryGetValue(legacyTaskId, out var legacyTask) ||
                        parentIds.Contains(legacyTaskId) ||
                        !legacyTask.IsCompleted ||
                        !legacyTask.CompletedAt.HasValue)
                        continue;
                    if (connection.Table<TaskProgressRecord>()
                        .Any(record => record.LegacyTaskId == legacyTaskId))
                        continue;

                    var completion = connection.Table<TaskCompletionRecord>()
                        .Where(record => record.TaskId == legacyTaskId)
                        .OrderByDescending(record => record.Id)
                        .FirstOrDefault();
                    if (completion is null) continue;

                    var destination = legacyTask.ParentId.HasValue &&
                                      taskById.TryGetValue(legacyTask.ParentId.Value, out var parent)
                        ? parent
                        : null;
                    var durationMinutes = Math.Max(
                        0,
                        completion.DurationMinutes ?? legacyTask.CompletionDurationMinutes ?? 0);
                    var completedAt = legacyTask.CompletedAt.Value;
                    connection.Insert(new TaskProgressRecord
                    {
                        TaskId = destination?.Id,
                        TaskTitleSnapshot = destination?.Title ?? string.Empty,
                        TaskPathSnapshot = destination is null
                            ? string.Empty
                            : BuildPath(tasks, destination.Id),
                        Note = legacyTask.Title,
                        StartedAt = completedAt.AddMinutes(-durationMinutes),
                        CompletedAt = completedAt,
                        CreatedAt = completedAt,
                        OriginalDurationMinutes = durationMinutes,
                        DurationMinutes = durationMinutes,
                        EndReason = "legacy_migration",
                        Source = TaskProgressSource.LegacyPomodoroTask,
                        LegacyTaskId = legacyTaskId
                    });

                    if (destination is not null && !destination.IsCompleted &&
                        destination.Status == TaskWorkStatus.NotStarted)
                    {
                        destination.Status = TaskWorkStatus.InProgress;
                        destination.LastModifiedAt = DateTime.Now;
                        connection.Update(destination);
                    }

                    connection.Execute(
                        "DELETE FROM TaskCompletionRecords WHERE TaskId = ?",
                        legacyTaskId);
                    connection.Execute(
                        "DELETE FROM TaskActivityRecords WHERE TaskId = ?",
                        legacyTaskId);
                    connection.Delete(legacyTask);
                }
            });
        }

        public async Task<TaskProgressRecord> AddProgressRecordAsync(
            int? taskId,
            string? note,
            DateTime startedAt,
            DateTime completedAt,
            int originalDurationMinutes,
            int durationMinutes,
            string endReason,
            TaskProgressSource source,
            int? sourceSessionId = null)
        {
            var normalizedNote = note?.Trim() ?? string.Empty;
            if (normalizedNote.Length > 5000)
                throw new ArgumentException("推进内容不能超过 5000 个字符。", nameof(note));
            if (durationMinutes <= 0)
                throw new ArgumentOutOfRangeException(nameof(durationMinutes), "推进时长必须大于 0 分钟。");
            if (completedAt < startedAt)
                startedAt = completedAt.AddMinutes(-durationMinutes);

            TaskProgressRecord? created = null;
            await _db.RunInTransactionAsync(connection =>
            {
                var tasks = connection.Table<TaskItem>().ToList();
                var task = taskId.HasValue
                    ? tasks.FirstOrDefault(candidate => candidate.Id == taskId.Value)
                    : null;
                if (taskId.HasValue && task is null)
                    throw new InvalidOperationException("任务不存在，无法记录推进。");

                created = new TaskProgressRecord
                {
                    TaskId = task?.Id,
                    TaskTitleSnapshot = task?.Title ?? string.Empty,
                    TaskPathSnapshot = task is null ? string.Empty : BuildPath(tasks, task.Id),
                    Note = normalizedNote,
                    StartedAt = startedAt,
                    CompletedAt = completedAt,
                    CreatedAt = DateTime.Now,
                    OriginalDurationMinutes = Math.Max(0, originalDurationMinutes),
                    DurationMinutes = durationMinutes,
                    EndReason = endReason?.Trim() ?? string.Empty,
                    Source = source,
                    SourceSessionId = sourceSessionId
                };
                connection.Insert(created);

                if (task is not null && !task.IsCompleted && task.Status == TaskWorkStatus.NotStarted)
                {
                    task.Status = TaskWorkStatus.InProgress;
                    task.LastModifiedAt = DateTime.Now;
                    connection.Update(task);
                }
            });

            OnDataChanged?.Invoke();
            return created!;
        }

        public async Task<List<TaskProgressRecord>> GetProgressRecordsForTaskAsync(
            int taskId,
            bool includeDescendants)
        {
            var taskIds = new HashSet<int> { taskId };
            if (includeDescendants)
            {
                var tasks = await GetTaskTreeItemsAsync();
                taskIds.UnionWith(GetDescendantIds(tasks, taskId));
            }

            var records = await _db.Table<TaskProgressRecord>().ToListAsync();
            return records
                .Where(record => record.TaskId.HasValue && taskIds.Contains(record.TaskId.Value))
                .OrderByDescending(record => record.CompletedAt)
                .ThenByDescending(record => record.Id)
                .ToList();
        }

        public async Task<List<TaskProgressRecord>> GetProgressRecordsForDateAsync(DateTime date)
        {
            var start = date.Date;
            var end = start.AddDays(1);
            return await _db.Table<TaskProgressRecord>()
                .Where(record => record.CompletedAt >= start && record.CompletedAt < end)
                .OrderByDescending(record => record.CompletedAt)
                .ToListAsync();
        }

        public async Task<TaskProgressRecord?> GetProgressRecordByIdAsync(int recordId) =>
            await _db.FindAsync<TaskProgressRecord>(recordId);

        public async Task<TaskProgressRecord> UpdateProgressRecordAsync(
            int recordId,
            int? taskId,
            string? note,
            int durationMinutes)
        {
            if (durationMinutes <= 0)
                throw new ArgumentOutOfRangeException(nameof(durationMinutes), "推进时长必须大于 0 分钟。");
            var normalizedNote = note?.Trim() ?? string.Empty;
            if (normalizedNote.Length > 5000)
                throw new ArgumentException("推进内容不能超过 5000 个字符。", nameof(note));

            TaskProgressRecord? updated = null;
            await _db.RunInTransactionAsync(connection =>
            {
                var record = connection.Find<TaskProgressRecord>(recordId)
                    ?? throw new InvalidOperationException("推进记录不存在。");
                var tasks = connection.Table<TaskItem>().ToList();
                var task = taskId.HasValue
                    ? tasks.FirstOrDefault(candidate => candidate.Id == taskId.Value)
                    : null;
                if (taskId.HasValue && task is null)
                    throw new InvalidOperationException("任务不存在，无法更改所属。");

                record.TaskId = task?.Id;
                record.TaskTitleSnapshot = task?.Title ?? string.Empty;
                record.TaskPathSnapshot = task is null ? string.Empty : BuildPath(tasks, task.Id);
                record.Note = normalizedNote;
                record.DurationMinutes = durationMinutes;
                connection.Update(record);

                if (task is not null && !task.IsCompleted && task.Status == TaskWorkStatus.NotStarted)
                {
                    task.Status = TaskWorkStatus.InProgress;
                    task.LastModifiedAt = DateTime.Now;
                    connection.Update(task);
                }
                updated = record;
            });

            OnDataChanged?.Invoke();
            return updated!;
        }

        public async Task<bool> DeleteProgressRecordAsync(int recordId)
        {
            var affected = await _db.DeleteAsync<TaskProgressRecord>(recordId);
            if (affected > 0) OnDataChanged?.Invoke();
            // 删除最后一条推进不会自动回退任务状态，这是已确认的业务规则。
            return affected > 0;
        }

        public async Task<int> DeleteProgressRecordsAsync(IReadOnlyCollection<int> recordIds)
        {
            ArgumentNullException.ThrowIfNull(recordIds);
            var distinctRecordIds = recordIds.Distinct().ToArray();
            if (distinctRecordIds.Length == 0 || distinctRecordIds.Any(id => id <= 0))
                throw new ArgumentException("批量删除必须提供有效的推进记录 ID。", nameof(recordIds));

            var affected = 0;
            await _db.RunInTransactionAsync(connection =>
            {
                foreach (var recordId in distinctRecordIds)
                {
                    if (connection.Find<TaskProgressRecord>(recordId) is null)
                        throw new InvalidOperationException($"推进记录 {recordId} 不存在。");
                }

                foreach (var recordId in distinctRecordIds)
                {
                    var deleted = connection.Delete<TaskProgressRecord>(recordId);
                    if (deleted != 1)
                        throw new InvalidOperationException($"推进记录 {recordId} 删除失败。");
                    affected += deleted;
                }
            });

            if (affected > 0) OnDataChanged?.Invoke();
            // 与单条删除保持一致，批量删除推进记录也不会自动回退任务状态。
            return affected;
        }

        private static bool HasProgress(SQLiteConnection connection, int taskId) =>
            connection.Table<TaskProgressRecord>().Any(record => record.TaskId == taskId);
    }
}
