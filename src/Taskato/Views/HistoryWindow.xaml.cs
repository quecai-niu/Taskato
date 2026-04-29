using System.Windows;
using System.Windows.Input;
using Taskato.ViewModels;

namespace Taskato.Views
{
    /// <summary>
    /// 历史查询窗体的代码后台
    /// </summary>
    public partial class HistoryWindow : Window
    {
        public HistoryWindow()
        {
            InitializeComponent();

            // 窗体加载后自动执行一次搜索，显示默认范围内的任务
            Loaded += async (s, e) =>
            {
                if (DataContext is HistoryViewModel vm)
                {
                    await vm.SearchAsync();
                }
            };
        }

        /// <summary>
        /// 拖拽标题栏移动窗体
        /// </summary>
        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            DragMove();
        }

        /// <summary>
        /// 关闭按钮 (RoutedEventArgs 版本，兼容 Button 控件)
        /// </summary>
        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        /// <summary>
        /// 点击删除按钮 → 弹出确认并执行历史删除
        /// </summary>
        private void DeleteButton_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement element && DataContext is HistoryViewModel vm)
            {
                if (ConfirmDialog.Show(this, "确定要从历史记录中删除这个任务吗？"))
                {
                    vm.DeleteTaskCommand.Execute(element.Tag);
                }
            }
            e.Handled = true;
        }

        /// <summary>
        /// 双击记录卡片 → 弹出编辑详情
        /// </summary>
        private async void TaskCard_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2 && sender is FrameworkElement element && element.DataContext is Models.TaskItem task)
            {
                var detailWindow = new TaskDetailWindow(task) { Owner = this };
                detailWindow.ShowDialog();
                
                // 详情页关闭后，判断用户是否点击了保存
                if (detailWindow.IsSaved)
                {
                    // 将替身数据覆盖回本体
                    task.Title = detailWindow.EditingTask.Title;
                    task.Priority = detailWindow.EditingTask.Priority;

                    if (DataContext is HistoryViewModel vm)
                    {
                        await vm.SaveTaskEditAsync(task);
                    }
                }
                
                e.Handled = true;
            }
        }

        /// <summary>
        /// 监听按键：支持按 Esc 键直接退出历史窗体
        /// </summary>
        protected override void OnKeyDown(System.Windows.Input.KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.Key == Key.Escape)
            {
                Close();
            }
        }
    }
}
