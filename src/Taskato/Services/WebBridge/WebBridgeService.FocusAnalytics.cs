using System.Globalization;
using System.Text.Json;

namespace Taskato.Services.WebBridge
{
    public sealed partial class WebBridgeService
    {
        private async Task<object> GetFocusAnalyticsAsync(JsonElement parameters)
        {
            var period = OptionalString(parameters, "period") ?? "day";
            if (period is not ("day" or "week" or "month"))
                throw new BridgeRequestException("invalid_period", "统计粒度必须是 day、week 或 month。");

            var anchorText = OptionalString(parameters, "anchorDate")
                ?? DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            if (!DateTime.TryParseExact(
                    anchorText,
                    "yyyy-MM-dd",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var anchorDate))
            {
                throw new BridgeRequestException("invalid_date", "锚点日期必须使用 yyyy-MM-dd 格式。");
            }

            return await _database.GetPomodoroAnalyticsAsync(period, anchorDate);
        }
    }
}
