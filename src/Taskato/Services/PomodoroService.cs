using System.Windows.Threading;

namespace Taskato.Services
{
    /// <summary>
    /// 番茄钟服务 — 管理工作/休息计时循环
    /// 
    /// 工作流程：
    /// 1. 用户点"开始" → 进入工作计时（默认 25 分钟）
    /// 2. 工作倒计时结束 → 触发 TimerCompleted 事件 → 弹窗提醒
    /// 3. 用户选择"休息一下" → 进入休息计时（默认 5 分钟）
    /// 4. 休息结束 → 触发 RestCompleted 事件
    /// 5. 循环...
    /// </summary>
    public class PomodoroService
    {
        // ==================== 计时器核心 ====================

        /// <summary>
        /// WPF 线程安全的计时器（每秒触发一次 Tick）
        /// 使用 DispatcherTimer 而非 System.Timers.Timer，
        /// 因为它会在 UI 线程上触发事件，避免跨线程更新 UI 的麻烦
        /// </summary>
        private readonly DispatcherTimer _timer;

        /// <summary>
        /// 当前倒计时剩余的总秒数
        /// </summary>
        private int _remainingSeconds;

        /// <summary>
        /// 当前阶段的总时长（秒），用于计算进度条百分比
        /// </summary>
        private int _totalSeconds;

        // ==================== 可配置的时间参数 ====================

        /// <summary>工作时长（分钟），可在设置中修改</summary>
        public int WorkMinutes { get; set; }

        /// <summary>休息时长（分钟），可在设置中修改</summary>
        public int RestMinutes { get; set; }

        // ==================== 状态枚举 ====================

        /// <summary>
        /// 番茄钟的运行状态
        /// </summary>
        public enum PomodoroState
        {
            /// <summary>空闲 — 未启动或已重置</summary>
            Idle,
            /// <summary>工作中 — 正在倒计时工作时间</summary>
            Working,
            /// <summary>已暂停 — 暂时停止倒计时</summary>
            Paused,
            /// <summary>休息中 — 正在倒计时休息时间</summary>
            Resting
        }

        /// <summary>
        /// 当前状态
        /// </summary>
        public PomodoroState CurrentState { get; private set; } = PomodoroState.Idle;

        // ==================== 事件回调 ====================

        /// <summary>
        /// 每秒触发一次 — 用于更新 UI 上的倒计时显示
        /// 参数: (剩余分钟, 剩余秒数, 进度百分比 0.0~1.0)
        /// </summary>
        public event Action<int, int, double>? Tick;

        /// <summary>
        /// 工作计时完成时触发 — 应弹出番茄钟提醒弹窗
        /// </summary>
        public event Action? WorkCompleted;

        /// <summary>
        /// 休息计时完成时触发 — 可提示用户开始新一轮工作
        /// </summary>
        public event Action? RestCompleted;

        /// <summary>
        /// 状态变更时触发 — 用于更新 UI 上的状态标签
        /// </summary>
        public event Action<PomodoroState>? StateChanged;

        // ==================== 构造与方法 ====================

        /// <summary>
        /// 构造函数 — 初始化计时器，设置每秒触发
        /// </summary>
        public PomodoroService(int defaultWorkMinutes = 25, int defaultRestMinutes = 5)
        {
            WorkMinutes = defaultWorkMinutes;
            RestMinutes = defaultRestMinutes;

            _timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1) // 每秒跳动一次
            };
            _timer.Tick += OnTimerTick;
        }

        /// <summary>
        /// 开始工作计时
        /// </summary>
        public void StartWork()
        {
            _totalSeconds = WorkMinutes * 60;       // 总秒数
            _remainingSeconds = _totalSeconds;       // 剩余 = 总
            CurrentState = PomodoroState.Working;
            StateChanged?.Invoke(CurrentState);
            _timer.Start();
        }

        /// <summary>
        /// 开始休息计时
        /// </summary>
        public void StartRest()
        {
            _totalSeconds = RestMinutes * 60;
            _remainingSeconds = _totalSeconds;
            CurrentState = PomodoroState.Resting;
            StateChanged?.Invoke(CurrentState);
            _timer.Start();
        }

        /// <summary>
        /// 暂停/恢复计时
        /// </summary>
        public void TogglePause()
        {
            if (CurrentState == PomodoroState.Paused)
            {
                // 恢复之前的状态（工作或休息）
                CurrentState = _remainingSeconds > RestMinutes * 60
                    ? PomodoroState.Working
                    : PomodoroState.Working; // 简化处理，暂停恢复后都算工作中
                _timer.Start();
            }
            else if (CurrentState == PomodoroState.Working || CurrentState == PomodoroState.Resting)
            {
                // 暂停
                CurrentState = PomodoroState.Paused;
                _timer.Stop();
            }
            StateChanged?.Invoke(CurrentState);
        }

        /// <summary>
        /// 停止并重置计时器
        /// </summary>
        public void Stop()
        {
            _timer.Stop();
            _remainingSeconds = 0;
            _totalSeconds = 0;
            CurrentState = PomodoroState.Idle;
            StateChanged?.Invoke(CurrentState);

            // 触发最后一次 Tick 更新 UI，恢复到随时准备工作的倒计时长
            Tick?.Invoke(WorkMinutes, 0, 1.0);
        }

        /// <summary>
        /// 每秒钟被调用一次的核心逻辑
        /// </summary>
        private void OnTimerTick(object? sender, EventArgs e)
        {
            _remainingSeconds--;

            // 计算显示用的 分:秒
            int minutes = _remainingSeconds / 60;
            int seconds = _remainingSeconds % 60;

            // 计算进度条百分比（1.0 = 刚开始，0.0 = 快结束）
            double progress = _totalSeconds > 0
                ? (double)_remainingSeconds / _totalSeconds
                : 0;

            // 通知 UI 更新
            Tick?.Invoke(minutes, seconds, progress);

            // 倒计时结束
            if (_remainingSeconds <= 0)
            {
                _timer.Stop();
                var previousState = CurrentState;
                CurrentState = PomodoroState.Idle;
                StateChanged?.Invoke(CurrentState);

                // 根据之前的状态触发对应的完成事件
                if (previousState == PomodoroState.Working)
                    WorkCompleted?.Invoke();
                else if (previousState == PomodoroState.Resting)
                    RestCompleted?.Invoke();

                // 恢复面板为主页常驻的默认时长
                Tick?.Invoke(WorkMinutes, 0, 1.0);
            }
        }

        /// <summary>
        /// 获取当前剩余时间的格式化字符串（mm:ss 格式）
        /// 用于系统托盘菜单等地方显示
        /// </summary>
        public string GetTimeDisplay()
        {
            int minutes = _remainingSeconds / 60;
            int seconds = _remainingSeconds % 60;
            return $"{minutes:D2}:{seconds:D2}";
        }
    }
}
