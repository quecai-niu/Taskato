using System.Windows;
using Taskato.Services;
using Taskato.ViewModels;
using Taskato.Views;
using System.Threading;
using System.Windows.Media;
using Taskato.Services.WebBridge;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace Taskato
{
    /// <summary>
    /// 应用程序入口 — 负责初始化所有核心服务并启动主窗口
    /// 
    /// 启动流程：
    /// 1. 初始化 DatabaseService（创建/连接 SQLite 数据库）
    /// 2. 初始化 PomodoroService（番茄钟计时器）
    /// 3. 初始化 TrayService（系统托盘图标）
    /// 4. 创建 MainViewModel 与 WebView2 桌面宿主
    /// 5. 显示主窗口
    /// </summary>
    public partial class App : Application
    {
        // Windows 可能拒绝普通 Activate() 抢占前台；计时完成属于用户明确启用的强提示场景。
        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr windowHandle);

        /// <summary>数据库服务（全局单例）</summary>
        private DatabaseService _dbService = null!;

        /// <summary>番茄钟服务（全局单例）</summary>
        private PomodoroService _pomodoroService = null!;

        /// <summary>系统托盘服务（全局单例）</summary>
        private TrayService _trayService = null!;

        /// <summary>系统设置服务（全局单例）</summary>
        private SettingsService _settingsService = null!;

        /// <summary>番茄钟通知订阅协调器（全局单例）</summary>
        private PomodoroNotificationCoordinator _notificationCoordinator = null!;
        private WebBridgeService _webBridgeService = null!;
        private MainViewModel _mainViewModel = null!;
        private WorkspaceContextService _workspaceContextService = null!;

        /// <summary>单例应用锁</summary>
        private static Mutex? _mutex;
        private static bool _ownsMutex;

        /// <summary>
        /// 应用启动事件 — 替代 StartupUri 的手动启动方式
        /// </summary>
        private async void Application_Startup(object sender, StartupEventArgs e)
        {
            // ---- 0. 检测是否已经运行（单例控制） ----
            var allowMultipleInstances = Environment.GetEnvironmentVariable("TASKATO_ALLOW_MULTIPLE_INSTANCES") == "1";
            if (!allowMultipleInstances)
            {
                _mutex = new Mutex(true, "TaskatoWpfAppMutex", out bool createdNew);
                _ownsMutex = createdNew;
                if (!createdNew)
                {
                    MessageBox.Show("Taskato 已经在运行中了！请在右下角系统托盘查看。", "Taskato 提示", MessageBoxButton.OK, MessageBoxImage.Information);
                    Shutdown();
                    return;
                }
            }

            // ---- 0.1 初始化设置服务并应用主题颜色 ----
            // 自动化烟测可注入隔离数据目录；未设置时保持原有 exe/Data 行为。
            var dataDirectory = Environment.GetEnvironmentVariable("TASKATO_DATA_DIRECTORY");
            var resolvedDataDirectory = string.IsNullOrWhiteSpace(dataDirectory)
                ? System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data")
                : System.IO.Path.GetFullPath(dataDirectory);
            System.IO.Directory.CreateDirectory(resolvedDataDirectory);
            DiagnosticsService.Initialize(resolvedDataDirectory);
            try
            {
                // 恢复必须发生在设置和 SQLite 建立连接之前；失败时保留当前数据并继续启动。
                DataBackupService.ApplyPendingRestore(resolvedDataDirectory);
            }
            catch (Exception exception)
            {
                DiagnosticsService.Log("应用备份恢复失败", exception);
                MessageBox.Show(
                    $"暂存的数据恢复未能应用，当前数据未被替换。\n\n{exception.Message}",
                    "Taskato 数据恢复",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
            _settingsService = string.IsNullOrWhiteSpace(dataDirectory)
                ? new SettingsService()
                : new SettingsService(System.IO.Path.Combine(resolvedDataDirectory, "settings.json"));
            ApplySavedTheme();

            // ---- 0.2 初始化飞书通知服务 ----
            var _feishuService = new FeishuService(_settingsService);
            // ---- 1. 初始化数据库 ----
            _dbService = string.IsNullOrWhiteSpace(dataDirectory)
                ? new DatabaseService()
                : new DatabaseService(System.IO.Path.Combine(resolvedDataDirectory, "taskato.db"));
            await _dbService.InitializeAsync();

            // ---- 2. 初始化番茄钟（加载配置中的时长） ----
            _pomodoroService = new PomodoroService(
                _settingsService.Config.WorkMinutes, 
                _settingsService.Config.RestMinutes);
            var pomodoroStateStore = new PomodoroStateStore(
                System.IO.Path.Combine(resolvedDataDirectory, "pomodoro-state.json"));

            // ---- 3. 初始化系统托盘 ----
            _trayService = new TrayService(_pomodoroService);
            _trayService.Initialize();

            // ---- 4. 创建主窗体和 ViewModel ----
            var mainVM = new MainViewModel(
                _dbService,
                _pomodoroService,
                _settingsService,
                _feishuService,
                pomodoroStateStore);
            _mainViewModel = mainVM;
            _workspaceContextService = new WorkspaceContextService();
            Microsoft.Win32.SystemEvents.PowerModeChanged += SystemEvents_PowerModeChanged;
            _notificationCoordinator = new PomodoroNotificationCoordinator(
                mainVM,
                showMainWindowAction: ShowMainWindowForTimerNotification);
            // 必须先建立通知订阅再恢复，否则关闭期间到期的完成事件会在启动时丢失。
            mainVM.RestorePomodoroState();
            _webBridgeService = new WebBridgeService(
                _dbService,
                mainVM,
                _settingsService,
                _workspaceContextService);

            var webWindow = new WebShellWindow(_webBridgeService);
            Window activeWindow = webWindow;

            void ActivateWindow(Window window)
            {
                activeWindow = window;
                MainWindow = window;
                RestoreWindowPlacement(window);
                // 最大化/还原时立即记录状态；隐藏到托盘时再保存一次尺寸，避免只依赖进程退出。
                window.StateChanged += (_, _) =>
                {
                    if (window.WindowState != WindowState.Minimized)
                        SaveWindowPlacement(window);
                };
                window.IsVisibleChanged += (_, _) =>
                {
                    if (!window.IsVisible)
                        SaveWindowPlacement(window);
                };
                window.Show();
            }
            ActivateWindow(webWindow);

            // ---- 5. 设置托盘事件回调 ----

            // 双击托盘图标 → 主窗口显示时隐藏，隐藏或最小化时恢复并激活
            _trayService.ToggleWindowRequested += () =>
            {
                if (activeWindow.IsVisible && activeWindow.WindowState != WindowState.Minimized)
                {
                    activeWindow.Hide();
                    return;
                }

                ShowAndActivateWindow(activeWindow);
            };

            // 托盘菜单"显示主窗口" → 显示并激活窗口
            _trayService.ShowWindowRequested += () =>
            {
                ShowAndActivateWindow(activeWindow);
            };

            // 托盘菜单"设置" → 显示主窗口并切换到内嵌设置页
            _trayService.ShowSettingsRequested += () =>
            {
                ShowAndActivateWindow(activeWindow);
                webWindow.NavigateTo("settings");
            };

            // 托盘菜单"退出" → 真正关闭应用
            _trayService.ExitRequested += () =>
            {
                SaveWindowPlacement(activeWindow);
                webWindow.IsAppShuttingDown = true;
                _trayService.Dispose();  // 清理托盘图标
                Shutdown();              // 退出应用
            };

            // ---- 6. 加载今日任务并显示窗口 ----
            await mainVM.LoadTodayTasksAsync();
            var skipFirstRunGuide = Environment.GetEnvironmentVariable("TASKATO_SKIP_FIRST_RUN_GUIDE") == "1";
            if (!_settingsService.Config.HasSeenFeishuGuide && !skipFirstRunGuide)
            {
                var guideWindow = new FeishuGuideWindow
                {
                    Owner = activeWindow
                };
                guideWindow.ShowDialog();

                _settingsService.Config.HasSeenFeishuGuide = true;
                _settingsService.Save();

                if (guideWindow.ShouldConfigure)
                    webWindow.NavigateTo("settings");
            }
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

        private void ShowMainWindowForTimerNotification()
        {
            // 恢复流程可能在主窗口创建前就产生到期事件；这种情况由正常启动流程负责显示窗口。
            Dispatcher.Invoke(() =>
            {
                if (MainWindow is not Window window) return;
                ShowAndActivateWindow(window);
            });
        }

        private void ShowAndActivateWindow(Window window)
        {
            window.Show();
            // 最小化不是用户选择的恢复形态，回到最近一次普通或最大化状态。
            window.WindowState = WindowStatePersistence.ResolveActivationState(
                window.WindowState,
                _settingsService.Config.IsWindowMaximized);
            // 短暂切换 Topmost 可把隐藏在其他程序后的窗口带到前面，随后立即恢复普通层级。
            window.Topmost = true;
            window.Activate();
            window.Topmost = false;
            window.Focus();
            var handle = new WindowInteropHelper(window).Handle;
            if (handle != IntPtr.Zero) SetForegroundWindow(handle);
        }

        /// <summary>把旧配置或屏幕外坐标约束到当前主屏可用区域。</summary>
        private void RestoreWindowPlacement(Window window)
        {
            var config = _settingsService.Config;
            var primaryWorkArea = SystemParameters.WorkArea;
            var workAreas = new[]
            {
                // WPF 的 Left/Top/Width/Height 使用设备无关单位；SystemParameters.WorkArea 与其坐标系一致。
                // WinForms Screen 返回物理像素，在高 DPI 显示器上直接赋值会把窗口推向右下角。
                new WindowPlacementCalculator.WorkArea(
                    primaryWorkArea.Left,
                    primaryWorkArea.Top,
                    primaryWorkArea.Width,
                    primaryWorkArea.Height,
                    IsPrimary: true)
            };
            var placement = WindowPlacementCalculator.Calculate(
                config.WindowWidth,
                config.WindowHeight,
                null,
                null,
                workAreas);

            // 普通状态每次都按已保存尺寸在主屏居中；最大化时也先设置居中的 RestoreBounds，
            // 这样用户退出最大化后仍回到屏幕中央，而不是恢复旧版本保存的任意坐标。
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            window.Width = placement.Width;
            window.Height = placement.Height;
            window.Left = placement.Left;
            window.Top = placement.Top;
            window.WindowState = config.IsWindowMaximized
                ? WindowState.Maximized
                : WindowState.Normal;
            WindowWorkAreaService.Attach(window);
        }

        private void SaveWindowPlacement(Window? sourceWindow = null)
        {
            // 单例检测失败会在服务初始化前进入 OnExit，此时不能访问设置服务。
            if (_settingsService is null) return;
            var window = sourceWindow ?? MainWindow as Window;
            if (window is null) return;
            var bounds = window.RestoreBounds;
            var config = _settingsService.Config;
            config.WindowWidth = Math.Max(960, bounds.Width);
            config.WindowHeight = Math.Max(640, bounds.Height);
            config.WindowLeft = bounds.Left;
            config.WindowTop = bounds.Top;
            // 最小化只是临时状态，不得覆盖最近一次普通/最大化选择。
            config.IsWindowMaximized = WindowStatePersistence.CaptureIsMaximized(
                config.IsWindowMaximized,
                window.WindowState);
            _settingsService.Save();
        }

        /// <summary>
        /// 应用退出时确保清理托盘图标
        /// </summary>
        protected override void OnExit(ExitEventArgs e)
        {
            SaveWindowPlacement();
            _mainViewModel?.SavePomodoroState();
            // 阶段边界写入采用串行队列；进程退出前必须冲刷，避免最后一次暂停或停止丢失。
            _mainViewModel?.FlushFocusTrackingAsync().GetAwaiter().GetResult();
            Microsoft.Win32.SystemEvents.PowerModeChanged -= SystemEvents_PowerModeChanged;
            _trayService?.Dispose();
            _notificationCoordinator?.Dispose();
            _webBridgeService?.Dispose();
            if (_ownsMutex)
            {
                try { _mutex?.ReleaseMutex(); }
                catch (ApplicationException) { /* 启动未完成时不持有锁。 */ }
            }
            _mutex?.Dispose();
            _mutex = null;
            _ownsMutex = false;
            base.OnExit(e);
        }

        private void SystemEvents_PowerModeChanged(
            object sender,
            Microsoft.Win32.PowerModeChangedEventArgs e)
        {
            if (e.Mode != Microsoft.Win32.PowerModes.Resume || _mainViewModel is null) return;

            // SystemEvents 可能来自非 UI 线程；计时状态与 WebView2 通知都必须回到 Dispatcher。
            _ = Dispatcher.InvokeAsync(() =>
            {
                foreach (var timer in _mainViewModel.AllTimers)
                    timer.RefreshElapsedTime();
                DiagnosticsService.Log("系统从休眠恢复，已同步所有番茄钟。", null);
            });
        }
    }
}
