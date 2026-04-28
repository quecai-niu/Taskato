using SQLite;

namespace Taskato.Models
{
    /// <summary>
    /// 任务数据模型 — 对应数据库中的 TaskItems 表
    /// 继承自 ViewModelBase 以支持界面的双向绑定（动态标签刷新）
    /// </summary>
    [Table("TaskItems")]
    public class TaskItem : Utils.ViewModelBase
    {
        private int _id;
        [PrimaryKey, AutoIncrement]
        public int Id { get => _id; set => SetProperty(ref _id, value); }

        private string _title = string.Empty;
        [MaxLength(500)]
        public string Title
        {
            get => _title;
            set
            {
                if (SetProperty(ref _title, value))
                {
                    OnPropertyChanged(nameof(Tags));
                    OnPropertyChanged(nameof(DisplayTitle));
                }
            }
        }

        private DateTime _createdAt = DateTime.Now;
        public DateTime CreatedAt { get => _createdAt; set => SetProperty(ref _createdAt, value); }

        private DateTime? _completedAt;
        public DateTime? CompletedAt { get => _completedAt; set => SetProperty(ref _completedAt, value); }

        private bool _isCompleted = false;
        public bool IsCompleted { get => _isCompleted; set => SetProperty(ref _isCompleted, value); }

        /// <summary>
        /// 后台逻辑属性：拖拽排序索引
        /// </summary>
        private int _orderIndex;
        public int OrderIndex { get => _orderIndex; set => SetProperty(ref _orderIndex, value); }

        // ==================== 微解析系统 ====================

        /// <summary>
        /// 动态提取文本中的 #标签
        /// </summary>
        [Ignore]
        public System.Collections.Generic.List<string> Tags
        {
            get
            {
                var tags = new System.Collections.Generic.List<string>();
                if (string.IsNullOrEmpty(Title)) return tags;
                
                var parts = Title.Split(' ');
                foreach (var part in parts)
                {
                    if (part.StartsWith("#") && part.Length > 1) 
                        tags.Add(part.Substring(1));
                }
                return tags;
            }
        }

        /// <summary>
        /// 剥离标签后的纯净标题
        /// </summary>
        [Ignore]
        public string DisplayTitle
        {
            get
            {
                if (string.IsNullOrEmpty(Title)) return string.Empty;
                var titles = Title.Split(' ').Where(p => !p.StartsWith("#"));
                return string.Join(" ", titles);
            }
        }
    }
}
