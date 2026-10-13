using SQLite;

namespace Taskato.Models
{
    [Table("TaskImages")]
    public sealed class TaskImage
    {
        [PrimaryKey, AutoIncrement]
        public int Id { get; set; }
        [Indexed]
        public int TaskId { get; set; }
        public string FileName { get; set; } = string.Empty;
        public string ContentType { get; set; } = string.Empty;
        public byte[] Data { get; set; } = Array.Empty<byte>();
        public byte[] Thumbnail { get; set; } = Array.Empty<byte>();
    }
}
