using SQLite;

namespace Taskato.Models
{
    public enum PomodoroFocusPhase
    {
        Work = 1,
        Rest = 2
    }

    /// <summary>
    /// 一次工作或休息阶段。会话负责保存业务上下文，连续运行区间由 PomodoroFocusSegmentRecord 表示。
    /// </summary>
    [Table("PomodoroFocusSessions")]
    public sealed class PomodoroFocusSessionRecord
    {
        [PrimaryKey, AutoIncrement]
        public int Id { get; set; }

        [Indexed]
        public string TimerSlotId { get; set; } = string.Empty;

        public string TimerName { get; set; } = string.Empty;
        public int? TaskId { get; set; }
        public string TaskTitleSnapshot { get; set; } = string.Empty;
        public PomodoroFocusPhase Phase { get; set; }

        [Indexed]
        public DateTime StartedAtUtc { get; set; }

        public DateTime? EndedAtUtc { get; set; }
        public string EndReason { get; set; } = string.Empty;

        /// <summary>
        /// 只有用户真正启动一轮工作时才显示启动标记；恢复旧快照或缺失会话时的降级记录不伪造启动事实。
        /// </summary>
        public bool HasStartMarker { get; set; }
    }

    /// <summary>暂停会切断连续运行区间，恢复后在同一会话下建立新区间。</summary>
    [Table("PomodoroFocusSegments")]
    public sealed class PomodoroFocusSegmentRecord
    {
        [PrimaryKey, AutoIncrement]
        public int Id { get; set; }

        [Indexed]
        public int SessionId { get; set; }

        [Indexed]
        public DateTime StartedAtUtc { get; set; }

        public DateTime? EndedAtUtc { get; set; }
    }
}
