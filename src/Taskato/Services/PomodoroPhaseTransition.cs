using Taskato.Models;

namespace Taskato.Services
{
    public enum PomodoroPhaseTransitionKind
    {
        Started,
        Paused,
        Resumed,
        Ended
    }

    /// <summary>
    /// 番茄钟阶段边界使用绝对时间传递，统计层无需从 UI 刷新频率反推实际耗时。
    /// </summary>
    public sealed record PomodoroPhaseTransition(
        PomodoroPhaseTransitionKind Kind,
        PomodoroFocusPhase Phase,
        DateTimeOffset OccurredAt,
        string EndReason = "");
}
