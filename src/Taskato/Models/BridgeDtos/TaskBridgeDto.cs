namespace Taskato.Models.BridgeDtos
{
    /// <summary>前端任务快照，避免把 SQLite 运行时属性直接暴露给网页。</summary>
    public sealed record TaskBridgeDto(
        int Id,
        string Title,
        int? ParentId,
        int Depth,
        bool IsCompleted,
        string Status,
        int Priority,
        double OrderIndex,
        bool HasChildren,
        bool CanAddChild,
        int CompletedLeafCount,
        int TotalLeafCount,
        DateTime CreatedAt,
        DateTime? CompletedAt,
        int? CompletionDurationMinutes);

    public sealed record TaskProgressBridgeDto(
        int Id,
        int? TaskId,
        string TaskTitle,
        string TaskPath,
        string Note,
        DateTime StartedAt,
        DateTime CompletedAt,
        DateTime CreatedAt,
        int OriginalDurationMinutes,
        int DurationMinutes,
        bool IsDurationAdjusted,
        string EndReason,
        string EndReasonDisplay,
        string Source,
        bool CanOpenTask);

    /// <summary>由只追加活动记录投影出的历史时间线条目。</summary>
    public sealed record TaskActivityBridgeDto(
        string Id,
        int? TaskId,
        string Title,
        string EventType,
        string EventLabel,
        DateTime OccurredAt,
        string Detail,
        string Tone,
        bool CanOpenTask,
        bool IsLegacyBackfill);

    public sealed record TimerBridgeDto(
        string Id,
        string Name,
        string Display,
        string Status,
        double Progress,
        int WorkMinutes,
        int RestMinutes,
        bool IsStarted,
        bool CanStart,
        bool CanPause,
        bool CanStop,
        bool CanRest);
}
