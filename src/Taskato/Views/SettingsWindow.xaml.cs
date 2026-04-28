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
        public SettingsWindow(PomodoroService pomodoroService, SettingsService settingsService)
        {
            InitializeComponent();
            _pomodoroService = pomodoroService;
            _settingsService = settingsService;

            // 加载选中的主题索引
            _selectedColorIndex = _settingsService.Config.SelectedColorIndex;

            // 显示当前的时长设置
            WorkTimeText.Text = $"{_pomodoroService.WorkMinutes} min";
            RestTimeText.Text = $"{_pomodoroService.RestMinutes} min";

            // 读取开机自启状态与连续专注状态
            AutoStartCheckBox.IsChecked = AutoStartService.IsAutoStartEnabled();
            AutoStartNextCheckBox.IsChecked = _settingsService.Config.AutoStartNextPomodoro;

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

        /// <summary>工作时长 -5分钟（最少 1 分钟测试档）</summary>
        private void DecWorkTime_Click(object sender, RoutedEventArgs e)
        {
            if (_pomodoroService.WorkMinutes > 5)
            {
                _pomodoroService.WorkMinutes -= 5;
            }
            else if (_pomodoroService.WorkMinutes == 5)
            {
                _pomodoroService.WorkMinutes = 1;
            }
            WorkTimeText.Text = $"{_pomodoroService.WorkMinutes} min";
            
            _settingsService.Config.WorkMinutes = _pomodoroService.WorkMinutes;
            _settingsService.Save();
        }

        /// <summary>工作时长 +5分钟（最多 60 分钟）</summary>
        private void IncWorkTime_Click(object sender, RoutedEventArgs e)
        {
            if (_pomodoroService.WorkMinutes == 1)
            {
                _pomodoroService.WorkMinutes = 5;
            }
            else if (_pomodoroService.WorkMinutes < 60)
            {
                _pomodoroService.WorkMinutes += 5;
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

        // ==================== 窗体操作 ====================

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            DragMove();
        }

        private void CloseButton_Click(object sender, MouseButtonEventArgs e)
        {
            Close();
        }
    }
}
