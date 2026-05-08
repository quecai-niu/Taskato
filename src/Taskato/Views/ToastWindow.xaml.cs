using System.Windows;

namespace Taskato.Views
{
    /// <summary>
    /// 番茄钟完成弹窗 — 在屏幕右下角置顶显示
    /// 
    /// 两种使用场景：
    /// 1. 工作完成：显示"休息一下"和"继续工作"按钮
    /// 2. 休息完成：显示"开始工作"按钮
    /// </summary>
    public partial class ToastWindow : Window
    {
        /// <summary>"休息一下"按钮的回调委托</summary>
        private readonly Action? _onRest;

        /// <summary>"继续工作"按钮的回调委托</summary>
        private readonly Action? _onContinue;

        /// <summary>弹窗计时器</summary>
        private System.Windows.Threading.DispatcherTimer? _elapsedTimer;
        private int _secondsElapsed = 0;

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
        /// <param name="soundChoice">提示音方案(0=无声,1=Notify,2=Ding,3=Background,4=Chimes,5=Custom)</param>
        /// <param name="customSoundPath">自定义音效路径</param>
        public ToastWindow(string title, string subtitle,
            Action? onRest, Action? onContinue, bool showTimer = false, bool isRestComplete = false, int soundChoice = 3, string customSoundPath = "")
        {
            InitializeComponent();

            // 设置弹窗文字内容
            TitleText.Text = title;
            SubtitleText.Text = subtitle;

            _onRest = onRest;
            _onContinue = onContinue;
            _soundChoice = soundChoice; // 记录音效方案，供 PlayNotificationSound 使用
            _customSoundPath = customSoundPath;

            // 如果启用了计时器，则初始化并启动
            if (showTimer)
            {
                ElapsedTimerText.Visibility = Visibility.Visible;
                ElapsedTimerText.Text = "已显示 00:00";
                
                _elapsedTimer = new System.Windows.Threading.DispatcherTimer
                {
                    Interval = TimeSpan.FromSeconds(1)
                };
                _elapsedTimer.Tick += (s, e) =>
                {
                    _secondsElapsed++;
                    int m = _secondsElapsed / 60;
                    int sec = _secondsElapsed % 60;
                    ElapsedTimerText.Text = $"已显示 {m:D2}:{sec:D2}";
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

            // 播放提示音 (使用 Windows 默认现代通知音)
            PlayNotificationSound();
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
        /// 窗体加载完成后 → 定位到屏幕右下角
        /// </summary>
        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // 获取工作区大小（排除任务栏）
            var workArea = SystemParameters.WorkArea;

            // 定位到右下角，留 20px 边距
            Left = workArea.Right - ActualWidth - 20;
            Top = workArea.Bottom - ActualHeight - 20;
        }

        /// <summary>
        /// 点击"休息一下"
        /// </summary>
        private void RestButton_Click(object sender, RoutedEventArgs e)
        {
            _onRest?.Invoke();
            Close();
        }

        /// <summary>
        /// 点击"继续工作"
        /// </summary>
        private void ContinueButton_Click(object sender, RoutedEventArgs e)
        {
            StopTimer();
            _onContinue?.Invoke();
            Close();
        }

        private void StopTimer()
        {
            if (_elapsedTimer != null)
            {
                _elapsedTimer.Stop();
                _elapsedTimer = null;
            }
        }

        /// <summary>
        /// 确保关闭窗口时停止计时器
        /// </summary>
        protected override void OnClosed(EventArgs e)
        {
            StopTimer();
            base.OnClosed(e);
        }
    }
}
