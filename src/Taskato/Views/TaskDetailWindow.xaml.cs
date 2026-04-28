using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;

namespace Taskato.Views
{
    /// <summary>
    /// TaskDetailWindow.xaml 的交互逻辑
    /// </summary>
    public partial class TaskDetailWindow : Window
    {
        public TaskDetailWindow(Models.TaskItem task)
        {
            InitializeComponent();
            DataContext = task;
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // 播放入场动画
            if (Resources["PopInAnimation"] is Storyboard sb)
            {
                sb.Begin(this);
            }
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // 允许拖拽窗体
            DragMove();
        }

        private void CloseButton_Click(object sender, MouseButtonEventArgs e)
        {
            Close();
        }
    }
}
