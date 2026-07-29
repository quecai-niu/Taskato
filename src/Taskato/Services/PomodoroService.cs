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
        private readonly Func<DateTimeOffset> _clock;
        private DateTimeOffset? _deadline;
        private PomodoroState _pausedState = PomodoroState.Working;
        private bool _restHalfwayRaised;

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
        /// 休息过半时触发 — 用于飞书提醒等
        /// </summary>
        public event Action? RestHalfway;

        /// <summary>
        /// 状态变更时触发 — 用于更新 UI 上的状态标签
        /// </summary>
        public event Action<PomodoroState>? StateChanged;

        // ==================== 构造与方法 ====================

        /// <summary>
        /// 构造函数 — 初始化计时器，设置每秒触发
        /// </summary>
        public PomodoroService(
            int defaultWorkMinutes = 25,
            int defaultRestMinutes = 5,
            Func<DateTimeOffset>? clock = null)
        {
            WorkMinutes = defaultWorkMinutes;
            RestMinutes = defaultRestMinutes;
            _clock = clock ?? (() => DateTimeOffset.Now);

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
            _deadline = _clock().AddSeconds(_remainingSeconds);
            _restHalfwayRaised = false;
            CurrentState = PomodoroState.Working;
            StateChanged?.Invoke(CurrentState);
            PublishTick();
            _timer.Start();
        }

        /// <summary>
        /// 开始休息计时
        /// </summary>
        public void StartRest()
        {
            _totalSeconds = RestMinutes * 60;
            _remainingSeconds = _totalSeconds;
            _deadline = _clock().AddSeconds(_remainingSeconds);
            _restHalfwayRaised = false;
            CurrentState = PomodoroState.Resting;
            StateChanged?.Invoke(CurrentState);
            PublishTick();
            _timer.Start();
        }

        /// <summary>
        /// 暂停/恢复计时
        /// </summary>
        public void TogglePause()
        {
            if (CurrentState == PomodoroState.Paused)
            {
                CurrentState = _pausedState;
                _deadline = _clock().AddSeconds(_remainingSeconds);
                _timer.Start();
            }
            else if (CurrentState == PomodoroState.Working || CurrentState == PomodoroState.Resting)
            {
                // 先按绝对截止时间结算，防止在系统唤醒后点击暂停时丢失休眠期间的时间。
                RefreshElapsedTime();
                if (CurrentState == PomodoroState.Idle) return;
                _pausedState = CurrentState;
                CurrentState = PomodoroState.Paused;
                _deadline = null;
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
            _deadline = null;
            _restHalfwayRaised = false;
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
            RefreshElapsedTime();
        }

        /// <summary>
        /// 按绝对截止时间同步剩余时间。DispatcherTimer 仅用于刷新界面，因此系统休眠、
        /// UI 线程繁忙或托盘驻留都不会让业务计时变慢；此方法也作为恢复事件和测试边界。
        /// </summary>
        public void RefreshElapsedTime()
        {
            if (CurrentState is not (PomodoroState.Working or PomodoroState.Resting) || _deadline is null)
                return;

            var previousRemaining = _remainingSeconds;
            _remainingSeconds = Math.Max(
                0,
                (int)Math.Ceiling((_deadline.Value - _clock()).TotalSeconds));
            PublishTick();

            if (CurrentState == PomodoroState.Resting &&
                !_restHalfwayRaised &&
                previousRemaining > _totalSeconds / 2 &&
                _remainingSeconds <= _totalSeconds / 2)
            {
                _restHalfwayRaised = true;
                RestHalfway?.Invoke();
            }

            if (_remainingSeconds > 0) return;

            _timer.Stop();
            _deadline = null;
            var previousState = CurrentState;
            CurrentState = PomodoroState.Idle;
            StateChanged?.Invoke(CurrentState);
            if (previousState == PomodoroState.Working)
                WorkCompleted?.Invoke();
            else
                RestCompleted?.Invoke();
            Tick?.Invoke(WorkMinutes, 0, 1.0);
        }

        private void PublishTick()
        {
            var minutes = _remainingSeconds / 60;
            var seconds = _remainingSeconds % 60;
            var progress = _totalSeconds > 0
                ? (double)_remainingSeconds / _totalSeconds
                : 0;
            Tick?.Invoke(minutes, seconds, progress);
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
