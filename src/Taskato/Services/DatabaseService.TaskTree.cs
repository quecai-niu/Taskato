using SQLite;
using Taskato.Models;

namespace Taskato.Services
{
    public partial class DatabaseService
    {
        public const int MaximumTaskDepth = 5;

        public async Task<List<TaskItem>> GetTaskTreeItemsAsync()
        {
            return await _db.Table<TaskItem>()
                .OrderByDescending(t => t.Priority)
                .ThenBy(t => t.OrderIndex)
                .ThenByDescending(t => t.CreatedAt)
                .ToListAsync();
        }

        public async Task<TaskItem?> GetTaskByIdAsync(int taskId)
        {
            return await _db.FindAsync<TaskItem>(taskId);
        }

        public async Task<List<TaskItem>> GetValidParentCandidatesAsync(int taskId)
        {
            var tasks = await GetTaskTreeItemsAsync();
            var task = tasks.FirstOrDefault(t => t.Id == taskId);
            if (task is null) return new List<TaskItem>();

            var descendants = GetDescendantIds(tasks, taskId);
            descendants.Add(taskId);
            var subtreeHeight = GetSubtreeHeight(tasks, taskId);

            return tasks
                .Where(candidate => !descendants.Contains(candidate.Id))
                .Where(candidate => GetDepth(tasks, candidate.Id) + 1 + subtreeHeight < MaximumTaskDepth)
                .OrderBy(candidate => BuildPath(tasks, candidate.Id), StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public async Task<TaskItem?> AddChildTaskAsync(
            TaskItem parent,
            string title,
            int priority = 0)
        {
            if (string.IsNullOrWhiteSpace(title)) return null;

            TaskItem? createdTask = null;
            await _db.RunInTransactionAsync(connection =>
            {
                var tasks = connection.Table<TaskItem>().ToList();
                var storedParent = tasks.FirstOrDefault(t => t.Id == parent.Id);
                if (storedParent is null || GetDepth(tasks, storedParent.Id) >= MaximumTaskDepth - 1)
                    return;

                createdTask = new TaskItem
                {
                    ParentId = storedParent.Id,
                    Title = title.Trim(),
                    Priority = priority,
                    CreatedAt = DateTime.Now,
                    LastModifiedAt = DateTime.Now,
                    OrderIndex = GetNextSiblingOrder(connection, storedParent.Id)
                };
                connection.Insert(createdTask);
                InsertActivityRecord(
                    connection,
                    createdTask,
                    TaskActivityType.Created,
                    $"创建为“{storedParent.Title}”的子任务",
                    createdTask.CreatedAt,
                    CreatedActivitySourceKey(createdTask.Id),
                    depthSnapshot: GetDepth(tasks.Append(createdTask).ToList(), createdTask.Id));

                tasks.Add(createdTask);
                ReopenTaskAndAncestors(
                    connection,
                    tasks,
                    storedParent.Id,
                    "新增未完成子任务，父级状态自动恢复为进行中");
            });

            if (createdTask is not null)
                OnDataChanged?.Invoke();
            return createdTask;
        }

        public async Task<TaskCompletionResult> CompleteTaskWithHierarchyValidationAsync(
            TaskItem task,
            DateTime completedAt,
            int? durationMinutes,
            CompletionTrigger triggerType)
        {
            var operationResult = new TaskCompletionResult();
            await _db.RunInTransactionAsync(connection =>
            {
                var tasks = connection.Table<TaskItem>().ToList();
                var storedTask = tasks.FirstOrDefault(t => t.Id == task.Id);
                if (storedTask is null) return;

                var children = tasks.Where(t => t.ParentId == storedTask.Id).ToList();
                var uncompletedDescendantCount = children.Count == 0
                    ? 0
                    : CountUncompletedLeaves(tasks, storedTask.Id);
                if (uncompletedDescendantCount > 0)
                {
                    operationResult = new TaskCompletionResult
                    {
                        Succeeded = false,
                        UncompletedDescendantCount = uncompletedDescendantCount
                    };
                    return;
                }

                var completedTaskIds = new List<int>();
                MarkTaskCompleted(
                    connection,
                    tasks,
                    storedTask,
                    completedAt,
                    durationMinutes,
                    triggerType,
                    completedTaskIds);

                operationResult = new TaskCompletionResult
                {
                    Succeeded = true,
                    CompletedTaskIds = completedTaskIds
                };
            });

            if (operationResult.Succeeded)
                OnDataChanged?.Invoke();
            return operationResult;
        }

        public async Task<int> ReopenTaskWithHierarchyRulesAsync(TaskItem task)
        {
            var changedCount = 0;
            await _db.RunInTransactionAsync(connection =>
            {
                var tasks = connection.Table<TaskItem>().ToList();
                var storedTask = tasks.FirstOrDefault(candidate => candidate.Id == task.Id);
                if (storedTask is null) return;

                var hasChildren = tasks.Any(candidate => candidate.ParentId == storedTask.Id);
                changedCount = hasChildren
                    ? ReopenSingleTask(
                        connection,
                        tasks,
                        storedTask,
                        "取消完成；原完成事实继续保留在历史记录中")
                    : ReopenTaskAndAncestors(
                        connection,
                        tasks,
                        storedTask.Id,
                        "取消完成；原完成事实继续保留在历史记录中");
            });

            if (changedCount > 0)
                OnDataChanged?.Invoke();
            return changedCount;
        }

        public async Task<TaskMoveResult> MoveTaskAsync(int taskId, int? newParentId)
        {
            var result = new TaskMoveResult();
            await _db.RunInTransactionAsync(connection =>
            {
                var tasks = connection.Table<TaskItem>().ToList();
                var task = tasks.FirstOrDefault(t => t.Id == taskId);
                if (task is null)
                {
                    result = new TaskMoveResult { ErrorMessage = "任务不存在。" };
                    return;
                }

                if (task.ParentId == newParentId)
                {
                    result = new TaskMoveResult { Succeeded = true };
                    return;
                }

                if (newParentId.HasValue)
                {
                    var newParent = tasks.FirstOrDefault(t => t.Id == newParentId.Value);
                    if (newParent is null)
                    {
                        result = new TaskMoveResult { ErrorMessage = "目标父任务不存在。" };
                        return;
                    }

                    var descendantIds = GetDescendantIds(tasks, taskId);
                    if (newParent.Id == taskId || descendantIds.Contains(newParent.Id))
                    {
                        result = new TaskMoveResult { ErrorMessage = "不能把任务移动到自身或后代任务中。" };
                        return;
                    }

                    var targetDepth = GetDepth(tasks, newParent.Id) + 1;
                    var subtreeHeight = GetSubtreeHeight(tasks, taskId);
                    if (targetDepth + subtreeHeight >= MaximumTaskDepth)
                    {
                        result = new TaskMoveResult { ErrorMessage = "移动后会超过最多 5 层的限制。" };
                        return;
                    }
                }

                var oldParentId = task.ParentId;
                var oldPath = BuildPath(tasks, task.Id);
                task.ParentId = newParentId;
                task.OrderIndex = GetNextSiblingOrder(connection, newParentId);
                task.LastModifiedAt = DateTime.Now;
                connection.Update(task);
                var newPath = BuildPath(tasks, task.Id);
                InsertActivityRecord(
                    connection,
                    task,
                    TaskActivityType.Moved,
                    $"由“{oldPath}”移动到“{newPath}”",
                    task.LastModifiedAt,
                    parentIdSnapshot: newParentId,
                    depthSnapshot: GetDepth(tasks, task.Id));

                RecalculateAncestorStates(connection, tasks, oldParentId);
                if (newParentId != oldParentId)
                    RecalculateAncestorStates(connection, tasks, newParentId);

                result = new TaskMoveResult { Succeeded = true };
            });

            if (result.Succeeded)
                OnDataChanged?.Invoke();
            return result;
        }

        public async Task<int> DeleteTaskAsync(TaskItem task, TaskDeleteMode deleteMode)
        {
            var affectedRows = 0;
            await _db.RunInTransactionAsync(connection =>
            {
                var tasks = connection.Table<TaskItem>().ToList();
                var storedTask = tasks.FirstOrDefault(t => t.Id == task.Id);
                if (storedTask is null) return;

                if (deleteMode == TaskDeleteMode.PromoteChildren)
                {
                    var directChildren = tasks.Where(t => t.ParentId == storedTask.Id).ToList();
                    foreach (var child in directChildren)
                    {
                        var oldPath = BuildPath(tasks, child.Id);
                        child.ParentId = storedTask.ParentId;
                        child.LastModifiedAt = DateTime.Now;
                        connection.Update(child);
                        InsertActivityRecord(
                            connection,
                            child,
                            TaskActivityType.Moved,
                            $"删除父任务后提升层级；原路径为“{oldPath}”",
                            child.LastModifiedAt,
                            parentIdSnapshot: child.ParentId,
                            depthSnapshot: GetDepth(tasks, child.Id));
                        affectedRows++;
                    }
                }
                else
                {
                    var branchIds = GetDescendantIds(tasks, storedTask.Id);
                    foreach (var descendantId in branchIds.OrderByDescending(id => GetDepth(tasks, id)))
                    {
                        var descendant = tasks.First(candidate => candidate.Id == descendantId);
                        InsertActivityRecord(
                            connection,
                            descendant,
                            TaskActivityType.Deleted,
                            $"随任务分支“{storedTask.Title}”一并删除",
                            DateTime.Now,
                            depthSnapshot: GetDepth(tasks, descendant.Id));
                        affectedRows += connection.Delete<TaskItem>(descendantId);
                    }
                    tasks.RemoveAll(candidate => branchIds.Contains(candidate.Id));
                }

                InsertActivityRecord(
                    connection,
                    storedTask,
                    TaskActivityType.Deleted,
                    deleteMode == TaskDeleteMode.PromoteChildren
                        ? "删除任务并将直属子任务提升一级"
                        : "删除任务及其全部子任务",
                    DateTime.Now,
                    depthSnapshot: GetDepth(tasks.Append(storedTask).ToList(), storedTask.Id));
                affectedRows += connection.Delete<TaskItem>(storedTask.Id);
                tasks.RemoveAll(candidate => candidate.Id == storedTask.Id);
                RecalculateAncestorStates(
                    connection,
                    tasks,
                    storedTask.ParentId);
            });

            if (affectedRows > 0)
                OnDataChanged?.Invoke();
            return affectedRows;
        }

        public async Task<List<TaskCompletionRecord>> GetCompletionRecordsOnAsync(DateTime date)
        {
            var dayStart = date.Date;
            var dayEnd = dayStart.AddDays(1);
            var records = await _db.Table<TaskCompletionRecord>()
                .Where(record => record.CompletedAt >= dayStart && record.CompletedAt < dayEnd)
                .OrderBy(record => record.CompletedAt)
                .ToListAsync();

            var tasks = await GetTaskTreeItemsAsync();
            var tasksById = tasks.ToDictionary(task => task.Id);
            foreach (var record in records)
            {
                if (tasksById.TryGetValue(record.TaskId, out var currentTask))
                    record.CurrentTask = currentTask;
            }

            return records;
        }

        public async Task<List<TaskCompletionRecord>> GetCompletionRecordsBetweenAsync(
            DateTime startDate,
            DateTime endDate)
        {
            var dayEnd = endDate.Date.AddDays(1);
            var records = await _db.Table<TaskCompletionRecord>()
                .Where(record => record.CompletedAt >= startDate.Date && record.CompletedAt < dayEnd)
                .OrderByDescending(record => record.CompletedAt)
                .ToListAsync();

            var tasksById = (await GetTaskTreeItemsAsync()).ToDictionary(task => task.Id);
            foreach (var record in records)
            {
                if (tasksById.TryGetValue(record.TaskId, out var currentTask))
                    record.CurrentTask = currentTask;
            }
            return records;
        }

        public async Task<List<TaskItem>> GetUncompletedLeafTasksAsync()
        {
            var tasks = await GetTaskTreeItemsAsync();
            var parentIds = tasks
                .Where(task => task.ParentId.HasValue)
                .Select(task => task.ParentId!.Value)
                .ToHashSet();
            return tasks
                .Where(task => !task.IsCompleted && !parentIds.Contains(task.Id))
                .OrderByDescending(task => task.Priority)
                .ThenBy(task => task.CreatedAt)
                .ToList();
        }

        private async Task MigrateLegacyCompletionRecordsAsync()
        {
            var tasks = await GetTaskTreeItemsAsync();
            var completedTasks = tasks
                .Where(task => task.IsCompleted && task.CompletedAt.HasValue)
                .ToList();
            if (completedTasks.Count == 0) return;

            var existingRecords = await _db.Table<TaskCompletionRecord>().ToListAsync();
            foreach (var task in completedTasks)
            {
                var alreadyRecorded = existingRecords.Any(record =>
                    record.TaskId == task.Id &&
                    record.CompletedAt == task.CompletedAt!.Value);
                if (alreadyRecorded) continue;

                var record = CreateCompletionRecord(
                    tasks,
                    task,
                    task.CompletedAt!.Value,
                    task.CompletionDurationMinutes,
                    CompletionTrigger.LegacyMigration);
                await _db.InsertAsync(record);
            }
        }

        private static void MarkTaskCompleted(
            SQLiteConnection connection,
            List<TaskItem> tasks,
            TaskItem task,
            DateTime completedAt,
            int? durationMinutes,
            CompletionTrigger triggerType,
            List<int> completedTaskIds)
        {
            task.IsCompleted = true;
            task.Status = TaskWorkStatus.Completed;
            task.CompletedAt = completedAt;
            task.CompletionDurationMinutes = durationMinutes is > 0 ? durationMinutes : null;
            task.LastModifiedAt = DateTime.Now;
            connection.Update(task);

            var record = CreateCompletionRecord(
                tasks,
                task,
                completedAt,
                task.CompletionDurationMinutes,
                triggerType);
            connection.Insert(record);
            InsertActivityRecord(
                connection,
                task,
                TaskActivityType.Completed,
                CompletionDetail(record),
                completedAt,
                CompletionActivitySourceKey(record.Id),
                record.Id,
                depthSnapshot: record.DepthSnapshot);
            completedTaskIds.Add(task.Id);
        }

        private static TaskCompletionRecord CreateCompletionRecord(
            List<TaskItem> tasks,
            TaskItem task,
            DateTime completedAt,
            int? durationMinutes,
            CompletionTrigger triggerType)
        {
            var ancestors = GetAncestorChain(tasks, task);
            var topLevelTask = ancestors.FirstOrDefault() ?? task;
            var depth = ancestors.Count;
            var hasChildren = tasks.Any(candidate => candidate.ParentId == task.Id);

            var completionKind = !hasChildren
                ? CompletionKind.LeafTask
                : task.ParentId is null
                    ? CompletionKind.TopLevelAchievement
                    : CompletionKind.ParentMilestone;

            return new TaskCompletionRecord
            {
                TaskId = task.Id,
                TopLevelTaskId = topLevelTask.Id,
                TitleSnapshot = task.Title,
                PathSnapshot = BuildPath(tasks, task.Id),
                TopLevelTitleSnapshot = topLevelTask.Title,
                DepthSnapshot = depth,
                PrioritySnapshot = task.Priority,
                CompletionKind = completionKind,
                TriggerType = triggerType,
                CompletedAt = completedAt,
                DurationMinutes = completionKind == CompletionKind.LeafTask
                    ? durationMinutes
                    : null
            };
        }

        private static int ReopenTaskAndAncestors(
            SQLiteConnection connection,
            List<TaskItem> tasks,
            int taskId,
            string detail = "任务状态自动恢复为进行中")
        {
            var changedCount = 0;
            var current = tasks.FirstOrDefault(task => task.Id == taskId);
            while (current is not null)
            {
                if (current.IsCompleted || current.CompletedAt.HasValue || current.CompletionDurationMinutes.HasValue)
                {
                    current.IsCompleted = false;
                    current.Status = HasProgress(connection, current.Id)
                        ? TaskWorkStatus.InProgress
                        : TaskWorkStatus.NotStarted;
                    current.CompletedAt = null;
                    current.CompletionDurationMinutes = null;
                    current.LastModifiedAt = DateTime.Now;
                    connection.Update(current);
                    InsertActivityRecord(
                        connection,
                        current,
                        TaskActivityType.Reopened,
                        current.Id == taskId ? detail : "下级任务取消完成，父级状态自动恢复为进行中",
                        current.LastModifiedAt,
                        depthSnapshot: GetDepth(tasks, current.Id));
                    changedCount++;
                }

                current = current.ParentId.HasValue
                    ? tasks.FirstOrDefault(task => task.Id == current.ParentId.Value)
                    : null;
            }

            return changedCount;
        }

        private static int ReopenSingleTask(
            SQLiteConnection connection,
            List<TaskItem> tasks,
            TaskItem task,
            string detail)
        {
            if (!task.IsCompleted && !task.CompletedAt.HasValue && !task.CompletionDurationMinutes.HasValue)
                return 0;

            // 父级成果由各层独立确认；取消当前父任务不能清除已经手动确认的祖先成果。
            task.IsCompleted = false;
            task.Status = HasProgress(connection, task.Id)
                ? TaskWorkStatus.InProgress
                : TaskWorkStatus.NotStarted;
            task.CompletedAt = null;
            task.CompletionDurationMinutes = null;
            task.LastModifiedAt = DateTime.Now;
            connection.Update(task);
            InsertActivityRecord(
                connection,
                task,
                TaskActivityType.Reopened,
                detail,
                task.LastModifiedAt,
                depthSnapshot: GetDepth(tasks, task.Id));
            return 1;
        }

        private static void RecalculateAncestorStates(
            SQLiteConnection connection,
            List<TaskItem> tasks,
            int? startTaskId)
        {
            // 结构变化只能使已完成父任务失效；即使所有叶子均已完成，也必须等待用户手动确认父任务。
            var currentId = startTaskId;
            var visited = new HashSet<int>();
            while (currentId.HasValue && visited.Add(currentId.Value))
            {
                var current = tasks.FirstOrDefault(task => task.Id == currentId.Value);
                if (current is null) break;

                var directChildren = tasks.Where(task => task.ParentId == current.Id).ToList();
                if (directChildren.Count == 0)
                {
                    currentId = current.ParentId;
                    continue;
                }

                if (CountUncompletedLeaves(tasks, current.Id) > 0)
                {
                    ReopenTaskAndAncestors(
                        connection,
                        tasks,
                        current.Id,
                        "任务树结构变化，父级状态自动恢复为进行中");
                    break;
                }

                currentId = current.ParentId;
            }
        }

        private static int CountUncompletedLeaves(List<TaskItem> tasks, int taskId)
        {
            var children = tasks.Where(task => task.ParentId == taskId).ToList();
            if (children.Count == 0)
            {
                var task = tasks.First(candidate => candidate.Id == taskId);
                return task.IsCompleted ? 0 : 1;
            }

            return children.Sum(child => CountUncompletedLeaves(tasks, child.Id));
        }

        private static HashSet<int> GetDescendantIds(List<TaskItem> tasks, int taskId)
        {
            var result = new HashSet<int>();
            var pending = new Queue<int>();
            pending.Enqueue(taskId);

            while (pending.Count > 0)
            {
                var parentId = pending.Dequeue();
                foreach (var child in tasks.Where(task => task.ParentId == parentId))
                {
                    if (result.Add(child.Id))
                        pending.Enqueue(child.Id);
                }
            }

            return result;
        }

        private static List<TaskItem> GetAncestorChain(List<TaskItem> tasks, TaskItem task)
        {
            var result = new List<TaskItem>();
            var currentParentId = task.ParentId;
            var visited = new HashSet<int> { task.Id };
            while (currentParentId.HasValue)
            {
                if (!visited.Add(currentParentId.Value)) break;
                var parent = tasks.FirstOrDefault(candidate => candidate.Id == currentParentId.Value);
                if (parent is null) break;
                result.Insert(0, parent);
                currentParentId = parent.ParentId;
            }

            return result;
        }

        private static int GetDepth(List<TaskItem> tasks, int taskId)
        {
            var task = tasks.FirstOrDefault(candidate => candidate.Id == taskId);
            return task is null ? 0 : GetAncestorChain(tasks, task).Count;
        }

        private static int GetSubtreeHeight(List<TaskItem> tasks, int taskId)
        {
            var children = tasks.Where(task => task.ParentId == taskId).ToList();
            return children.Count == 0
                ? 0
                : 1 + children.Max(child => GetSubtreeHeight(tasks, child.Id));
        }

        private static string BuildPath(List<TaskItem> tasks, int taskId)
        {
            var task = tasks.FirstOrDefault(candidate => candidate.Id == taskId);
            if (task is null) return string.Empty;
            var pathParts = GetAncestorChain(tasks, task)
                .Select(ancestor => TaskItem.FirstLine(ancestor.Title))
                .Append(TaskItem.FirstLine(task.Title));
            return string.Join(" / ", pathParts);
        }

        private static double GetNextSiblingOrder(SQLiteConnection connection, int? parentId)
        {
            var siblings = connection.Table<TaskItem>()
                .Where(task => task.ParentId == parentId)
                .ToList();
            return siblings.Count == 0 ? 0 : siblings.Max(task => task.OrderIndex) + 1;
        }
    }
}
