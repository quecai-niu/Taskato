using SQLite;

namespace Taskato.Models
{
    public enum TaskActivityType
    {
        Created = 0,
        Updated = 1,
        Moved = 2,
        Completed = 3,
        Reopened = 4,
        Deleted = 5,
        MigrationBoundary = 6
    }

    /// <summary>
    /// 只追加的任务活动事实。任务删除后仍保留快照，用于可靠重建历史时间线。
    /// </summary>
    [Table("TaskActivityRecords")]
    public sealed class TaskActivityRecord
    {
        [PrimaryKey, AutoIncrement]
        public int Id { get; set; }

        [Indexed]
        public int? TaskId { get; set; }

        [Indexed]
        public TaskActivityType ActivityType { get; set; }

        [MaxLength(500)]
        public string TitleSnapshot { get; set; } = string.Empty;

        [MaxLength(2000)]
        public string DetailSnapshot { get; set; } = string.Empty;

        public int? ParentIdSnapshot { get; set; }

        public int DepthSnapshot { get; set; }

        [Indexed]
        public DateTime OccurredAt { get; set; }

        public int? CompletionRecordId { get; set; }

        /// <summary>
        /// 为创建和完成迁移提供稳定幂等键；实时编辑、移动等事件使用随机键。
        /// </summary>
        [Indexed(Unique = true), MaxLength(160)]
        public string SourceKey { get; set; } = string.Empty;

        public bool IsLegacyBackfill { get; set; }
    }
}
