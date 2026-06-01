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

        /// <summary>番茄钟服务 — 负责计时逻辑</summary>
        private readonly PomodoroService _pomodoroService;

        /// <summary>用户设置服务 — 负责持久化用户配置</summary>
        private readonly SettingsService _settingsService;

        /// <summary>飞书通知服务</summary>
        private readonly FeishuService _feishuService;
        public FeishuService FeishuService => _feishuService;

        /// <summary>是否开启弹窗计时器（透传配置）</summary>
        public bool EnableToastTimer => _settingsService.Config.EnableToastTimer;

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

        /// <summary>结束时重复发送飞书消息 3 次</summary>
        public bool FeishuRepeatEnabled => _settingsService.Config.FeishuRepeatEnabled;

        /// <summary>休息过半时发送飞书提醒</summary>
        public bool FeishuRestHalfwayEnabled => _settingsService.Config.FeishuRestHalfwayEnabled;

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

        /// <summary>打开历史查询窗体的命令</summary>
        public ICommand OpenHistoryCommand { get; }

        /// <summary>显示设置窗体命令</summary>
        public ICommand OpenSettingsCommand { get; }

        /// <summary>添加附加番茄钟命令</summary>
        public ICommand AddAdditionalTimerCommand { get; }

        /// <summary>移除附加番茄钟命令</summary>
        public ICommand RemoveAdditionalTimerCommand { get; }

        /// <summary>打开每日总结窗体命令</summary>
        public ICommand OpenDailySummaryCommand { get; }

        // ==================== 构造函数 ====================

        /// <summary>
        /// 构造函数 — 接收服务依赖，初始化所有命令
        /// </summary>
        /// <param name="dbService">数据库服务</param>
        /// <param name="pomodoroService">番茄钟服务</param>
        /// <param name="settingsService">设置服务</param>
        public MainViewModel(DatabaseService dbService, PomodoroService pomodoroService, SettingsService settingsService, FeishuService feishuService)
        {
            _dbService = dbService;
            _pomodoroService = pomodoroService;
            _settingsService = settingsService;
            _feishuService = feishuService;

            // 初始化主番茄钟并添加到列表
            var mainTimer = new PomodoroTimerViewModel(_pomodoroService, "主番茄钟", newWorkMinutes => 
            {
                _settingsService.Config.WorkMinutes = newWorkMinutes;
                _settingsService.Save();
            });
            AllTimers.Add(mainTimer);
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
                    }
                });
                AllTimers.Add(timerVM);
            }

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

            // 附加计时器管理
            AddAdditionalTimerCommand = new RelayCommand(_ => AddAdditionalTimer());
            RemoveAdditionalTimerCommand = new RelayCommand(param => 
            {
                if (param is PomodoroTimerViewModel timer)
                {
                    int index = AllTimers.IndexOf(timer) - 1;

                    AllTimers.Remove(timer);
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
                }
            });

            // 打开子窗体命令（具体的窗体创建逻辑在 View 层处理）
            OpenHistoryCommand = new RelayCommand(_ => OpenHistoryWindow());
            OpenSettingsCommand = new RelayCommand(_ => OpenSettingsWindow());
            OpenDailySummaryCommand = new RelayCommand(_ => OpenDailySummaryWindow());

            // ---------- 订阅数据库全局变更事件 ----------
            // 当任何地方（如历史界面）删除了任务或修改了内容，主界面自动同步刷新
            _dbService.OnDataChanged += () =>
            {
                Application.Current.Dispatcher.InvokeAsync(async () =>
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
                        var summaryVM = new DailySummaryViewModel(_dbService)
                        {
                            CurrentDate = yesterdayDate
                        };
                        await summaryVM.LoadSummaryAsync();
                        var summaryWindow = new Views.DailySummaryWindow
                        {
                            DataContext = summaryVM,
                            Owner = Application.Current.MainWindow
                        };
                        summaryWindow.ShowDialog();
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
            // 注意：因为订阅了 OnDataChanged 事件，这里其实不需要手动 LoadTodayTasksAsync，
            // 但为了双重保险也可以保留，或者由事件统一接管
        }

        /// <summary>
        /// 保存任务修改内容（供详情页编辑后调用）
        /// </summary>
        public async Task SaveTaskEditAsync(TaskItem task)
        {
            await _dbService.UpdateTaskAsync(task);
        }

        /// <summary>
        /// 更新统计数字（完成数 / 总数）
        /// </summary>
        private void UpdateCounts()
        {
            TotalCount = TodayTasks.Count;
            CompletedCount = TodayTasks.Count(t => t.IsCompleted);
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
        private void OpenSettingsWindow(Views.SettingsSection? initialSection = null)
        {
            var settingsWindow = new Views.SettingsWindow(_pomodoroService, _settingsService, _feishuService, initialSection)
            {
                Owner = Application.Current.MainWindow
            };
            settingsWindow.ShowDialog();

            if (_pomodoroService.CurrentState == PomodoroService.PomodoroState.Idle &&
                CurrentActiveTimer is not null &&
                CurrentActiveTimer == AllTimers.FirstOrDefault())
            {
                CurrentActiveTimer.WorkMinutes = _pomodoroService.WorkMinutes;
            }

            // 同步所有的子番茄钟的休息时间
            foreach (var timer in AllTimers)
            {
                timer.RestMinutes = _settingsService.Config.RestMinutes;
            }

            // 同步多组模式的状态
            OnPropertyChanged(nameof(IsMultiMode));
        }

        /// <summary>
        /// 打开设置并定位到飞书通知配置。
        /// </summary>
        public void OpenFeishuSettings()
        {
            OpenSettingsWindow(Views.SettingsSection.Feishu);
        }

        /// <summary>
        /// 打开每日总结窗体
        /// </summary>
        private void OpenDailySummaryWindow()
        {
            var summaryVM = new DailySummaryViewModel(_dbService);
            var summaryWindow = new Views.DailySummaryWindow
            {
                DataContext = summaryVM,
                Owner = Application.Current.MainWindow
            };
            summaryWindow.Loaded += async (_, _) => await summaryVM.LoadSummaryAsync();
            summaryWindow.ShowDialog();
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
                }
            });
            AllTimers.Add(timerVM);
            
            // 更新配置并保存
            _settingsService.Config.AdditionalTimersWorkMinutes.Add(_settingsService.Config.WorkMinutes);
            _settingsService.Save();

            // 自动选中新添加的计时器
            CurrentActiveTimer = timerVM;
        }
    }
}
