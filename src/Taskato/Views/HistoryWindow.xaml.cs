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
        /// 关闭按钮
        /// </summary>
        private void CloseButton_Click(object sender, MouseButtonEventArgs e)
        {
            Close();
        }

        /// <summary>
        /// 点击删除按钮 → 删除历史任务
        /// </summary>
        private void DeleteButton_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement element && DataContext is HistoryViewModel vm)
            {
                if (vm.DeleteTaskCommand.CanExecute(element.Tag))
                {
                    vm.DeleteTaskCommand.Execute(element.Tag);
                }
            }
            e.Handled = true;
        }
    }
}
