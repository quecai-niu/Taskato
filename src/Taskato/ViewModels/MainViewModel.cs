using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Taskato.Models;
using Taskato.Services;
using Taskato.Utils;

namespace Taskato.ViewModels
{
    /// <summary>
    /// 主窗体的 ViewModel — 管理任务列表和番茄钟的所有逻辑
    /// 
    /// 职责：
    /// 1. 任务的增删改查（绑定到任务列表 UI）
    /// 2. 番茄钟的启动/暂停/停止（绑定到番茄钟 UI）
    /// 3. 连接 DatabaseService 和 PomodoroService
    /// </summary>
    public class MainViewModel : ViewModelBase
    {
        // ==================== 服务依赖 ====================

        /// <summary>数据库服务 — 负责任务的持久化存储</summary>
        private readonly DatabaseService _dbService;
        public DatabaseService DatabaseService => _dbService;

        /// <summary>番茄钟服务 — 负责计时逻辑</summary>
        private readonly PomodoroService _pomodoroService;

        /// <summary>用户设置服务 — 负责持久化用户配置</summary>
        private readonly SettingsService _settingsService;

        /// <summary>飞书通知服务</summary>
        private readonly FeishuService _feishuService;
        private readonly PomodoroStateStore? _pomodoroStateStore;
        private readonly PomodoroFocusTracker _focusTracker;
        // UI 列表 index 会因删除附加计时器而变化；统计和恢复必须使用稳定槽位，避免关闭错会话。
        private readonly Dictionary<PomodoroTimerViewModel, string> _focusTimerSlotIds = new();
        private readonly List<string> _restoredTimerSlotIds = new();
        private bool _isInitializingTimers = true;
        private bool _isRestoringPomodoroState;
        public FeishuService FeishuService => _feishuService;

        /// <summary>是否开启弹窗计时器（透传配置）</summary>
        public bool EnableToastTimer => _settingsService.Config.EnableToastTimer;

        /// <summary>提醒弹窗操作按钮延迟启用秒数（透传配置）</summary>
        public double ToastActionDelaySeconds => _settingsService.Config.ToastActionDelaySeconds;

        /// <summary>计时结束提示时是否同步恢复主界面。</summary>
        public bool ShowMainWindowOnTimerNotification =>
            _settingsService.Config.ShowMainWindowOnTimerNotification;

        /// <summary>是否开启多组番茄钟模式</summary>
        public bool IsMultiMode => _settingsService.Config.EnableMultiplePomodoros;

        /// <summary>提示音方案，透传给 ToastWindow 使用</summary>
        public int NotificationSoundChoice => _settingsService.Config.NotificationSoundChoice;

        /// <summary>自定义提示音路径，透传给 ToastWindow 使用</summary>
        public string CustomSoundPath => _settingsService.Config.CustomSoundPath;

        /// <summary>是否启用飞书通知</summary>
        public bool FeishuEnabled => _settingsService.Config.FeishuEnabled;

        /// <summary>工作完成时发送飞书通知</summary>
        public bool FeishuNotifyOnWork => _settingsService.Config.FeishuNotifyOnWork;

        /// <summary>休息完成时发送飞书通知</summary>
        public bool FeishuNotifyOnRest => _settingsService.Config.FeishuNotifyOnRest;

        /// <summary>休息过半时发送飞书提醒</summary>
        public bool FeishuRestHalfwayEnabled => _settingsService.Config.FeishuRestHalfwayEnabled;

        public int FeishuWorkNotificationCount => _settingsService.Config.FeishuWorkNotificationCount;
        public int FeishuRestHalfwayNotificationCount => _settingsService.Config.FeishuRestHalfwayNotificationCount;
        public int FeishuRestCompletedNotificationCount => _settingsService.Config.FeishuRestCompletedNotificationCount;

        // ==================== 任务相关属性 ====================

        /// <summary>
        /// 今日任务列表（ObservableCollection 会自动通知 UI 更新列表）
        /// </summary>
        public ObservableCollection<TaskItem> TodayTasks { get; } = new();

