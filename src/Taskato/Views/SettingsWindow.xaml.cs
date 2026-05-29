using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Taskato.Services;

namespace Taskato.Views
{
    /// <summary>
    /// 设置窗体的代码后台
    /// 
    /// 因为设置项较少且直接操作 Service 层，没有使用 ViewModel。
    /// 对于这种简单的配置窗体，直接在 Code-Behind 中处理更简洁。
    /// </summary>
    public partial class SettingsWindow : Window
    {
        /// <summary>番茄钟服务（直接修改工作/休息时长）</summary>
        private readonly PomodoroService _pomodoroService;

        /// <summary>用户设置服务，用于持久化</summary>
        private readonly SettingsService _settingsService;

        /// <summary>飞书通知服务，用于测试发送</summary>
        private readonly FeishuService _feishuService;

        /// <summary>当前选中的颜色选项索引</summary>
        private int _selectedColorIndex = 0;

        /// <summary>
        /// 可选的主题颜色列表（渐变色的起止色对）
        /// </summary>
        private readonly (Color Start, Color End)[] _themeColors = new[]
        {
            // 紫色（默认）
            ((Color)ColorConverter.ConvertFromString("#6366F1"),
             (Color)ColorConverter.ConvertFromString("#A855F7")),
            // 粉色
            ((Color)ColorConverter.ConvertFromString("#EC4899"),
             (Color)ColorConverter.ConvertFromString("#F43F5E")),
            // 青色
            ((Color)ColorConverter.ConvertFromString("#14B8A6"),
             (Color)ColorConverter.ConvertFromString("#06B6D4")),
            // 橙色
            ((Color)ColorConverter.ConvertFromString("#F59E0B"),
             (Color)ColorConverter.ConvertFromString("#F97316")),
            // 绿色
            ((Color)ColorConverter.ConvertFromString("#22C55E"),
             (Color)ColorConverter.ConvertFromString("#10B981")),
            // 蓝色
            ((Color)ColorConverter.ConvertFromString("#3B82F6"),
             (Color)ColorConverter.ConvertFromString("#2563EB")),
        };

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="pomodoroService">番茄钟服务实例</param>
        /// <param name="settingsService">设置服务实例</param>
        public SettingsWindow(PomodoroService pomodoroService, SettingsService settingsService, FeishuService feishuService)
        {
            InitializeComponent();
            _pomodoroService = pomodoroService;
            _settingsService = settingsService;
            _feishuService = feishuService;

            // 加载选中的主题索引
            _selectedColorIndex = _settingsService.Config.SelectedColorIndex;

            // 显示当前的时长设置
            WorkTimeText.Text = $"{_pomodoroService.WorkMinutes} min";
            RestTimeText.Text = $"{_pomodoroService.RestMinutes} min";

            // 读取开关状态
            AutoStartCheckBox.IsChecked = AutoStartService.IsAutoStartEnabled();
            AutoStartNextCheckBox.IsChecked = _settingsService.Config.AutoStartNextPomodoro;
            ToastTimerCheckBox.IsChecked = _settingsService.Config.EnableToastTimer;
            MultiPomodoroCheckBox.IsChecked = _settingsService.Config.EnableMultiplePomodoros;

            // 加载飞书通知配置
            FeishuEnabledCheckBox.IsChecked = _settingsService.Config.FeishuEnabled;
            FeishuWebhookUrlBox.Text = _settingsService.Config.FeishuWebhookUrl;
            FeishuNotifyWorkCheckBox.IsChecked = _settingsService.Config.FeishuNotifyOnWork;
            FeishuNotifyRestCheckBox.IsChecked = _settingsService.Config.FeishuNotifyOnRest;
            FeishuRepeatCheckBox.IsChecked = _settingsService.Config.FeishuRepeatEnabled;
            FeishuRestHalfwayCheckBox.IsChecked = _settingsService.Config.FeishuRestHalfwayEnabled;
            AutoDailySummaryCheckBox.IsChecked = _settingsService.Config.AutoShowDailySummary;

            // 初始化提示音选择 — 按配置值勾选对应 RadioButton
            InitSoundRadio(_settingsService.Config.NotificationSoundChoice);

            // 构建颜色选择面板
            BuildColorPalette();
        }

        // ==================== 主题颜色选择 ====================

