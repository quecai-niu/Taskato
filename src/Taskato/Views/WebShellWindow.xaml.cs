using System.IO;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using Taskato.Services;
using Taskato.Services.WebBridge;

namespace Taskato.Views
{
    /// <summary>WebView2 桌面宿主，只处理浏览器生命周期与原生窗口行为。</summary>
    public partial class WebShellWindow : Window
    {
        private const string VirtualHostName = "app.taskato.local";
        private readonly WebBridgeService _bridge;
        private bool _initialized;
        private bool _coreConfigured;

        public bool IsAppShuttingDown { get; set; }
        public event Action? LegacyWorkspaceRequested;

        public WebShellWindow(WebBridgeService bridge)
        {
            _bridge = bridge;
            InitializeComponent();
            Loaded += async (_, _) => await InitializeWebViewAsync();
            _bridge.WindowCommandRequested += HandleWindowCommand;
        }

        public void NavigateTo(string page) => _bridge.NavigateTo(page);

        private async Task InitializeWebViewAsync()
        {
            if (_initialized) return;
            try
            {
                LoadingPanel.Visibility = Visibility.Visible;
                ErrorPanel.Visibility = Visibility.Collapsed;
                WebView.Visibility = Visibility.Visible;
                await WebView.EnsureCoreWebView2Async();
                if (!_coreConfigured)
                {
                    ConfigureWebView(WebView.CoreWebView2);
                    _coreConfigured = true;
                }
                _bridge.Attach(WebView.CoreWebView2);

                var developmentUrl = Environment.GetEnvironmentVariable("TASKATO_WEB_DEV_URL");
                if (!string.IsNullOrWhiteSpace(developmentUrl))
                {
                    WebView.Source = new Uri(developmentUrl);
                }
                else
                {
                    var webRoot = Path.Combine(AppContext.BaseDirectory, "Web");
                    var entryPath = Path.Combine(webRoot, "index.html");
                    if (!File.Exists(entryPath))
                        throw new FileNotFoundException("前端构建产物不存在，请先执行 npm run build。", entryPath);

                    WebView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                        VirtualHostName, webRoot, CoreWebView2HostResourceAccessKind.DenyCors);
                    WebView.Source = new Uri($"https://{VirtualHostName}/index.html");
                }
                _initialized = true;
            }
            catch (Exception exception)
            {
                ShowInitializationError(exception);
            }
        }

        private void ConfigureWebView(CoreWebView2 core)
        {
            core.Settings.AreDefaultContextMenusEnabled = false;
#if !DEBUG
            core.Settings.AreDevToolsEnabled = false;
#endif
            core.Settings.IsStatusBarEnabled = false;
            core.NavigationCompleted += (_, args) =>
            {
                if (args.IsSuccess) LoadingPanel.Visibility = Visibility.Collapsed;
                else ShowInitializationError(new InvalidOperationException($"网页加载失败：{args.WebErrorStatus}"));
            };
            core.ProcessFailed += (_, args) =>
                ShowInitializationError(new InvalidOperationException($"WebView2 进程异常退出：{args.ProcessFailedKind}"));
        }

        private void HandleWindowCommand(string command)
        {
            switch (command)
            {
                case "minimize": WindowState = WindowState.Minimized; break;
                case "maximize": WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized; break;
                case "close": Hide(); break;
                case "drag":
                    try { DragMove(); }
                    catch (InvalidOperationException) { /* 鼠标释放后迟到的拖动消息无需处理。 */ }
                    break;
            }
        }

        private void ShowInitializationError(Exception exception)
        {
            DiagnosticsService.Log("WebView2 工作区初始化失败", exception);
            _initialized = false;
            WebView.Visibility = Visibility.Collapsed;
            LoadingPanel.Visibility = Visibility.Collapsed;
            ErrorPanel.Visibility = Visibility.Visible;
            ErrorMessage.Text = exception is WebView2RuntimeNotFoundException
                ? "当前系统未安装 Microsoft Edge WebView2 Runtime。安装后可重试，或先使用旧版 WPF 界面。"
                : exception.Message;
        }

        private async void RetryButton_Click(object sender, RoutedEventArgs e) => await InitializeWebViewAsync();
        private void LegacyButton_Click(object sender, RoutedEventArgs e) => LegacyWorkspaceRequested?.Invoke();

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            if (!IsAppShuttingDown)
            {
                e.Cancel = true;
                Hide();
                return;
            }
            _bridge.WindowCommandRequested -= HandleWindowCommand;
            WebView.Dispose();
            base.OnClosing(e);
        }
    }
}
