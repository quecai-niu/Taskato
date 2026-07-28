using System.Collections.ObjectModel;
using System.Text;
using System.Windows;
using System.Windows.Input;
using Taskato.Models;
using Taskato.Services;
using Taskato.Utils;

namespace Taskato.ViewModels
{
    public sealed class DailySummaryGroup
    {
        public int TopLevelTaskId { get; init; }
        public string Title { get; init; } = string.Empty;
        public ObservableCollection<TaskCompletionRecord> Records { get; } = new();
    }

    /// <summary>
    /// 每日总结基于不可变完成记录生成，但只展示当前仍然有效的最后一次完成。
    /// 取消完成不会删除历史记录，只会让对应记录退出每日总结。
    /// </summary>
    public class DailySummaryViewModel : ViewModelBase
    {
        private readonly DatabaseService _dbService;
        public DatabaseService DatabaseService => _dbService;

        private DateTime _currentDate = DateTime.Today;
        public DateTime CurrentDate
        {
            get => _currentDate;
            set
            {
                if (!SetProperty(ref _currentDate, value)) return;
                OnPropertyChanged(nameof(DateDisplay));
                _ = LoadSummaryAsync();
            }
        }

        public string DateDisplay => $"{CurrentDate:yyyy-MM-dd} {GetWeekdayText(CurrentDate)} 每日总结";

        private string _summaryLine = string.Empty;
        public string SummaryLine
        {
            get => _summaryLine;
            set => SetProperty(ref _summaryLine, value);
        }

        private int _completedCount;
        /// <summary>当天完成记录总数，包含叶子任务与自动完成的父级成果。</summary>
        public int CompletedCount
        {
            get => _completedCount;
            set => SetProperty(ref _completedCount, value);
        }

        private int _leafCompletedCount;
        public int LeafCompletedCount
        {
            get => _leafCompletedCount;
            set => SetProperty(ref _leafCompletedCount, value);
        }

        private int _parentAchievementCount;
        public int ParentAchievementCount
        {
            get => _parentAchievementCount;
            set => SetProperty(ref _parentAchievementCount, value);
        }

        private int _highPriorityCompleted;
        public int HighPriorityCompleted
        {
            get => _highPriorityCompleted;
            set => SetProperty(ref _highPriorityCompleted, value);
        }

        private int _totalDurationMinutes;
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

        public ObservableCollection<DailySummaryGroup> CompletionGroups { get; } = new();

        public ICommand GoPrevCommand { get; }
        public ICommand GoNextCommand { get; }
        public ICommand CopyCommand { get; }
        public ICommand ExportCommand { get; }
        public ICommand BackfillTaskCommand { get; }

        public DailySummaryViewModel(DatabaseService dbService)
        {
            _dbService = dbService;

            GoPrevCommand = new RelayCommand(_ => CurrentDate = CurrentDate.AddDays(-1));
            GoNextCommand = new RelayCommand(_ =>
            {
                if (CurrentDate < DateTime.Today)
                    CurrentDate = CurrentDate.AddDays(1);
            });
            CopyCommand = new RelayCommand(_ => System.Windows.Clipboard.SetText(BuildSummaryText()));
            ExportCommand = new RelayCommand(_ => ExportSummary());
            BackfillTaskCommand = new RelayCommand(async _ => await BackfillTaskAsync());
        }

        public async Task LoadSummaryAsync()
        {
            var allRecords = await _dbService.GetCompletionRecordsOnAsync(CurrentDate);
            var records = allRecords.Where(IsActiveCompletionRecord).ToList();

            CompletedCount = records.Count;
            LeafCompletedCount = records.Count(record => record.CompletionKind == CompletionKind.LeafTask);
            ParentAchievementCount = records.Count - LeafCompletedCount;
            HighPriorityCompleted = records.Count(record => record.PrioritySnapshot >= 2);
            TotalDurationMinutes = records.Sum(record => record.DurationMinutes ?? 0);

            var dayLabel = CurrentDate.Date == DateTime.Today ? "今天" : "这一天";
            SummaryLine = CompletedCount == 0
                ? $"{dayLabel}没有完成任务记录。"
                : $"{dayLabel}完成了 {LeafCompletedCount} 个叶子任务，并达成 {ParentAchievementCount} 个父级成果。";

            CompletionGroups.Clear();
            foreach (var group in records
                         .GroupBy(record => new
                         {
                             record.TopLevelTaskId,
                             record.TopLevelTitleSnapshot
                         })
                         .OrderBy(group => group.Min(record => record.CompletedAt)))
            {
                var summaryGroup = new DailySummaryGroup
                {
                    TopLevelTaskId = group.Key.TopLevelTaskId,
                    Title = group.Key.TopLevelTitleSnapshot
                };

                foreach (var record in group
                             .OrderBy(record => record.CompletedAt)
                             .ThenBy(record => record.DepthSnapshot))
                {
                    summaryGroup.Records.Add(record);
                }
                CompletionGroups.Add(summaryGroup);
            }
        }

