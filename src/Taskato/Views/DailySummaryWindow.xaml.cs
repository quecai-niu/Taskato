using System.Windows;
using System.Windows.Input;
using Taskato.Models;
using Taskato.Utils;
using Taskato.ViewModels;

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
            VisualEffects.Initialize(this);
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
                VisualEffects.RunWithTemporaryReduction(this, () =>
                {
                    WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
                });
            else
                VisualEffects.RunWithTemporaryReduction(this, DragMove);
        }

        private void MinimizeButton_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

        private void MaximizeButton_Click(object sender, RoutedEventArgs e)
            => VisualEffects.RunWithTemporaryReduction(this, () =>
            {
                WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
            });

        private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

        private async void CompletedTaskCard_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount != 2 ||
                sender is not FrameworkElement element ||
                element.DataContext is not TaskItem task)
            {
                return;
            }

            var detailWindow = new TaskDetailWindow(task) { Owner = this };
            detailWindow.ShowDialog();

            if (detailWindow.IsSaved)
            {
                task.Title = detailWindow.EditingTask.Title;
                task.Priority = detailWindow.EditingTask.Priority;
                task.IsCompleted = detailWindow.EditingTask.IsCompleted;
                task.CompletedAt = detailWindow.EditingTask.CompletedAt;
                task.CompletionDurationMinutes = detailWindow.EditingTask.CompletionDurationMinutes;

                if (DataContext is DailySummaryViewModel vm)
                    await vm.SaveTaskEditAsync(task);
            }

            e.Handled = true;
        }
    }
}
