using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;
using Taskato.Models;
using Taskato.Services;
using Taskato.Utils;

namespace Taskato.Views
{
    public sealed class TaskParentOption
    {
        public int? TaskId { get; init; }
        public string DisplayName { get; init; } = string.Empty;
    }

    /// <summary>
    /// 任务详情与树形管理入口。状态变更直接调用 DatabaseService，
    /// 避免详情页绕过完成记录和父级级联规则。
    /// </summary>
    public partial class TaskDetailWindow : Window
    {
        private readonly DatabaseService _dbService;

        public TaskItem OriginalTask { get; }
        public TaskItem EditingTask { get; private set; }
        public bool IsSaved { get; private set; }

        public ObservableCollection<TaskParentOption> ParentOptions { get; } = new();
        public ObservableCollection<TaskItem> DirectChildren { get; } = new();

        public int? SelectedParentId { get; set; }

        public TaskDetailWindow(TaskItem task, DatabaseService dbService)
        {
            InitializeComponent();
            VisualEffects.Initialize(this);

            OriginalTask = task;
            _dbService = dbService;
            EditingTask = CloneTask(task);
            SelectedParentId = task.ParentId;
            DataContext = EditingTask;
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            if (Resources["PopInAnimation"] is Storyboard storyboard)
                storyboard.Begin(this);

            await LoadTreeManagementDataAsync();
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            VisualEffects.RunWithTemporaryReduction(this, DragMove);
        }

        private async void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(EditingTask.Title))
            {
                MessageBox.Show(this, "任务标题不能为空。", "保存任务", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var moveResult = await _dbService.MoveTaskAsync(OriginalTask.Id, SelectedParentId);
            if (!moveResult.Succeeded)
            {
                MessageBox.Show(this, moveResult.ErrorMessage, "无法移动任务", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            OriginalTask.Title = EditingTask.Title.Trim();
            OriginalTask.Priority = EditingTask.Priority;
            OriginalTask.ParentId = SelectedParentId;
            await _dbService.UpdateTaskAsync(OriginalTask);
            IsSaved = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            IsSaved = false;
            Close();
        }

        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void MaximizeButton_Click(object sender, RoutedEventArgs e)
        {
            VisualEffects.RunWithTemporaryReduction(this, () =>
            {
                WindowState = WindowState == WindowState.Maximized
                    ? WindowState.Normal
                    : WindowState.Maximized;
            });
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            IsSaved = false;
            Close();
        }

        protected override void OnKeyDown(System.Windows.Input.KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.Key != Key.Escape) return;
            IsSaved = false;
            Close();
        }

        private async void StatusBadge_Click(object sender, MouseButtonEventArgs e)
        {
            var allTasks = await _dbService.GetTaskTreeItemsAsync();
            if (allTasks.Any(task => task.ParentId == OriginalTask.Id))
            {
                var remaining = CountUncompletedLeaves(allTasks, OriginalTask.Id);
                MessageBox.Show(
                    this,
                    remaining > 0
                        ? $"父任务还有 {remaining} 个叶子任务未完成，请先完成子任务。"
                        : "父任务由子任务自动完成；如需重开，请取消一个叶子任务的完成状态。",
                    "父任务状态",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                e.Handled = true;
                return;
            }

            if (!OriginalTask.IsCompleted)
            {
                if (!TaskCompletionDialog.TryShowForCompletion(this, OriginalTask, out var durationMinutes))
                {
                    e.Handled = true;
                    return;
                }

                await _dbService.CompleteTaskWithCascadeAsync(
                    OriginalTask,
                    DateTime.Now,
                    durationMinutes,
                    CompletionTrigger.Manual);
            }
            else
            {
                await _dbService.ReopenTaskWithAncestorsAsync(OriginalTask);
            }

            await ReloadCurrentTaskAsync();
            e.Handled = true;
        }

        private async void AddChildButton_Click(object sender, RoutedEventArgs e)
        {
            var title = NewChildTitleTextBox.Text;
            if (string.IsNullOrWhiteSpace(title)) return;

            var child = await _dbService.AddChildTaskAsync(OriginalTask, title, OriginalTask.Priority);
            if (child is null)
            {
                MessageBox.Show(
                    this,
                    "无法添加子任务，当前任务可能已经处于第 5 层。",
                    "添加子任务",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            NewChildTitleTextBox.Clear();
            await ReloadCurrentTaskAsync();
            await LoadTreeManagementDataAsync();
        }

        private void NewChildTitleTextBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key != Key.Enter) return;
            e.Handled = true;
            AddChildButton_Click(sender, new RoutedEventArgs());
        }

        private async void OpenChildButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement element || element.Tag is not TaskItem child)
                return;

            var childWindow = new TaskDetailWindow(child, _dbService) { Owner = this };
            childWindow.ShowDialog();
            await LoadTreeManagementDataAsync();
        }

        private async void DeleteChildButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement element || element.Tag is not TaskItem child)
                return;

