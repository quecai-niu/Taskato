using System.Windows;

namespace Taskato.Views
{
    /// <summary>
    /// 番茄钟完成弹窗 — 在屏幕四角轮换置顶显示
    /// 
    /// 两种使用场景：
    /// 1. 工作完成：显示"休息一下"和"继续工作"按钮
    /// 2. 休息完成：显示"开始工作"按钮
    /// </summary>
    public partial class ToastWindow : Window
    {
        private const double ScreenMargin = 20;
        private const double MinActionDelaySeconds = 0.0;
        private const double MaxActionDelaySeconds = 5.0;
        private static readonly object PlacementLock = new();
        private static int s_nextPlacementIndex = 0;

        private enum ToastPlacement
        {
            BottomRight,
            BottomLeft,
            TopRight,
            TopLeft
        }

        private static readonly ToastPlacement[] PlacementRotation =
        {
            ToastPlacement.BottomRight,
            ToastPlacement.BottomLeft,
            ToastPlacement.TopRight,
            ToastPlacement.TopLeft
        };

        /// <summary>"休息一下"按钮的回调委托</summary>
        private readonly Action? _onRest;

        /// <summary>"继续工作"按钮的回调委托</summary>
        private readonly Action? _onContinue;
        private readonly Func<string, bool, Task>? _onCompletionSubmit;
        private bool _isSubmitting;

        /// <summary>弹窗计时器</summary>
        private System.Windows.Threading.DispatcherTimer? _elapsedTimer;
        private int _secondsElapsed = 0;
        private DateTimeOffset _elapsedStartedAtUtc;

        /// <summary>操作按钮延迟启用计时器</summary>
        private System.Windows.Threading.DispatcherTimer? _actionDelayTimer;
        private DateTime _actionUnlockAt;
        private readonly double _actionDelaySeconds;

        /// <summary>提示音方案(0=无声,1=Notify,2=Ding,3=Background,4=Chimes,5=Custom)</summary>
        private readonly int _soundChoice;

        /// <summary>自定义音效路径</summary>
        private readonly string _customSoundPath;

        /// <summary>媒体播放器实例（双通道增益）</summary>
        private System.Windows.Media.MediaPlayer? _mediaPlayer1;
        private System.Windows.Media.MediaPlayer? _mediaPlayer2;
        
        /// <summary>WAV播放器实例（双通道增益）</summary>
        private System.Media.SoundPlayer? _soundPlayer1;
        private System.Media.SoundPlayer? _soundPlayer2;

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="title">弹窗标题（如"番茄钟时间到！"）</param>
        /// <param name="subtitle">副标题（如"你已完成 25 分钟专注工作"）</param>
        /// <param name="onRest">点击"休息"的回调</param>
        /// <param name="onContinue">点击"继续"的回调</param>
        /// <param name="showTimer">是否显示弹窗已持续时长的计时器</param>
        /// <param name="isRestComplete">是否为"休息结束"场景</param>
        /// <param name="actionDelaySeconds">操作按钮延迟启用秒数</param>
        /// <param name="soundChoice">提示音方案(0=无声,1=Notify,2=Ding,3=Background,4=Chimes,5=Custom)</param>
        /// <param name="customSoundPath">自定义音效路径</param>
        public ToastWindow(string title, string subtitle,
            Action? onRest, Action? onContinue, bool showTimer = false, bool isRestComplete = false,
            double actionDelaySeconds = 1.5, int soundChoice = 3, string customSoundPath = "",
            int initialElapsedSeconds = 0,
            Func<string, bool, Task>? onCompletionSubmit = null)
        {
            InitializeComponent();

            // 设置弹窗文字内容
            TitleText.Text = title;
            SubtitleText.Text = subtitle;

            _onRest = onRest;
            _onContinue = onContinue;
            _onCompletionSubmit = onCompletionSubmit;
            _soundChoice = soundChoice; // 记录音效方案，供 PlayNotificationSound 使用
            _customSoundPath = customSoundPath;
            _actionDelaySeconds = Math.Clamp(actionDelaySeconds, MinActionDelaySeconds, MaxActionDelaySeconds);

            // 如果启用了计时器，则初始化并启动
            if (showTimer)
            {
                _secondsElapsed = Math.Max(0, initialElapsedSeconds);
                _elapsedStartedAtUtc = DateTimeOffset.UtcNow.AddSeconds(-_secondsElapsed);
                ElapsedTimerText.Visibility = Visibility.Visible;
                ElapsedTimerText.Text = FormatElapsed(_secondsElapsed);
                
                _elapsedTimer = new System.Windows.Threading.DispatcherTimer
                {
                    Interval = TimeSpan.FromSeconds(1)
                };
                _elapsedTimer.Tick += (s, e) =>
                {
                    // 使用绝对时间而非累加 Tick，系统休眠或 UI 忙碌后仍能显示真实经过时长。
                    _secondsElapsed = Math.Max(
                        0,
                        (int)Math.Floor((DateTimeOffset.UtcNow - _elapsedStartedAtUtc).TotalSeconds));
                    ElapsedTimerText.Text = FormatElapsed(_secondsElapsed);
                };
                _elapsedTimer.Start();
            }

            // 休息结束场景：隐藏"休息"按钮，"继续"按钮改为主按钮样式
            if (isRestComplete)
            {
                RestButton.Visibility = Visibility.Collapsed;
                ContinueButton.Content = "💪 开始工作";
                ContinueButton.Style = (Style)FindResource("PrimaryButton");
            }
            else if (_onCompletionSubmit is not null)
            {
                CompletionInputPanel.Visibility = Visibility.Visible;
                RestButton.Content = "☕ 完成并休息";
                ContinueButton.Content = "💪 完成并继续";
                Loaded += (_, _) => CompletionNoteTextBox.Focus();
            }

            // 播放提示音 (使用 Windows 默认现代通知音)
            PlayNotificationSound();
        }

