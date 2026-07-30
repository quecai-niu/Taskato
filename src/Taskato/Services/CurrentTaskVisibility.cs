using Taskato.Models;

namespace Taskato.Services
{
    /// <summary>
    /// 统一计算任务工作区应显示的任务。历史页面仍读取完整数据，工作区只显示当前任务及其必要祖先。
    /// </summary>
    public static class CurrentTaskVisibility
    {
        public static HashSet<int> GetVisibleTaskIds(IEnumerable<TaskItem> tasks, DateTime currentDate)
        {
            var taskList = tasks.ToList();
            var tasksById = taskList.ToDictionary(task => task.Id);
            var dayStart = currentDate.Date;
            var dayEnd = dayStart.AddDays(1);
            var visibleTaskIds = new HashSet<int>();

            foreach (var task in taskList.Where(task => IsCurrent(task, dayStart, dayEnd)))
            {
                var current = task;
                var branch = new HashSet<int>();
                while (branch.Add(current.Id))
                {
                    visibleTaskIds.Add(current.Id);
                    if (!current.ParentId.HasValue ||
                        !tasksById.TryGetValue(current.ParentId.Value, out current))
                        break;
                }
            }

            return visibleTaskIds;
        }

        private static bool IsCurrent(TaskItem task, DateTime dayStart, DateTime dayEnd) =>
            !task.IsCompleted ||
            (task.CreatedAt >= dayStart && task.CreatedAt < dayEnd) ||
            (task.CompletedAt >= dayStart && task.CompletedAt < dayEnd);
    }
}
