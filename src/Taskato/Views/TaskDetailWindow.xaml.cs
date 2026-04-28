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
        /// <summary>原始任务引用（本体）</summary>
        public Models.TaskItem OriginalTask { get; }
        
        /// <summary>编辑中的替身（克隆体）</summary>
        public Models.TaskItem EditingTask { get; }
        
        /// <summary>标记用户是否点击了保存</summary>
        public bool IsSaved { get; private set; } = false;

        public TaskDetailWindow(Models.TaskItem task)
        {
            InitializeComponent();
            OriginalTask = task;
            
            // 创建一个替身用于绑定和编辑，避免污染原数据
            EditingTask = new Models.TaskItem 
            { 
                Id = task.Id, 
                Title = task.Title, 
                Priority = task.Priority, 
                IsCompleted = task.IsCompleted, 
                CreatedAt = task.CreatedAt, 
                CompletedAt = task.CompletedAt, 
                OrderIndex = task.OrderIndex 
            };
            
            DataContext = EditingTask;
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
            DragMove();
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            IsSaved = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            IsSaved = false;
            Close();
        }

        private void CloseButton_Click(object sender, MouseButtonEventArgs e)
        {
            // 点击右上角 X 默认视为取消
            IsSaved = false;
            Close();
        }

        protected override void OnKeyDown(System.Windows.Input.KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.Key == Key.Escape)
            {
                IsSaved = false;
                Close();
            }
        }
    }
}
