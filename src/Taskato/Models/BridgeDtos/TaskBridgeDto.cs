namespace Taskato.Models.BridgeDtos
{
    /// <summary>前端任务快照，避免把 SQLite 运行时属性直接暴露给网页。</summary>
    public sealed record TaskBridgeDto(
        int Id,
        string Title,
        int? ParentId,
        int Depth,
        bool IsCompleted,
        int Priority,
        bool HasChildren,
        bool CanAddChild,
        int CompletedLeafCount,
        int TotalLeafCount,
        DateTime CreatedAt,
        DateTime? CompletedAt,
        int? CompletionDurationMinutes);

    /// <summary>由任务与不可变完成记录投影出的历史时间线条目。</summary>
    public sealed record TaskActivityBridgeDto(
        string Id,
        int? TaskId,
        string Title,
        string EventType,
        string EventLabel,
        DateTime OccurredAt,
        string Detail,
        string Tone,
        bool CanDelete);

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
