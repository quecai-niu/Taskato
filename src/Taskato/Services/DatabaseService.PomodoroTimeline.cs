using Taskato.Models;

namespace Taskato.Services
{
    public partial class DatabaseService
    {
        public async Task<double> GetPreviousContinuousWorkSecondsAsync(string timerSlotId)
        {
            var current = await _db.Table<PomodoroFocusSessionRecord>()
                .Where(session => session.TimerSlotId == timerSlotId)
                .OrderByDescending(session => session.Id).FirstOrDefaultAsync();
            if (current is null || current.Phase != PomodoroFocusPhase.Work || current.EndedAtUtc != null)
                return 0;

            var boundary = await _db.Table<PomodoroFocusSessionRecord>()
                .Where(session => session.TimerSlotId == timerSlotId && session.Id < current.Id &&
                    (session.Phase == PomodoroFocusPhase.Rest ||
                     (session.EndReason != "completed" && session.EndReason != "completed_early")))
                .OrderByDescending(session => session.Id).FirstOrDefaultAsync();
            var lastStop = await _db.Table<PomodoroTimelineRecord>()
                .Where(entry => entry.TimerSlotId == timerSlotId && entry.Title == "停止计时" &&
                    entry.OccurredAtUtc <= current.StartedAtUtc)
                .OrderByDescending(entry => entry.Id).FirstOrDefaultAsync();
            // 使用完整会话而非最近 100 条时间链；只累计已结束轮次的有效区间。
            var segments = await _db.QueryAsync<PomodoroFocusSegmentRecord>(
                "SELECT segment.* FROM PomodoroFocusSegments AS segment " +
                "INNER JOIN PomodoroFocusSessions AS session ON session.Id = segment.SessionId " +
                "WHERE session.TimerSlotId = ? AND session.Id > ? AND session.Id < ? " +
                "AND session.StartedAtUtc > ? AND segment.EndedAtUtc IS NOT NULL",
                timerSlotId, boundary?.Id ?? 0, current.Id, lastStop?.OccurredAtUtc ?? DateTime.MinValue);
            return segments.Sum(segment => (segment.EndedAtUtc!.Value - segment.StartedAtUtc).TotalSeconds);
        }

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

        public async Task<List<PomodoroTimelineRecord>> GetPomodoroTimelineAsync(string timerSlotId)
        {
            var entries = await _db.Table<PomodoroTimelineRecord>()
                .Where(entry => entry.TimerSlotId == timerSlotId)
                .OrderByDescending(entry => entry.Id)
                .Take(100)
                .ToListAsync();
            if (entries.Count == 0) return entries;

            var firstEventAt = entries.Min(entry => entry.OccurredAtUtc);
            var lastEventAt = entries.Max(entry => entry.OccurredAtUtc);
            var sessions = await _db.Table<PomodoroFocusSessionRecord>()
                .Where(session => session.TimerSlotId == timerSlotId &&
                    session.StartedAtUtc <= lastEventAt &&
                    (session.EndedAtUtc == null || session.EndedAtUtc >= firstEventAt))
                .ToListAsync();
            var previousSession = await _db.Table<PomodoroFocusSessionRecord>()
                .Where(session => session.TimerSlotId == timerSlotId && session.EndedAtUtc < firstEventAt)
                .OrderByDescending(session => session.EndedAtUtc)
                .FirstOrDefaultAsync();
            if (previousSession is not null) sessions.Add(previousSession);
            if (sessions.Count == 0) return entries;

            var firstSessionAt = sessions.Min(session => session.StartedAtUtc);
            var segments = await _db.QueryAsync<PomodoroFocusSegmentRecord>(
                "SELECT segment.* FROM PomodoroFocusSegments AS segment " +
                "INNER JOIN PomodoroFocusSessions AS session ON session.Id = segment.SessionId " +
                "WHERE session.TimerSlotId = ? AND session.StartedAtUtc >= ? AND session.StartedAtUtc <= ?",
                timerSlotId, firstSessionAt, lastEventAt);

            foreach (var entry in entries)
            {
                var starts = entry.Title is "开始专注" or "开始休息";
                var resumes = entry.Title is "恢复专注" or "恢复休息";
                var pauses = entry.Title is "暂停专注" or "暂停休息";
                var ends = entry.Title is "停止专注" or "停止休息" or "提前完成专注" or "专注时间到" or "休息时间到";
                if (!starts && !resumes && !pauses && !ends) continue;

                var phase = entry.Title.Contains("专注") ? PomodoroFocusPhase.Work : PomodoroFocusPhase.Rest;
                var session = sessions.Where(item => item.Phase == phase && item.StartedAtUtc <= entry.OccurredAtUtc &&
                        (item.EndedAtUtc == null || item.EndedAtUtc >= entry.OccurredAtUtc) &&
                        (!starts || item.StartedAtUtc == entry.OccurredAtUtc) &&
                        (!ends || item.EndedAtUtc == entry.OccurredAtUtc))
                    .OrderByDescending(item => item.Id).FirstOrDefault();
                // 没有对应计时会话时，不用提醒或选择事件的间隔推算时长。
                if (session is null) continue;

                if (starts)
                {
                    var previousEnd = sessions.Where(item => item.Id != session.Id && item.EndedAtUtc <= entry.OccurredAtUtc)
                        .OrderByDescending(item => item.EndedAtUtc).FirstOrDefault()?.EndedAtUtc;
                    entry.DurationSeconds = previousEnd.HasValue ? (entry.OccurredAtUtc - previousEnd.Value).TotalSeconds : 0;
                    entry.DurationLabel = "暂停了";
                }
                else if (resumes)
                {
                    var pauseAt = segments.Where(segment => segment.SessionId == session.Id && segment.EndedAtUtc <= entry.OccurredAtUtc)
                        .OrderByDescending(segment => segment.EndedAtUtc).FirstOrDefault()?.EndedAtUtc;
                    if (pauseAt.HasValue)
                    {
                        entry.DurationSeconds = (entry.OccurredAtUtc - pauseAt.Value).TotalSeconds;
                        entry.DurationLabel = "暂停了";
                    }
                }
                else
                {
                    double runningSeconds = 0;
                    foreach (var segment in segments.Where(segment => segment.SessionId == session.Id && segment.StartedAtUtc <= entry.OccurredAtUtc))
                    {
                        var segmentEnd = segment.EndedAtUtc ?? entry.OccurredAtUtc;
                        if (segmentEnd > entry.OccurredAtUtc) segmentEnd = entry.OccurredAtUtc;
                        runningSeconds += (segmentEnd - segment.StartedAtUtc).TotalSeconds;
                    }
                    entry.DurationSeconds = runningSeconds;
                    entry.DurationLabel = "进行了";
                }
            }
            return entries;
        }
    }
}
