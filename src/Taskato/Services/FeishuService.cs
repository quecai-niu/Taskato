using System;
using System.Net.Http;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Taskato.Services
{
    /// <summary>
    /// 飞书 Webhook 通知服务 — 通过 HTTP POST 向飞书群机器人发送文本消息
    /// </summary>
    public class FeishuService
    {
        public const int MaximumNotificationCount = 20;
        private readonly SettingsService _settings;
        private readonly HttpClient _http;

        public FeishuService(SettingsService settings, HttpClient? httpClient = null)
        {
            _settings = settings;
            _http = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        }

        /// <summary>
        /// 发送飞书 post 格式消息
        /// </summary>
        /// <returns>是否发送成功</returns>
        public async Task<bool> SendAsync(string title, string content) =>
            (await SendDetailedAsync(title, content, null)).Sent;

        /// <summary>
        /// 使用页面中尚未保存的 Webhook 发送测试消息；常规通知不传覆盖值，继续读取持久配置。
        /// </summary>
        public async Task<bool> SendAsync(string title, string content, string? webhookUrlOverride) =>
            (await SendDetailedAsync(title, content, webhookUrlOverride)).Sent;

        /// <summary>按设置次数发送测试消息，并返回可直接展示给用户的结果。</summary>
        public async Task<FeishuSendResult> TestAsync(
            string webhookUrl,
            int count,
            int intervalSeconds = 3)
        {
            var safeCount = Math.Clamp(count, 1, MaximumNotificationCount);
            for (var index = 0; index < safeCount; index++)
            {
                var result = await SendDetailedAsync(
                    "Taskato 测试消息",
                    "如果你收到这条消息，说明飞书 Webhook 配置成功！",
                    webhookUrl);
                if (!result.Sent) return result;
                if (index < safeCount - 1 && intervalSeconds > 0)
                    await Task.Delay(TimeSpan.FromSeconds(intervalSeconds));
            }

            return FeishuSendResult.Success($"测试消息已发送 {safeCount} 条，请在飞书群中确认。");
        }

        private async Task<FeishuSendResult> SendDetailedAsync(
            string title,
            string content,
            string? webhookUrlOverride)
        {
            var webhookUrl = webhookUrlOverride;
            // 非测试调用仍允许部署环境通过变量覆盖本地设置。
            if (string.IsNullOrWhiteSpace(webhookUrl))
            {
                webhookUrl = Environment.GetEnvironmentVariable("FEISHU_WEBHOOK_URL");
                if (string.IsNullOrWhiteSpace(webhookUrl))
                    webhookUrl = _settings.Config.FeishuWebhookUrl;
            }

            if (string.IsNullOrWhiteSpace(webhookUrl) ||
                !Uri.TryCreate(webhookUrl.Trim(), UriKind.Absolute, out var webhookUri) ||
                webhookUri.Scheme is not ("https" or "http"))
                return FeishuSendResult.Failure("invalid_url", "请填写有效的飞书 Webhook URL。");

            try
            {
                var payload = new
                {
                    msg_type = "post",
                    content = new
                    {
                        post = new
                        {
                            zh_cn = new
                            {
                                title = title,
                                content = new[]
                                {
                                    new[]
                                    {
                                        new { tag = "text", text = content }
                                    }
                                }
                            }
                        }
                    }
                };

                var json = JsonSerializer.Serialize(payload);
                var httpContent = new StringContent(json, Encoding.UTF8, "application/json");
                using var response = await _http.PostAsync(webhookUri, httpContent);
                var body = await response.Content.ReadAsStringAsync();
                return InterpretResponse(response.StatusCode, body);
            }
            catch (TaskCanceledException)
            {
                return FeishuSendResult.Failure("timeout", "连接飞书超时，请检查网络后重试。");
            }
            catch (HttpRequestException)
            {
                return FeishuSendResult.Failure("network_error", "无法连接飞书，请检查网络或代理设置。");
            }
            catch (JsonException)
            {
                return FeishuSendResult.Failure("invalid_response", "飞书返回了无法识别的响应，请检查 Webhook 地址。");
            }
            catch
            {
                // 常规通知不得打断当前工作流；测试入口会通过结果对象向用户说明失败。
                return FeishuSendResult.Failure("unknown_error", "发送失败，请稍后重试。");
            }
        }

        /// <summary>
        /// 同时兼容飞书机器人新响应 code/msg 与旧响应 StatusCode/StatusMessage。
        /// </summary>
        public static FeishuSendResult InterpretResponse(HttpStatusCode statusCode, string body)
        {
            if ((int)statusCode < 200 || (int)statusCode >= 300)
                return FeishuSendResult.Failure("http_error", $"飞书服务返回 HTTP {(int)statusCode}，请检查 Webhook 是否仍有效。");

            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            if (TryGetInt32(root, "code", out var code))
                return code == 0
                    ? FeishuSendResult.Success()
                    : FeishuSendResult.Failure("feishu_error", ReadMessage(root, "msg", $"飞书拒绝了请求（错误码 {code}）。"));

            if (TryGetInt32(root, "StatusCode", out var status))
                return status == 0
                    ? FeishuSendResult.Success()
                    : FeishuSendResult.Failure("feishu_error", ReadMessage(root, "StatusMessage", $"飞书拒绝了请求（错误码 {status}）。"));

            return FeishuSendResult.Failure("invalid_response", "飞书返回了无法识别的响应，请检查 Webhook 地址。");
        }

        private static bool TryGetInt32(JsonElement root, string propertyName, out int value)
        {
            value = 0;
            return root.TryGetProperty(propertyName, out var property) &&
                   property.ValueKind == JsonValueKind.Number &&
                   property.TryGetInt32(out value);
        }

        private static string ReadMessage(JsonElement root, string propertyName, string fallback) =>
            root.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
                ? property.GetString() ?? fallback
                : fallback;

        /// <summary>
        /// 按当前事件自己的次数发送飞书通知；0 次用于关闭该事件。
        /// </summary>
        public async Task NotifyAsync(string title, string content, int count, int intervalSeconds = 3)
        {
            var safeCount = Math.Clamp(count, 0, MaximumNotificationCount);
            if (safeCount == 0) return;
            if (safeCount == 1)
                await SendAsync(title, content);
            else
                await SendRepeatedAsync(title, content, safeCount, intervalSeconds);
        }

        /// <summary>
        /// 间隔重复发送飞书消息
        /// </summary>
        /// <param name="count">发送次数，限制为 0～20。</param>
        /// <param name="intervalSeconds">间隔秒数，默认 3</param>
        public async Task SendRepeatedAsync(string title, string content, int count = 3, int intervalSeconds = 3)
        {
            var safeCount = Math.Clamp(count, 0, MaximumNotificationCount);
            for (var index = 0; index < safeCount; index++)
            {
                await SendAsync(title, content);
                if (index < safeCount - 1 && intervalSeconds > 0)
                    await Task.Delay(TimeSpan.FromSeconds(intervalSeconds));
            }
        }
    }

    public sealed record FeishuSendResult(bool Sent, string ErrorCode, string Message)
    {
        public static FeishuSendResult Success(string message = "测试消息已发送，请在飞书群中确认。") =>
            new(true, string.Empty, message);
        public static FeishuSendResult Failure(string errorCode, string message) => new(false, errorCode, message);
    }
}
