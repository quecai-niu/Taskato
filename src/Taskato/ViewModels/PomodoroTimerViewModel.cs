using System;
using System.Windows.Input;
using Taskato.Services;
using Taskato.Utils;

namespace Taskato.ViewModels
{
    /// <summary>
    /// 单个番茄钟计时器的 ViewModel
    /// 封装了 PomodoroService 的逻辑，方便在多组模式下复用
    /// </summary>
    public class PomodoroTimerViewModel : ViewModelBase
    {
        private readonly PomodoroService _pomodoroService;
        private string _timerDisplay = "25:00";
        private double _progress = 1.0;
        private string _statusText = "空闲";
        private string _timerName = "番茄钟";
        private bool _isStarted = false;

        public string TimerName
        {
            get => _timerName;
            set => SetProperty(ref _timerName, value);
        }

        public string TimerDisplay
        {
            get => _timerDisplay;
            set => SetProperty(ref _timerDisplay, value);
        }

        public double Progress
        {
            get => _progress;
            set => SetProperty(ref _progress, value);
        }

        public string StatusText
        {
            get => _statusText;
            set => SetProperty(ref _statusText, value);
        }

        public PomodoroService.PomodoroState CurrentState => _pomodoroService.CurrentState;

        public bool IsStarted
        {
            get => _isStarted;
            set => SetProperty(ref _isStarted, value);
        }

        public bool IsWorkingState => _pomodoroService.CurrentState == PomodoroService.PomodoroState.Working;
        public bool IsRestingState => _pomodoroService.CurrentState == PomodoroService.PomodoroState.Resting;
        public bool IsPaused => _pomodoroService.CurrentState == PomodoroService.PomodoroState.Paused;
        public int CompletionElapsedSeconds => _pomodoroService.CompletionElapsedSeconds;
        public bool IsRecoveredCompletion => _pomodoroService.IsRecoveredCompletion;
        public string TimerPhase => _pomodoroService.DisplayPhase == PomodoroService.PomodoroState.Resting
            ? "rest"
            : "work";
        public int RemainingSeconds => _pomodoroService.DisplayRemainingSeconds;
        public int MinimumAdjustableSeconds => PomodoroService.MinimumAdjustableSeconds;
        public int MaximumAdjustableSeconds => _pomodoroService.MaximumAdjustableSeconds;
        public int AdjustmentStepSeconds => PomodoroService.AdjustmentStepSeconds;
        public bool CanAdjustRemaining => _pomodoroService.CanAdjustRemaining;

        public int WorkMinutes
        {
            get => _pomodoroService.WorkMinutes;
            set
            {
                _pomodoroService.WorkMinutes = value;
                if (CurrentState == PomodoroService.PomodoroState.Idle)
                {
                    TimerDisplay = $"{value:D2}:00";
                }
                OnPropertyChanged();
            }
        }

        public int RestMinutes
        {
            get => _pomodoroService.RestMinutes;
            set
            {
                _pomodoroService.RestMinutes = value;
                OnPropertyChanged();
            }
        }

        // 事件转发
        public event Action? WorkCompleted;
        public event Action<PomodoroWorkCompletion>? WorkCompletionRequested;
        public event Action? RestCompleted;
        public event Action? RestHalfway;
        public event Action<PomodoroPhaseTransition>? PhaseTransitioned;

        public ICommand StartWorkCommand { get; }
        public ICommand PauseCommand { get; }
        public ICommand StopCommand { get; }
        public ICommand EarlyRestCommand { get; }
        public ICommand IncreaseTimeCommand { get; }
        public ICommand DecreaseTimeCommand { get; }

