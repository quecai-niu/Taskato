using SQLite;
using Taskato.Models;
using Taskato.Models.BridgeDtos;

namespace Taskato.Services
{
    public partial class DatabaseService
    {
        private async Task InitializeFocusAnalyticsAsync()
        {
            await _db.CreateTableAsync<PomodoroFocusSessionRecord>();
            await _db.CreateTableAsync<PomodoroFocusSegmentRecord>();
            await _db.ExecuteAsync(
                "CREATE INDEX IF NOT EXISTS IX_PomodoroFocusSessions_Open " +
                "ON PomodoroFocusSessions(TimerSlotId, EndedAtUtc)");
            await _db.ExecuteAsync(
                "CREATE INDEX IF NOT EXISTS IX_PomodoroFocusSegments_Range " +
                "ON PomodoroFocusSegments(StartedAtUtc, EndedAtUtc)");
        }

        public Task StartPomodoroPhaseAsync(
            string timerSlotId,
            string timerName,
            int? taskId,
            string taskTitle,
            PomodoroFocusPhase phase,
            DateTimeOffset occurredAt,
            bool hasStartMarker)
        {
            var occurredAtUtc = occurredAt.UtcDateTime;
            return _db.RunInTransactionAsync(connection =>
            {
                CloseOpenSession(connection, timerSlotId, occurredAtUtc, "replaced");
                var session = new PomodoroFocusSessionRecord
                {
                    TimerSlotId = timerSlotId,
                    TimerName = timerName,
                    TaskId = taskId,
                    TaskTitleSnapshot = taskTitle,
                    Phase = phase,
                    StartedAtUtc = occurredAtUtc,
                    HasStartMarker = hasStartMarker
                };
                connection.Insert(session);
                connection.Insert(new PomodoroFocusSegmentRecord
                {
                    SessionId = session.Id,
                    StartedAtUtc = occurredAtUtc
                });
            });
        }

        public Task PausePomodoroPhaseAsync(
            string timerSlotId,
            PomodoroFocusPhase phase,
            DateTimeOffset occurredAt)
        {
            var occurredAtUtc = occurredAt.UtcDateTime;
            return _db.RunInTransactionAsync(connection =>
            {
                var session = FindOpenSession(connection, timerSlotId, phase);
                if (session is not null)
                    CloseOpenSegments(connection, session.Id, occurredAtUtc);
            });
        }

        public Task ResumePomodoroPhaseAsync(
            string timerSlotId,
            string timerName,
            int? taskId,
            string taskTitle,
            PomodoroFocusPhase phase,
            DateTimeOffset occurredAt)
        {
            var occurredAtUtc = occurredAt.UtcDateTime;
            return _db.RunInTransactionAsync(connection =>
            {
                var session = FindOpenSession(connection, timerSlotId, phase);
                if (session is null)
                {
                    // 缺失会话只可能来自升级、损坏或上次写入失败；恢复时记录后续时长，但不伪造启动标记。
                    CloseOpenSession(connection, timerSlotId, occurredAtUtc, "recovered");
                    session = new PomodoroFocusSessionRecord
                    {
                        TimerSlotId = timerSlotId,
                        TimerName = timerName,
                        TaskId = taskId,
                        TaskTitleSnapshot = taskTitle,
                        Phase = phase,
                        StartedAtUtc = occurredAtUtc,
                        HasStartMarker = false
                    };
                    connection.Insert(session);
                }

                var openSegment = connection.Table<PomodoroFocusSegmentRecord>()
                    .FirstOrDefault(segment => segment.SessionId == session.Id && segment.EndedAtUtc == null);
                if (openSegment is null)
                {
                    connection.Insert(new PomodoroFocusSegmentRecord
                    {
                        SessionId = session.Id,
                        StartedAtUtc = occurredAtUtc
                    });
                }
            });
        }

        public Task EndPomodoroPhaseAsync(
            string timerSlotId,
            PomodoroFocusPhase phase,
            DateTimeOffset occurredAt,
            string reason)
        {
            var occurredAtUtc = occurredAt.UtcDateTime;
            return _db.RunInTransactionAsync(connection =>
            {
                var session = FindOpenSession(connection, timerSlotId, phase);
                if (session is not null)
                    CloseSession(connection, session, occurredAtUtc, reason);
            });
        }

