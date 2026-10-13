using System.Text.Json;

namespace Taskato.Services.WebBridge
{
    public sealed partial class WebBridgeService
    {
        private async Task<object> GetPomodoroTimelineAsync(JsonElement parameters)
        {
            var timerId = RequiredString(parameters, "timerId");
            var timer = _taskWorkspace.AllTimers.FirstOrDefault(item => TimerId(item) == timerId);
            if (timer is null)
                throw new BridgeRequestException("invalid_timer", "计时器不存在。");

            var slotId = _taskWorkspace.GetFocusTimerSlotId(timer);
            await _taskWorkspace.FlushFocusTrackingAsync();
            var entries = await _database.GetPomodoroTimelineAsync(slotId);
            return entries.Select(entry => new
            {
                entry.Id,
                occurredAt = DateTime.SpecifyKind(entry.OccurredAtUtc, DateTimeKind.Utc).ToString("O"),
                entry.Kind,
                entry.Title,
                entry.Detail,
                entry.DurationSeconds,
                entry.DurationLabel,
                taskTitle = entry.TaskTitleSnapshot
            }).ToList();
        }
    }
}