        public PomodoroTimerViewModel(
            PomodoroService pomodoroService,
            string name = "番茄钟",
            Action<int>? onTimeAdjusted = null,
            Action? onRuntimeStateChanged = null)
        {
            _pomodoroService = pomodoroService;
            _timerName = name;
            
            TimerDisplay = $"{_pomodoroService.WorkMinutes:D2}:00";

            _pomodoroService.Tick += (m, s, p) =>
            {
                TimerDisplay = CurrentState == PomodoroService.PomodoroState.CompletionPending
                    ? $"+{m:D2}:{s:D2}"
                    : $"{m:D2}:{s:D2}";
                Progress = p;
                OnPropertyChanged(nameof(RemainingSeconds));
                OnPropertyChanged(nameof(CompletionElapsedSeconds));
            };

            _pomodoroService.StateChanged += state =>
            {
                StatusText = state switch
                {
                    PomodoroService.PomodoroState.Idle => "空闲",
                    PomodoroService.PomodoroState.Working => "专注中",
                    PomodoroService.PomodoroState.Paused => "已暂停",
                    PomodoroService.PomodoroState.Resting => "休息中",
                    PomodoroService.PomodoroState.CompletionPending => "等待处理",
                    _ => "未知"
                };
                IsStarted = state != PomodoroService.PomodoroState.Idle;
                OnPropertyChanged(nameof(CurrentState));
                OnPropertyChanged(nameof(IsWorkingState));
                OnPropertyChanged(nameof(IsRestingState));
                OnPropertyChanged(nameof(IsPaused));
                OnPropertyChanged(nameof(CompletionElapsedSeconds));
                OnPropertyChanged(nameof(TimerPhase));
                OnPropertyChanged(nameof(RemainingSeconds));
                OnPropertyChanged(nameof(MaximumAdjustableSeconds));
                OnPropertyChanged(nameof(CanAdjustRemaining));
            };

            _pomodoroService.WorkCompleted += () => WorkCompleted?.Invoke();
            _pomodoroService.WorkCompletionRequested += completion =>
                WorkCompletionRequested?.Invoke(completion);
            _pomodoroService.RestCompleted += () => RestCompleted?.Invoke();
            _pomodoroService.RestHalfway += () => RestHalfway?.Invoke();
            _pomodoroService.PhaseTransitioned += transition => PhaseTransitioned?.Invoke(transition);
            if (onRuntimeStateChanged is not null)
                _pomodoroService.PersistenceRequested += onRuntimeStateChanged;

            StartWorkCommand = new RelayCommand(_ => _pomodoroService.StartWork(), _ => CurrentState == PomodoroService.PomodoroState.Idle || CurrentState == PomodoroService.PomodoroState.Paused);
            PauseCommand = new RelayCommand(
                _ => _pomodoroService.TogglePause(),
                _ => CurrentState is PomodoroService.PomodoroState.Working or
                    PomodoroService.PomodoroState.Resting or
                    PomodoroService.PomodoroState.Paused);
            StopCommand = new RelayCommand(_ => _pomodoroService.Stop(), _ => IsStarted);
            EarlyRestCommand = new RelayCommand(_ => _pomodoroService.CompleteWorkEarly(), _ => IsWorkingState);

            IncreaseTimeCommand = new RelayCommand(_ =>
            {
                if (_pomodoroService.WorkMinutes < 240)
                {
                    _pomodoroService.WorkMinutes = Math.Min(240, _pomodoroService.WorkMinutes + 5);
                    TimerDisplay = $"{_pomodoroService.WorkMinutes:D2}:00";
                    onTimeAdjusted?.Invoke(_pomodoroService.WorkMinutes);
                }
            }, _ => CurrentState == PomodoroService.PomodoroState.Idle);

            DecreaseTimeCommand = new RelayCommand(_ =>
            {
                if (_pomodoroService.WorkMinutes > 1)
                {
                    _pomodoroService.WorkMinutes = Math.Max(1, _pomodoroService.WorkMinutes - 5);
                    TimerDisplay = $"{_pomodoroService.WorkMinutes:D2}:00";
                    onTimeAdjusted?.Invoke(_pomodoroService.WorkMinutes);
                }
            }, _ => CurrentState == PomodoroService.PomodoroState.Idle);
        }

        public void StartRest() => _pomodoroService.StartRest();
        public void StartWork() => _pomodoroService.StartWork();
        public bool TryAdjustRemainingSeconds(int remainingSeconds) =>
            _pomodoroService.TryAdjustRemainingSeconds(remainingSeconds);
        public bool IsValidRemainingAdjustment(int remainingSeconds) =>
            _pomodoroService.IsValidRemainingAdjustment(remainingSeconds);

        /// <summary>
        /// 在系统恢复或宿主重新激活时按绝对截止时间同步，避免休眠期间倒计时停滞。
        /// </summary>
        public void RefreshElapsedTime() => _pomodoroService.RefreshElapsedTime();

        public PomodoroRuntimeSnapshot CreateSnapshot(string slotId) =>
            _pomodoroService.CreateSnapshot(slotId);

        public void Restore(PomodoroRuntimeSnapshot snapshot) =>
            _pomodoroService.Restore(snapshot);
    }
}
