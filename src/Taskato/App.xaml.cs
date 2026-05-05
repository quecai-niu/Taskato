using System.Windows;
using Taskato.Services;
using Taskato.ViewModels;
using Taskato.Views;
using System.Threading;
using System.Windows.Media;

namespace Taskato
{
    /// <summary>
    /// 应用程序入口 — 负责初始化所有核心服务并启动主窗口
    /// 
    /// 启动流程：
    /// 1. 初始化 DatabaseService（创建/连接 SQLite 数据库）
    /// 2. 初始化 PomodoroService（番茄钟计时器）
    /// 3. 初始化 TrayService（系统托盘图标）
    /// 4. 创建 MainViewModel 并绑定到 MainWindow
    /// 5. 显示主窗口
    /// </summary>
    public partial class App : Application
    {
        /// <summary>数据库服务（全局单例）</summary>
        private DatabaseService _dbService = null!;

        /// <summary>番茄钟服务（全局单例）</summary>
        private PomodoroService _pomodoroService = null!;

        /// <summary>系统托盘服务（全局单例）</summary>
        private TrayService _trayService = null!;

        /// <summary>系统设置服务（全局单例）</summary>
        private SettingsService _settingsService = null!;

        /// <summary>单例应用锁</summary>
        private static Mutex _mutex = null!;

        /// <summary>
        /// 应用启动事件 — 替代 StartupUri 的手动启动方式
        /// </summary>
        private async void Application_Startup(object sender, StartupEventArgs e)
        {
            // ---- 0. 检测是否已经运行（单例控制） ----
            _mutex = new Mutex(true, "TaskatoWpfAppMutex", out bool createdNew);
            if (!createdNew)
            {
                MessageBox.Show("Taskato 已经在运行中了！请在右下角系统托盘查看。", "Taskato 提示", MessageBoxButton.OK, MessageBoxImage.Information);
                Shutdown();
                return;
            }

            // ---- 0.1 初始化设置服务并应用主题颜色 ----
            _settingsService = new SettingsService();
            ApplySavedTheme();
            // ---- 1. 初始化数据库 ----
            _dbService = new DatabaseService();
            await _dbService.InitializeAsync();

            // ---- 2. 初始化番茄钟（加载配置中的时长） ----
            _pomodoroService = new PomodoroService(
                _settingsService.Config.WorkMinutes, 
                _settingsService.Config.RestMinutes);

            // ---- 3. 初始化系统托盘 ----
            _trayService = new TrayService(_pomodoroService);
            _trayService.Initialize();

            // ---- 4. 创建主窗体和 ViewModel ----
            var mainVM = new MainViewModel(_dbService, _pomodoroService, _settingsService);
            var mainWindow = new MainWindow
            {
                DataContext = mainVM
            };

            // ---- 5. 设置托盘事件回调 ----

            // 托盘菜单"显示主窗口" → 显示并激活窗口
            _trayService.ShowWindowRequested += () =>
            {
                mainWindow.Show();
                mainWindow.WindowState = WindowState.Normal;
                mainWindow.Activate();
            };

            // 托盘菜单"设置" → 直接弹出设置窗口
            _trayService.ShowSettingsRequested += () =>
            {
                if (mainVM.OpenSettingsCommand.CanExecute(null))
                    mainVM.OpenSettingsCommand.Execute(null);
            };

            // 托盘菜单"退出" → 真正关闭应用
            _trayService.ExitRequested += () =>
            {
                mainWindow.IsAppShuttingDown = true; // 标记应用正在退出，允许窗口销毁
                _trayService.Dispose();  // 清理托盘图标
                Shutdown();              // 退出应用
            };

            // ---- 6. 加载今日任务并显示窗口 ----
            await mainVM.LoadTodayTasksAsync();
            mainWindow.Show();
        }

        /// <summary>
        /// 根据配置应用当前选择的主题色
        /// </summary>
        private void ApplySavedTheme()
        {
            var config = _settingsService.Config;
            var primary = (Color)ColorConverter.ConvertFromString(config.ThemePrimaryColor);
            var secondary = (Color)ColorConverter.ConvertFromString(config.ThemeSecondaryColor);

            var resources = Current.Resources;
            resources["ThemePrimaryColor"] = primary;
            resources["ThemeSecondaryColor"] = secondary;
            resources["ThemePrimaryBrush"] = new SolidColorBrush(primary);
            resources["ThemeGradientBrush"] = new LinearGradientBrush(primary, secondary, 135);
        }

        /// <summary>
        /// 应用退出时确保清理托盘图标
        /// </summary>
        protected override void OnExit(ExitEventArgs e)
        {
            _trayService?.Dispose();
            base.OnExit(e);
        }
    }
}
