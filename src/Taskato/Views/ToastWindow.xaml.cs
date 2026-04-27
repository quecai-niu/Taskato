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

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="title">弹窗标题（如"番茄钟时间到！"）</param>
        /// <param name="subtitle">副标题（如"你已完成 25 分钟专注工作"）</param>
        /// <param name="onRest">点击"休息"的回调</param>
        /// <param name="onContinue">点击"继续"的回调</param>
        /// <param name="isRestComplete">是否为"休息结束"场景</param>
        public ToastWindow(string title, string subtitle,
            Action? onRest, Action? onContinue, bool isRestComplete = false)
        {
            InitializeComponent();

            // 设置弹窗文字内容
            TitleText.Text = title;
            SubtitleText.Text = subtitle;

            _onRest = onRest;
            _onContinue = onContinue;

            // 休息结束场景：隐藏"休息"按钮，"继续"按钮改为主按钮样式
            if (isRestComplete)
            {
                RestButton.Visibility = Visibility.Collapsed;
                ContinueButton.Content = "💪 开始工作";
                ContinueButton.Style = (Style)FindResource("PrimaryButton");
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
            _onContinue?.Invoke();
            Close();
        }
    }
}
