using Taskato.Models;

namespace Taskato.Services
{
    public sealed record WorkspaceContextSnapshot(
        string ActivePage,
        string TaskViewMode,
        int? SelectedTaskId,
        int? ProgressTargetTaskId = null);

    public sealed record CompletionDestination(int? TaskId, string DisplayName)
    {
        public static CompletionDestination Ungrouped { get; } = new(null, "未归类");
    }

    /// <summary>
    /// 保存网页工作区当前可见上下文。这里只保存导航事实；任务组解析始终回到数据库验证，
    /// 避免网页状态过期或目标被删除时把完成记录写到错误位置。
    /// </summary>
    public sealed class WorkspaceContextService
    {
        private readonly object _sync = new();
        private WorkspaceContextSnapshot _current = new("tasks", "detail", null);

        public event Action? ContextChanged;

        public WorkspaceContextSnapshot Current
        {
            get
            {
                lock (_sync) return _current;
            }
        }

        public void Update(
            string activePage,
            string taskViewMode,
            int? selectedTaskId,
            int? progressTargetTaskId = null)
        {
            var normalizedPage = string.IsNullOrWhiteSpace(activePage) ? "tasks" : activePage.Trim();
            var normalizedMode = taskViewMode == "create" ? "create" : "detail";
            var next = new WorkspaceContextSnapshot(
                normalizedPage,
                normalizedMode,
                selectedTaskId,
                progressTargetTaskId);
            lock (_sync)
            {
                if (_current == next) return;
                _current = next;
            }
            ContextChanged?.Invoke();
        }

        public async Task<CompletionDestination> ResolveDestinationAsync(DatabaseService database)
        {
            var snapshot = Current;
            if (snapshot.ActivePage != "tasks" || snapshot.TaskViewMode != "detail" ||
                !snapshot.SelectedTaskId.HasValue)
                return CompletionDestination.Ungrouped;

            var tasks = await database.GetTaskTreeItemsAsync();
            var byId = tasks.ToDictionary(task => task.Id);
            if (!byId.TryGetValue(snapshot.SelectedTaskId.Value, out var selected))
                return CompletionDestination.Ungrouped;

            if (snapshot.ProgressTargetTaskId.HasValue &&
                byId.TryGetValue(snapshot.ProgressTargetTaskId.Value, out var progressTarget) &&
                progressTarget.ParentId == selected.Id)
            {
                return new CompletionDestination(
                    progressTarget.Id,
                    DisplayTitle(progressTarget));
            }

            return new CompletionDestination(selected.Id, DisplayTitle(selected));
        }

        private static string DisplayTitle(TaskItem task)
        {
            var firstLine = task.DisplayTitle
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault()?.Trim();
            if (string.IsNullOrWhiteSpace(firstLine)) return $"任务 #{task.Id}";
            return firstLine.Length <= 48 ? firstLine : $"{firstLine[..48]}…";
        }
    }
}
