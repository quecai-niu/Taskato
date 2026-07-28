using System.Windows;
using System.Windows.Input;
using Taskato.Utils;

namespace Taskato.Views
{
    /// <summary>应用壳层代码后台，仅处理窗口本身的桌面交互。</summary>
    public partial class MainWindow : Window
    {
        public bool IsAppShuttingDown { get; set; }

        public MainWindow()
        {
            InitializeComponent();
            VisualEffects.Initialize(this);
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
                VisualEffects.RunWithTemporaryReduction(this, ToggleMaximize);
            else
                VisualEffects.RunWithTemporaryReduction(this, DragMove);
        }

        private void MinimizeButton_Click(object sender, RoutedEventArgs e) =>
            WindowState = WindowState.Minimized;

        private void MaximizeButton_Click(object sender, RoutedEventArgs e) =>
            VisualEffects.RunWithTemporaryReduction(this, ToggleMaximize);

        private void CloseButton_Click(object sender, RoutedEventArgs e) => Hide();

        private void ToggleMaximize() =>
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            if (IsAppShuttingDown) return;
            e.Cancel = true;
            Hide();
        }
    }
}
