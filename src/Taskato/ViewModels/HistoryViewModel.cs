using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using System.Windows.Input;
using Taskato.Models;
using Taskato.Services;
using Taskato.Utils;

namespace Taskato.ViewModels
{
    /// <summary>
    /// 历史查询窗体的 ViewModel — 管理搜索条件和结果列表
    /// 
    /// 功能：
    /// 1. 按日期范围 + 关键词搜索历史任务
    /// 2. 将搜索结果导出为 CSV 文件
    /// </summary>
    public class HistoryViewModel : ViewModelBase
    {
        /// <summary>数据库服务</summary>
        private readonly DatabaseService _dbService;

        // ==================== 搜索条件 ====================

        /// <summary>
        /// 开始日期（默认为一个月前）
        /// </summary>
        private DateTime _startDate = DateTime.Today.AddMonths(-1);
        public DateTime StartDate
        {
            get => _startDate;
            set => SetProperty(ref _startDate, value);
        }

        /// <summary>
        /// 结束日期（默认为今天）
        /// </summary>
        private DateTime _endDate = DateTime.Today;
        public DateTime EndDate
        {
            get => _endDate;
            set => SetProperty(ref _endDate, value);
        }

        /// <summary>
        /// 搜索关键词（可选）
        /// </summary>
        private string _keyword = string.Empty;
        public string Keyword
        {
            get => _keyword;
            set => SetProperty(ref _keyword, value);
        }

        // ==================== 搜索结果 ====================

        /// <summary>
        /// 搜索结果列表
        /// </summary>
        public ObservableCollection<TaskItem> SearchResults { get; } = new();

        /// <summary>
        /// 搜索结果计数（显示在 UI 上）
        /// </summary>
        private int _resultCount;
        public int ResultCount
        {
            get => _resultCount;
            set => SetProperty(ref _resultCount, value);
        }

        // ==================== 命令 ====================

        /// <summary>执行搜索的命令</summary>
        public ICommand SearchCommand { get; }

        /// <summary>导出 CSV 的命令</summary>
        public ICommand ExportCsvCommand { get; }

        // ==================== 构造函数 ====================

        public HistoryViewModel(DatabaseService dbService)
        {
            _dbService = dbService;

            SearchCommand = new RelayCommand(async _ => await SearchAsync());
            ExportCsvCommand = new RelayCommand(async _ => await ExportCsvAsync(),
                _ => SearchResults.Count > 0);
        }

        // ==================== 方法 ====================

        /// <summary>
        /// 执行搜索 — 根据日期范围和关键词查询任务
        /// </summary>
        public async Task SearchAsync()
        {
            var results = await _dbService.SearchTasksAsync(StartDate, EndDate, Keyword);

            SearchResults.Clear();
            foreach (var task in results)
            {
                SearchResults.Add(task);
            }

            ResultCount = SearchResults.Count;
        }

        /// <summary>
        /// 导出搜索结果为 CSV 文件
        /// CSV 格式：任务标题, 创建时间, 完成时间, 状态
        /// </summary>
        private async Task ExportCsvAsync()
        {
            // 弹出文件保存对话框
            var saveDialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "CSV 文件|*.csv",
                DefaultExt = ".csv",
                FileName = $"Taskato_历史任务_{DateTime.Now:yyyyMMdd_HHmmss}"
            };

            if (saveDialog.ShowDialog() == true)
            {
                var sb = new StringBuilder();

                // CSV 表头（带 BOM 标记，确保 Excel 正确识别中文编码）
                sb.AppendLine("任务标题,创建时间,完成时间,状态");

                // 逐行写入任务数据
                foreach (var task in SearchResults)
                {
                    var status = task.IsCompleted ? "已完成" : "未完成";
                    var completedTime = task.CompletedAt?.ToString("yyyy-MM-dd HH:mm:ss") ?? "";

                    // CSV 字段用双引号包围，防止标题中的逗号导致格式错乱
                    sb.AppendLine($"\"{task.Title}\",\"{task.CreatedAt:yyyy-MM-dd HH:mm:ss}\",\"{completedTime}\",\"{status}\"");
                }

                // 写入文件（UTF-8 with BOM，Excel 才能正确显示中文）
                await File.WriteAllTextAsync(saveDialog.FileName, sb.ToString(), new UTF8Encoding(true));
            }
        }
    }
}