        public Task ReconcilePomodoroPhaseAsync(
            string timerSlotId,
            string timerName,
            int? taskId,
            string taskTitle,
            PomodoroRuntimeSnapshot snapshot,
            DateTimeOffset observedAt)
        {
            var observedAtUtc = observedAt.UtcDateTime;
            return _db.RunInTransactionAsync(connection =>
            {
                if (snapshot.State == PomodoroService.PomodoroState.CompletionPending)
                {
                    var endedAt = snapshot.CompletedAtUtc?.UtcDateTime ?? observedAtUtc;
                    CloseOpenSession(connection, timerSlotId, endedAt, "completed");
                    return;
                }

                if (snapshot.State == PomodoroService.PomodoroState.Idle)
                {
                    CloseOpenSession(connection, timerSlotId, observedAtUtc, "reconciled-idle");
                    return;
                }

                var state = snapshot.State == PomodoroService.PomodoroState.Paused
                    ? snapshot.PausedState
                    : snapshot.State;
                var phase = state == PomodoroService.PomodoroState.Resting
                    ? PomodoroFocusPhase.Rest
                    : PomodoroFocusPhase.Work;
                var session = FindOpenSession(connection, timerSlotId, phase);
                if (session is null)
                {
                    CloseOpenSession(connection, timerSlotId, observedAtUtc, "recovered");
                    session = new PomodoroFocusSessionRecord
                    {
                        TimerSlotId = timerSlotId,
                        TimerName = timerName,
                        TaskId = taskId,
                        TaskTitleSnapshot = taskTitle,
                        Phase = phase,
                        StartedAtUtc = observedAtUtc,
                        HasStartMarker = false
                    };
                    connection.Insert(session);
                }

                if (snapshot.State == PomodoroService.PomodoroState.Paused)
                {
                    CloseOpenSegments(connection, session.Id, observedAtUtc);
                    return;
                }

                var openSegment = connection.Table<PomodoroFocusSegmentRecord>()
                    .FirstOrDefault(segment => segment.SessionId == session.Id && segment.EndedAtUtc == null);
                if (openSegment is null)
                {
                    connection.Insert(new PomodoroFocusSegmentRecord
                    {
                        SessionId = session.Id,
                        StartedAtUtc = observedAtUtc
                    });
                }
            });
        }

