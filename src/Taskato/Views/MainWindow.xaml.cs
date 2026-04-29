using System.Windows;
using System.Windows.Input;
using Taskato.ViewModels;

namespace Taskato.Views
{
    /// <summary>
    /// 主窗体的代码后台
    /// 
    /// 注意：MVVM 架构下，业务逻辑应在 ViewModel 中处理。
    /// 这里只处理 View 层特有的 UI 操作（拖拽窗体、窗口按钮等），
    /// 以及一些 XAML 绑定不方便处理的事件转发。
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();

            // 设置日期显示（静态文本，不需要绑定）
            DateLabel.Text = $"今日 · {DateTime.Today:yyyy年M月d日}";

            // 绑定 ViewModel 事件 — 番茄钟完成时弹出 Toast 窗口
            Loaded += (s, e) =>
            {
                if (DataContext is MainViewModel vm)
                {
                    // 工作完成 → 弹出"番茄钟时间到"提醒
                    vm.OnWorkCompleted += () =>
                    {
                        var toast = new ToastWindow(
                            "番茄钟时间到！",
                            "你已完成专注工作，要休息一下吗？",
                            onRest: () => vm.StartRest(),
                            onContinue: () => vm.ContinueWork()
                        );
                        toast.Show();
                    };

                    // 休息完成 → 弹出"休息结束"提醒
                    vm.OnRestCompleted += () =>
                    {
                        var toast = new ToastWindow(
                            "休息时间结束！",
                            "精力充沛了吗？开始新一轮专注吧！",
                            onRest: null,
                            onContinue: () => vm.ContinueWork(),
                            isRestComplete: true
                        );
                        toast.Show();
                    };
                }
            };
        }

        // ==================== 标题栏操作 ====================

        /// <summary>
        /// 拖拽标题栏移动窗体
        /// </summary>
        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                // 双击标题栏 → 最大化/还原
                ToggleMaximize();
            }
            else
            {
                DragMove();
            }
        }

        /// <summary>
        /// 最小化按钮 → 隐藏到任务栏
        /// </summary>
        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        /// <summary>
        /// 最大化/还原按钮
        /// </summary>
        private void MaximizeButton_Click(object sender, RoutedEventArgs e)
        {
            ToggleMaximize();
        }

        /// <summary>
        /// 关闭按钮 → 隐藏到托盘而非真正退出
        /// </summary>
        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Hide(); // 只是隐藏，应用继续在后台运行
        }

        /// <summary>
        /// 切换最大化/还原状态
        /// </summary>
        private void ToggleMaximize()
        {
            WindowState = WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;
        }

        // ==================== 任务列表操作 ====================

        /// <summary>
        /// 输入框按 Enter 键添加任务
        /// </summary>
        private void TaskInput_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == Key.Enter && DataContext is MainViewModel vm)
            {
                if (vm.AddTaskCommand.CanExecute(null))
                    vm.AddTaskCommand.Execute(null);
            }
        }

        /// <summary>
        /// 点击勾选框 → 切换任务完成状态
        /// 因为 Border 不支持 Command 绑定，所以在代码后台转发到 ViewModel
        /// </summary>
        private void Checkbox_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement element && DataContext is MainViewModel vm)
            {
                vm.ToggleTaskCommand.Execute(element.Tag);
            }
            e.Handled = true;
        }

        /// <summary>
        /// 点击删除按钮 → 弹出确认对话框并执行删除
        /// </summary>
        private void DeleteButton_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement element && DataContext is MainViewModel vm)
            {
                if (ConfirmDialog.Show(this, "确定要删除这个任务吗？此操作无法撤销。"))
                {
                    vm.DeleteTaskCommand.Execute(element.Tag);
                }
            }
            e.Handled = true;
        }

        /// <summary>
        /// 任务卡片按下 → 处理双击详情页编辑
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
                    // 数据覆盖逻辑：在 V2 中我们使用替身编辑，保存时同步本体
                    task.Title = detailWindow.EditingTask.Title;
                    task.Priority = detailWindow.EditingTask.Priority;
                    
                    if (DataContext is MainViewModel vm)
                    {
                        await vm.SaveTaskEditAsync(task);
                    }
                }

                e.Handled = true;
            }
        }
    }
}
