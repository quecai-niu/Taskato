using Taskato.Models;

namespace Taskato.Services
{
    public sealed record PomodoroCompletionSaveResult(
        TaskProgressRecord CreatedRecord,
        CompletionDestination Destination);

    /// <summary>协调完成记录的动态归属、分钟取整和数据库写入。</summary>
    public sealed class PomodoroCompletionService
    {
        private readonly DatabaseService _database;
        private readonly WorkspaceContextService _workspaceContext;

        public event Action? DestinationChanged
        {
            add => _workspaceContext.ContextChanged += value;
            remove => _workspaceContext.ContextChanged -= value;
        }

        public PomodoroCompletionService(
            DatabaseService database,
            WorkspaceContextService workspaceContext)
        {
            _database = database;
            _workspaceContext = workspaceContext;
        }

        public Task<CompletionDestination> GetDestinationAsync() =>
            _workspaceContext.ResolveDestinationAsync(_database);

        public async Task<PomodoroCompletionSaveResult> SaveAsync(
            PomodoroWorkCompletion completion,
            string? note)
        {
            var destination = await GetDestinationAsync();
            var normalizedNote = note?.Trim() ?? string.Empty;
            if (normalizedNote.Length > 5000)
                throw new InvalidOperationException("推进记录不能超过 5000 个字符。");

            var durationMinutes = completion.FocusedSeconds <= 0
                ? (int?)null
                : Math.Max(1, (int)Math.Round(
                    completion.FocusedSeconds / 60d,
                    MidpointRounding.AwayFromZero));
            var completedAt = completion.CompletedAt.ToLocalTime().DateTime;
            var actualDurationMinutes = durationMinutes ?? 1;
            var startedAt = completedAt.AddSeconds(-Math.Max(0, completion.FocusedSeconds));
            var created = await _database.AddProgressRecordAsync(
                destination.TaskId,
                normalizedNote,
                startedAt,
                completedAt,
                actualDurationMinutes,
                actualDurationMinutes,
                completion.EndReason,
                TaskProgressSource.Pomodoro);
            return new PomodoroCompletionSaveResult(created, destination);
        }
    }
}
