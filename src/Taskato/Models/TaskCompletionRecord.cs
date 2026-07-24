using SQLite;

namespace Taskato.Models
{
    public enum CompletionKind
    {
        LeafTask = 0,
        ParentMilestone = 1,
        TopLevelAchievement = 2
    }

    public enum CompletionTrigger
    {
        Manual = 0,
        AutomaticCascade = 1,
        Backfill = 2,
        LegacyMigration = 3
    }

    /// <summary>
    /// 一次不可变的任务完成事实。快照字段确保任务后续改名、移动或删除时，
    /// 已生成的每日总结仍保持当时的内容。
    /// </summary>
    [Table("TaskCompletionRecords")]
    public class TaskCompletionRecord
    {
        [PrimaryKey, AutoIncrement]
        public int Id { get; set; }

        [Indexed]
        public int TaskId { get; set; }

        [Indexed]
        public int TopLevelTaskId { get; set; }

        [MaxLength(500)]
        public string TitleSnapshot { get; set; } = string.Empty;

        [MaxLength(2000)]
        public string PathSnapshot { get; set; } = string.Empty;

        [MaxLength(500)]
        public string TopLevelTitleSnapshot { get; set; } = string.Empty;

        public int DepthSnapshot { get; set; }

        public int PrioritySnapshot { get; set; }

        public CompletionKind CompletionKind { get; set; }

        public CompletionTrigger TriggerType { get; set; }

        [Indexed]
        public DateTime CompletedAt { get; set; }

        public int? DurationMinutes { get; set; }

        [Ignore]
        public TaskItem? CurrentTask { get; set; }

        [Ignore]
        public double IndentWidth => DepthSnapshot * 22;

        [Ignore]
        public bool HasDuration => DurationMinutes is > 0;

        [Ignore]
        public string DurationDisplay
        {
            get
            {
                if (DurationMinutes is not > 0) return string.Empty;
                var hours = DurationMinutes.Value / 60;
                var minutes = DurationMinutes.Value % 60;
                if (hours > 0 && minutes > 0) return $"{hours}小时{minutes}分钟";
                if (hours > 0) return $"{hours}小时";
                return $"{minutes}分钟";
            }
        }

        [Ignore]
        public string KindDisplay => CompletionKind switch
        {
            CompletionKind.TopLevelAchievement => "大任务完成",
            CompletionKind.ParentMilestone => "阶段完成",
            _ => "子任务完成"
        };
    }
}
