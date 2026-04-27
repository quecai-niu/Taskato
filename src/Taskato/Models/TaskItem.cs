using SQLite;

namespace Taskato.Models
{
    /// <summary>
    /// 任务数据模型 — 对应数据库中的 TaskItems 表
    /// 每条记录代表用户创建的一个工作任务
    /// </summary>
    [Table("TaskItems")]
    public class TaskItem
    {
        /// <summary>
        /// 任务唯一标识（主键，自增）
        /// </summary>
        [PrimaryKey, AutoIncrement]
        public int Id { get; set; }

        /// <summary>
        /// 任务标题 / 内容描述
        /// </summary>
        [MaxLength(500)]
        public string Title { get; set; } = string.Empty;

        /// <summary>
        /// 任务创建时间（自动记录添加任务的时刻）
        /// </summary>
        public DateTime CreatedAt { get; set; } = DateTime.Now;

        /// <summary>
        /// 任务完成时间（勾选完成后记录，未完成时为 null）
        /// </summary>
        public DateTime? CompletedAt { get; set; }

        /// <summary>
        /// 是否已完成（勾选状态）
        /// </summary>
        public bool IsCompleted { get; set; } = false;
    }
}
