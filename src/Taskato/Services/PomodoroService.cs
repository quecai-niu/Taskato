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
        private DateTimeOffset? _completedAt;
        private PomodoroState? _completedPhase;
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
            Resting,
            /// <summary>本阶段已到期，等待用户选择休息、继续或停止</summary>
            CompletionPending
        }

        /// <summary>
        /// 当前状态
        /// </summary>
        public PomodoroState CurrentState { get; private set; } = PomodoroState.Idle;
        public bool IsRecoveredCompletion { get; private set; }

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

        /// <summary>开始、暂停、恢复、停止或到期时触发，供宿主持久化稳定状态边界。</summary>
        public event Action? PersistenceRequested;

        /// <summary>
        /// 只在工作或休息阶段的真实边界触发。统计层据此记录有效区间，不能使用每秒 Tick，
        /// 否则 UI 卡顿、系统休眠或程序关闭都会改变统计结果。
        /// </summary>
        public event Action<PomodoroPhaseTransition>? PhaseTransitioned;

        public int CompletionElapsedSeconds
        {
            get
            {
                if (_completedAt is null) return 0;
                var elapsedSeconds = (_clock().ToUniversalTime() - _completedAt.Value).TotalSeconds;
                if (elapsedSeconds <= 0) return 0;
                return elapsedSeconds >= int.MaxValue
                    ? int.MaxValue
                    : (int)Math.Floor(elapsedSeconds);
            }
        }

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
            var startedAt = _clock();
            EndCurrentPhase(startedAt, "replaced");
            _totalSeconds = WorkMinutes * 60;       // 总秒数
            _remainingSeconds = _totalSeconds;       // 剩余 = 总
            _deadline = startedAt.AddSeconds(_remainingSeconds);
            _completedAt = null;
            _completedPhase = null;
            IsRecoveredCompletion = false;
            _restHalfwayRaised = false;
            CurrentState = PomodoroState.Working;
            StateChanged?.Invoke(CurrentState);
            PhaseTransitioned?.Invoke(new PomodoroPhaseTransition(
                PomodoroPhaseTransitionKind.Started,
                Models.PomodoroFocusPhase.Work,
                startedAt));
            PublishTick();
            _timer.Start();
            PersistenceRequested?.Invoke();
        }

        /// <summary>
        /// 开始休息计时
        /// </summary>
        public void StartRest()
        {
            var startedAt = _clock();
            EndCurrentPhase(startedAt, "replaced");
            _totalSeconds = RestMinutes * 60;
            _remainingSeconds = _totalSeconds;
            _deadline = startedAt.AddSeconds(_remainingSeconds);
            _completedAt = null;
            _completedPhase = null;
            IsRecoveredCompletion = false;
            _restHalfwayRaised = false;
            CurrentState = PomodoroState.Resting;
            StateChanged?.Invoke(CurrentState);
            PhaseTransitioned?.Invoke(new PomodoroPhaseTransition(
                PomodoroPhaseTransitionKind.Started,
                Models.PomodoroFocusPhase.Rest,
                startedAt));
            PublishTick();
            _timer.Start();
            PersistenceRequested?.Invoke();
        }

        /// <summary>
        /// 暂停/恢复计时
        /// </summary>
        public void TogglePause()
        {
            if (CurrentState == PomodoroState.Paused)
            {
                var resumedAt = _clock();
                CurrentState = _pausedState;
                _deadline = resumedAt.AddSeconds(_remainingSeconds);
                _timer.Start();
                StateChanged?.Invoke(CurrentState);
                PhaseTransitioned?.Invoke(new PomodoroPhaseTransition(
                    PomodoroPhaseTransitionKind.Resumed,
                    PhaseForState(CurrentState)!.Value,
                    resumedAt));
                PersistenceRequested?.Invoke();
                return;
            }
            else if (CurrentState == PomodoroState.Working || CurrentState == PomodoroState.Resting)
            {
                // 先按绝对截止时间结算，防止在系统唤醒后点击暂停时丢失休眠期间的时间。
                RefreshElapsedTime();
                if (CurrentState is not (PomodoroState.Working or PomodoroState.Resting)) return;
                _pausedState = CurrentState;
                var pausedAt = _clock();
                var phase = PhaseForState(CurrentState)!.Value;
                CurrentState = PomodoroState.Paused;
                _deadline = null;
                _timer.Stop();
                StateChanged?.Invoke(CurrentState);
                PhaseTransitioned?.Invoke(new PomodoroPhaseTransition(
                    PomodoroPhaseTransitionKind.Paused,
                    phase,
                    pausedAt));
                PersistenceRequested?.Invoke();
                return;
            }
        }

        /// <summary>
        /// 停止并重置计时器
        /// </summary>
        public void Stop()
        {
            var stoppedAt = _clock();
            var phase = CurrentPhase();
            _timer.Stop();
            _remainingSeconds = 0;
            _totalSeconds = 0;
            _deadline = null;
            _completedAt = null;
            _completedPhase = null;
            IsRecoveredCompletion = false;
            _restHalfwayRaised = false;
            CurrentState = PomodoroState.Idle;
            StateChanged?.Invoke(CurrentState);
            if (phase.HasValue)
            {
                PhaseTransitioned?.Invoke(new PomodoroPhaseTransition(
                    PomodoroPhaseTransitionKind.Ended,
                    phase.Value,
                    stoppedAt,
                    "stopped"));
            }

            // 触发最后一次 Tick 更新 UI，恢复到随时准备工作的倒计时长
            Tick?.Invoke(WorkMinutes, 0, 1.0);
            PersistenceRequested?.Invoke();
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
            if (CurrentState == PomodoroState.CompletionPending)
            {
                PublishCompletionElapsed();
                return;
            }

            if (CurrentState is not (PomodoroState.Working or PomodoroState.Resting) || _deadline is null)
                return;

            var completedAt = _deadline.Value.ToUniversalTime();
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

            _deadline = null;
            var previousState = CurrentState;
            _completedAt = completedAt;
            _completedPhase = previousState;
            CurrentState = PomodoroState.CompletionPending;
            StateChanged?.Invoke(CurrentState);
            PhaseTransitioned?.Invoke(new PomodoroPhaseTransition(
                PomodoroPhaseTransitionKind.Ended,
                PhaseForState(previousState)!.Value,
                completedAt,
                "completed"));
            PublishCompletionElapsed();
            // 到期后继续每秒刷新“已过去”时间，直到用户明确选择下一步。
            _timer.Start();
            PersistenceRequested?.Invoke();
            if (previousState == PomodoroState.Working)
                WorkCompleted?.Invoke();
            else
                RestCompleted?.Invoke();
        }

        public PomodoroRuntimeSnapshot CreateSnapshot(string slotId) => new()
        {
            SlotId = slotId,
            State = CurrentState,
            PausedState = _pausedState,
            CompletedPhase = _completedPhase,
            RemainingSeconds = _remainingSeconds,
            TotalSeconds = _totalSeconds,
            DeadlineUtc = _deadline?.ToUniversalTime(),
            CompletedAtUtc = _completedAt?.ToUniversalTime(),
            RestHalfwayRaised = _restHalfwayRaised,
            WorkMinutes = WorkMinutes,
            RestMinutes = RestMinutes
        };

        /// <summary>
        /// 恢复已验证的快照。完成事件只在本次恢复中触发一次，后续界面刷新不会重复弹窗。
        /// </summary>
        public void Restore(PomodoroRuntimeSnapshot snapshot)
        {
            _timer.Stop();
            // 缺失字段会反序列化为 0；此时沿用设置中的时长，而不是意外改成一分钟。
            if (snapshot.WorkMinutes is >= 1 and <= 240) WorkMinutes = snapshot.WorkMinutes;
            if (snapshot.RestMinutes is >= 1 and <= 240) RestMinutes = snapshot.RestMinutes;
            _remainingSeconds = Math.Max(0, snapshot.RemainingSeconds);
            _totalSeconds = Math.Max(0, snapshot.TotalSeconds);
            _deadline = snapshot.DeadlineUtc?.ToUniversalTime();
            _completedAt = snapshot.CompletedAtUtc?.ToUniversalTime();
            _completedPhase = snapshot.CompletedPhase;
            _pausedState = snapshot.PausedState is PomodoroState.Working or PomodoroState.Resting
                ? snapshot.PausedState
                : PomodoroState.Working;
            _restHalfwayRaised = snapshot.RestHalfwayRaised;
            CurrentState = snapshot.State;
            IsRecoveredCompletion = false;

            if (CurrentState is PomodoroState.Working or PomodoroState.Resting)
            {
                if (_deadline is null)
                {
                    Stop();
                    return;
                }
                StateChanged?.Invoke(CurrentState);
                PublishTick();
                _timer.Start();
                IsRecoveredCompletion = _deadline.Value <= _clock().ToUniversalTime();
                RefreshElapsedTime();
                return;
            }

            if (CurrentState == PomodoroState.Paused)
            {
                _deadline = null;
                StateChanged?.Invoke(CurrentState);
                PublishTick();
                return;
            }

            if (CurrentState == PomodoroState.CompletionPending &&
                _completedAt is not null &&
                _completedPhase is PomodoroState.Working or PomodoroState.Resting)
            {
                IsRecoveredCompletion = true;
                StateChanged?.Invoke(CurrentState);
                PublishCompletionElapsed();
                _timer.Start();
                if (_completedPhase == PomodoroState.Working) WorkCompleted?.Invoke();
                else RestCompleted?.Invoke();
                return;
            }

            Stop();
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

        private void PublishCompletionElapsed()
        {
            var elapsed = CompletionElapsedSeconds;
            Tick?.Invoke(elapsed / 60, elapsed % 60, 0);
        }

        private Models.PomodoroFocusPhase? CurrentPhase()
        {
            if (CurrentState == PomodoroState.Paused)
                return PhaseForState(_pausedState);
            return PhaseForState(CurrentState);
        }

        private static Models.PomodoroFocusPhase? PhaseForState(PomodoroState state) => state switch
        {
            PomodoroState.Working => Models.PomodoroFocusPhase.Work,
            PomodoroState.Resting => Models.PomodoroFocusPhase.Rest,
            _ => null
        };

        private void EndCurrentPhase(DateTimeOffset endedAt, string reason)
        {
            var phase = CurrentPhase();
            if (!phase.HasValue || CurrentState == PomodoroState.CompletionPending) return;
            PhaseTransitioned?.Invoke(new PomodoroPhaseTransition(
                PomodoroPhaseTransitionKind.Ended,
                phase.Value,
                endedAt,
                reason));
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