        private readonly List<TaskItem> _visibleTaskTree = new();
        private readonly HashSet<int> _visibleTaskIds = new();
        private readonly HashSet<int> _collapsedTaskIds = new();

        private string _taskTreeExpansionLabel = "全部收起";
        public string TaskTreeExpansionLabel
        {
            get => _taskTreeExpansionLabel;
            private set => SetProperty(ref _taskTreeExpansionLabel, value);
        }

        private TaskItem? _selectedTask;
        /// <summary>任务工作区当前选中的任务，用于中栏详情和右侧专注上下文。</summary>
        public TaskItem? SelectedTask
        {
            get => _selectedTask;
            set => SetProperty(ref _selectedTask, value);
        }

        /// <summary>
        /// 新任务输入框绑定的文本内容
        /// </summary>
        private string _newTaskTitle = string.Empty;
        public string NewTaskTitle
        {
            get => _newTaskTitle;
            set => SetProperty(ref _newTaskTitle, value);
        }

        /// <summary>
        /// 新任务快捷优先级：0=无, 1=中, 2=高, 3=紧急
        /// </summary>
        private int _newTaskPriority = 0;
        public int NewTaskPriority
        {
            get => _newTaskPriority;
            set => SetProperty(ref _newTaskPriority, value);
        }

        /// <summary>
        /// 今日统计：已完成任务数
        /// </summary>
        private int _completedCount;
        public int CompletedCount
        {
            get => _completedCount;
            set => SetProperty(ref _completedCount, value);
        }

        /// <summary>
        /// 今日统计：总任务数
        /// </summary>
        private int _totalCount;
        public int TotalCount
        {
            get => _totalCount;
            set => SetProperty(ref _totalCount, value);
        }

        /// <summary>日期标签文本，绑定到 MainWindow 的 DateLabel</summary>
        private string _dateLabelText = $"待办与今日 · {DateTime.Today:M月d日}";
        public string DateLabelText
        {
            get => _dateLabelText;
            set => SetProperty(ref _dateLabelText, value);
        }

        /// <summary>上次检测的日期，用于跨天判断</summary>
        private DateTime _lastCheckDate = DateTime.Today;

        /// <summary>跨天检测定时器（30 秒间隔）</summary>
        private readonly DispatcherTimer _dayChangeTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(30)
        };

        // ==================== 番茄钟相关属性 ====================

        public ObservableCollection<PomodoroTimerViewModel> AllTimers { get; } = new();

        /// <summary>跨天自动总结请求，由主壳层接管导航。</summary>
        public event Action<DateTime>? DailySummaryRequested;
        public event Action? FocusTrackingChanged;

        private PomodoroTimerViewModel? _currentActiveTimer;
        /// <summary>
        /// 当前选中的附加番茄钟（用于UI切换显示，避免列表过长）
        /// </summary>
        public PomodoroTimerViewModel? CurrentActiveTimer
        {
            get => _currentActiveTimer;
            set => SetProperty(ref _currentActiveTimer, value);
        }

        // ==================== 任务及其他命令 ====================

        /// <summary>添加新任务的命令</summary>
        public ICommand AddTaskCommand { get; }

        /// <summary>切换任务完成状态的命令</summary>
        public ICommand ToggleTaskCommand { get; }

        /// <summary>删除任务的命令</summary>
        public ICommand DeleteTaskCommand { get; }

        /// <summary>展开或折叠一个任务分支</summary>
        public ICommand ToggleTaskExpansionCommand { get; }

        /// <summary>在全部展开与全部收起之间切换当前任务树</summary>
        public ICommand ToggleAllTaskExpansionCommand { get; }

        /// <summary>显示或提交直属子任务输入框</summary>
        public ICommand AddChildTaskCommand { get; }

        /// <summary>添加附加番茄钟命令</summary>
        public ICommand AddAdditionalTimerCommand { get; }

        /// <summary>移除附加番茄钟命令</summary>
        public ICommand RemoveAdditionalTimerCommand { get; }

