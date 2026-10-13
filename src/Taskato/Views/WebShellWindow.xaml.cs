using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
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
        private bool _smokeCompleted;

        // WebView2 会接管鼠标消息，桥接回调到达 WPF 时 DragMove 已经失去按下态；
        // 因此按 Win32 标题栏拖动协议把当前窗口交还给系统窗口管理器。
        private const int WmNcLeftButtonDown = 0x00A1;
        private const int HitTestCaption = 2;

        [DllImport("user32.dll")]
        private static extern bool ReleaseCapture();

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr windowHandle, int message, IntPtr wParam, IntPtr lParam);

        public bool IsAppShuttingDown { get; set; }
        public WebShellWindow(WebBridgeService bridge)
        {
            _bridge = bridge;
            InitializeComponent();
            Loaded += async (_, _) => await InitializeWebViewAsync();
            _bridge.WindowCommandRequested += HandleWindowCommand;
            StateChanged += (_, _) => _bridge.NotifyWindowStateChanged(WindowState == WindowState.Maximized);
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
                var webViewDataDirectory = ResolveWebViewDataDirectory();
                Directory.CreateDirectory(webViewDataDirectory);
                var environment = await CoreWebView2Environment.CreateAsync(userDataFolder: webViewDataDirectory);
                await WebView.EnsureCoreWebView2Async(environment);
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
            core.NavigationCompleted += async (_, args) =>
            {
                if (args.IsSuccess)
                {
                    LoadingPanel.Visibility = Visibility.Collapsed;
                    if (IsSmokeMode()) await CompleteSmokeAsync(core);
                }
                else
                {
                    ShowInitializationError(new InvalidOperationException($"网页加载失败：{args.WebErrorStatus}"));
                }
            };
            core.ProcessFailed += (_, args) =>
                ShowInitializationError(new InvalidOperationException($"WebView2 进程异常退出：{args.ProcessFailedKind}"));
        }

        private void HandleWindowCommand(string command)
        {
            switch (command)
            {
                case "state": _bridge.NotifyWindowStateChanged(WindowState == WindowState.Maximized); break;
                case "minimize": WindowState = WindowState.Minimized; break;
                case "maximize": WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized; break;
                case "close": Hide(); break;
                case "drag":
                    BeginNativeWindowDrag();
                    break;
            }
        }

        private void BeginNativeWindowDrag()
        {
            var windowHandle = new WindowInteropHelper(this).Handle;
            if (windowHandle == IntPtr.Zero) return;
            ReleaseCapture();
            SendMessage(windowHandle, WmNcLeftButtonDown, new IntPtr(HitTestCaption), IntPtr.Zero);
        }

        private void ShowInitializationError(Exception exception)
        {
            DiagnosticsService.Log("WebView2 工作区初始化失败", exception);
            _initialized = false;
            WebView.Visibility = Visibility.Collapsed;
            LoadingPanel.Visibility = Visibility.Collapsed;
            ErrorPanel.Visibility = Visibility.Visible;
            ErrorMessage.Text = exception is WebView2RuntimeNotFoundException
                ? "当前系统未安装 Microsoft Edge WebView2 Runtime。安装运行时后点击重试。"
                : exception.Message;
            if (IsSmokeMode()) _ = CompleteSmokeAsync(null, exception.Message);
        }

        private static bool IsSmokeMode() =>
            Environment.GetEnvironmentVariable("TASKATO_E2E_SMOKE") == "1";

        private static string ResolveWebViewDataDirectory()
        {
            var explicitDirectory = Environment.GetEnvironmentVariable("TASKATO_WEBVIEW_USER_DATA_DIRECTORY");
            if (!string.IsNullOrWhiteSpace(explicitDirectory))
                return Path.GetFullPath(explicitDirectory);

            // 浏览器缓存属于可变用户数据，不能落在可执行文件旁边，否则便携包运行后会污染发布目录。
            var dataDirectory = Environment.GetEnvironmentVariable("TASKATO_DATA_DIRECTORY");
            var resolvedDataDirectory = string.IsNullOrWhiteSpace(dataDirectory)
                ? Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Taskato")
                : Path.GetFullPath(dataDirectory);
            return Path.Combine(resolvedDataDirectory, "WebView2");
        }

        private async Task CompleteSmokeAsync(CoreWebView2? core, string? initialError = null)
        {
            if (_smokeCompleted) return;
            _smokeCompleted = true;
            var resultPath = Environment.GetEnvironmentVariable("TASKATO_E2E_RESULT_PATH");
            try
            {
                var error = initialError;
                if (core is not null && string.IsNullOrWhiteSpace(error))
                {
                    var ready = false;
                    for (var attempt = 0; attempt < 50; attempt++)
                    {
                        var scriptResult = await core.ExecuteScriptAsync(
                            "document.readyState === 'complete' && " +
                            "Boolean(document.querySelector('#root')) && " +
                            "document.body.innerText.includes('项目与任务') && " +
                            "Boolean(window.chrome && window.chrome.webview) && " +
                            "!Boolean(document.querySelector('.error-toast'))");
                        if (string.Equals(scriptResult, "true", StringComparison.OrdinalIgnoreCase))
                        {
                            ready = true;
                            break;
                        }
                        await Task.Delay(100);
                    }
                    if (!ready) error = "网页未在限定时间内完成 React 渲染或本地桥接初始化。";
                }

                if (!string.IsNullOrWhiteSpace(resultPath))
                {
                    var fullPath = Path.GetFullPath(resultPath);
                    Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
                    await File.WriteAllTextAsync(
                        fullPath,
                        JsonSerializer.Serialize(new
                        {
                            passed = string.IsNullOrWhiteSpace(error),
                            error,
                            checkedAt = DateTimeOffset.Now
                        }));
                }
                if (!string.IsNullOrWhiteSpace(error))
                    DiagnosticsService.Log("WebView2 自动烟测失败", null, error);
                // 先释放浏览器控制器，确保独立的 WebView2 子进程关闭并释放烟测目录中的缓存句柄。
                WebView.Dispose();
                Application.Current.Shutdown(string.IsNullOrWhiteSpace(error) ? 0 : 2);
            }
            catch (Exception exception)
            {
                DiagnosticsService.Log("WebView2 自动烟测无法写入结果", exception);
                Application.Current.Shutdown(3);
            }
        }

        private async void RetryButton_Click(object sender, RoutedEventArgs e) => await InitializeWebViewAsync();
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
