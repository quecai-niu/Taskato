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
        /// <summary>
        /// 是否正在退出整个应用程序。
        /// 如果为 true，则允许窗口真正关闭；否则只允许隐藏。
        /// </summary>
        public bool IsAppShuttingDown { get; set; } = false;

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
                    // 监听所有计时器集合变化，为每个新加入的计时器注册独立的弹窗回调
                    vm.AllTimers.CollectionChanged += (_, args) =>
                    {
                        if (args.NewItems == null) return;
                        foreach (PomodoroTimerViewModel subVm in args.NewItems)
                        {
                            var capturedSubVm = subVm; // 闭包捕获，避免变量引用问题
                            capturedSubVm.WorkCompleted += () =>
                            {
                                var toast = new ToastWindow(
                                    $"{capturedSubVm.TimerName} 时间到！",
                                    "要休息一下吗？",
                                    onRest: () => capturedSubVm.StartRest(),
                                    onContinue: () => capturedSubVm.StartWork(),
                                    showTimer: vm.EnableToastTimer,
                                    soundChoice: vm.NotificationSoundChoice,
                                    customSoundPath: vm.CustomSoundPath
                                );
                                toast.Show();
                            };

                            capturedSubVm.RestCompleted += () =>
                            {
                                var toast = new ToastWindow(
                                    $"{capturedSubVm.TimerName} 休息结束！",
                                    "精力充沛了吗？开始新一轮专注吧！",
                                    onRest: null,
                                    onContinue: () => capturedSubVm.StartWork(),
                                    showTimer: vm.EnableToastTimer,
                                    isRestComplete: true,
                                    soundChoice: vm.NotificationSoundChoice,
                                    customSoundPath: vm.CustomSoundPath
                                );
                                toast.Show();
                            };
                        }
                    };
                    
                    // 手动为初始就在集合中的计时器（如主番茄钟）触发一次注册
                    foreach (var subVm in vm.AllTimers)
                    {
                        var capturedSubVm = subVm;
                        capturedSubVm.WorkCompleted += () =>
                        {
                            var toast = new ToastWindow(
                                $"{capturedSubVm.TimerName} 时间到！",
                                "你已完成专注工作，要休息一下吗？",
                                onRest: () => capturedSubVm.StartRest(),
                                onContinue: () => capturedSubVm.StartWork(),
                                showTimer: vm.EnableToastTimer,
                                soundChoice: vm.NotificationSoundChoice,
                                customSoundPath: vm.CustomSoundPath
                            );
                            toast.Show();
                        };

                        capturedSubVm.RestCompleted += () =>
                        {
                            var toast = new ToastWindow(
                                $"{capturedSubVm.TimerName} 休息结束！",
                                "精力充沛了吗？开始新一轮专注吧！",
                                onRest: null,
                                onContinue: () => capturedSubVm.StartWork(),
                                showTimer: vm.EnableToastTimer,
                                isRestComplete: true,
                                soundChoice: vm.NotificationSoundChoice,
                                customSoundPath: vm.CustomSoundPath
                            );
                            toast.Show();
                        };
                    }
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

        /// <summary>
        /// 关键：拦截窗口关闭事件
        /// 当用户通过 Alt+F4、任务栏右键或其他系统方式关闭窗口时，
        /// 拦截该行为并改为“隐藏”，以防止窗口对象被销毁导致无法再次通过托盘唤起。
        /// </summary>
        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            if (IsAppShuttingDown)
            {
                // 如果是应用正在退出，允许窗口正常关闭
                return;
            }

            // 拦截手动关闭操作
            e.Cancel = true;
            // 改为隐藏
            this.Hide();
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
        /// 全局按键拦截 — 当用户直接敲击字母/数字键时，自动聚焦到输入框并开始录入
        /// </summary>
        protected override void OnPreviewKeyDown(System.Windows.Input.KeyEventArgs e)
        {
            // 排除系统组合键、功能键以及输入框已获得焦点的情况
            if (!TaskInput.IsFocused && 
                ((e.Key >= Key.A && e.Key <= Key.Z) || (e.Key >= Key.D0 && e.Key <= Key.D9) || (e.Key >= Key.NumPad0 && e.Key <= Key.NumPad9)))
            {
                TaskInput.Focus();
                // 强制将光标移至末尾
                TaskInput.SelectionStart = TaskInput.Text.Length;
            }
            base.OnPreviewKeyDown(e);
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
