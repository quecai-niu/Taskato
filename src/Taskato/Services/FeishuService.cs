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

        /// <summary>
        /// 根据配置发送飞书通知（自动判断是否重复发送）
        /// </summary>
        public async Task NotifyAsync(string title, string content)
        {
            if (_settings.Config.FeishuRepeatEnabled)
                await SendRepeatedAsync(title, content);
            else
                await SendAsync(title, content);
        }

        /// <summary>
        /// 间隔重复发送飞书消息
        /// </summary>
        /// <param name="count">发送次数，默认 3</param>
        /// <param name="intervalSeconds">间隔秒数，默认 3</param>
        public async Task SendRepeatedAsync(string title, string content, int count = 3, int intervalSeconds = 3)
        {
            for (int i = 0; i < count; i++)
            {
                await SendAsync(title, content);
                if (i < count - 1)
                    await Task.Delay(intervalSeconds * 1000);
            }
        }
    }
}