        private static string FormatElapsed(int totalSeconds)
        {
            var elapsed = TimeSpan.FromSeconds(Math.Max(0, totalSeconds));
            if (elapsed.TotalDays >= 1)
                return $"已过去 {(int)elapsed.TotalDays} 天 {elapsed.Hours:D2}:{elapsed.Minutes:D2}:{elapsed.Seconds:D2}";
            if (elapsed.TotalHours >= 1)
                return $"已过去 {(int)elapsed.TotalHours:D2}:{elapsed.Minutes:D2}:{elapsed.Seconds:D2}";
            return $"已过去 {elapsed.Minutes:D2}:{elapsed.Seconds:D2}";
        }

        private void PlayNotificationSound()
        {
            try
            {
                // 根据 soundChoice 字段选择对应音效文件
                var soundMap = new Dictionary<int, string>
                {
                    { 1, @"C:\Windows\Media\Windows Notify.wav" },
                    { 2, @"C:\Windows\Media\Windows Ding.wav" },
                    { 3, @"C:\Windows\Media\Windows Background.wav" },
                    { 4, @"C:\Windows\Media\chimes.wav" },
                    { 5, _customSoundPath }
                };

                if (_soundChoice == 0) return; // 无声模式

                if (!soundMap.TryGetValue(_soundChoice, out string? soundPath) || !System.IO.File.Exists(soundPath))
                {
                    System.Media.SystemSounds.Exclamation.Play();
                    return;
                }

                if (soundPath.EndsWith(".wav", StringComparison.OrdinalIgnoreCase))
                {
                    // 双通道并行播放 WAV：叠加振幅，感官音量提升约 6dB
                    _soundPlayer1 = new System.Media.SoundPlayer(soundPath);
                    _soundPlayer2 = new System.Media.SoundPlayer(soundPath);
                    _soundPlayer1.Play();
                    _soundPlayer2.Play();
                }
                else
                {
                    // 双通道并行播放 MP3/其他
                    _mediaPlayer1 = new System.Windows.Media.MediaPlayer();
                    _mediaPlayer2 = new System.Windows.Media.MediaPlayer();
                    _mediaPlayer1.Volume = 1.0;
                    _mediaPlayer2.Volume = 1.0;
                    _mediaPlayer1.Open(new Uri(soundPath, UriKind.Absolute));
                    _mediaPlayer2.Open(new Uri(soundPath, UriKind.Absolute));
                    _mediaPlayer1.Play();
                    _mediaPlayer2.Play();
                }
            }
            catch
            {
                // 忽略播放错误，不影响核心逻辑
            }
        }

        /// <summary>
        /// 窗体加载完成后 → 按顺序轮换到屏幕四角
        /// </summary>
        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // 获取工作区大小（排除任务栏）
            var workArea = SystemParameters.WorkArea;
            var placement = GetNextPlacement();

            switch (placement)
            {
                case ToastPlacement.BottomLeft:
                    Left = workArea.Left + ScreenMargin;
                    Top = workArea.Bottom - ActualHeight - ScreenMargin;
                    break;
                case ToastPlacement.TopRight:
                    Left = workArea.Right - ActualWidth - ScreenMargin;
                    Top = workArea.Top + ScreenMargin;
                    break;
                case ToastPlacement.TopLeft:
                    Left = workArea.Left + ScreenMargin;
                    Top = workArea.Top + ScreenMargin;
                    break;
                case ToastPlacement.BottomRight:
                default:
                    Left = workArea.Right - ActualWidth - ScreenMargin;
                    Top = workArea.Bottom - ActualHeight - ScreenMargin;
                    break;
            }