        public async Task<FocusAnalyticsBridgeDto> GetPomodoroAnalyticsAsync(
            string period,
            DateTime anchorDate,
            DateTimeOffset? observedAt = null)
        {
            var normalizedPeriod = period is "week" or "month" ? period : "day";
            var anchor = anchorDate.Date;
            var rangeStartLocal = normalizedPeriod switch
            {
                "week" => anchor.AddDays(-(((int)anchor.DayOfWeek + 6) % 7)),
                "month" => new DateTime(anchor.Year, anchor.Month, 1),
                _ => anchor
            };
            var rangeEndLocal = normalizedPeriod switch
            {
                "week" => rangeStartLocal.AddDays(7),
                "month" => rangeStartLocal.AddMonths(1),
                _ => rangeStartLocal.AddDays(1)
            };
            var rangeStartUtc = LocalToUtc(rangeStartLocal);
            var rangeEndUtc = LocalToUtc(rangeEndLocal);
            var nowUtc = (observedAt ?? DateTimeOffset.Now).UtcDateTime;

            var sessions = await _db.Table<PomodoroFocusSessionRecord>()
                .Where(session => session.StartedAtUtc < rangeEndUtc &&
                                  (session.EndedAtUtc == null || session.EndedAtUtc > rangeStartUtc))
                .ToListAsync();
            var segments = await _db.Table<PomodoroFocusSegmentRecord>()
                .Where(segment => segment.StartedAtUtc < rangeEndUtc &&
                                  (segment.EndedAtUtc == null || segment.EndedAtUtc > rangeStartUtc))
                .ToListAsync();
            var sessionById = sessions.ToDictionary(session => session.Id);
            segments = segments.Where(segment => sessionById.ContainsKey(segment.SessionId)).ToList();

            var cells = CreateCells(normalizedPeriod, rangeStartLocal, rangeEndLocal);
            var sessionDurations = new Dictionary<int, double>();
            foreach (var segment in segments)
            {
                var session = sessionById[segment.SessionId];
                var segmentEndUtc = segment.EndedAtUtc ?? nowUtc;
                if (session.EndedAtUtc.HasValue && session.EndedAtUtc.Value < segmentEndUtc)
                    segmentEndUtc = session.EndedAtUtc.Value;
                var clippedStartUtc = segment.StartedAtUtc > rangeStartUtc
                    ? segment.StartedAtUtc
                    : rangeStartUtc;
                var clippedEndUtc = segmentEndUtc < rangeEndUtc ? segmentEndUtc : rangeEndUtc;
                if (clippedEndUtc <= clippedStartUtc) continue;

                sessionDurations[session.Id] = sessionDurations.GetValueOrDefault(session.Id) +
                    (clippedEndUtc - clippedStartUtc).TotalSeconds;
                foreach (var cell in cells)
                {
                    var overlapStart = clippedStartUtc > cell.StartsAtUtc ? clippedStartUtc : cell.StartsAtUtc;
                    var overlapEnd = clippedEndUtc < cell.EndsAtUtc ? clippedEndUtc : cell.EndsAtUtc;
                    if (overlapEnd <= overlapStart) continue;
                    var seconds = (overlapEnd - overlapStart).TotalSeconds;
                    if (session.Phase == PomodoroFocusPhase.Work) cell.WorkSeconds += seconds;
                    else cell.RestSeconds += seconds;
                }
            }

            foreach (var session in sessions.Where(session =>
                         session.Phase == PomodoroFocusPhase.Work && session.HasStartMarker))
            {
                var localStart = UtcToLocal(session.StartedAtUtc);
                var cell = cells.FirstOrDefault(candidate =>
                    session.StartedAtUtc >= candidate.StartsAtUtc &&
                    session.StartedAtUtc < candidate.EndsAtUtc);
                cell?.WorkStarts.Add(new FocusStartBridgeDto(
                    localStart.ToString("HH:mm"),
                    session.TimerName,
                    session.TaskId,
                    session.TaskTitleSnapshot));
            }

            var resultCells = cells.Select(cell => new FocusAnalyticsCellBridgeDto(
                cell.Key,
                cell.LocalStart.ToString("yyyy-MM-dd"),
                cell.Label,
                ToLocalIso(cell.LocalStart),
                ToLocalIso(cell.LocalEnd),
                RoundSeconds(cell.WorkSeconds),
                RoundSeconds(cell.RestSeconds),
                cell.WorkStarts)).ToList();
            var workSeconds = resultCells.Sum(cell => cell.WorkSeconds);
            var restSeconds = resultCells.Sum(cell => cell.RestSeconds);
            var activeDays = resultCells
                .Where(cell => cell.WorkSeconds + cell.RestSeconds > 0)
                .Select(cell => cell.Date)
                .Distinct(StringComparer.Ordinal)
                .Count();

            return new FocusAnalyticsBridgeDto(
                normalizedPeriod,
                anchor.ToString("yyyy-MM-dd"),
                RangeLabel(normalizedPeriod, rangeStartLocal, rangeEndLocal),
                rangeStartLocal.ToString("yyyy-MM-dd"),
                rangeEndLocal.ToString("yyyy-MM-dd"),
                workSeconds,
                restSeconds,
                workSeconds + restSeconds,
                resultCells.Sum(cell => cell.WorkStarts.Count),
                RoundSeconds(sessionDurations.Values.DefaultIfEmpty(0).Max()),
                activeDays,
                resultCells);
        }

        private static PomodoroFocusSessionRecord? FindOpenSession(
            SQLiteConnection connection,
            string timerSlotId,
            PomodoroFocusPhase phase) => connection.Table<PomodoroFocusSessionRecord>()
            .Where(session => session.TimerSlotId == timerSlotId &&
                              session.Phase == phase &&
                              session.EndedAtUtc == null)
            .OrderByDescending(session => session.Id)
            .FirstOrDefault();

