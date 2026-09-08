using System.Collections.Specialized;
using Taskato.ViewModels;
using Taskato.Views;

namespace Taskato.Services
{
    /// <summary>
    /// 统一管理所有番茄钟的 Toast 与飞书通知订阅，避免页面切换或窗口恢复造成重复回调。
    /// </summary>
    public sealed class PomodoroNotificationCoordinator : IDisposable
    {
        private readonly MainViewModel _viewModel;
        private readonly Dictionary<PomodoroTimerViewModel, TimerHandlers> _handlers = new();
        private readonly Action<PomodoroTimerViewModel>? _workCompletedAction;
        private readonly Action<PomodoroTimerViewModel> _restCompletedAction;
        private readonly Action<PomodoroTimerViewModel> _restHalfwayAction;
        private readonly Action _showMainWindowAction;

        public PomodoroNotificationCoordinator(
            MainViewModel viewModel,
            Action<PomodoroTimerViewModel>? workCompletedAction = null,
            Action<PomodoroTimerViewModel>? restCompletedAction = null,
            Action<PomodoroTimerViewModel>? restHalfwayAction = null,
            Action? showMainWindowAction = null)
        {
            _viewModel = viewModel;
            // 可注入动作让订阅生命周期能够在无弹窗、无外部网络的测试中被直接证明。
            _workCompletedAction = workCompletedAction;
            _restCompletedAction = restCompletedAction ?? ShowRestCompleted;
            _restHalfwayAction = restHalfwayAction ?? ShowRestHalfway;
            _showMainWindowAction = showMainWindowAction ?? (() => { });
            _viewModel.AllTimers.CollectionChanged += Timers_CollectionChanged;
            foreach (var timer in _viewModel.AllTimers) Register(timer);
        }

        private void Timers_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.OldItems is not null)
                foreach (PomodoroTimerViewModel timer in e.OldItems) Unregister(timer);
            if (e.NewItems is not null)
                foreach (PomodoroTimerViewModel timer in e.NewItems) Register(timer);
        }

        private void Register(PomodoroTimerViewModel timer)
        {
            if (_handlers.ContainsKey(timer)) return;

            Action<PomodoroWorkCompletion> workCompleted = completion =>
            {
                ShowMainWindowWhenEnabled();
                if (_workCompletedAction is not null)
                    _workCompletedAction(timer);
                else
                    ShowWorkCompleted(timer, completion);
            };

            Action restCompleted = () =>
            {
                ShowMainWindowWhenEnabled();
                _restCompletedAction(timer);
            };

            Action restHalfway = () => _restHalfwayAction(timer);

            timer.WorkCompletionRequested += workCompleted;
            timer.RestCompleted += restCompleted;
            timer.RestHalfway += restHalfway;
            _handlers[timer] = new TimerHandlers(workCompleted, restCompleted, restHalfway);
        }

        private void ShowMainWindowWhenEnabled()
        {
            // 休息过半只有远程提醒，不应因该事件打断用户；主界面只随两类完成 Toast 恢复。
            if (_viewModel.ShowMainWindowOnTimerNotification)
                _showMainWindowAction();
        }

        private void Unregister(PomodoroTimerViewModel timer)
        {
            if (!_handlers.Remove(timer, out var handlers)) return;
            timer.WorkCompletionRequested -= handlers.WorkCompleted;
            timer.RestCompleted -= handlers.RestCompleted;
            timer.RestHalfway -= handlers.RestHalfway;
        }

        private async Task NotifyFeishuAsync(bool enabled, string title, string content, int notificationCount)
        {
            if (enabled)
                await _viewModel.FeishuService.NotifyAsync(title, content, notificationCount);
        }

        private void ShowWorkCompleted(PomodoroTimerViewModel timer, PomodoroWorkCompletion completion)
        {
            var elapsedSeconds = timer.CompletionElapsedSeconds;
            ToastWindow? toast = null;
            toast = new ToastWindow(
                $"{timer.TimerName} 时间到！", "你已完成专注工作，要休息一下吗？",
                () => timer.StartRest(), () => timer.StartWork(),
                _viewModel.EnableToastTimer || timer.IsRecoveredCompletion,
                actionDelaySeconds: _viewModel.ToastActionDelaySeconds,
                soundChoice: _viewModel.NotificationSoundChoice,
                customSoundPath: _viewModel.CustomSoundPath,
                initialElapsedSeconds: elapsedSeconds);
            toast.Show();
            _ = NotifyFeishuAsync(
                _viewModel.FeishuEnabled && _viewModel.FeishuNotifyOnWork,
                $"{timer.TimerName} 时间到！", "你已完成专注工作，要休息一下吗？",
                _viewModel.FeishuWorkNotificationCount);
        }

        private void ShowRestCompleted(PomodoroTimerViewModel timer)
        {
            var elapsedSeconds = timer.CompletionElapsedSeconds;
            new ToastWindow(
                $"{timer.TimerName} 休息结束！", "精力充沛了吗？开始新一轮专注吧！",
                null, () => timer.StartWork(), _viewModel.EnableToastTimer || timer.IsRecoveredCompletion,
                isRestComplete: true,
                actionDelaySeconds: _viewModel.ToastActionDelaySeconds,
                soundChoice: _viewModel.NotificationSoundChoice,
                customSoundPath: _viewModel.CustomSoundPath,
                initialElapsedSeconds: elapsedSeconds).Show();
            _ = NotifyFeishuAsync(
                _viewModel.FeishuEnabled && _viewModel.FeishuNotifyOnRest,
                $"{timer.TimerName} 休息结束！", "精力充沛了吗？开始新一轮专注吧！",
                _viewModel.FeishuRestCompletedNotificationCount);
        }

        private void ShowRestHalfway(PomodoroTimerViewModel timer)
        {
            _ = NotifyFeishuAsync(
                _viewModel.FeishuEnabled && _viewModel.FeishuRestHalfwayEnabled,
                $"{timer.TimerName} 休息过半", "休息时间已过半，准备回到工作状态吧！",
                _viewModel.FeishuRestHalfwayNotificationCount);
        }

        public void Dispose()
        {
            _viewModel.AllTimers.CollectionChanged -= Timers_CollectionChanged;
            foreach (var timer in _handlers.Keys.ToList()) Unregister(timer);
        }

        private sealed record TimerHandlers(
            Action<PomodoroWorkCompletion> WorkCompleted,
            Action RestCompleted,
            Action RestHalfway);
    }
}
