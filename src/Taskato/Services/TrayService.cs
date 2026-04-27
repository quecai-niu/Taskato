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
        private WinForms.ToolStripMenuItem? _pomodoroMenuItem;

        // ==================== 事件 ====================

        /// <summary>
        /// 用户点击"显示主窗口"时触发
        /// </summary>
        public event Action? ShowWindowRequested;

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
            // 创建托盘图标（使用应用程序图标，如果没有则用系统默认图标）
            _notifyIcon = new WinForms.NotifyIcon
            {
                // 尝试从应用程序资源获取图标
                Icon = GetAppIcon(),
                Text = "Taskato - 工作任务管理",  // 鼠标悬停提示文字
                Visible = true                      // 显示在托盘区域
            };

            // 双击托盘图标 → 显示主窗口
            _notifyIcon.DoubleClick += (s, e) => ShowWindowRequested?.Invoke();

            // 创建右键菜单
            var contextMenu = new WinForms.ContextMenuStrip();

            // 菜单项 1: 显示主窗口
            var showItem = new WinForms.ToolStripMenuItem("📱 显示主窗口");
            showItem.Click += (s, e) => ShowWindowRequested?.Invoke();
            contextMenu.Items.Add(showItem);

            // 菜单项 2: 番茄钟状态（动态更新文本）
            _pomodoroMenuItem = new WinForms.ToolStripMenuItem("🍅 番茄钟: 空闲");
            _pomodoroMenuItem.Enabled = false; // 只显示状态，不可点击
            contextMenu.Items.Add(_pomodoroMenuItem);

            // 分隔线
            contextMenu.Items.Add(new WinForms.ToolStripSeparator());

            // 菜单项 3: 退出应用
            var exitItem = new WinForms.ToolStripMenuItem("✕ 退出 Taskato");
            exitItem.Click += (s, e) => ExitRequested?.Invoke();
            contextMenu.Items.Add(exitItem);

            _notifyIcon.ContextMenuStrip = contextMenu;

            // 订阅番茄钟的 Tick 事件来实时更新托盘菜单中的计时显示
            _pomodoroService.Tick += OnPomodoroTick;
            _pomodoroService.StateChanged += OnPomodoroStateChanged;
        }

        /// <summary>
        /// 番茄钟每秒跳动时更新托盘菜单文本
        /// </summary>
        private void OnPomodoroTick(int minutes, int seconds, double progress)
        {
            if (_pomodoroMenuItem != null)
            {
                _pomodoroMenuItem.Text = $"🍅 番茄钟: {minutes:D2}:{seconds:D2}";
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
                _pomodoroMenuItem.Text = $"🍅 番茄钟: {stateText}";
            }
        }

        /// <summary>
        /// 显示气泡通知（Windows 10 会显示为 Toast 通知）
        /// </summary>
        /// <param name="title">通知标题</param>
        /// <param name="message">通知内容</param>
        /// <param name="timeout">显示时长（毫秒）</param>
        public void ShowBalloonTip(string title, string message, int timeout = 3000)
        {
            _notifyIcon?.ShowBalloonTip(timeout, title, message, WinForms.ToolTipIcon.Info);
        }

        /// <summary>
        /// 获取应用程序图标
        /// 优先使用嵌入的资源图标，如果没有则生成一个简单的默认图标
        /// </summary>
        private Icon GetAppIcon()
        {
            try
            {
                // 尝试从当前程序集获取图标
                var exePath = System.Reflection.Assembly.GetExecutingAssembly().Location;
                if (!string.IsNullOrEmpty(exePath) && System.IO.File.Exists(exePath))
                {
                    var icon = Icon.ExtractAssociatedIcon(exePath);
                    if (icon != null) return icon;
                }
            }
            catch
            {
                // 获取失败时使用默认图标
            }

            // 回退方案：使用系统默认图标
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
                _notifyIcon.Visible = false;   // 隐藏图标
                _notifyIcon.Dispose();          // 释放资源
                _notifyIcon = null;
            }
        }
    }
}
