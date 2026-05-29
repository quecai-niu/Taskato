using System;
using System.Net.Http;
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
        private readonly SettingsService _settings;
        private readonly HttpClient _http;

        public FeishuService(SettingsService settings)
        {
            _settings = settings;
            _http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        }

        /// <summary>
        /// 发送飞书 post 格式消息
        /// </summary>
        /// <returns>是否发送成功</returns>
        public async Task<bool> SendAsync(string title, string content)
        {
            // 环境变量优先
            var webhookUrl = Environment.GetEnvironmentVariable("FEISHU_WEBHOOK_URL");
            if (string.IsNullOrWhiteSpace(webhookUrl))
            {
                webhookUrl = _settings.Config.FeishuWebhookUrl;
            }

            if (string.IsNullOrWhiteSpace(webhookUrl))
                return false;

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
                var response = await _http.PostAsync(webhookUrl, httpContent);

                if (response.IsSuccessStatusCode)
                {
                    var body = await response.Content.ReadAsStringAsync();
                    using var doc = JsonDocument.Parse(body);
                    if (doc.RootElement.TryGetProperty("code", out var code) && code.GetInt32() == 0)
                        return true;
                }
                return false;
            }
            catch
            {
                // 发送失败静默忽略，不打断用户
                return false;
            }
        }
    }
}
