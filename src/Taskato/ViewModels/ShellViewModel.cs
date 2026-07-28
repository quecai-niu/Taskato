using System.Windows.Input;
using Taskato.Models;
using Taskato.Services;
using Taskato.Utils;

namespace Taskato.ViewModels
{
    /// <summary>
    /// 主窗口壳层：组合各业务页面，并统一处理一级导航与页面激活刷新。
    /// </summary>
    public sealed class ShellViewModel : ViewModelBase
    {
        private readonly SettingsService _settingsService;
        private AppSection _activeSection;

        public MainViewModel TaskWorkspace { get; }
        public DailySummaryViewModel DailySummary { get; }
        public HistoryViewModel History { get; }
        public SettingsViewModel Settings { get; }

        public AppSection ActiveSection
        {
            get => _activeSection;
            private set
            {
                if (!SetProperty(ref _activeSection, value)) return;
                OnPropertyChanged(nameof(IsTasksActive));
                OnPropertyChanged(nameof(IsDailySummaryActive));
                OnPropertyChanged(nameof(IsHistoryActive));
                OnPropertyChanged(nameof(IsSettingsActive));
                OnPropertyChanged(nameof(PageTitle));
            }
        }

        public bool IsTasksActive => ActiveSection == AppSection.Tasks;
        public bool IsDailySummaryActive => ActiveSection == AppSection.DailySummary;
        public bool IsHistoryActive => ActiveSection == AppSection.History;
        public bool IsSettingsActive => ActiveSection == AppSection.Settings;

        public string PageTitle => ActiveSection switch
        {
            AppSection.DailySummary => "每日总结",
            AppSection.History => "历史记录",
            AppSection.Settings => "设置",
            _ => "任务工作区"
        };

        public ICommand ShowTasksCommand { get; }
        public ICommand ShowDailySummaryCommand { get; }
        public ICommand ShowHistoryCommand { get; }
        public ICommand ShowSettingsCommand { get; }

        public ShellViewModel(
            MainViewModel taskWorkspace,
            DatabaseService databaseService,
            SettingsService settingsService)
        {
            TaskWorkspace = taskWorkspace;
            DailySummary = new DailySummaryViewModel(databaseService);
            History = new HistoryViewModel(databaseService);
            Settings = new SettingsViewModel(settingsService, taskWorkspace);
            _settingsService = settingsService;
            _activeSection = ParseSavedSection(settingsService.Config.LastAppSection);
            TaskWorkspace.DailySummaryRequested += date => _ = ShowDailySummaryAsync(date);

            ShowTasksCommand = new RelayCommand(_ => _ = NavigateAsync(AppSection.Tasks));
            ShowDailySummaryCommand = new RelayCommand(_ => _ = NavigateAsync(AppSection.DailySummary));
            ShowHistoryCommand = new RelayCommand(_ => _ = NavigateAsync(AppSection.History));
            ShowSettingsCommand = new RelayCommand(_ => _ = NavigateAsync(AppSection.Settings));
        }

        public async Task NavigateAsync(AppSection section)
        {
            ActiveSection = section;
            _settingsService.Config.LastAppSection = section.ToString();
            _settingsService.Save();

            if (section == AppSection.DailySummary)
            {
                DailySummary.CurrentDate = DateTime.Today;
                await DailySummary.LoadSummaryAsync();
            }
            else if (section == AppSection.History)
            {
                await History.SearchAsync();
            }
            else if (section == AppSection.Tasks)
            {
                await TaskWorkspace.LoadTodayTasksAsync();
            }
            else if (section == AppSection.Settings)
            {
                Settings.ReloadFromSettings();
            }
        }

        private async Task ShowDailySummaryAsync(DateTime date)
        {
            ActiveSection = AppSection.DailySummary;
            DailySummary.CurrentDate = date.Date;
            _settingsService.Config.LastAppSection = AppSection.DailySummary.ToString();
            _settingsService.Save();
            await DailySummary.LoadSummaryAsync();
        }

        private static AppSection ParseSavedSection(string? value) =>
            Enum.TryParse(value, true, out AppSection section) ? section : AppSection.Tasks;
    }
}
