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
        public event Action? RestCompleted;

        public ICommand StartWorkCommand { get; }
        public ICommand PauseCommand { get; }
        public ICommand StopCommand { get; }

        public PomodoroTimerViewModel(PomodoroService pomodoroService, string name = "番茄钟")
        {
            _pomodoroService = pomodoroService;
            _timerName = name;
            
            TimerDisplay = $"{_pomodoroService.WorkMinutes:D2}:00";

            _pomodoroService.Tick += (m, s, p) =>
            {
                TimerDisplay = $"{m:D2}:{s:D2}";
                Progress = p;
            };

            _pomodoroService.StateChanged += state =>
            {
                StatusText = state switch
                {
                    PomodoroService.PomodoroState.Idle => "空闲",
                    PomodoroService.PomodoroState.Working => "专注中",
                    PomodoroService.PomodoroState.Paused => "已暂停",
                    PomodoroService.PomodoroState.Resting => "休息中",
                    _ => "未知"
                };
                IsStarted = state != PomodoroService.PomodoroState.Idle;
                OnPropertyChanged(nameof(CurrentState));
            };

            _pomodoroService.WorkCompleted += () => WorkCompleted?.Invoke();
            _pomodoroService.RestCompleted += () => RestCompleted?.Invoke();

            StartWorkCommand = new RelayCommand(_ => _pomodoroService.StartWork(), _ => CurrentState == PomodoroService.PomodoroState.Idle || CurrentState == PomodoroService.PomodoroState.Paused);
            PauseCommand = new RelayCommand(_ => _pomodoroService.TogglePause(), _ => IsStarted);
            StopCommand = new RelayCommand(_ => _pomodoroService.Stop(), _ => IsStarted);
        }

        public void StartRest() => _pomodoroService.StartRest();
    }
}
