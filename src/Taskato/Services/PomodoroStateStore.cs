using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Taskato.Services
{
    /// <summary>单个番茄钟的可恢复运行快照。</summary>
    public sealed class PomodoroRuntimeSnapshot
    {
        public string SlotId { get; set; } = string.Empty;
        public PomodoroService.PomodoroState State { get; set; }
        public PomodoroService.PomodoroState PausedState { get; set; } = PomodoroService.PomodoroState.Working;
        public PomodoroService.PomodoroState? CompletedPhase { get; set; }
        public int RemainingSeconds { get; set; }
        public int TotalSeconds { get; set; }
        public DateTimeOffset? DeadlineUtc { get; set; }
        public DateTimeOffset? CompletedAtUtc { get; set; }
        public bool RestHalfwayRaised { get; set; }
        public int WorkMinutes { get; set; }
        public int RestMinutes { get; set; }
    }

    internal sealed class PomodoroStateDocument
    {
        public int Version { get; set; } = 1;
        public DateTimeOffset SavedAtUtc { get; set; }
        public List<PomodoroRuntimeSnapshot> Timers { get; set; } = new();
    }

    /// <summary>
    /// 把番茄钟运行状态保存到独立 JSON 文件。采用同目录临时文件替换，避免进程退出或断电时留下半份 JSON。
    /// </summary>
    public sealed class PomodoroStateStore
    {
        private const int CurrentVersion = 1;
        private readonly string _statePath;
        private readonly JsonSerializerOptions _jsonOptions = new()
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() }
        };

        public string StatePath => _statePath;

        public PomodoroStateStore(string statePath)
        {
            _statePath = Path.GetFullPath(statePath);
            Directory.CreateDirectory(Path.GetDirectoryName(_statePath)!);
        }

        public IReadOnlyList<PomodoroRuntimeSnapshot> Load()
        {
            if (!File.Exists(_statePath)) return Array.Empty<PomodoroRuntimeSnapshot>();

            try
            {
                var document = JsonSerializer.Deserialize<PomodoroStateDocument>(
                    File.ReadAllText(_statePath), _jsonOptions);
                if (document is null || document.Version != CurrentVersion)
                {
                    DiagnosticsService.Log($"忽略不兼容的番茄钟状态文件：{_statePath}", null);
                    return Array.Empty<PomodoroRuntimeSnapshot>();
                }

                if (document.Timers is null)
                {
                    DiagnosticsService.Log($"忽略缺少计时器列表的番茄钟状态文件：{_statePath}", null);
                    return Array.Empty<PomodoroRuntimeSnapshot>();
                }

                // 可空性标注无法约束外部 JSON；过滤数组中的 null，避免损坏状态阻断应用启动。
                return document.Timers.OfType<PomodoroRuntimeSnapshot>().ToList();
            }
            catch (Exception exception)
            {
                // 运行状态损坏不应阻断主程序；降级为空闲后，下一次有效操作会覆盖坏文件。
                DiagnosticsService.Log($"读取番茄钟状态失败：{_statePath}", exception);
                return Array.Empty<PomodoroRuntimeSnapshot>();
            }
        }

        public void Save(IEnumerable<PomodoroRuntimeSnapshot> snapshots)
        {
            var temporaryPath = _statePath + ".tmp";
            try
            {
                var document = new PomodoroStateDocument
                {
                    Version = CurrentVersion,
                    SavedAtUtc = DateTimeOffset.UtcNow,
                    Timers = snapshots.ToList()
                };
                File.WriteAllText(temporaryPath, JsonSerializer.Serialize(document, _jsonOptions));
                File.Move(temporaryPath, _statePath, overwrite: true);
            }
            catch (Exception exception)
            {
                DiagnosticsService.Log($"保存番茄钟状态失败：{_statePath}", exception);
                try
                {
                    if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
                }
                catch
                {
                    // 清理失败只会遗留可安全覆盖的临时文件，不影响现有有效状态。
                }
            }
        }
    }
}
