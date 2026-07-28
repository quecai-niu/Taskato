using System.Windows.Input;
using System.Windows.Media;
using Taskato.Services;
using Taskato.Utils;

namespace Taskato.ViewModels
{
    /// <summary>整窗设置页的可绑定状态；保存时仍写入原有 settings.json。</summary>
    public sealed class SettingsViewModel : ViewModelBase
    {
        private readonly SettingsService _settingsService;
        private readonly MainViewModel _taskWorkspace;
        private int _workMinutes;
        private int _restMinutes;
        private bool _autoStartNextPomodoro;
        private bool _enableMultiplePomodoros;
        private bool _enableToastTimer;
        private bool _autoShowDailySummary;
        private bool _feishuEnabled;
        private string _feishuWebhookUrl = string.Empty;
        private string _themePrimaryColor = string.Empty;
        private string _themeSecondaryColor = string.Empty;
        private string _statusMessage = string.Empty;

        public int WorkMinutes { get => _workMinutes; set => SetProperty(ref _workMinutes, value); }
        public int RestMinutes { get => _restMinutes; set => SetProperty(ref _restMinutes, value); }
        public bool AutoStartNextPomodoro { get => _autoStartNextPomodoro; set => SetProperty(ref _autoStartNextPomodoro, value); }
        public bool EnableMultiplePomodoros { get => _enableMultiplePomodoros; set => SetProperty(ref _enableMultiplePomodoros, value); }
        public bool EnableToastTimer { get => _enableToastTimer; set => SetProperty(ref _enableToastTimer, value); }
        public bool AutoShowDailySummary { get => _autoShowDailySummary; set => SetProperty(ref _autoShowDailySummary, value); }
        public bool FeishuEnabled { get => _feishuEnabled; set => SetProperty(ref _feishuEnabled, value); }
        public string FeishuWebhookUrl { get => _feishuWebhookUrl; set => SetProperty(ref _feishuWebhookUrl, value); }
        public string ThemePrimaryColor { get => _themePrimaryColor; set => SetProperty(ref _themePrimaryColor, value); }
        public string ThemeSecondaryColor { get => _themeSecondaryColor; set => SetProperty(ref _themeSecondaryColor, value); }
        public string StatusMessage { get => _statusMessage; private set => SetProperty(ref _statusMessage, value); }

        public ICommand SaveCommand { get; }

        public SettingsViewModel(SettingsService settingsService, MainViewModel taskWorkspace)
        {
            _settingsService = settingsService;
            _taskWorkspace = taskWorkspace;
            SaveCommand = new RelayCommand(_ => Save());
            ReloadFromSettings();
        }

        public void ReloadFromSettings()
        {
            var config = _settingsService.Config;
            WorkMinutes = config.WorkMinutes;
            RestMinutes = config.RestMinutes;
            AutoStartNextPomodoro = config.AutoStartNextPomodoro;
            EnableMultiplePomodoros = config.EnableMultiplePomodoros;
            EnableToastTimer = config.EnableToastTimer;
            AutoShowDailySummary = config.AutoShowDailySummary;
            FeishuEnabled = config.FeishuEnabled;
            FeishuWebhookUrl = config.FeishuWebhookUrl;
            ThemePrimaryColor = config.ThemePrimaryColor;
            ThemeSecondaryColor = config.ThemeSecondaryColor;
            StatusMessage = string.Empty;
        }

        private void Save()
        {
            if (WorkMinutes is < 1 or > 240 || RestMinutes is < 1 or > 120)
            {
                StatusMessage = "请输入有效时长：专注 1–240 分钟，休息 1–120 分钟。";
                return;
            }

            if (!TryParseColor(ThemePrimaryColor, out var primary) ||
                !TryParseColor(ThemeSecondaryColor, out var secondary))
            {
                StatusMessage = "主题色需使用 #RRGGBB 格式。";
                return;
            }

            var config = _settingsService.Config;
            config.WorkMinutes = WorkMinutes;
            config.RestMinutes = RestMinutes;
            config.AutoStartNextPomodoro = AutoStartNextPomodoro;
            config.EnableMultiplePomodoros = EnableMultiplePomodoros;
            config.EnableToastTimer = EnableToastTimer;
            config.AutoShowDailySummary = AutoShowDailySummary;
            config.FeishuEnabled = FeishuEnabled;
            config.FeishuWebhookUrl = FeishuWebhookUrl.Trim();
            config.ThemePrimaryColor = ThemePrimaryColor.Trim();
            config.ThemeSecondaryColor = ThemeSecondaryColor.Trim();
            _settingsService.Save();
            _taskWorkspace.ApplySettingsFromConfig();

            var resources = System.Windows.Application.Current.Resources;
            resources["ThemePrimaryColor"] = primary;
            resources["ThemeSecondaryColor"] = secondary;
            resources["ThemePrimaryBrush"] = new SolidColorBrush(primary);
            resources["ThemeGradientBrush"] = new LinearGradientBrush(primary, secondary, 135);
            StatusMessage = "设置已保存。";
        }

        private static bool TryParseColor(string value, out Color color)
        {
            try
            {
                color = (Color)ColorConverter.ConvertFromString(value.Trim());
                return true;
            }
            catch
            {
                color = default;
                return false;
            }
        }
    }
}
