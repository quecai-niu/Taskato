using System.Drawing;
using System.Windows;
using WinForms = System.Windows.Forms;

namespace Taskato.Services
{
    /// <summary>
    /// 系统托盘服务 — 管理任务栏通知区域的图标和右键菜单
    /// 
    /// 使用 WinForms 的 NotifyIcon 组件（WPF 没有原生托盘支持）
    /// 关闭窗口时不退出程序，而是隐藏到托盘区域继续运行
    /// </summary>
    public class TrayService : IDisposable
    {
        /// <summary>
        /// WinForms 托盘图标组件
        /// </summary>
        private WinForms.NotifyIcon? _notifyIcon;

        /// <summary>
        /// 番茄钟服务引用 — 用于在托盘菜单中显示当前计时状态
        /// </summary>
        private readonly PomodoroService _pomodoroService;

        /// <summary>
        /// 托盘菜单中"番茄钟状态"的菜单项引用，需要动态更新文本
        /// </summary>
        private System.Windows.Controls.MenuItem? _pomodoroMenuItem;

        // ==================== 事件 ====================

        /// <summary>
        /// 用户点击"显示主界面"时触发
        /// </summary>
        public event Action? ShowWindowRequested;

        /// <summary>
        /// 用户点击"设置"时触发
        /// </summary>
        public event Action? ShowSettingsRequested;

        /// <summary>
        /// 用户点击"退出"时触发
        /// </summary>
        public event Action? ExitRequested;

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="pomodoroService">番茄钟服务（用于获取计时状态）</param>
        public TrayService(PomodoroService pomodoroService)
        {
            _pomodoroService = pomodoroService;
        }

        /// <summary>
        /// 初始化托盘图标和右键菜单
        /// 必须在 UI 线程上调用
        /// </summary>
        public void Initialize()
        {
            // 1. 创建 WinForms 托盘图标
            _notifyIcon = new WinForms.NotifyIcon
            {
                Icon = GetAppIcon(),
                Text = "Taskato - 工作任务管理",
                Visible = true
            };

            // 2. 双击托盘图标 → 显示主窗口
            _notifyIcon.DoubleClick += (s, e) => ShowWindowRequested?.Invoke();

            // 3. 构建 WPF 版气泡风右键菜单
            var contextMenu = CreateWpfContextMenu();

            // 4. 接管右键点击事件，显示 WPF 菜单
            _notifyIcon.MouseClick += (s, e) =>
            {
                if (e.Button == WinForms.MouseButtons.Right)
                {
                    // 确保菜单能够弹出在鼠标位置，并处理焦点丢失自动关闭
                    contextMenu.IsOpen = true;
                }
            };

            // 订阅番茄钟的 Tick 事件来实时更新托盘菜单中的计时显示
            _pomodoroService.Tick += OnPomodoroTick;
            _pomodoroService.StateChanged += OnPomodoroStateChanged;
        }

        /// <summary>
        /// 创建并配置气泡风样式的 WPF ContextMenu
        /// </summary>
        private System.Windows.Controls.ContextMenu CreateWpfContextMenu()
        {
            var menu = new System.Windows.Controls.ContextMenu
            {
                Style = (Style)Application.Current.Resources["BubbleTrayMenu"],
                Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint
            };

            // 菜单项 1: 显示主界面
            var item1 = new System.Windows.Controls.MenuItem
            {
                Header = "🎈 呼唤主界面",
                Style = (Style)Application.Current.Resources["BubbleTrayMenuItem"]
            };
            item1.Click += (s, e) => ShowWindowRequested?.Invoke();
            menu.Items.Add(item1);

            // 菜单项 2: 番茄钟状态
            _pomodoroMenuItem = new System.Windows.Controls.MenuItem
            {
                Header = "🍅 专注小统计: 空闲",
                Style = (Style)Application.Current.Resources["BubbleTrayMenuItem"],
                IsEnabled = false // 状态展示，不可点击
            };
            menu.Items.Add(_pomodoroMenuItem);

            // 菜单项 3: 设置
            var item3 = new System.Windows.Controls.MenuItem
            {
                Header = "🎨 偏好设置",
                Style = (Style)Application.Current.Resources["BubbleTrayMenuItem"]
            };
            item3.Click += (s, e) => ShowSettingsRequested?.Invoke();
            menu.Items.Add(item3);

            // 分隔线
            menu.Items.Add(new System.Windows.Controls.Separator { Style = (Style)Application.Current.Resources["BubbleTraySeparator"] });

            // 菜单项 4: 退出 (注：Foreground 使用西瓜红)
            var item4 = new System.Windows.Controls.MenuItem
            {
                Header = "💨 溜了溜了 (退出)",
                Style = (Style)Application.Current.Resources["BubbleTrayMenuItem"],
                Foreground = (System.Windows.Media.Brush)Application.Current.Resources["Pri3Brush"]
            };
            item4.Click += (s, e) => ExitRequested?.Invoke();
            menu.Items.Add(item4);

            return menu;
        }

        /// <summary>
        /// 番茄钟每秒跳动时更新托盘菜单文本
        /// </summary>
        private void OnPomodoroTick(int minutes, int seconds, double progress)
        {
            if (_pomodoroMenuItem != null)
            {
                _pomodoroMenuItem.Header = $"🍅 专注小统计: {minutes:D2}:{seconds:D2}";
            }
        }

        /// <summary>
        /// 番茄钟状态变更时更新托盘菜单文本
        /// </summary>
        private void OnPomodoroStateChanged(PomodoroService.PomodoroState state)
        {
            if (_pomodoroMenuItem != null)
            {
                var stateText = state switch
                {
                    PomodoroService.PomodoroState.Working => "专注中",
                    PomodoroService.PomodoroState.Paused => "已暂停",
                    PomodoroService.PomodoroState.Resting => "休息中",
                    _ => "空闲"
                };
                _pomodoroMenuItem.Header = $"🍅 专注小统计: {stateText}";
            }
        }

        /// <summary>
        /// 显示气泡通知（Windows 10 会显示为 Toast 通知）
        /// </summary>
        public void ShowBalloonTip(string title, string message, int timeout = 3000)
        {
            _notifyIcon?.ShowBalloonTip(timeout, title, message, WinForms.ToolTipIcon.Info);
        }

        /// <summary>
        /// 获取应用程序图标
        /// </summary>
        private Icon GetAppIcon()
        {
            try
            {
                var exePath = System.Reflection.Assembly.GetExecutingAssembly().Location;
                if (!string.IsNullOrEmpty(exePath) && System.IO.File.Exists(exePath))
                {
                    var icon = Icon.ExtractAssociatedIcon(exePath);
                    if (icon != null) return icon;
                }
            }
            catch { }
            return SystemIcons.Application;
        }

        /// <summary>
        /// 释放托盘资源（应用退出时调用）
        /// </summary>
        public void Dispose()
        {
            _pomodoroService.Tick -= OnPomodoroTick;
            _pomodoroService.StateChanged -= OnPomodoroStateChanged;

            if (_notifyIcon != null)
            {
                _notifyIcon.Visible = false;
                _notifyIcon.Dispose();
                _notifyIcon = null;
            }
        }
    }
}
