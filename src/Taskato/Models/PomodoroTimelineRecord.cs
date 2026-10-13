using SQLite;

namespace Taskato.Models
{
    [Table("PomodoroTimelineEvents")]
    public sealed class PomodoroTimelineRecord
    {
        [PrimaryKey, AutoIncrement]
        public int Id { get; set; }
        [Indexed]
        public string TimerSlotId { get; set; } = string.Empty;
        public DateTime OccurredAtUtc { get; set; }
        public string Kind { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Detail { get; set; } = string.Empty;
        public string TaskTitleSnapshot { get; set; } = string.Empty;
        [Ignore]
        public double? DurationSeconds { get; set; }
        [Ignore]
        public string DurationLabel { get; set; } = string.Empty;
    }

    public sealed record PomodoroTimelineEntry(
        DateTimeOffset OccurredAt, string Kind, string Title, string Detail = "");
}
