using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Input;
using Taskato.Models;
using Taskato.Utils;

namespace Taskato.Views
{
    /// <summary>
    /// 完成任务时记录可选耗时，也用于每日总结里的补录流程。
    /// </summary>
    public partial class TaskCompletionDialog : Window
    {
        private static readonly Regex DigitsOnlyRegex = new("^[0-9]+$");

        public TaskItem? SelectedTask { get; private set; }
        public int? DurationMinutes { get; private set; }

        private TaskCompletionDialog()
        {
            InitializeComponent();
            VisualEffects.Initialize(this);
        }

        public static bool TryShowForCompletion(Window? owner, TaskItem task, out int? durationMinutes)
        {
            var dialog = new TaskCompletionDialog();
            dialog.ConfigureForCompletion(task);
            AttachOwner(dialog, owner);

            var accepted = dialog.ShowDialog() == true;
            durationMinutes = accepted ? dialog.DurationMinutes : null;
            return accepted;
        }

        public static bool TryShowForBackfill(
            Window? owner,
            DateTime completionDate,
            IReadOnlyList<TaskItem> tasks,
            out TaskItem? selectedTask,
            out int? durationMinutes)
        {
            var dialog = new TaskCompletionDialog();
            dialog.ConfigureForBackfill(completionDate, tasks);
            AttachOwner(dialog, owner);

            var accepted = dialog.ShowDialog() == true;
            selectedTask = accepted ? dialog.SelectedTask : null;
            durationMinutes = accepted ? dialog.DurationMinutes : null;
            return accepted;
        }

        private static void AttachOwner(Window dialog, Window? owner)
        {
            if (owner is not null && owner != dialog)
                dialog.Owner = owner;
        }

        private void ConfigureForCompletion(TaskItem task)
        {
            HeaderText.Text = "完成任务";
            DescriptionText.Text = "记录这次完成的实际耗时；不确定可以留空。";
            ConfirmButton.Content = "确认完成";
            TaskPickerPanel.Visibility = Visibility.Collapsed;
            SingleTaskPanel.Visibility = Visibility.Visible;
            TaskTitleText.Text = task.DisplayTitle;
            CompletionDateText.Text = FormatDate(DateTime.Today);
            SelectedTask = task;
            DurationBox.Focus();
        }

        private void ConfigureForBackfill(DateTime completionDate, IReadOnlyList<TaskItem> tasks)
        {
            HeaderText.Text = "补录完成任务";
            DescriptionText.Text = "从当前未完成任务中选择一项，补记到当前总结日期。";
            ConfirmButton.Content = "补录完成";
            TaskPickerPanel.Visibility = Visibility.Visible;
            SingleTaskPanel.Visibility = Visibility.Collapsed;
            CompletionDateText.Text = FormatDate(completionDate);
            TaskComboBox.ItemsSource = tasks
                .OrderByDescending(t => t.Priority)
                .ThenBy(t => t.OrderIndex)
                .ThenByDescending(t => t.CreatedAt)
                .ToList();
            TaskComboBox.SelectedIndex = TaskComboBox.Items.Count > 0 ? 0 : -1;
            TaskComboBox.Focus();
        }

        private static string FormatDate(DateTime date)
        {
            var weekday = date.DayOfWeek switch
            {
                DayOfWeek.Monday => "星期一",
                DayOfWeek.Tuesday => "星期二",
                DayOfWeek.Wednesday => "星期三",
                DayOfWeek.Thursday => "星期四",
                DayOfWeek.Friday => "星期五",
                DayOfWeek.Saturday => "星期六",
                DayOfWeek.Sunday => "星期日",
                _ => string.Empty
            };
            return $"{date:yyyy-MM-dd} {weekday}";
        }

        private void ConfirmButton_Click(object sender, RoutedEventArgs e)
        {
            ErrorText.Visibility = Visibility.Collapsed;

            if (TaskPickerPanel.Visibility == Visibility.Visible)
            {
                SelectedTask = TaskComboBox.SelectedItem as TaskItem;
                if (SelectedTask is null)
                {
                    ShowError("请选择要补录的任务。");
                    return;
                }
            }

            if (!TryReadDuration(out var durationMinutes))
                return;

            DurationMinutes = durationMinutes;
            DialogResult = true;
            Close();
        }

        private bool TryReadDuration(out int? durationMinutes)
        {
            durationMinutes = null;
            var text = DurationBox.Text.Trim();
            if (string.IsNullOrEmpty(text))
                return true;

            if (!int.TryParse(text, out var parsed) || parsed < 0)
            {
                ShowError("耗时只能填写非负整数分钟。");
                return false;
            }

            if (parsed > 1440)
            {
                ShowError("单次任务耗时不能超过 1440 分钟。");
                return false;
            }

            durationMinutes = parsed > 0 ? parsed : null;
            return true;
        }

        private void ShowError(string message)
        {
            ErrorText.Text = message;
            ErrorText.Visibility = Visibility.Visible;
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            VisualEffects.RunWithTemporaryReduction(this, DragMove);
        }

        private void DurationBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            e.Handled = !DigitsOnlyRegex.IsMatch(e.Text);
        }

        private void DurationBox_Pasting(object sender, DataObjectPastingEventArgs e)
        {
            if (!e.DataObject.GetDataPresent(System.Windows.DataFormats.Text))
            {
                e.CancelCommand();
                return;
            }

            var text = e.DataObject.GetData(System.Windows.DataFormats.Text) as string ?? string.Empty;
            if (!DigitsOnlyRegex.IsMatch(text))
                e.CancelCommand();
        }
    }
}