        // ==================== 构造函数 ====================

        /// <summary>
        /// 构造函数 — 接收服务依赖，初始化所有命令
        /// </summary>
        /// <param name="dbService">数据库服务</param>
        /// <param name="pomodoroService">番茄钟服务</param>
        /// <param name="settingsService">设置服务</param>
        public MainViewModel(
            DatabaseService dbService,
            PomodoroService pomodoroService,
            SettingsService settingsService,
            FeishuService feishuService,
            PomodoroStateStore? pomodoroStateStore = null)
        {
            _dbService = dbService;
            _pomodoroService = pomodoroService;
            _settingsService = settingsService;
            _feishuService = feishuService;
            _pomodoroStateStore = pomodoroStateStore;
            if (_pomodoroStateStore is not null)
            {
                _restoredTimerSlotIds.AddRange(
                    _pomodoroStateStore.Load()
                        .Select(snapshot => snapshot.SlotId)
                        .Where(slotId => !string.IsNullOrWhiteSpace(slotId)));
            }
            _focusTracker = new PomodoroFocusTracker(
                dbService,
                () => FocusTrackingChanged?.Invoke());

            // 初始化主番茄钟并添加到列表
            var mainTimer = new PomodoroTimerViewModel(_pomodoroService, "主番茄钟", newWorkMinutes => 
            {
                _settingsService.Config.WorkMinutes = newWorkMinutes;
                _settingsService.Save();
                SavePomodoroState();
            }, SavePomodoroState);
            AddTrackedTimer(mainTimer);
            CurrentActiveTimer = mainTimer;

            // 根据配置加载历史保存的附加组
            foreach (int workMinutes in _settingsService.Config.AdditionalTimersWorkMinutes)
            {
                var service = new PomodoroService(workMinutes, _settingsService.Config.RestMinutes);
                PomodoroTimerViewModel? timerVM = null;
                timerVM = new PomodoroTimerViewModel(service, $"附加组 {AllTimers.Count}", newWorkMinutes => 
                {
                    int index = AllTimers.IndexOf(timerVM!) - 1;
                    if (index >= 0 && index < _settingsService.Config.AdditionalTimersWorkMinutes.Count)
                    {
                        _settingsService.Config.AdditionalTimersWorkMinutes[index] = newWorkMinutes;
                        _settingsService.Save();
                        SavePomodoroState();
                    }
                }, SavePomodoroState);
                AddTrackedTimer(timerVM);
            }

            _isInitializingTimers = false;

            // ---------- 初始化命令 ----------

            // 添加任务：输入框非空时可执行
            AddTaskCommand = new RelayCommand(
                async _ => await AddTaskAsync(),
                _ => !string.IsNullOrWhiteSpace(NewTaskTitle)
            );

            // 切换完成状态：参数是 TaskItem 对象
            ToggleTaskCommand = new RelayCommand(async param =>
            {
                if (param is TaskItem task)
                    await ToggleTaskAsync(task);
            });

            // 删除任务：参数是 TaskItem 对象
            DeleteTaskCommand = new RelayCommand(async param =>
            {
                if (param is TaskItem task)
                    await DeleteTaskAsync(task);
            });

            ToggleTaskExpansionCommand = new RelayCommand(param =>
            {
                if (param is not TaskItem task || !task.HasVisibleChildren) return;
                task.IsExpanded = !task.IsExpanded;
                if (task.IsExpanded)
                    _collapsedTaskIds.Remove(task.Id);
                else
                    _collapsedTaskIds.Add(task.Id);
                RefreshFlattenedTasks();
            });

            ToggleAllTaskExpansionCommand = new RelayCommand(_ => ToggleAllTaskExpansion());

            AddChildTaskCommand = new RelayCommand(async param =>
            {
                if (param is not TaskItem parent || !parent.CanAddChild) return;

                if (!parent.IsAddingChild)
                {
                    parent.IsAddingChild = true;
                    return;
                }

                await AddChildTaskAsync(parent);
            });

            // 附加计时器管理
            AddAdditionalTimerCommand = new RelayCommand(_ => AddAdditionalTimer());
            RemoveAdditionalTimerCommand = new RelayCommand(param => 
            {
                if (param is PomodoroTimerViewModel timer)
                {
                    int index = AllTimers.IndexOf(timer) - 1;

                    // 活跃计时器移除前必须先结束统计区间，否则数据库会留下永久运行的开放会话。
                    if (timer.StopCommand.CanExecute(null))
                        timer.StopCommand.Execute(null);

                    AllTimers.Remove(timer);
                    _focusTimerSlotIds.Remove(timer);
                    if (CurrentActiveTimer == timer)
                    {
                        CurrentActiveTimer = AllTimers.FirstOrDefault();
                    }

                    // 更新配置并保存
                    if (index >= 0 && index < _settingsService.Config.AdditionalTimersWorkMinutes.Count)
                    {
                        _settingsService.Config.AdditionalTimersWorkMinutes.RemoveAt(index);
                        _settingsService.Save();
                    }
                    SavePomodoroState();
                }
            });

            // ---------- 订阅数据库全局变更事件 ----------
            // 当任何地方（如历史界面）删除了任务或修改了内容，主界面自动同步刷新
            _dbService.OnDataChanged += () =>
            {
                // Headless test（无界面测试）或宿主尚未创建 Application 时只跳过 UI 刷新，
                // 不让数据库写入因为缺少 Dispatcher（调度器）而失败。
                Application.Current?.Dispatcher.InvokeAsync(async () =>
                {
                    await LoadTodayTasksAsync();
                });
            };

            // ---------- 跨天检测定时器 ----------
            _dayChangeTimer.Tick += async (_, _) =>
            {
                if (DateTime.Today != _lastCheckDate)
                {
                    var yesterdayDate = _lastCheckDate; // 跨天前的日期（即"昨天"）
                    _lastCheckDate = DateTime.Today;
                    DateLabelText = $"待办与今日 · {DateTime.Today:M月d日}";
                    await LoadTodayTasksAsync();

                    if (_settingsService.Config.AutoShowDailySummary)
                    {
                        DailySummaryRequested?.Invoke(yesterdayDate);
                    }
                }
            };
            _dayChangeTimer.Start();
        }

