namespace Taskato.Models.BridgeDtos
{
    public sealed record FocusStartBridgeDto(
        string Time,
        string TimerName,
        int? TaskId,
        string TaskTitle);

    public sealed record FocusAnalyticsCellBridgeDto(
        string Key,
        string Date,
        string Label,
        string StartsAt,
        string EndsAt,
        int WorkSeconds,
        int RestSeconds,
        IReadOnlyList<FocusStartBridgeDto> WorkStarts);

    public sealed record FocusAnalyticsBridgeDto(
        string Period,
        string AnchorDate,
        string RangeLabel,
        string RangeStart,
        string RangeEndExclusive,
        int WorkSeconds,
        int RestSeconds,
        int TotalSeconds,
        int WorkStartCount,
        int LongestSessionSeconds,
        int ActiveDays,
        IReadOnlyList<FocusAnalyticsCellBridgeDto> Cells);
}
