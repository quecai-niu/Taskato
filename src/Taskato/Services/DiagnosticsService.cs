using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Threading;

namespace Taskato.Services
{
    /// <summary>
    /// 把未处理异常写入本地日志，便于用户在无法复现时提供诊断信息。
    /// </summary>
    public static class DiagnosticsService
    {
        private static readonly object FileLock = new();
        private static string? _logDirectory;
        private static bool _initialized;

        public static string? LogDirectory => _logDirectory;

        public static void Initialize(string dataDirectory)
        {
            if (_initialized) return;
            _logDirectory = Path.Combine(dataDirectory, "Logs");
            Directory.CreateDirectory(_logDirectory);
            RemoveExpiredLogs();

            AppDomain.CurrentDomain.UnhandledException += (_, args) =>
                Log("AppDomain 未处理异常", args.ExceptionObject as Exception);
            TaskScheduler.UnobservedTaskException += (_, args) =>
            {
                Log("未观察到的后台任务异常", args.Exception);
                args.SetObserved();
            };
            if (Application.Current is { } application)
            {
                application.DispatcherUnhandledException += DispatcherUnhandledException;
            }
            _initialized = true;
        }

        public static void Log(string category, Exception? exception, string? detail = null)
        {
            if (string.IsNullOrWhiteSpace(_logDirectory)) return;
            try
            {
                var builder = new StringBuilder()
                    .AppendLine($"[{DateTimeOffset.Now:O}] {category}");
                if (!string.IsNullOrWhiteSpace(detail)) builder.AppendLine(detail);
                if (exception is not null) builder.AppendLine(exception.ToString());
                builder.AppendLine();

                var path = Path.Combine(_logDirectory, $"taskato-{DateTime.Today:yyyyMMdd}.log");
                lock (FileLock)
                {
                    File.AppendAllText(path, builder.ToString(), Encoding.UTF8);
                }
            }
            catch
            {
                // 诊断日志不能反过来阻断应用的异常降级路径。
            }
        }

        private static void DispatcherUnhandledException(
            object sender,
            DispatcherUnhandledExceptionEventArgs args)
        {
            Log("WPF UI 线程未处理异常", args.Exception);
            // 不擅自吞掉未知 UI 异常；日志完成后仍由 WPF 的默认策略决定是否退出。
        }

        private static void RemoveExpiredLogs()
        {
            try
            {
                foreach (var path in Directory.EnumerateFiles(_logDirectory!, "taskato-*.log"))
                {
                    if (File.GetLastWriteTime(path) < DateTime.Now.AddDays(-30))
                        File.Delete(path);
                }
            }
            catch
            {
                // 清理失败只会保留旧日志，不影响应用启动。
            }
        }
    }
}