        // ==================== 任务操作方法 ====================

        /// <summary>
        /// 加载今日任务（应用启动时和添加/删除后调用）
        /// </summary>
        public async Task LoadTodayTasksAsync()
        {
            var selectedId = SelectedTask?.Id;
            var tasks = await _dbService.GetTaskTreeItemsAsync();
            BuildVisibleTaskTree(tasks);
            RefreshFlattenedTasks();
            SelectedTask = selectedId.HasValue
                ? TodayTasks.FirstOrDefault(task => task.Id == selectedId.Value) ?? TodayTasks.FirstOrDefault()
                : TodayTasks.FirstOrDefault();
            UpdateCounts();
        }

        /// <summary>
        /// 添加新任务
        /// </summary>
        private async Task AddTaskAsync()
        {
            if (string.IsNullOrWhiteSpace(NewTaskTitle)) return;

            var task = new TaskItem
            {
                Title = NewTaskTitle.Trim(),
                CreatedAt = DateTime.Now,
                IsCompleted = false,
                Priority = NewTaskPriority
            };

            await _dbService.AddTaskAsync(task);
            NewTaskTitle = string.Empty;  // 清空输入框
            NewTaskPriority = 0;          // 重置优先级
            await LoadTodayTasksAsync();  // 刷新列表
        }