            StartActionDelay();
        }

        private static ToastPlacement GetNextPlacement()
        {
            lock (PlacementLock)
            {
                var placement = PlacementRotation[s_nextPlacementIndex];
                s_nextPlacementIndex = (s_nextPlacementIndex + 1) % PlacementRotation.Length;
                return placement;
            }
        }

        private void StartActionDelay()
        {
            if (_actionDelaySeconds <= 0)
            {
                SetActionButtonsEnabled(true);
                // Hidden 会保留倒计时提示的布局空间，避免按钮解锁时窗口高度突变。
                ActionDelayText.Visibility = Visibility.Hidden;
                return;
            }

            _actionUnlockAt = DateTime.Now.AddSeconds(_actionDelaySeconds);
            SetActionButtonsEnabled(false);
            ActionDelayText.Visibility = Visibility.Visible;
            UpdateActionDelayText();

            _actionDelayTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(100)
            };
            _actionDelayTimer.Tick += (_, _) =>
            {
                if ((_actionUnlockAt - DateTime.Now).TotalMilliseconds <= 0)
                {
                    StopActionDelay();
                    SetActionButtonsEnabled(true);
                    ActionDelayText.Visibility = Visibility.Hidden;
                    return;
                }

                UpdateActionDelayText();
            };
            _actionDelayTimer.Start();
        }

        private void UpdateActionDelayText()
        {
            var remainingSeconds = Math.Max(0, (_actionUnlockAt - DateTime.Now).TotalSeconds);
            ActionDelayText.Text = $"请先看一眼 · {remainingSeconds:0.0} 秒后可操作";
        }

        private void SetActionButtonsEnabled(bool isEnabled)
        {
            var effectiveEnabled = isEnabled && !_isSubmitting;
            RestButton.IsEnabled = effectiveEnabled;
            ContinueButton.IsEnabled = effectiveEnabled;
            RestButton.Opacity = effectiveEnabled ? 1.0 : 0.55;
            ContinueButton.Opacity = effectiveEnabled ? 1.0 : 0.55;
        }

        public void SetCompletionDestination(string displayName)
        {
            DestinationText.Text = $"将归入：{displayName}";
        }

        /// <summary>
        /// 将提醒窗口收进任务栏，保留当前计时与操作状态。
        /// </summary>
        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        /// <summary>
        /// 点击"休息一下"
        /// </summary>
        private async void RestButton_Click(object sender, RoutedEventArgs e)
        {
            if (!RestButton.IsEnabled) return;

            if (_onCompletionSubmit is not null)
            {
                await SubmitCompletionAsync(startRest: true);
                return;
            }

            _onRest?.Invoke();
            Close();
        }

        /// <summary>
        /// 点击"继续工作"
        /// </summary>
        private async void ContinueButton_Click(object sender, RoutedEventArgs e)
        {
            if (!ContinueButton.IsEnabled) return;

            if (_onCompletionSubmit is not null)
            {
                await SubmitCompletionAsync(startRest: false);
                return;
            }

            StopTimer();
            _onContinue?.Invoke();
            Close();
        }

        private async Task SubmitCompletionAsync(bool startRest)
        {
            if (_onCompletionSubmit is null || _isSubmitting) return;

            _isSubmitting = true;
            SubmissionErrorText.Visibility = Visibility.Collapsed;
            SetActionButtonsEnabled(false);
            try
            {
                await _onCompletionSubmit(CompletionNoteTextBox.Text, startRest);
                StopTimer();
                Close();
            }
            catch (Exception exception)
            {
                SubmissionErrorText.Text = exception.Message;
                SubmissionErrorText.Visibility = Visibility.Visible;
                _isSubmitting = false;
                SetActionButtonsEnabled(true);
                CompletionNoteTextBox.Focus();
            }
        }

        private void StopTimer()
        {
            if (_elapsedTimer != null)
            {
                _elapsedTimer.Stop();
                _elapsedTimer = null;
            }
        }

        private void StopActionDelay()
        {
            if (_actionDelayTimer != null)
            {
                _actionDelayTimer.Stop();
                _actionDelayTimer = null;
            }
        }

        /// <summary>
        /// 确保关闭窗口时停止计时器
        /// </summary>
        protected override void OnClosed(EventArgs e)
        {
            StopTimer();
            StopActionDelay();
            base.OnClosed(e);
        }
    }
}