            var allTasks = await _dbService.GetTaskTreeItemsAsync();
            var hasChildren = allTasks.Any(task => task.ParentId == child.Id);
            if (!hasChildren)
            {
                if (!ConfirmDialog.Show(this, "确定删除这个子任务吗？历史完成记录会保留。"))
                    return;
                await _dbService.DeleteTaskAsync(child, TaskDeleteMode.DeleteBranch);
            }
            else
            {
                var choice = MessageBox.Show(
                    this,
                    "选择“是”：删除整个子任务分支。\n选择“否”：仅删除该任务，并将直属子任务提升一级。",
                    "删除子任务",
                    MessageBoxButton.YesNoCancel,
                    MessageBoxImage.Warning);
                if (choice == MessageBoxResult.Cancel) return;
                await _dbService.DeleteTaskAsync(
                    child,
                    choice == MessageBoxResult.Yes
                        ? TaskDeleteMode.DeleteBranch
                        : TaskDeleteMode.PromoteChildren);
            }

            await LoadTreeManagementDataAsync();
        }

        private async Task LoadTreeManagementDataAsync()
        {
            var allTasks = await _dbService.GetTaskTreeItemsAsync();
            var candidates = await _dbService.GetValidParentCandidatesAsync(OriginalTask.Id);

            ParentOptions.Clear();
            ParentOptions.Add(new TaskParentOption { TaskId = null, DisplayName = "无（顶级任务）" });
            foreach (var candidate in candidates)
            {
                ParentOptions.Add(new TaskParentOption
                {
                    TaskId = candidate.Id,
                    DisplayName = BuildPath(allTasks, candidate)
                });
            }

            ParentTaskComboBox.ItemsSource = ParentOptions;
            ParentTaskComboBox.SelectedValuePath = nameof(TaskParentOption.TaskId);
            ParentTaskComboBox.DisplayMemberPath = nameof(TaskParentOption.DisplayName);
            ParentTaskComboBox.SelectedValue = SelectedParentId;

            DirectChildren.Clear();
            foreach (var child in allTasks
                         .Where(task => task.ParentId == OriginalTask.Id)
                         .OrderByDescending(task => task.Priority)
                         .ThenBy(task => task.OrderIndex))
            {
                DirectChildren.Add(child);
            }

            ChildrenItemsControl.ItemsSource = DirectChildren;
            AddChildPanel.Visibility = GetDepth(allTasks, OriginalTask) < DatabaseService.MaximumTaskDepth - 1
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        private async Task ReloadCurrentTaskAsync()
        {
            var storedTask = await _dbService.GetTaskByIdAsync(OriginalTask.Id);
            if (storedTask is null) return;

            CopyStoredValues(storedTask, OriginalTask);
            EditingTask = CloneTask(storedTask);
            DataContext = EditingTask;
        }

        private static TaskItem CloneTask(TaskItem task)
        {
            return new TaskItem
            {
                Id = task.Id,
                ParentId = task.ParentId,
                Title = task.Title,
                Priority = task.Priority,
                IsCompleted = task.IsCompleted,
                CreatedAt = task.CreatedAt,
                CompletedAt = task.CompletedAt,
                CompletionDurationMinutes = task.CompletionDurationMinutes,
                LastModifiedAt = task.LastModifiedAt,
                OrderIndex = task.OrderIndex
            };
        }

        private static void CopyStoredValues(TaskItem source, TaskItem target)
        {
            target.ParentId = source.ParentId;
            target.Title = source.Title;
            target.Priority = source.Priority;
            target.IsCompleted = source.IsCompleted;
            target.CreatedAt = source.CreatedAt;
            target.CompletedAt = source.CompletedAt;
            target.CompletionDurationMinutes = source.CompletionDurationMinutes;
            target.LastModifiedAt = source.LastModifiedAt;
            target.OrderIndex = source.OrderIndex;
        }

        private static string BuildPath(List<TaskItem> tasks, TaskItem task)
        {
            var parts = new List<string> { task.DisplayTitle };
            var parentId = task.ParentId;
            var visited = new HashSet<int> { task.Id };
            while (parentId.HasValue && visited.Add(parentId.Value))
            {
                var parent = tasks.FirstOrDefault(candidate => candidate.Id == parentId.Value);
                if (parent is null) break;
                parts.Insert(0, parent.DisplayTitle);
                parentId = parent.ParentId;
            }
            return string.Join(" / ", parts);
        }

        private static int GetDepth(List<TaskItem> tasks, TaskItem task)
        {
            var depth = 0;
            var parentId = task.ParentId;
            var visited = new HashSet<int> { task.Id };
            while (parentId.HasValue && visited.Add(parentId.Value))
            {
                var parent = tasks.FirstOrDefault(candidate => candidate.Id == parentId.Value);
                if (parent is null) break;
                depth++;
                parentId = parent.ParentId;
            }
            return depth;
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
    }
}