        private static void CloseOpenSession(
            SQLiteConnection connection,
            string timerSlotId,
            DateTime endedAtUtc,
            string reason)
        {
            foreach (var session in connection.Table<PomodoroFocusSessionRecord>()
                         .Where(item => item.TimerSlotId == timerSlotId && item.EndedAtUtc == null)
                         .ToList())
            {
                CloseSession(connection, session, endedAtUtc, reason);
            }
        }

        private static void CloseSession(
            SQLiteConnection connection,
            PomodoroFocusSessionRecord session,
            DateTime endedAtUtc,
            string reason)
        {
            var safeEnd = endedAtUtc < session.StartedAtUtc ? session.StartedAtUtc : endedAtUtc;
            CloseOpenSegments(connection, session.Id, safeEnd);
            session.EndedAtUtc = safeEnd;
            session.EndReason = reason;
            connection.Update(session);
        }

        private static void CloseOpenSegments(
            SQLiteConnection connection,
            int sessionId,
            DateTime endedAtUtc)
        {
            foreach (var segment in connection.Table<PomodoroFocusSegmentRecord>()
                         .Where(item => item.SessionId == sessionId && item.EndedAtUtc == null)
                         .ToList())
            {
                segment.EndedAtUtc = endedAtUtc < segment.StartedAtUtc
                    ? segment.StartedAtUtc
                    : endedAtUtc;
                connection.Update(segment);
            }
        }

        private static List<FocusCellAccumulator> CreateCells(
            string period,
            DateTime rangeStartLocal,
            DateTime rangeEndLocal)
        {
            var step = period == "month" ? TimeSpan.FromDays(1) : TimeSpan.FromMinutes(30);
            var cells = new List<FocusCellAccumulator>();
            for (var cursor = rangeStartLocal; cursor < rangeEndLocal; cursor = cursor.Add(step))
            {
                var end = cursor.Add(step);
                cells.Add(new FocusCellAccumulator(
                    period == "month" ? cursor.ToString("yyyy-MM-dd") : cursor.ToString("yyyy-MM-ddTHH:mm"),
                    cursor,
                    end,
                    LocalToUtc(cursor),
                    LocalToUtc(end),
                    period == "month" ? cursor.ToString("M月d日") : cursor.ToString("HH:mm")));
            }
            return cells;
        }

        private static DateTime LocalToUtc(DateTime value) =>
            TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(value, DateTimeKind.Unspecified));

        private static DateTime UtcToLocal(DateTime value) =>
            TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(value, DateTimeKind.Utc), TimeZoneInfo.Local);

        private static string ToLocalIso(DateTime localValue)
        {
            var offset = TimeZoneInfo.Local.GetUtcOffset(localValue);
            return new DateTimeOffset(DateTime.SpecifyKind(localValue, DateTimeKind.Unspecified), offset)
                .ToString("yyyy-MM-dd'T'HH:mm:sszzz");
        }

        private static string RangeLabel(string period, DateTime start, DateTime end) => period switch
        {
            "month" => start.ToString("yyyy年M月"),
            "week" => $"{start:M月d日} – {end.AddDays(-1):M月d日}",
            _ => start.ToString("yyyy年M月d日")
        };

        private static int RoundSeconds(double seconds) =>
            seconds <= 0 ? 0 : (int)Math.Round(seconds, MidpointRounding.AwayFromZero);

        private sealed class FocusCellAccumulator
        {
            public FocusCellAccumulator(
                string key,
                DateTime localStart,
                DateTime localEnd,
                DateTime startsAtUtc,
                DateTime endsAtUtc,
                string label)
            {
                Key = key;
                LocalStart = localStart;
                LocalEnd = localEnd;
                StartsAtUtc = startsAtUtc;
                EndsAtUtc = endsAtUtc;
                Label = label;
            }

            public string Key { get; }
            public DateTime LocalStart { get; }
            public DateTime LocalEnd { get; }
            public DateTime StartsAtUtc { get; }
            public DateTime EndsAtUtc { get; }
            public string Label { get; }
            public double WorkSeconds { get; set; }
            public double RestSeconds { get; set; }
            public List<FocusStartBridgeDto> WorkStarts { get; } = new();
        }
    }
}
