using SQLite;

namespace Taskato.Models
{
    public enum TaskProgressSource
    {
        Pomodoro = 0,
        Manual = 1,
        LegacyPomodoroTask = 2
    }

    public enum TaskWorkStatus
    {
        NotStarted = 0,
        InProgress = 1,
        Completed = 2
    }

    /// <summary>
    /// 一次任务推进事实。任务可为空，用于用户在没有打开具体任务时完成的番茄钟。
    /// 原始时长与统计时长分别保存，使人工修正可恢复且不会覆盖计时事实。
    /// </summary>
    [Table("TaskProgressRecords")]
    public sealed class TaskProgressRecord
    {
        [PrimaryKey, AutoIncrement]
        public int Id { get; set; }

        [Indexed]
        public int? TaskId { get; set; }

        [MaxLength(500)]
        public string TaskTitleSnapshot { get; set; } = string.Empty;

        [MaxLength(2000)]
        public string TaskPathSnapshot { get; set; } = string.Empty;

        [MaxLength(5000)]
        public string Note { get; set; } = string.Empty;

        [Indexed]
        public DateTime StartedAt { get; set; }

        [Indexed]
        public DateTime CompletedAt { get; set; }

        public DateTime CreatedAt { get; set; }

        public int OriginalDurationMinutes { get; set; }

        public int DurationMinutes { get; set; }

        [MaxLength(80)]
        public string EndReason { get; set; } = string.Empty;

        public TaskProgressSource Source { get; set; }

        public int? SourceSessionId { get; set; }

        [Indexed(Unique = true)]
        public int? LegacyTaskId { get; set; }
    }
}
