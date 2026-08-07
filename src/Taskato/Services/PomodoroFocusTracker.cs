using Taskato.Models;
using Taskato.ViewModels;

namespace Taskato.Services
{
    /// <summary>
    /// 把同步的计时器阶段事件串行写入 SQLite。写入失败只影响统计并记录诊断，不能中断计时主流程。
    /// </summary>
    public sealed class PomodoroFocusTracker
    {
        private readonly DatabaseService _database;
        private readonly Action _changed;
        private readonly object _queueGate = new();
        private Task _pending = Task.CompletedTask;

        public PomodoroFocusTracker(DatabaseService database, Action? changed = null)
        {
            _database = database;
            _changed = changed ?? (() => { });
        }

        public void Track(
            string timerSlotId,
            PomodoroTimerViewModel timer,
            TaskItem? task,
            PomodoroPhaseTransition transition)
        {
            var taskId = task?.Id;
            var taskTitle = task?.Title ?? string.Empty;
            Enqueue(async () =>
            {
                switch (transition.Kind)
                {
                    case PomodoroPhaseTransitionKind.Started:
                        await _database.StartPomodoroPhaseAsync(
                            timerSlotId,
                            timer.TimerName,
                            taskId,
                            taskTitle,
                            transition.Phase,
                            transition.OccurredAt,
                            transition.Phase == PomodoroFocusPhase.Work);
                        break;
                    case PomodoroPhaseTransitionKind.Paused:
                        await _database.PausePomodoroPhaseAsync(
                            timerSlotId, transition.Phase, transition.OccurredAt);
                        break;
                    case PomodoroPhaseTransitionKind.Resumed:
                        await _database.ResumePomodoroPhaseAsync(
                            timerSlotId,
                            timer.TimerName,
                            taskId,
                            taskTitle,
                            transition.Phase,
                            transition.OccurredAt);
                        break;
                    case PomodoroPhaseTransitionKind.Ended:
                        await _database.EndPomodoroPhaseAsync(
                            timerSlotId,
                            transition.Phase,
                            transition.OccurredAt,
                            transition.EndReason);
                        break;
                }
            });
        }

        public void Reconcile(
            string timerSlotId,
            PomodoroTimerViewModel timer,
            TaskItem? task,
            PomodoroRuntimeSnapshot snapshot,
            DateTimeOffset observedAt)
        {
            Enqueue(() => _database.ReconcilePomodoroPhaseAsync(
                timerSlotId,
                timer.TimerName,
                task?.Id,
                task?.Title ?? string.Empty,
                snapshot,
                observedAt));
        }

        public Task FlushAsync()
        {
            lock (_queueGate) return _pending;
        }

        private void Enqueue(Func<Task> operation)
        {
            lock (_queueGate)
            {
                _pending = _pending.ContinueWith(
                    async _ =>
                    {
                        try
                        {
                            await operation();
                            _changed();
                        }
                        catch (Exception exception)
                        {
                            DiagnosticsService.Log("保存番茄钟统计失败", exception);
                        }
                    },
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default).Unwrap();
            }
        }
    }
}
