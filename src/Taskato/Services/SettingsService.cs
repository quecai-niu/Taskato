using System;
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
        public string ThemePrimaryColor { get; set; } = "#6366F1";
        public string ThemeSecondaryColor { get; set; } = "#A855F7";
        public int SelectedColorIndex { get; set; } = 0;
        
        /// <summary>点击继续工作时，是否自动开始下一个番茄钟倒计时</summary>
        public bool AutoStartNextPomodoro { get; set; } = false;
    }

    /// <summary>
    /// 配置读取与保存服务，基于 json 配置文件
    /// </summary>
    public class SettingsService
    {
        private readonly string _configPath;
        public SettingsConfig Config { get; private set; }

        public SettingsService()
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var dataDir = Path.Combine(baseDir, "Data");
            if (!Directory.Exists(dataDir))
            {
                Directory.CreateDirectory(dataDir);
            }
            
            _configPath = Path.Combine(dataDir, "settings.json");
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
        }

        /// <summary>
        /// 将当前内存中的配置保存到磁盘
        /// </summary>
        public void Save()
        {
            try
            {
                string json = JsonSerializer.Serialize(Config, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_configPath, json);
            }
            catch
            {
                // 忽略没有读写权限时的报错（例如权限问题）
            }
        }
    }
}