        /// <summary>
        /// 切换任务的完成/未完成状态
        /// </summary>
        private async Task ToggleTaskAsync(TaskItem task)
        {
            if (!task.IsCompleted && task.HasChildren)
            {
                var remaining = Math.Max(0, task.TotalLeafCount - task.CompletedLeafCount);
                if (remaining > 0)
                {
                    MessageBox.Show(
                        Application.Current?.MainWindow,
                        $"“{task.DisplayTitle}”还有 {remaining} 个叶子任务未完成，请先完成子任务。",
                        "父任务状态",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                    task.IsExpanded = true;
                    _collapsedTaskIds.Remove(task.Id);
                    RefreshFlattenedTasks();
                    return;
                }
            }

            if (!task.IsCompleted)
            {
                var owner = Application.Current?.MainWindow;
                if (!Views.TaskCompletionDialog.TryShowForCompletion(owner, task, out var durationMinutes))
                    return;

                await _dbService.CompleteTaskWithHierarchyValidationAsync(
                    task,
                    DateTime.Now,
                    durationMinutes,
                    CompletionTrigger.Manual);
                return;
            }

            await _dbService.ReopenTaskWithHierarchyRulesAsync(task);
        }

        public async Task AddChildTaskAsync(TaskItem parent)
        {
            if (!parent.CanAddChild) return;
            if (string.IsNullOrWhiteSpace(parent.NewChildTitle))
            {
                parent.IsAddingChild = true;
                return;
            }

            var createdTask = await _dbService.AddChildTaskAsync(
                parent,
                parent.NewChildTitle,
                parent.Priority);
            if (createdTask is null) return;

            parent.NewChildTitle = string.Empty;
            parent.IsAddingChild = false;
            await LoadTodayTasksAsync();
        }

        /// <summary>
        /// 删除任务
        /// </summary>
        public async Task DeleteTaskAsync(
            TaskItem task,
            TaskDeleteMode deleteMode = TaskDeleteMode.DeleteBranch)
        {
            await _dbService.DeleteTaskAsync(task, deleteMode);
        }

        /// <summary>
        /// 保存任务修改内容（供详情页编辑后调用）
        /// </summary>
        public async Task SaveTaskEditAsync(TaskItem task)
        {
            await _dbService.UpdateTaskAsync(task);
        }

        public async Task<TaskMoveResult> SaveTaskEditAsync(TaskItem task, int? parentId)
        {
            await _dbService.UpdateTaskAsync(task);
            return await _dbService.MoveTaskAsync(task.Id, parentId);
        }

        /// <summary>
        /// 更新统计数字（完成数 / 总数）
        /// </summary>
        private void UpdateCounts()
        {
            var leafTasks = _visibleTaskTree.Where(task => task.IsLeaf).ToList();
            TotalCount = leafTasks.Count;
            CompletedCount = leafTasks.Count(task => task.IsCompleted);
        }

        private void BuildVisibleTaskTree(List<TaskItem> tasks)
        {
            foreach (var task in tasks)
            {
                task.Children.Clear();
                task.IsExpanded = !_collapsedTaskIds.Contains(task.Id);
            }

            var tasksById = tasks.ToDictionary(task => task.Id);
            var roots = new List<TaskItem>();
            foreach (var task in tasks)
            {
                if (task.ParentId.HasValue &&
                    tasksById.TryGetValue(task.ParentId.Value, out var parent) &&
                    parent.Id != task.Id)
                {
                    parent.Children.Add(task);
                }
                else
                {
                    roots.Add(task);
                }
            }

            foreach (var task in tasks)
                task.NotifyTreeStateChanged();

            _visibleTaskTree.Clear();
            _visibleTaskIds.Clear();
            _visibleTaskIds.UnionWith(CurrentTaskVisibility.GetVisibleTaskIds(tasks, DateTime.Today));
            foreach (var task in tasks)
                task.HasVisibleChildren = task.Children.Any(child => _visibleTaskIds.Contains(child.Id));

            foreach (var root in roots
                         .OrderByDescending(task => task.Priority)
                         .ThenBy(task => task.OrderIndex)
                         .ThenByDescending(task => task.CreatedAt))
            {
                CalculateTreeState(root, 0, new HashSet<int>());
                if (_visibleTaskIds.Contains(root.Id))
                    _visibleTaskTree.Add(root);
            }
        }

        private void RefreshFlattenedTasks()
        {
            TodayTasks.Clear();
            foreach (var root in _visibleTaskTree)
                AddVisibleBranch(root);
            UpdateTaskTreeExpansionLabel();
        }

        private void AddVisibleBranch(TaskItem task)
        {
            TodayTasks.Add(task);
            if (!task.IsExpanded) return;
            foreach (var child in task.Children
                         .OrderBy(item => item.IsCompleted)
                         .ThenByDescending(item => item.Priority)
                         .ThenBy(item => item.OrderIndex)
                         .ThenByDescending(item => item.CreatedAt))
            {
                if (_visibleTaskIds.Contains(child.Id))
                    AddVisibleBranch(child);
            }
        }

        private void ToggleAllTaskExpansion()
        {
            var expandableTasks = EnumerateVisibleTasks()
                .Where(task => task.HasVisibleChildren)
                .ToList();
            var collapseAll = expandableTasks.Any(task => task.IsExpanded);

            foreach (var task in expandableTasks)
            {
                task.IsExpanded = !collapseAll;
                if (collapseAll)
                    _collapsedTaskIds.Add(task.Id);
                else
                    _collapsedTaskIds.Remove(task.Id);
            }

            RefreshFlattenedTasks();
        }

        private IEnumerable<TaskItem> EnumerateVisibleTasks()
        {
            var pending = new Stack<TaskItem>(_visibleTaskTree.AsEnumerable().Reverse());
            while (pending.Count > 0)
            {
                var task = pending.Pop();
                yield return task;
                foreach (var child in task.Children
                             .Where(child => _visibleTaskIds.Contains(child.Id))
                             .Reverse())
                    pending.Push(child);
            }
        }

        private void UpdateTaskTreeExpansionLabel()
        {
            TaskTreeExpansionLabel = EnumerateVisibleTasks()
                .Any(task => task.HasVisibleChildren && task.IsExpanded)
                ? "全部收起"
                : "全部展开";
        }

        private static (int CompletedLeaves, int TotalLeaves) CalculateTreeState(
            TaskItem task,
            int depth,
            HashSet<int> branch)
        {
            task.Depth = Math.Min(depth, DatabaseService.MaximumTaskDepth - 1);
            if (!branch.Add(task.Id))
            {
                task.CompletedLeafCount = task.IsCompleted ? 1 : 0;
                task.TotalLeafCount = 1;
                task.NotifyTreeStateChanged();
                return (task.CompletedLeafCount, 1);
            }

            if (task.Children.Count == 0)
            {
                task.CompletedLeafCount = task.IsCompleted ? 1 : 0;
                task.TotalLeafCount = 1;
            }
            else
            {
                var completedLeaves = 0;
                var totalLeaves = 0;
                foreach (var child in task.Children)
                {
                    var childState = CalculateTreeState(child, depth + 1, branch);
                    completedLeaves += childState.CompletedLeaves;
                    totalLeaves += childState.TotalLeaves;
                }

                task.CompletedLeafCount = completedLeaves;
                task.TotalLeafCount = totalLeaves;
            }

            branch.Remove(task.Id);
            task.NotifyTreeStateChanged();
            return (task.CompletedLeafCount, task.TotalLeafCount);
        }

        /// <summary>设置页保存后，把可即时生效的配置同步到正在运行的计时器和绑定属性。</summary>
        public void ApplySettingsFromConfig()
        {
            var mainTimer = AllTimers.FirstOrDefault();
            if (mainTimer is not null && mainTimer.CurrentState == PomodoroService.PomodoroState.Idle)
                mainTimer.WorkMinutes = _settingsService.Config.WorkMinutes;

            foreach (var timer in AllTimers)
                timer.RestMinutes = _settingsService.Config.RestMinutes;

            OnPropertyChanged(nameof(IsMultiMode));
            OnPropertyChanged(nameof(EnableToastTimer));
            OnPropertyChanged(nameof(NotificationSoundChoice));
            OnPropertyChanged(nameof(FeishuEnabled));
            SavePomodoroState();
        }

        private void AddAdditionalTimer()
        {
            // 使用与设置页面一致的工作/休息时长，而非写死的默认值
            var service = new PomodoroService(_settingsService.Config.WorkMinutes, _settingsService.Config.RestMinutes);
            
            PomodoroTimerViewModel? timerVM = null;
            timerVM = new PomodoroTimerViewModel(service, $"附加组 {AllTimers.Count}", newWorkMinutes => 
            {
                int index = AllTimers.IndexOf(timerVM!) - 1;
                if (index >= 0 && index < _settingsService.Config.AdditionalTimersWorkMinutes.Count)
                {
                    _settingsService.Config.AdditionalTimersWorkMinutes[index] = newWorkMinutes;
                    _settingsService.Save();
                    SavePomodoroState();
                }
            }, SavePomodoroState);
            AddTrackedTimer(timerVM);
            
            // 更新配置并保存
            _settingsService.Config.AdditionalTimersWorkMinutes.Add(_settingsService.Config.WorkMinutes);
            _settingsService.Save();
            SavePomodoroState();

            // 自动选中新添加的计时器
            CurrentActiveTimer = timerVM;
        }

        /// <summary>
        /// 通知协调器完成订阅后再恢复，关闭期间到期的计时器才能走与在线到期相同的提醒链路。
        /// </summary>
        public void RestorePomodoroState()
        {
            if (_pomodoroStateStore is null) return;

            _isRestoringPomodoroState = true;
            try
            {
                var snapshots = _pomodoroStateStore.Load()
                    .Where(snapshot => !string.IsNullOrWhiteSpace(snapshot.SlotId))
                    .GroupBy(snapshot => snapshot.SlotId, StringComparer.Ordinal)
                    .ToDictionary(group => group.Key, group => group.Last(), StringComparer.Ordinal);
                for (var index = 0; index < AllTimers.Count; index++)
                {
                    if (snapshots.TryGetValue(GetFocusTimerSlotId(AllTimers[index]), out var snapshot))
                        AllTimers[index].Restore(snapshot);
                }
            }
            finally
            {
                _isRestoringPomodoroState = false;
            }

            // 跨越截止时间后会变成“等待处理”，恢复结束立即覆盖旧的运行中快照。
            SavePomodoroState();
            var observedAt = DateTimeOffset.Now;
            for (var index = 0; index < AllTimers.Count; index++)
            {
                var slotId = GetFocusTimerSlotId(AllTimers[index]);
                _focusTracker.Reconcile(
                    slotId,
                    AllTimers[index],
                    SelectedTask,
                    AllTimers[index].CreateSnapshot(slotId),
                    observedAt);
            }
        }

        public void SavePomodoroState()
        {
            if (_pomodoroStateStore is null || _isRestoringPomodoroState) return;
            _pomodoroStateStore.Save(AllTimers.Select((timer, index) =>
                timer.CreateSnapshot(GetFocusTimerSlotId(timer))));
        }

        public Task FlushFocusTrackingAsync() => _focusTracker.FlushAsync();

        private void AddTrackedTimer(PomodoroTimerViewModel timer)
        {
            var index = AllTimers.Count;
            var slotId = _isInitializingTimers && index < _restoredTimerSlotIds.Count
                ? _restoredTimerSlotIds[index]
                : $"timer-{Guid.NewGuid():N}";
            AllTimers.Add(timer);
            _focusTimerSlotIds[timer] = slotId;
            timer.PhaseTransitioned += transition =>
            {
                if (!_focusTimerSlotIds.TryGetValue(timer, out var currentSlotId)) return;
                _focusTracker.Track(currentSlotId, timer, SelectedTask, transition);
            };
        }

        private string GetFocusTimerSlotId(PomodoroTimerViewModel timer) =>
            _focusTimerSlotIds.TryGetValue(timer, out var slotId)
                ? slotId
                : $"timer-{Guid.NewGuid():N}";
    }
}