        /// <summary>
        /// 动态创建颜色选择圆块
        /// </summary>
        private void BuildColorPalette()
        {
            ColorPalette.Children.Clear();

            for (int i = 0; i < _themeColors.Length; i++)
            {
                var (start, end) = _themeColors[i];
                var index = i; // 闭包捕获

                // 创建圆角方块
                var border = new Border
                {
                    Width = 40,
                    Height = 40,
                    CornerRadius = new CornerRadius(10),
                    Margin = new Thickness(0, 0, 10, 0),
                    Cursor = Cursors.Hand,
                    Background = new LinearGradientBrush(start, end, 135),
                    BorderThickness = new Thickness(3),
                    BorderBrush = Brushes.Transparent
                };

                // 如果是当前选中的，显示白色边框和勾号
                if (i == _selectedColorIndex)
                {
                    border.BorderBrush = Brushes.White;
                    border.Effect = new System.Windows.Media.Effects.DropShadowEffect
                    {
                        Color = System.Windows.Media.Colors.White,
                        BlurRadius = 15,
                        ShadowDepth = 0,
                        Opacity = 0.2
                    };
                    // 勾号
                    border.Child = new TextBlock
                    {
                        Text = "✓",
                        FontSize = 16,
                        FontWeight = FontWeights.Bold,
                        Foreground = Brushes.White,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    };
                }

                // 点击切换主题色
                border.MouseLeftButtonDown += (s, e) =>
                {
                    _selectedColorIndex = index;
                    ApplyThemeColor(start, end);
                    BuildColorPalette(); // 重建以更新选中状态
                };

                ColorPalette.Children.Add(border);
            }
        }

        /// <summary>
        /// 应用主题颜色到全局资源（动态换肤），并保存到配置
        /// </summary>
        private void ApplyThemeColor(Color primary, Color secondary)
        {
            var resources = Application.Current.Resources;

            // 更新颜色资源
            resources["ThemePrimaryColor"] = primary;
            resources["ThemeSecondaryColor"] = secondary;

            // 更新画刷资源
            resources["ThemePrimaryBrush"] = new SolidColorBrush(primary);
            resources["ThemeGradientBrush"] = new LinearGradientBrush(primary, secondary, 135);

            // 保存配置
            _settingsService.Config.ThemePrimaryColor = primary.ToString();
            _settingsService.Config.ThemeSecondaryColor = secondary.ToString();
            _settingsService.Config.SelectedColorIndex = _selectedColorIndex;
            _settingsService.Save();
        }

        // ==================== 番茄钟时长调节 ====================

        /// <summary>工作时长 -1分钟</summary>
        private void DecWorkTime_Click(object sender, RoutedEventArgs e)
        {
            if (_pomodoroService.WorkMinutes > 1)
            {
                _pomodoroService.WorkMinutes -= 1;
            }
            WorkTimeText.Text = $"{_pomodoroService.WorkMinutes} min";
            
            _settingsService.Config.WorkMinutes = _pomodoroService.WorkMinutes;
            _settingsService.Save();
        }

        /// <summary>工作时长 +1分钟</summary>
        private void IncWorkTime_Click(object sender, RoutedEventArgs e)
        {
            if (_pomodoroService.WorkMinutes < 240)
            {
                _pomodoroService.WorkMinutes += 1;
            }
            WorkTimeText.Text = $"{_pomodoroService.WorkMinutes} min";
            
            _settingsService.Config.WorkMinutes = _pomodoroService.WorkMinutes;
            _settingsService.Save();
        }

        /// <summary>休息时长 -1分钟（最少 1 分钟）</summary>
        private void DecRestTime_Click(object sender, RoutedEventArgs e)
        {
            if (_pomodoroService.RestMinutes > 1)
            {
                _pomodoroService.RestMinutes -= 1;
                RestTimeText.Text = $"{_pomodoroService.RestMinutes} min";
                
                _settingsService.Config.RestMinutes = _pomodoroService.RestMinutes;
                _settingsService.Save();
            }
        }

        /// <summary>休息时长 +1分钟（最多 30 分钟）</summary>
        private void IncRestTime_Click(object sender, RoutedEventArgs e)
        {
            if (_pomodoroService.RestMinutes < 30)
            {
                _pomodoroService.RestMinutes += 1;
                RestTimeText.Text = $"{_pomodoroService.RestMinutes} min";
                
                _settingsService.Config.RestMinutes = _pomodoroService.RestMinutes;
                _settingsService.Save();
            }
        }

