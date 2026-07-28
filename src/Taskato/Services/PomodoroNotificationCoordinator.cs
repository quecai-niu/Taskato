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

        public PomodoroNotificationCoordinator(MainViewModel viewModel)
        {
            _viewModel = viewModel;
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

            Action workCompleted = () =>
            {
                new ToastWindow(
                    $"{timer.TimerName} 时间到！", "你已完成专注工作，要休息一下吗？",
                    () => timer.StartRest(), () => timer.StartWork(),
                    _viewModel.EnableToastTimer,
                    actionDelaySeconds: _viewModel.ToastActionDelaySeconds,
                    soundChoice: _viewModel.NotificationSoundChoice,
                    customSoundPath: _viewModel.CustomSoundPath).Show();
                _ = NotifyFeishuAsync(
                    _viewModel.FeishuEnabled && _viewModel.FeishuNotifyOnWork,
                    $"{timer.TimerName} 时间到！", "你已完成专注工作，要休息一下吗？");
            };

            Action restCompleted = () =>
            {
                new ToastWindow(
                    $"{timer.TimerName} 休息结束！", "精力充沛了吗？开始新一轮专注吧！",
                    null, () => timer.StartWork(), _viewModel.EnableToastTimer,
                    isRestComplete: true,
                    actionDelaySeconds: _viewModel.ToastActionDelaySeconds,
                    soundChoice: _viewModel.NotificationSoundChoice,
                    customSoundPath: _viewModel.CustomSoundPath).Show();
                _ = NotifyFeishuAsync(
                    _viewModel.FeishuEnabled && _viewModel.FeishuNotifyOnRest,
                    $"{timer.TimerName} 休息结束！", "精力充沛了吗？开始新一轮专注吧！");
            };

            Action restHalfway = () => _ = NotifyFeishuAsync(
                _viewModel.FeishuEnabled && _viewModel.FeishuRestHalfwayEnabled,
                $"{timer.TimerName} 休息过半", "休息时间已过半，准备回到工作状态吧！");

            timer.WorkCompleted += workCompleted;
            timer.RestCompleted += restCompleted;
            timer.RestHalfway += restHalfway;
            _handlers[timer] = new TimerHandlers(workCompleted, restCompleted, restHalfway);
        }

        private void Unregister(PomodoroTimerViewModel timer)
        {
            if (!_handlers.Remove(timer, out var handlers)) return;
            timer.WorkCompleted -= handlers.WorkCompleted;
            timer.RestCompleted -= handlers.RestCompleted;
            timer.RestHalfway -= handlers.RestHalfway;
        }

        private async Task NotifyFeishuAsync(bool enabled, string title, string content)
        {
            if (enabled) await _viewModel.FeishuService.NotifyAsync(title, content);
        }

        public void Dispose()
        {
            _viewModel.AllTimers.CollectionChanged -= Timers_CollectionChanged;
            foreach (var timer in _handlers.Keys.ToList()) Unregister(timer);
        }

        private sealed record TimerHandlers(Action WorkCompleted, Action RestCompleted, Action RestHalfway);
    }
}
