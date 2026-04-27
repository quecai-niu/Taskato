using System.Windows.Input;

namespace Taskato.Utils
{
    /// <summary>
    /// 通用命令实现 — 将 Action/Func 委托封装为 ICommand
    /// 这样 ViewModel 就可以通过 Command 绑定来响应按钮点击等 UI 事件，
    /// 而无需在代码后台写事件处理函数（保持 MVVM 架构干净）。
    /// </summary>
    public class RelayCommand : ICommand
    {
        /// <summary>
        /// 命令执行时调用的委托（实际要做的事情）
        /// </summary>
        private readonly Action<object?> _execute;

        /// <summary>
        /// 判断命令是否可以执行的委托（控制按钮是否可点击等）
        /// </summary>
        private readonly Func<object?, bool>? _canExecute;

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="execute">执行逻辑（必须提供）</param>
        /// <param name="canExecute">可选的可执行判断逻辑</param>
        public RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        /// <summary>
        /// 当命令的可执行状态可能发生变化时触发
        /// 挂载到 CommandManager.RequerySuggested 上，让 WPF 自动管理刷新时机
        /// </summary>
        public event EventHandler? CanExecuteChanged
        {
            add => CommandManager.RequerySuggested += value;
            remove => CommandManager.RequerySuggested -= value;
        }

        /// <summary>
        /// 判断命令当前是否可以执行
        /// </summary>
        public bool CanExecute(object? parameter)
        {
            return _canExecute == null || _canExecute(parameter);
        }

        /// <summary>
        /// 执行命令
        /// </summary>
        public void Execute(object? parameter)
        {
            _execute(parameter);
        }

        /// <summary>
        /// 手动触发 CanExecuteChanged 事件，强制 WPF 重新评估命令的可执行状态
        /// </summary>
        public void RaiseCanExecuteChanged()
        {
            CommandManager.InvalidateRequerySuggested();
        }
    }
}
