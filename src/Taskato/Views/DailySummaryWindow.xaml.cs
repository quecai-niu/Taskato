using System.Windows;
using System.Windows.Input;

namespace Taskato.Views
{
    /// <summary>
    /// 每日总结窗体的代码后台 — 处理标题栏拖拽和窗口控制按钮
    /// </summary>
    public partial class DailySummaryWindow : Window
    {
        public DailySummaryWindow()
        {
            InitializeComponent();
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
                WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
            else
                DragMove();
        }

        private void MinimizeButton_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

        private void MaximizeButton_Click(object sender, RoutedEventArgs e)
            => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

        private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
    }
}
