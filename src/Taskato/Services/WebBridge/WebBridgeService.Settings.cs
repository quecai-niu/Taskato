using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace Taskato.Services.WebBridge
{
    /// <summary>
    /// 设置、原生文件能力与数据维护命令。与任务协议分离后，可独立审查本地副作用边界。
    /// </summary>
    public sealed partial class WebBridgeService
    {
        private object GetSettings()
        {
            var c = _settings.Config;
            return new
            {
                c.WorkMinutes, c.RestMinutes, c.AppearanceMode,
                c.ThemePrimaryColor, c.ThemeSecondaryColor, c.SelectedColorIndex,
                c.AutoStartNextPomodoro, c.EnableToastTimer, c.ToastActionDelaySeconds,
                c.ShowMainWindowOnTimerNotification,
                c.EnableMultiplePomodoros, c.NotificationSoundChoice, c.CustomSoundPath,
                c.FeishuEnabled, c.FeishuWebhookUrl, c.FeishuNotifyOnWork, c.FeishuNotifyOnRest,
                c.FeishuRepeatEnabled, c.FeishuRestHalfwayEnabled, c.AutoShowDailySummary,
                c.FeishuWorkNotificationCount, c.FeishuRestHalfwayNotificationCount,
                c.FeishuRestCompletedNotificationCount, c.FeishuTestNotificationCount,
                c.UseLegacyWorkspace,
                autoStartEnabled = AutoStartService.IsAutoStartEnabled()
            };
        }

        private object SaveSettings(JsonElement parameters)
        {
            var c = _settings.Config;
            c.WorkMinutes = ClampedOptionalInt(parameters, "workMinutes", c.WorkMinutes, 1, 240);
            c.RestMinutes = ClampedOptionalInt(parameters, "restMinutes", c.RestMinutes, 1, 30);
            if (OptionalString(parameters, "appearanceMode") is { } appearanceMode)
            {
                c.AppearanceMode = appearanceMode is "dark" or "light"
                    ? appearanceMode
                    : throw new BridgeRequestException("invalid_appearance", "外观模式只支持 dark 或 light。");
            }
            c.AutoStartNextPomodoro = OptionalBool(parameters, "autoStartNextPomodoro") ?? c.AutoStartNextPomodoro;
            c.EnableToastTimer = OptionalBool(parameters, "enableToastTimer") ?? c.EnableToastTimer;
            c.ShowMainWindowOnTimerNotification = OptionalBool(
                parameters,
                "showMainWindowOnTimerNotification") ?? c.ShowMainWindowOnTimerNotification;
            var toastDelay = OptionalDouble(parameters, "toastActionDelaySeconds") ?? c.ToastActionDelaySeconds;
            c.ToastActionDelaySeconds = Math.Round(Math.Clamp(toastDelay, 0, 5) * 2) / 2;
            c.EnableMultiplePomodoros = OptionalBool(parameters, "enableMultiplePomodoros") ?? c.EnableMultiplePomodoros;
            c.NotificationSoundChoice = ClampedOptionalInt(parameters, "notificationSoundChoice", c.NotificationSoundChoice, 0, 5);
            c.CustomSoundPath = OptionalString(parameters, "customSoundPath") ?? c.CustomSoundPath;
            c.FeishuEnabled = OptionalBool(parameters, "feishuEnabled") ?? c.FeishuEnabled;
            c.FeishuWebhookUrl = OptionalString(parameters, "feishuWebhookUrl") ?? c.FeishuWebhookUrl;
            c.FeishuNotifyOnWork = OptionalBool(parameters, "feishuNotifyOnWork") ?? c.FeishuNotifyOnWork;
            c.FeishuNotifyOnRest = OptionalBool(parameters, "feishuNotifyOnRest") ?? c.FeishuNotifyOnRest;
            c.FeishuRepeatEnabled = OptionalBool(parameters, "feishuRepeatEnabled") ?? c.FeishuRepeatEnabled;
            c.FeishuRestHalfwayEnabled = OptionalBool(parameters, "feishuRestHalfwayEnabled") ?? c.FeishuRestHalfwayEnabled;
            c.FeishuWorkNotificationCount = ClampedOptionalInt(parameters, "feishuWorkNotificationCount", c.FeishuWorkNotificationCount, 0, FeishuService.MaximumNotificationCount);
            c.FeishuRestHalfwayNotificationCount = ClampedOptionalInt(parameters, "feishuRestHalfwayNotificationCount", c.FeishuRestHalfwayNotificationCount, 0, FeishuService.MaximumNotificationCount);
            c.FeishuRestCompletedNotificationCount = ClampedOptionalInt(parameters, "feishuRestCompletedNotificationCount", c.FeishuRestCompletedNotificationCount, 0, FeishuService.MaximumNotificationCount);
            c.FeishuTestNotificationCount = ClampedOptionalInt(parameters, "feishuTestNotificationCount", c.FeishuTestNotificationCount, 1, FeishuService.MaximumNotificationCount);
            c.AutoShowDailySummary = OptionalBool(parameters, "autoShowDailySummary") ?? c.AutoShowDailySummary;
            c.UseLegacyWorkspace = OptionalBool(parameters, "useLegacyWorkspace") ?? c.UseLegacyWorkspace;
            c.ThemePrimaryColor = ValidatedColor(parameters, "themePrimaryColor", c.ThemePrimaryColor);
            c.ThemeSecondaryColor = ValidatedColor(parameters, "themeSecondaryColor", c.ThemeSecondaryColor);
            c.SelectedColorIndex = ClampedOptionalInt(parameters, "selectedColorIndex", c.SelectedColorIndex, 0, 5);
            if (OptionalBool(parameters, "autoStartEnabled") is { } autoStartEnabled)
                AutoStartService.SetAutoStart(autoStartEnabled);
            _settings.Save();
            _taskWorkspace.ApplySettingsFromConfig();
            ApplyNativeTheme(c.ThemePrimaryColor, c.ThemeSecondaryColor);
            PostEvent("settings.changed", GetSettings());
            return GetSettings();
        }

        private static void ApplyNativeTheme(string primaryValue, string secondaryValue)
        {
            var primary = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(primaryValue);
            var secondary = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(secondaryValue);
            var resources = System.Windows.Application.Current.Resources;
            resources["ThemePrimaryColor"] = primary;
            resources["ThemeSecondaryColor"] = secondary;
            resources["ThemePrimaryBrush"] = new System.Windows.Media.SolidColorBrush(primary);
            resources["ThemeGradientBrush"] = new System.Windows.Media.LinearGradientBrush(primary, secondary, 135);
        }

        private object ChooseCustomSound()
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "音频文件|*.wav;*.mp3;*.wma|所有文件|*.*",
                Title = "选择自定义音效"
            };
            if (dialog.ShowDialog() != true) return new { selected = false, path = _settings.Config.CustomSoundPath };
            _settings.Config.CustomSoundPath = dialog.FileName;
            _settings.Config.NotificationSoundChoice = 5;
            _settings.Save();
            return new { selected = true, path = dialog.FileName };
        }

        private object PreviewSound(JsonElement parameters)
        {
            var soundChoice = ClampedOptionalInt(parameters, "notificationSoundChoice", _settings.Config.NotificationSoundChoice, 0, 5);
            var customSoundPath = OptionalString(parameters, "customSoundPath") ?? _settings.Config.CustomSoundPath;
            var paths = new Dictionary<int, string>
            {
                [1] = @"C:\Windows\Media\Windows Notify.wav",
                [2] = @"C:\Windows\Media\Windows Ding.wav",
                [3] = @"C:\Windows\Media\Windows Background.wav",
                [4] = @"C:\Windows\Media\chimes.wav",
                [5] = customSoundPath
            };
            if (!paths.TryGetValue(soundChoice, out var path) || !File.Exists(path))
                return new { played = false };

            if (path.EndsWith(".wav", StringComparison.OrdinalIgnoreCase))
                new System.Media.SoundPlayer(path).Play();
            else
            {
                _previewMediaPlayer?.Close();
                _previewMediaPlayer = new System.Windows.Media.MediaPlayer();
                _previewMediaPlayer.MediaEnded += (_, _) =>
                {
                    _previewMediaPlayer?.Close();
                    _previewMediaPlayer = null;
                };
                _previewMediaPlayer.Open(new Uri(path, UriKind.Absolute));
                _previewMediaPlayer.Play();
            }
            return new { played = true };
        }

        private async Task<object> TestFeishuAsync(JsonElement parameters)
        {
            var webhookUrl = OptionalString(parameters, "webhookUrl") ?? string.Empty;
            var notificationCount = ClampedOptionalInt(
                parameters,
                "notificationCount",
                _settings.Config.FeishuTestNotificationCount,
                1,
                FeishuService.MaximumNotificationCount);
            var result = await _taskWorkspace.FeishuService.TestAsync(webhookUrl, notificationCount);
            return new { result.Sent, result.ErrorCode, result.Message };
        }

        private object OpenFeishuGuide()
        {
            var guide = new Taskato.Views.FeishuGuideWindow
            {
                Owner = System.Windows.Application.Current.MainWindow
            };
            guide.ShowDialog();
            return new { shouldConfigure = guide.ShouldConfigure };
        }

        private object OpenDataFolder()
        {
            var path = Path.GetDirectoryName(_database.DatabasePath)!;
            Process.Start(new ProcessStartInfo("explorer.exe", path) { UseShellExecute = true });
            return new { opened = true };
        }

        private object OpenLogsFolder()
        {
            var path = DiagnosticsService.LogDirectory ??
                Path.Combine(Path.GetDirectoryName(_database.DatabasePath)!, "Logs");
            Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo("explorer.exe", path) { UseShellExecute = true });
            return new { opened = true };
        }

        private async Task<object> BackupDataAsync()
        {
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "Taskato 备份|*.taskato-backup",
                DefaultExt = ".taskato-backup",
                FileName = $"Taskato_备份_{DateTime.Now:yyyyMMdd_HHmmss}"
            };
            if (dialog.ShowDialog() != true) return new { created = false };

            await _dataBackup.CreateArchiveAsync(dialog.FileName);
            return new { created = true, fileName = Path.GetFileName(dialog.FileName) };
        }

        private async Task<object> StageDataRestoreAsync()
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Taskato 备份|*.taskato-backup",
                Title = "选择要恢复的 Taskato 备份"
            };
            if (dialog.ShowDialog() != true) return new { staged = false };

            var dataDirectory = Path.GetDirectoryName(_database.DatabasePath)!;
            await DataBackupService.StageRestoreAsync(dialog.FileName, dataDirectory);
            return new { staged = true, restartRequired = true };
        }
    }
}
