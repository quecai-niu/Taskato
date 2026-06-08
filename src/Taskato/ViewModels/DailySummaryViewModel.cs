using System.Collections.ObjectModel;
using System.Text;
using System.Windows;
using System.Windows.Input;
using Taskato.Models;
using Taskato.Services;
using Taskato.Utils;

namespace Taskato.ViewModels
{
    /// <summary>
    /// 每日总结窗体的 ViewModel — 展示某一天的任务完成统计和列表
    /// </summary>
    public class DailySummaryViewModel : ViewModelBase
    {
        private readonly DatabaseService _dbService;

        private DateTime _currentDate = DateTime.Today;
        /// <summary>
        /// 当前查看的日期，切换时自动重新加载数据
        /// </summary>
        public DateTime CurrentDate
        {
            get => _currentDate;
            set
            {
                if (SetProperty(ref _currentDate, value))
                {
                    OnPropertyChanged(nameof(DateDisplay));
                    _ = LoadSummaryAsync();
                }
            }
        }

        /// <summary>格式化日期显示文本</summary>
        public string DateDisplay => $"{CurrentDate:yyyy-MM-dd} {GetWeekdayText(CurrentDate)} 每日总结";

        private string _summaryLine = string.Empty;
        /// <summary>一句话总结导语</summary>
        public string SummaryLine
        {
            get => _summaryLine;
            set => SetProperty(ref _summaryLine, value);
        }

        private int _totalCount;
        /// <summary>当天任务总数</summary>
        public int TotalCount
        {
            get => _totalCount;
            set => SetProperty(ref _totalCount, value);
        }

        private int _completedCount;
        /// <summary>当天已完成数</summary>
        public int CompletedCount
        {
            get => _completedCount;
            set => SetProperty(ref _completedCount, value);
        }

        private string _completionRate = string.Empty;
        /// <summary>完成率百分比文本</summary>
        public string CompletionRate
        {
            get => _completionRate;
            set => SetProperty(ref _completionRate, value);
        }

        private int _highPriorityCompleted;
        /// <summary>当天完成的高优先级任务数</summary>
        public int HighPriorityCompleted
        {
            get => _highPriorityCompleted;
            set => SetProperty(ref _highPriorityCompleted, value);
        }

        private int _totalDurationMinutes;
        /// <summary>当天记录的完成耗时合计（分钟）</summary>
        public int TotalDurationMinutes
        {
            get => _totalDurationMinutes;
            set
            {
                if (SetProperty(ref _totalDurationMinutes, value))
                    OnPropertyChanged(nameof(TotalDurationDisplay));
            }
        }

        public string TotalDurationDisplay => FormatDuration(TotalDurationMinutes);

        /// <summary>已完成任务列表</summary>
        public ObservableCollection<TaskItem> CompletedTasks { get; } = new();

        /// <summary>未完成任务列表</summary>
        public ObservableCollection<TaskItem> UncompletedTasks { get; } = new();

        // ==================== 命令 ====================

        /// <summary>切换到前一天</summary>
        public ICommand GoPrevCommand { get; }

        /// <summary>切换到后一天（不超过今天）</summary>
        public ICommand GoNextCommand { get; }

        /// <summary>复制总结文本到剪贴板</summary>
        public ICommand CopyCommand { get; }

        /// <summary>导出总结为文本文件</summary>
        public ICommand ExportCommand { get; }

        /// <summary>补录当前日期完成的任务</summary>
        public ICommand BackfillTaskCommand { get; }

        public DailySummaryViewModel(DatabaseService dbService)
        {
            _dbService = dbService;

            GoPrevCommand = new RelayCommand(_ =>
            {
                CurrentDate = CurrentDate.AddDays(-1);
            });

            GoNextCommand = new RelayCommand(_ =>
            {
                if (CurrentDate < DateTime.Today)
                    CurrentDate = CurrentDate.AddDays(1);
            });

            CopyCommand = new RelayCommand(_ =>
            {
                System.Windows.Clipboard.SetText(BuildSummaryText());
            });

            ExportCommand = new RelayCommand(_ =>
            {
                var saveDialog = new Microsoft.Win32.SaveFileDialog
                {
                    Filter = "文本文件|*.txt",
                    DefaultExt = ".txt",
                    FileName = $"Taskato_每日总结_{CurrentDate:yyyyMMdd}"
                };
                if (saveDialog.ShowDialog() == true)
                {
                    System.IO.File.WriteAllText(saveDialog.FileName, BuildSummaryText(),
                        new UTF8Encoding(true));
                }
            });

            BackfillTaskCommand = new RelayCommand(async _ => await BackfillTaskAsync());
        }