        /// <summary>
        /// 已删除任务无法再判断当前状态，因此继续保留完成时快照。
        /// 对仍存在的任务，完成状态和完成时间必须同时匹配，才能排除已经取消或被后续完成替代的旧记录。
        /// </summary>
        private static bool IsActiveCompletionRecord(TaskCompletionRecord record)
        {
            if (record.CurrentTask is null)
                return true;

            return record.CurrentTask.IsCompleted &&
                   record.CurrentTask.CompletedAt == record.CompletedAt;
        }

        private async Task BackfillTaskAsync()
        {
            var completionDate = CurrentDate.Date;
            if (completionDate > DateTime.Today)
            {
                MessageBox.Show(
                    GetOwnerWindow(),
                    "不能为未来日期补充任务。",
                    "补充任务",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            var candidates = await _dbService.GetUncompletedLeafTasksAsync();
            if (candidates.Count == 0)
            {
                MessageBox.Show(
                    GetOwnerWindow(),
                    "当前没有可补充的未完成叶子任务。",
                    "补充任务",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            if (!Views.TaskCompletionDialog.TryShowForBackfill(
                    GetOwnerWindow(),
                    completionDate,
                    candidates,
                    out var selectedTask,
                    out var durationMinutes) ||
                selectedTask is null)
            {
                return;
            }

            await _dbService.CompleteTaskWithCascadeAsync(
                selectedTask,
                BuildCompletionTime(completionDate),
                durationMinutes,
                CompletionTrigger.Backfill);
            await LoadSummaryAsync();
        }

        private void ExportSummary()
        {
            var saveDialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "文本文件|*.txt",
                DefaultExt = ".txt",
                FileName = $"Taskato_每日总结_{CurrentDate:yyyyMMdd}"
            };
            if (saveDialog.ShowDialog() != true) return;
            System.IO.File.WriteAllText(saveDialog.FileName, BuildSummaryText(), new UTF8Encoding(true));
        }

        private string BuildSummaryText()
        {
            var builder = new StringBuilder();
            builder.AppendLine($"{CurrentDate:yyyy-MM-dd} {GetWeekdayText(CurrentDate)} 每日总结");
            builder.AppendLine(new string('-', 36));
            builder.AppendLine();
            builder.AppendLine(SummaryLine);
            builder.AppendLine();
            builder.AppendLine(
                $"统计：完成记录 {CompletedCount} | 叶子任务 {LeafCompletedCount} | 父级成果 {ParentAchievementCount} | 高优先级成果 {HighPriorityCompleted} | 耗时 {TotalDurationDisplay}");

            foreach (var group in CompletionGroups)
            {
                builder.AppendLine();
                builder.AppendLine($"[{group.Title}]");
                foreach (var record in group.Records)
                {
                    var indent = new string(' ', record.DepthSnapshot * 2);
                    var priority = record.PrioritySnapshot switch
                    {
                        3 => "[紧急] ",
                        2 => "[高] ",
                        1 => "[中] ",
                        _ => string.Empty
                    };
                    var duration = record.HasDuration ? $"（耗时 {record.DurationDisplay}）" : string.Empty;
                    builder.AppendLine(
                        $"{indent}{priority}{record.TitleSnapshot} · {record.KindDisplay}{duration}");
                }
            }
            return builder.ToString();
        }

        private static DateTime BuildCompletionTime(DateTime completionDate)
        {
            return completionDate.Date == DateTime.Today
                ? DateTime.Now
                : completionDate.Date.Add(DateTime.Now.TimeOfDay);
        }

        private static Window? GetOwnerWindow()
        {
            return Application.Current?.Windows.OfType<Window>().FirstOrDefault(window => window.IsActive)
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
