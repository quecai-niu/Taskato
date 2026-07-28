using System.Windows;
using System.Windows.Input;
using Taskato.Models;
using Taskato.ViewModels;

namespace Taskato.Views.Pages
{
    public partial class TaskWorkspaceView : System.Windows.Controls.UserControl
    {
        private MainViewModel? ViewModel => DataContext as MainViewModel;

        public TaskWorkspaceView()
        {
            InitializeComponent();
            SizeChanged += (_, _) => UpdateResponsiveColumns();
        }

        private void UpdateResponsiveColumns()
        {
            var showPomodoro = ActualWidth >= 970;
            PomodoroColumn.Width = showPomodoro ? new GridLength(300) : new GridLength(0);
            PomodoroSpacerColumn.Width = showPomodoro ? new GridLength(10) : new GridLength(0);
            PomodoroPanel.Visibility = showPomodoro ? Visibility.Visible : Visibility.Collapsed;
        }

        private void TaskInput_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (ViewModel is not { } vm) return;
            if (e.Key == Key.Enter && !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
            {
                e.Handled = true;
                if (vm.AddTaskCommand.CanExecute(null)) vm.AddTaskCommand.Execute(null);
            }
            else if (e.Key == Key.Tab)
            {
                e.Handled = true;
                vm.NewTaskPriority = (vm.NewTaskPriority + 1) % 4;
            }
        }

        private void Checkbox_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement { Tag: TaskItem task } && ViewModel is { } vm)
                vm.ToggleTaskCommand.Execute(task);
            e.Handled = true;
        }

        private async void DeleteTask_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement { Tag: TaskItem task } || ViewModel is not { } vm) return;
            var owner = System.Windows.Window.GetWindow(this);
            if (!task.HasChildren)
            {
                if (ConfirmDialog.Show(owner, "确定删除这个任务吗？完成历史会保留。"))
                    await vm.DeleteTaskAsync(task);
                return;
            }

            var choice = MessageBox.Show(owner,
                "该任务包含子任务。\n\n选择“是”删除整个分支；选择“否”仅删除父任务并提升直属子任务。",
                "删除任务分支", MessageBoxButton.YesNoCancel, MessageBoxImage.Warning);
            if (choice == MessageBoxResult.Yes)
                await vm.DeleteTaskAsync(task, TaskDeleteMode.DeleteBranch);
            else if (choice == MessageBoxResult.No)
                await vm.DeleteTaskAsync(task, TaskDeleteMode.PromoteChildren);
        }

        private async void ChildTaskInput_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (sender is not FrameworkElement { Tag: TaskItem parent } || ViewModel is not { } vm) return;
            if (e.Key == Key.Escape)
            {
                e.Handled = true;
                parent.NewChildTitle = string.Empty;
                parent.IsAddingChild = false;
            }
            else if (e.Key == Key.Enter)
            {
                e.Handled = true;
                await vm.AddChildTaskAsync(parent);
            }
        }

        private void ChildTaskInput_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (sender is not System.Windows.Controls.TextBox input || !input.IsVisible) return;
            input.Dispatcher.BeginInvoke(() => { input.Focus(); input.SelectAll(); }, System.Windows.Threading.DispatcherPriority.Input);
        }

        private async void TaskCard_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is not FrameworkElement { DataContext: TaskItem task } || ViewModel is not { } vm) return;
            vm.SelectedTask = task;
            if (e.ClickCount != 2) return;
            var detail = new TaskDetailWindow(task, vm.DatabaseService) { Owner = System.Windows.Window.GetWindow(this) };
            detail.ShowDialog();
            await vm.LoadTodayTasksAsync();
            e.Handled = true;
        }

        private void Workspace_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (Keyboard.FocusedElement is System.Windows.Controls.Primitives.TextBoxBase) return;
            if ((e.Key >= Key.A && e.Key <= Key.Z) || (e.Key >= Key.D0 && e.Key <= Key.D9) || (e.Key >= Key.NumPad0 && e.Key <= Key.NumPad9))
            {
                TaskInput.Focus();
                TaskInput.SelectionStart = TaskInput.Text.Length;
            }
        }
    }
}
