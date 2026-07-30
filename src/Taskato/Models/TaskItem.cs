using SQLite;
using System.Collections.ObjectModel;

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
        [MaxLength(5000)]
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

        /// <summary>
        /// 完成时记录的实际耗时（分钟），为空表示未记录。
        /// </summary>
        private int? _completionDurationMinutes;
        public int? CompletionDurationMinutes
        {
            get => _completionDurationMinutes;
            set
            {
                if (SetProperty(ref _completionDurationMinutes, value))
                {
                    OnPropertyChanged(nameof(HasCompletionDuration));
                    OnPropertyChanged(nameof(CompletionDurationDisplay));
                }
            }
        }

        // 仅保留旧版数据库兼容。初始化时会把历史内容合并进 Title，网页端不再读写第二份任务内容。
        private string _notes = string.Empty;
        [MaxLength(4000)]
        public string Notes
        {
            get => _notes;
            set => SetProperty(ref _notes, value ?? string.Empty);
        }

        private DateTime _lastModifiedAt = DateTime.Now;
        public DateTime LastModifiedAt { get => _lastModifiedAt; set => SetProperty(ref _lastModifiedAt, value); }

        /// <summary>
        /// 父任务编号；为空表示顶级任务。
        /// </summary>
        private int? _parentId;
        public int? ParentId { get => _parentId; set => SetProperty(ref _parentId, value); }

        private bool _isCompleted = false;
        public bool IsCompleted { get => _isCompleted; set => SetProperty(ref _isCompleted, value); }

        /// <summary>
        /// 优先级：0=无, 1=中, 2=高, 3=紧急
        /// </summary>
        private int _priority = 0;
        public int Priority { get => _priority; set => SetProperty(ref _priority, value); }

        /// <summary>
        /// 拖拽排序序号
        /// </summary>
        private double _orderIndex = 0;
        public double OrderIndex { get => _orderIndex; set => SetProperty(ref _orderIndex, value); }

        // ==================== 树形界面运行时属性 ====================

        private int _depth;
        [Ignore]
        public int Depth
        {
            get => _depth;
            set
            {
                if (SetProperty(ref _depth, value))
                {
                    OnPropertyChanged(nameof(IndentWidth));
                    OnPropertyChanged(nameof(CanAddChild));
                }
            }
        }

        private bool _isExpanded = true;
        [Ignore]
        public bool IsExpanded { get => _isExpanded; set => SetProperty(ref _isExpanded, value); }

        private string _newChildTitle = string.Empty;
        [Ignore]
        public string NewChildTitle { get => _newChildTitle; set => SetProperty(ref _newChildTitle, value); }

        private bool _isAddingChild;
        [Ignore]
        public bool IsAddingChild { get => _isAddingChild; set => SetProperty(ref _isAddingChild, value); }

        [Ignore]
        public ObservableCollection<TaskItem> Children { get; } = new();

        private int _completedLeafCount;
        [Ignore]
        public int CompletedLeafCount
        {
            get => _completedLeafCount;
            set
            {
                if (SetProperty(ref _completedLeafCount, value))
                    OnPropertyChanged(nameof(ProgressDisplay));
            }
        }

        private int _totalLeafCount = 1;
        [Ignore]
        public int TotalLeafCount
        {
            get => _totalLeafCount;
            set
            {
                if (SetProperty(ref _totalLeafCount, value))
                    OnPropertyChanged(nameof(ProgressDisplay));
            }
        }

        [Ignore]
        public bool HasChildren => Children.Count > 0;

        private bool _hasVisibleChildren;
        /// <summary>当前任务列表中是否存在可见子任务；历史子任务不会制造空的展开按钮。</summary>
        [Ignore]
        public bool HasVisibleChildren
        {
            get => _hasVisibleChildren;
            set => SetProperty(ref _hasVisibleChildren, value);
        }

        [Ignore]
        public bool IsLeaf => !HasChildren;

        [Ignore]
        public bool CanAddChild => Depth < 4;

        [Ignore]
        public double IndentWidth => Depth * 24;

        [Ignore]
        public string ProgressDisplay => $"{CompletedLeafCount}/{TotalLeafCount}";

        public void NotifyTreeStateChanged()
        {
            OnPropertyChanged(nameof(HasChildren));
            OnPropertyChanged(nameof(IsLeaf));
            OnPropertyChanged(nameof(CanAddChild));
            OnPropertyChanged(nameof(ProgressDisplay));
        }

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

        [Ignore]
        public bool HasCompletionDuration => CompletionDurationMinutes is > 0;

        [Ignore]
        public string CompletionDurationDisplay
        {
            get
            {
                if (CompletionDurationMinutes is not > 0) return string.Empty;
                var hours = CompletionDurationMinutes.Value / 60;
                var minutes = CompletionDurationMinutes.Value % 60;

                if (hours > 0 && minutes > 0) return $"{hours}小时{minutes}分钟";
                if (hours > 0) return $"{hours}小时";
                return $"{minutes}分钟";
            }
        }
    }
}
