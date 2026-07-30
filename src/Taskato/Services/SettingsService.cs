using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Taskato.Services
{
    /// <summary>
    /// 持久化配置数据模型
    /// </summary>
    public class SettingsConfig
    {
        public int WorkMinutes { get; set; } = 25;
        public int RestMinutes { get; set; } = 5;

        /// <summary>网页工作区外观模式；仅允许 dark 或 light，旧配置默认使用夜间模式。</summary>
        public string AppearanceMode { get; set; } = "dark";

        public string ThemePrimaryColor { get; set; } = "#6366F1";
        public string ThemeSecondaryColor { get; set; } = "#A855F7";
        public int SelectedColorIndex { get; set; } = 0;
        
        /// <summary>点击继续工作时，是否自动开始下一个番茄钟倒计时</summary>
        public bool AutoStartNextPomodoro { get; set; } = false;

        /// <summary>是否在弹窗中显示计时器（记录弹窗出现了多久）</summary>
        public bool EnableToastTimer { get; set; } = false;

        /// <summary>提醒弹窗出现后，操作按钮延迟启用的秒数</summary>
        public double ToastActionDelaySeconds { get; set; } = 1.5;

        /// <summary>专注或休息结束时，除 Toast 外是否同时恢复并激活主界面。</summary>
        public bool ShowMainWindowOnTimerNotification { get; set; } = false;

        /// <summary>是否开启多组番茄钟功能</summary>
        public bool EnableMultiplePomodoros { get; set; } = false;

        /// <summary>保存的附加番茄钟各自的工作时长</summary>
        public List<int> AdditionalTimersWorkMinutes { get; set; } = new List<int>();

        /// <summary>
        /// 提示音方案选择：
        /// 0 = 无声, 1 = Windows Notify, 2 = Windows Ding, 3 = Windows Background, 4 = chimes, 5 = 自定义音效
        /// </summary>
        public int NotificationSoundChoice { get; set; } = 3;

        /// <summary>自定义音效文件路径</summary>
        public string CustomSoundPath { get; set; } = string.Empty;

        /// <summary>是否启用飞书通知</summary>
        public bool FeishuEnabled { get; set; } = false;

        /// <summary>飞书 Webhook URL（环境变量 FEISHU_WEBHOOK_URL 优先）</summary>
        public string FeishuWebhookUrl { get; set; } = string.Empty;

        /// <summary>工作完成时发送飞书通知</summary>
        public bool FeishuNotifyOnWork { get; set; } = true;

        /// <summary>休息完成时发送飞书通知</summary>
        public bool FeishuNotifyOnRest { get; set; } = true;

        /// <summary>旧版统一重复开关；仅为兼容已有配置保留，新版使用各通知独立次数。</summary>
        public bool FeishuRepeatEnabled { get; set; } = true;

        /// <summary>休息过半时发送飞书提醒</summary>
        public bool FeishuRestHalfwayEnabled { get; set; } = true;

        /// <summary>工作完成通知次数；0 表示不发送。</summary>
        public int FeishuWorkNotificationCount { get; set; } = 0;

        /// <summary>休息过半通知次数；0 表示不发送。</summary>
        public int FeishuRestHalfwayNotificationCount { get; set; } = 3;

        /// <summary>休息完成通知次数；0 表示不发送。</summary>
        public int FeishuRestCompletedNotificationCount { get; set; } = 6;

        /// <summary>手动测试通知次数；测试入口至少发送 1 条。</summary>
        public int FeishuTestNotificationCount { get; set; } = 1;

        /// <summary>是否已处理首次启动的飞书通知引导</summary>
        public bool HasSeenFeishuGuide { get; set; } = false;

        /// <summary>跨天时自动弹出昨日总结</summary>
        public bool AutoShowDailySummary { get; set; } = false;

        /// <summary>主窗口宽度和高度；旧配置缺失字段时使用整窗工作区默认值。</summary>
        public double WindowWidth { get; set; } = 1280;
        public double WindowHeight { get; set; } = 820;

        public double? WindowLeft { get; set; }
        public double? WindowTop { get; set; }
        public bool IsWindowMaximized { get; set; }

        /// <summary>最近一次访问的一级工作区。</summary>
        public string LastAppSection { get; set; } = "Tasks";

        /// <summary>WebView2 初始化失败时允许用户持久切回旧 WPF 工作区。</summary>
        public bool UseLegacyWorkspace { get; set; } = false;
    }

    /// <summary>
    /// 配置读取与保存服务，基于 json 配置文件
    /// </summary>
    public class SettingsService
    {
        private readonly string _configPath;
        public string ConfigPath => _configPath;
        public SettingsConfig Config { get; private set; } = new SettingsConfig();

        public SettingsService(string? configPath = null)
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var dataDir = string.IsNullOrWhiteSpace(configPath)
                ? Path.Combine(baseDir, "Data")
                : Path.GetDirectoryName(Path.GetFullPath(configPath))!;
            if (!Directory.Exists(dataDir))
            {
                Directory.CreateDirectory(dataDir);
            }

            // 可注入路径用于自动化测试，生产环境仍固定保存到 exe/Data/settings.json。
            _configPath = string.IsNullOrWhiteSpace(configPath)
                ? Path.Combine(dataDir, "settings.json")
                : Path.GetFullPath(configPath);
            Load();
        }

        /// <summary>
        /// 从文件加载配置，如果不存在则使用默认值
        /// </summary>
        public void Load()
        {
            try
            {
                if (File.Exists(_configPath))
                {
                    string json = File.ReadAllText(_configPath);
                    Config = JsonSerializer.Deserialize<SettingsConfig>(json) ?? new SettingsConfig();
                }
                else
                {
                    Config = new SettingsConfig();
                }
            }
            catch
            {
                // 若反序列化出错则降级到默认配置
                Config = new SettingsConfig();
            }
            NormalizeNotificationCounts();
        }

        /// <summary>
        /// 将当前内存中的配置保存到磁盘
        /// </summary>
        public void Save()
        {
            try
            {
                NormalizeNotificationCounts();
                string json = JsonSerializer.Serialize(Config, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_configPath, json);
            }
            catch
            {
                // 忽略没有读写权限时的报错（例如权限问题）
            }
        }

        private void NormalizeNotificationCounts()
        {
            // 配置文件可能由旧版本或手工编辑产生；持久化边界统一收敛，避免异常次数进入通知循环。
            Config.FeishuWorkNotificationCount = Math.Clamp(
                Config.FeishuWorkNotificationCount, 0, FeishuService.MaximumNotificationCount);
            Config.FeishuRestHalfwayNotificationCount = Math.Clamp(
                Config.FeishuRestHalfwayNotificationCount, 0, FeishuService.MaximumNotificationCount);
            Config.FeishuRestCompletedNotificationCount = Math.Clamp(
                Config.FeishuRestCompletedNotificationCount, 0, FeishuService.MaximumNotificationCount);
            Config.FeishuTestNotificationCount = Math.Clamp(
                Config.FeishuTestNotificationCount, 1, FeishuService.MaximumNotificationCount);
        }
    }
}
