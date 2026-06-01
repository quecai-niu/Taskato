using System.Windows;

namespace Taskato.Views
{
    /// <summary>
    /// 首次启动时展示的飞书通知轻引导。
    /// </summary>
    public partial class FeishuGuideWindow : Window
    {
        public bool ShouldConfigure { get; private set; }

        public FeishuGuideWindow()
        {
            InitializeComponent();
        }

        private void ConfigureButton_Click(object sender, RoutedEventArgs e)
        {
            ShouldConfigure = true;
            DialogResult = true;
            Close();
        }

        private void LaterButton_Click(object sender, RoutedEventArgs e)
        {
            ShouldConfigure = false;
            DialogResult = false;
            Close();
        }
    }
}
