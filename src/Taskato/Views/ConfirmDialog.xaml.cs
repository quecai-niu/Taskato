using System.Windows;

namespace Taskato.Views
{
    public partial class ConfirmDialog : Window
    {
        public bool Result { get; private set; } = false;

        public ConfirmDialog(string message, string title = "确认删除？", string confirmText = "删除")
        {
            InitializeComponent();
            MessageText.Text = message;
            TitleText.Text = title;
            ConfirmBtn.Content = confirmText;
        }

        private void ConfirmBtn_Click(object sender, RoutedEventArgs e)
        {
            Result = true;
            DialogResult = true;
            Close();
        }

        private void CancelBtn_Click(object sender, RoutedEventArgs e)
        {
            Result = false;
            DialogResult = false;
            Close();
        }

        /// <summary>
        /// 静态便捷方法：弹出确认框并返回用户选择
        /// </summary>
        public static bool Show(Window owner, string message, string title = "确认删除？", string confirmText = "删除")
        {
            var dialog = new ConfirmDialog(message, title, confirmText)
            {
                Owner = owner
            };
            dialog.ShowDialog();
            return dialog.Result;
        }
    }
}
