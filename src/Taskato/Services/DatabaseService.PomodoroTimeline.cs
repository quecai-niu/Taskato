using Taskato.Models;

namespace Taskato.Services
{
    public partial class DatabaseService
    {
        public Task AppendPomodoroTimelineAsync(
            string timerSlotId, string taskTitle, PomodoroTimelineEntry entry) =>
            _db.InsertAsync(new PomodoroTimelineRecord
            {
                TimerSlotId = timerSlotId,
                TaskTitleSnapshot = taskTitle,
                OccurredAtUtc = entry.OccurredAt.UtcDateTime,
                Kind = entry.Kind,
                Title = entry.Title,
                Detail = entry.Detail
            });

        public Task<List<PomodoroTimelineRecord>> GetPomodoroTimelineAsync(string timerSlotId) =>
            _db.Table<PomodoroTimelineRecord>()
                .Where(entry => entry.TimerSlotId == timerSlotId)
                .OrderByDescending(entry => entry.Id)
                .Take(100)
                .ToListAsync();
    }
}