        // ==================== 系统设置 ====================

        /// <summary>
        /// 开机自启复选框状态变更
        /// </summary>
        private void AutoStartCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            var isChecked = AutoStartCheckBox.IsChecked == true;
            AutoStartService.SetAutoStart(isChecked);
        }

        /// <summary>
        /// 继续工作时自动开始下个番茄钟的状态变更
        /// </summary>
        private void AutoStartNextCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (_settingsService != null) // 避免初始化时触发
            {
                _settingsService.Config.AutoStartNextPomodoro = AutoStartNextCheckBox.IsChecked == true;
                _settingsService.Save();
            }
        }
        /// <summary>
        /// 弹窗计时器开关变更
        /// </summary>
        private void ToastTimerCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (_settingsService != null)
            {
                _settingsService.Config.EnableToastTimer = ToastTimerCheckBox.IsChecked == true;
                _settingsService.Save();
            }
        }

        /// <summary>
        /// 多组番茄钟模式开关变更
        /// </summary>
        private void MultiPomodoroCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (_settingsService != null)
            {
                _settingsService.Config.EnableMultiplePomodoros = MultiPomodoroCheckBox.IsChecked == true;
                _settingsService.Save();
            }
        }

        /// <summary>
        /// 提示音选择变更 — 保存并同步配置
        /// </summary>
        private void SoundChoiceComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // 备用，实际不再使用（ComboBox 已改为 RadioButton）
        }

        /// <summary>
        /// RadioButton 选中事件 — 保存声音方案并更新配置
        /// </summary>
        private void SoundRadio_Checked(object sender, RoutedEventArgs e)
        {
            if (_settingsService == null) return;
            if (sender is System.Windows.Controls.RadioButton rb && int.TryParse(rb.Tag?.ToString(), out int choice))
            {
                _settingsService.Config.NotificationSoundChoice = choice;
                _settingsService.Save();

                if (SelectCustomSoundBtn != null && CustomSoundPathText != null)
                {
                    SelectCustomSoundBtn.Visibility = choice == 5 ? Visibility.Visible : Visibility.Collapsed;
                    CustomSoundPathText.Visibility = choice == 5 ? Visibility.Visible : Visibility.Collapsed;
                }
            }
        }

        /// <summary>
        /// 根据索引勾选对应的 RadioButton
        /// </summary>
        private void InitSoundRadio(int index)
        {
            var radios = new[] { Sound0, Sound1, Sound2, Sound3, Sound4, Sound5 };
            int safeIndex = Math.Clamp(index, 0, radios.Length - 1);
            // 暫时解除事件避免初始化时触发保存
            foreach (var r in radios) r.Checked -= SoundRadio_Checked;
            radios[safeIndex].IsChecked = true;
            
            // 更新自定义音效路径显示
            if (!string.IsNullOrEmpty(_settingsService.Config.CustomSoundPath))
            {
                CustomSoundPathText.Text = System.IO.Path.GetFileName(_settingsService.Config.CustomSoundPath);
            }
            
            // 初始设置按钮可见性
            SelectCustomSoundBtn.Visibility = safeIndex == 5 ? Visibility.Visible : Visibility.Collapsed;
            CustomSoundPathText.Visibility = safeIndex == 5 ? Visibility.Visible : Visibility.Collapsed;

            foreach (var r in radios) r.Checked += SoundRadio_Checked;
        }

        private System.Windows.Media.MediaPlayer? _mediaPlayer1;
        private System.Windows.Media.MediaPlayer? _mediaPlayer2;
        private System.Media.SoundPlayer? _soundPlayer1;
        private System.Media.SoundPlayer? _soundPlayer2;

        /// <summary>
        /// 试听按钮 — 使用 MediaPlayer 异步播放以避免卡顿
        /// </summary>
        private void PreviewSoundBtn_Click(object sender, RoutedEventArgs e)
        {
            var soundMap = new System.Collections.Generic.Dictionary<int, string>
            {
                { 1, @"C:\Windows\Media\Windows Notify.wav" },
                { 2, @"C:\Windows\Media\Windows Ding.wav" },
                { 3, @"C:\Windows\Media\Windows Background.wav" },
                { 4, @"C:\Windows\Media\chimes.wav" },
                { 5, _settingsService.Config.CustomSoundPath }
            };

            int choice = _settingsService.Config.NotificationSoundChoice;
            if (choice == 0) return; // 无声

            if (soundMap.TryGetValue(choice, out string? path) && System.IO.File.Exists(path))
            {
                if (path.EndsWith(".wav", StringComparison.OrdinalIgnoreCase))
                {
                    // 双通道并行试听
                    if (_soundPlayer1 != null) _soundPlayer1.Dispose();
                    if (_soundPlayer2 != null) _soundPlayer2.Dispose();
                    _soundPlayer1 = new System.Media.SoundPlayer(path);
                    _soundPlayer2 = new System.Media.SoundPlayer(path);
                    _soundPlayer1.Play();
                    _soundPlayer2.Play();
                }
                else
                {
                    if (_mediaPlayer1 == null) _mediaPlayer1 = new System.Windows.Media.MediaPlayer();
                    if (_mediaPlayer2 == null) _mediaPlayer2 = new System.Windows.Media.MediaPlayer();
                    
                    _mediaPlayer1.Volume = 1.0;
                    _mediaPlayer2.Volume = 1.0;
                    _mediaPlayer1.Open(new Uri(path, UriKind.Absolute));
                    _mediaPlayer2.Open(new Uri(path, UriKind.Absolute));
                    _mediaPlayer1.Play();
                    _mediaPlayer2.Play();
                }
            }
        }

        private void SelectCustomSoundBtn_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "音频文件|*.wav;*.mp3;*.wma|所有文件|*.*",
                Title = "选择自定义音效"
            };

            if (dialog.ShowDialog() == true)
            {
                _settingsService.Config.CustomSoundPath = dialog.FileName;
                _settingsService.Save();
                CustomSoundPathText.Text = System.IO.Path.GetFileName(dialog.FileName);
            }
        }

        // ==================== 窗体操作 ====================

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            DragMove();
        }

        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void MaximizeButton_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        }

        /// <summary>
        /// 关闭按钮 (RoutedEventArgs 版本)
        /// </summary>
        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        // ==================== 飞书通知设置 ====================

        private void FeishuEnabledCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (_settingsService == null) return;
            _settingsService.Config.FeishuEnabled = FeishuEnabledCheckBox.IsChecked == true;
            _settingsService.Save();
        }

        private void FeishuWebhookUrlBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_settingsService == null) return;
            _settingsService.Config.FeishuWebhookUrl = FeishuWebhookUrlBox.Text;
            _settingsService.Save();
        }

        private void FeishuNotifyWorkCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (_settingsService == null) return;
            _settingsService.Config.FeishuNotifyOnWork = FeishuNotifyWorkCheckBox.IsChecked == true;
            _settingsService.Save();
        }

        private void FeishuNotifyRestCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (_settingsService == null) return;
            _settingsService.Config.FeishuNotifyOnRest = FeishuNotifyRestCheckBox.IsChecked == true;
            _settingsService.Save();
        }

        private void FeishuRepeatCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (_settingsService == null) return;
            _settingsService.Config.FeishuRepeatEnabled = FeishuRepeatCheckBox.IsChecked == true;
            _settingsService.Save();
        }

        private void FeishuRestHalfwayCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (_settingsService == null) return;
            _settingsService.Config.FeishuRestHalfwayEnabled = FeishuRestHalfwayCheckBox.IsChecked == true;
            _settingsService.Save();
        }

        private async void FeishuTestBtn_Click(object sender, RoutedEventArgs e)
        {
            FeishuTestBtn.IsEnabled = false;
            FeishuTestBtn.Content = "发送中...";
            var ok = await _feishuService.SendAsync("Taskato 测试消息", "如果你收到这条消息，说明飞书 Webhook 配置成功！");
            FeishuTestBtn.Content = ok ? "发送成功" : "发送失败";
            // 2 秒后恢复按钮文字
            await Task.Delay(2000);
            FeishuTestBtn.Content = "测试";
            FeishuTestBtn.IsEnabled = true;
        }

        /// <summary>
        /// 每日自动总结开关变更
        /// </summary>
        private void AutoDailySummaryCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (_settingsService == null) return;
            _settingsService.Config.AutoShowDailySummary = AutoDailySummaryCheckBox.IsChecked == true;
            _settingsService.Save();
        }
    }
}
