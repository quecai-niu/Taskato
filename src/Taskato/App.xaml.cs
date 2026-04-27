using System.Windows;
using Taskato.Services;
using Taskato.ViewModels;
using Taskato.Views;

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

        /// <summary>
        /// 应用启动事件 — 替代 StartupUri 的手动启动方式
        /// </summary>
        private async void Application_Startup(object sender, StartupEventArgs e)
        {
            // ---- 1. 初始化数据库 ----
            _dbService = new DatabaseService();
            await _dbService.InitializeAsync();

            // ---- 2. 初始化番茄钟 ----
            _pomodoroService = new PomodoroService();

            // ---- 3. 初始化系统托盘 ----
            _trayService = new TrayService(_pomodoroService);
            _trayService.Initialize();

            // ---- 4. 创建主窗体和 ViewModel ----
            var mainVM = new MainViewModel(_dbService, _pomodoroService);
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

            // 托盘菜单"退出" → 真正关闭应用
            _trayService.ExitRequested += () =>
            {
                _trayService.Dispose();  // 清理托盘图标
                Shutdown();              // 退出应用
            };

            // ---- 6. 加载今日任务并显示窗口 ----
            await mainVM.LoadTodayTasksAsync();
            mainWindow.Show();
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
