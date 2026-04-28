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
        /// 最小化按钮（黄色圆点） → 隐藏到托盘
        /// </summary>
        private void MinimizeButton_Click(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
            WindowState = WindowState.Minimized;
        }

        /// <summary>
        /// 最大化/还原按钮（绿色圆点）
        /// </summary>
        private void MaximizeButton_Click(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
            ToggleMaximize();
        }

        /// <summary>
        /// 关闭按钮（红色圆点） → 隐藏到托盘而非真正退出
        /// </summary>
        private void CloseButton_Click(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
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
        /// 点击删除按钮 → 删除任务
        /// </summary>
        private void DeleteButton_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement element && DataContext is MainViewModel vm)
            {
                vm.DeleteTaskCommand.Execute(element.Tag);
            }
            e.Handled = true;
        }

        // ==================== 滑动删除手势相关状态 ====================

        private System.Windows.Point _swipeStartPoint;
        private bool _isSwiping = false;
        private System.Windows.Controls.Border? _currentSwipingCard = null;
        private System.Windows.Media.TranslateTransform? _currentTransform = null;

        /// <summary>
        /// 任务卡片按下 → 双击详情或准备滑动
        /// </summary>
        private void TaskCard_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2 && sender is FrameworkElement element && element.DataContext is Models.TaskItem task)
            {
                var detailWindow = new TaskDetailWindow(task) { Owner = this };
                detailWindow.ShowDialog();
                e.Handled = true;
                return;
            }

            if (sender is System.Windows.Controls.Border card)
            {
                var transform = card.RenderTransform as System.Windows.Media.TranslateTransform;
                
                // 如果没有变换器，或者对象被 WPF 冻结（只读），需要克隆或新建一个解冻的实例
                if (transform == null || transform.IsFrozen)
                {
                    transform = new System.Windows.Media.TranslateTransform(transform?.X ?? 0, 0);
                    card.RenderTransform = transform;
                }

                // 重点：必须清除之前可能遗留的回弹/固定动画锁定，否则后面改值依旧报错
                transform.BeginAnimation(System.Windows.Media.TranslateTransform.XProperty, null);

                _swipeStartPoint = e.GetPosition(this);
                _isSwiping = true;
                _currentSwipingCard = card;
                _currentTransform = transform;
                card.CaptureMouse();
            }
        }

        private void TaskCard_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (_isSwiping && _currentSwipingCard != null && _currentTransform != null)
            {
                var currentPoint = e.GetPosition(this);
                var deltaX = currentPoint.X - _swipeStartPoint.X;

                // 仅允许向左滑动（负值），最大拖拽距离设置点阻尼（不要超过太多）
                if (deltaX < 0 && deltaX > -100)
                {
                    _currentTransform.X = deltaX;
                }
                else if (deltaX >= 0)
                {
                    _currentTransform.X = 0;
                }
            }
        }

        private void TaskCard_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            ReleaseSwipe();
        }

        private void TaskCard_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
        {
            // 防止拖拽异常遗留
            if (_isSwiping && e.LeftButton == MouseButtonState.Released)
            {
                ReleaseSwipe();
            }
        }

        private void ReleaseSwipe()
        {
            if (_isSwiping && _currentSwipingCard != null && _currentTransform != null)
            {
                // 如果左滑距离超过 35 像素，则磁吸固定在打开状态 (-70)；否则弹回 (0)
                var finalTargetX = _currentTransform.X < -35 ? -70 : 0;
                
                var anim = new System.Windows.Media.Animation.DoubleAnimation(
                    finalTargetX, new TimeSpan(0, 0, 0, 0, 250))
                {
                    EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut }
                };
                
                // 停止之前的硬编码转换（如果有的话），开始动画恢复
                _currentTransform.BeginAnimation(System.Windows.Media.TranslateTransform.XProperty, anim);

                _currentSwipingCard.ReleaseMouseCapture();
                _isSwiping = false;
                _currentSwipingCard = null;
                _currentTransform = null;
            }
        }
    }
}
