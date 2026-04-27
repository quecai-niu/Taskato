using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
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

        /// <summary>番茄钟服务 — 负责计时逻辑</summary>
        private readonly PomodoroService _pomodoroService;

        // ==================== 任务相关属性 ====================

        /// <summary>
        /// 今日任务列表（ObservableCollection 会自动通知 UI 更新列表）
        /// </summary>
        public ObservableCollection<TaskItem> TodayTasks { get; } = new();

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

        // ==================== 番茄钟相关属性 ====================

        /// <summary>
        /// 番茄钟倒计时显示文本（格式 mm:ss）
        /// </summary>
        private string _timerDisplay = "25:00";
        public string TimerDisplay
        {
            get => _timerDisplay;
            set => SetProperty(ref _timerDisplay, value);
        }

        /// <summary>
        /// 番茄钟进度条值（0.0 ~ 1.0，1.0 表示满）
        /// </summary>
        private double _timerProgress = 1.0;
        public double TimerProgress
        {
            get => _timerProgress;
            set => SetProperty(ref _timerProgress, value);
        }

        /// <summary>
        /// 番茄钟当前状态文本（显示在状态标签上）
        /// </summary>
        private string _pomodoroStatusText = "空闲";
        public string PomodoroStatusText
        {
            get => _pomodoroStatusText;
            set => SetProperty(ref _pomodoroStatusText, value);
        }

        /// <summary>
        /// 番茄钟是否正在运行中（用于控制按钮的显隐）
        /// </summary>
        private bool _isTimerRunning;
        public bool IsTimerRunning
        {
            get => _isTimerRunning;
            set => SetProperty(ref _isTimerRunning, value);
        }

        // ==================== 命令绑定 ====================

        /// <summary>添加新任务的命令</summary>
        public ICommand AddTaskCommand { get; }

        /// <summary>切换任务完成状态的命令</summary>
        public ICommand ToggleTaskCommand { get; }

        /// <summary>删除任务的命令</summary>
        public ICommand DeleteTaskCommand { get; }

        /// <summary>开始番茄钟工作的命令</summary>
        public ICommand StartPomodoroCommand { get; }

        /// <summary>暂停/恢复番茄钟的命令</summary>
        public ICommand PausePomodoroCommand { get; }

        /// <summary>停止番茄钟的命令</summary>
        public ICommand StopPomodoroCommand { get; }

        /// <summary>打开历史查询窗体的命令</summary>
        public ICommand OpenHistoryCommand { get; }

        /// <summary>打开设置窗体的命令</summary>
        public ICommand OpenSettingsCommand { get; }

        // ==================== 弹窗事件 ====================

        /// <summary>
        /// 番茄钟工作完成时触发 — 让 View 层弹出 ToastWindow
        /// ViewModel 不应直接操作窗体，通过事件通知 View 来做
        /// </summary>
        public event Action? OnWorkCompleted;

        /// <summary>
        /// 番茄钟休息完成时触发
        /// </summary>
        public event Action? OnRestCompleted;

        // ==================== 构造函数 ====================

        /// <summary>
        /// 构造函数 — 接收服务依赖，初始化所有命令
        /// </summary>
        /// <param name="dbService">数据库服务</param>
        /// <param name="pomodoroService">番茄钟服务</param>
        public MainViewModel(DatabaseService dbService, PomodoroService pomodoroService)
        {
            _dbService = dbService;
            _pomodoroService = pomodoroService;

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

            // 番茄钟控制命令
            StartPomodoroCommand = new RelayCommand(_ => StartPomodoro(),
                _ => !IsTimerRunning);
            PausePomodoroCommand = new RelayCommand(_ => _pomodoroService.TogglePause(),
                _ => IsTimerRunning);
            StopPomodoroCommand = new RelayCommand(_ => _pomodoroService.Stop(),
                _ => IsTimerRunning);

            // 打开子窗体命令（具体的窗体创建逻辑在 View 层处理）
            OpenHistoryCommand = new RelayCommand(_ => OpenHistoryWindow());
            OpenSettingsCommand = new RelayCommand(_ => OpenSettingsWindow());

            // ---------- 订阅番茄钟事件 ----------

            // 每秒更新 UI 显示
            _pomodoroService.Tick += (min, sec, progress) =>
            {
                TimerDisplay = $"{min:D2}:{sec:D2}";
                TimerProgress = progress;
            };

            // 状态变更时更新状态文本和运行标记
            _pomodoroService.StateChanged += state =>
            {
                PomodoroStatusText = state switch
                {
                    PomodoroService.PomodoroState.Working => "专注中",
                    PomodoroService.PomodoroState.Paused => "已暂停",
                    PomodoroService.PomodoroState.Resting => "休息中",
                    _ => "空闲"
                };

                IsTimerRunning = state != PomodoroService.PomodoroState.Idle;
            };

            // 工作完成 → 通知 View 弹出提醒窗口
            _pomodoroService.WorkCompleted += () => OnWorkCompleted?.Invoke();
            _pomodoroService.RestCompleted += () => OnRestCompleted?.Invoke();
        }

        // ==================== 任务操作方法 ====================

        /// <summary>
        /// 加载今日任务（应用启动时和添加/删除后调用）
        /// </summary>
        public async Task LoadTodayTasksAsync()
        {
            var tasks = await _dbService.GetTodayTasksAsync();

            TodayTasks.Clear();
            foreach (var task in tasks)
            {
                TodayTasks.Add(task);
            }

            // 刷新统计
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
                IsCompleted = false
            };

            await _dbService.AddTaskAsync(task);
            NewTaskTitle = string.Empty;  // 清空输入框
            await LoadTodayTasksAsync();  // 刷新列表
        }

        /// <summary>
        /// 切换任务的完成/未完成状态
        /// </summary>
        private async Task ToggleTaskAsync(TaskItem task)
        {
            task.IsCompleted = !task.IsCompleted;
            task.CompletedAt = task.IsCompleted ? DateTime.Now : null;
            await _dbService.UpdateTaskAsync(task);
            await LoadTodayTasksAsync(); // 刷新列表以更新 UI
        }

        /// <summary>
        /// 删除任务
        /// </summary>
        private async Task DeleteTaskAsync(TaskItem task)
        {
            await _dbService.DeleteTaskAsync(task);
            await LoadTodayTasksAsync(); // 刷新列表
        }

        /// <summary>
        /// 更新统计数字（完成数 / 总数）
        /// </summary>
        private void UpdateCounts()
        {
            TotalCount = TodayTasks.Count;
            CompletedCount = TodayTasks.Count(t => t.IsCompleted);
        }

        // ==================== 番茄钟操作方法 ====================

        /// <summary>
        /// 开始番茄钟工作计时
        /// </summary>
        private void StartPomodoro()
        {
            // 更新初始显示
            TimerDisplay = $"{_pomodoroService.WorkMinutes:D2}:00";
            TimerProgress = 1.0;
            _pomodoroService.StartWork();
        }

        /// <summary>
        /// 开始番茄钟休息计时（由 ToastWindow 调用）
        /// </summary>
        public void StartRest()
        {
            TimerDisplay = $"{_pomodoroService.RestMinutes:D2}:00";
            TimerProgress = 1.0;
            _pomodoroService.StartRest();
        }

        // ==================== 子窗体打开 ====================

        /// <summary>
        /// 打开历史查询窗体
        /// </summary>
        private void OpenHistoryWindow()
        {
            var historyVM = new HistoryViewModel(_dbService);
            var historyWindow = new Views.HistoryWindow
            {
                DataContext = historyVM,
                Owner = Application.Current.MainWindow
            };
            historyWindow.ShowDialog();
        }

        /// <summary>
        /// 打开设置窗体
        /// </summary>
        private void OpenSettingsWindow()
        {
            var settingsWindow = new Views.SettingsWindow(_pomodoroService)
            {
                Owner = Application.Current.MainWindow
            };
            settingsWindow.ShowDialog();
        }
    }
}