        /// <summary>
        /// 加载当前日期的已完成任务数据并刷新统计
        /// </summary>
        public async Task LoadSummaryAsync()
        {
            var completed = await _dbService.GetTasksCompletedOnAsync(CurrentDate);

            TotalCount = completed.Count;
            CompletedCount = completed.Count;
            CompletionRate = CompletedCount > 0 ? "100%" : "N/A";
            HighPriorityCompleted = completed.Count(t => t.Priority >= 2);
            TotalDurationMinutes = completed.Sum(t => t.CompletionDurationMinutes ?? 0);

            var dayLabel = CurrentDate.Date == DateTime.Today ? "今天" : "这一天";
            if (CompletedCount == 0)
                SummaryLine = $"{dayLabel}没有完成任务记录。";
            else
                SummaryLine = $"{dayLabel}完成了 {CompletedCount} 个任务，太棒了！";

            CompletedTasks.Clear();
            foreach (var t in completed.OrderByDescending(t => t.Priority))
                CompletedTasks.Add(t);

            UncompletedTasks.Clear();
        }

        private async Task BackfillTaskAsync()
        {
            var completionDate = CurrentDate.Date;
            if (completionDate > DateTime.Today)
            {
                MessageBox.Show(GetOwnerWindow(), "不能补录未来日期的任务。", "补录完成任务",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var candidates = await _dbService.GetUncompletedTasksAsync();
            if (candidates.Count == 0)
            {
                MessageBox.Show(GetOwnerWindow(), "当前没有可补录的未完成任务。", "补录完成任务",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var owner = GetOwnerWindow();
            if (!Views.TaskCompletionDialog.TryShowForBackfill(
                    owner, completionDate, candidates, out var selectedTask, out var durationMinutes) ||
                selectedTask is null)
            {
                return;
            }

            await _dbService.CompleteTaskAsync(
                selectedTask,
                BuildCompletionTime(completionDate),
                durationMinutes);
            await LoadSummaryAsync();
        }

        public async Task SaveTaskEditAsync(TaskItem task)
        {
            await _dbService.UpdateTaskAsync(task);
            await LoadSummaryAsync();
        }

        /// <summary>
        /// 构建纯文本格式的总结内容，供剪贴板复制和文件导出使用
        /// </summary>
        private string BuildSummaryText()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"{CurrentDate:yyyy-MM-dd} {GetWeekdayText(CurrentDate)} 每日总结");
            sb.AppendLine(new string('-', 30));
            sb.AppendLine();
            sb.AppendLine(SummaryLine);
            sb.AppendLine();
            sb.AppendLine($"统计：完成 {CompletedCount} | 高优先级完成 {HighPriorityCompleted} | 耗时 {TotalDurationDisplay}");
            sb.AppendLine();
            sb.AppendLine("[已完成]：");
            foreach (var t in CompletedTasks)
            {
                var pLabel = t.Priority switch { 3 => "[紧急]", 2 => "[高]", 1 => "[中]", _ => "" };
                var duration = t.HasCompletionDuration ? $"（耗时 {t.CompletionDurationDisplay}）" : "";
                sb.AppendLine($"  {pLabel} {t.Title}{duration}");
            }
            return sb.ToString();
        }

        private static DateTime BuildCompletionTime(DateTime completionDate)
        {
            return completionDate.Date == DateTime.Today
                ? DateTime.Now
                : completionDate.Date.Add(DateTime.Now.TimeOfDay);
        }

        private static Window? GetOwnerWindow()
        {
            return Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive)
                ?? Application.Current?.MainWindow;
        }

        private static string GetWeekdayText(DateTime date)
        {
            return date.DayOfWeek switch
            {
                DayOfWeek.Monday => "星期一",
                DayOfWeek.Tuesday => "星期二",
                DayOfWeek.Wednesday => "星期三",
                DayOfWeek.Thursday => "星期四",
                DayOfWeek.Friday => "星期五",
                DayOfWeek.Saturday => "星期六",
                DayOfWeek.Sunday => "星期日",
                _ => string.Empty
            };
        }

        private static string FormatDuration(int durationMinutes)
        {
            if (durationMinutes <= 0) return "0分钟";

            var hours = durationMinutes / 60;
            var minutes = durationMinutes % 60;

            if (hours > 0 && minutes > 0) return $"{hours}小时{minutes}分钟";
            if (hours > 0) return $"{hours}小时";
            return $"{minutes}分钟";
        }
    }
}
