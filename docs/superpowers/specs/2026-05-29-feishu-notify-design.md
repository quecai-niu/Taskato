# 飞书 Webhook 通知 — 设计文档

> 日期：2026-05-29 | 状态：待实现

## 目标

番茄钟工作/休息结束时，通过飞书 Webhook 发送通知消息。

## 需求摘要

1. 工作结束和休息结束时均可发送飞书通知，用户可在设置中分别开关
2. Webhook URL 在设置界面配置并持久化，环境变量 `FEISHU_WEBHOOK_URL` 优先
3. 消息格式：飞书 post（简单文本）

## 架构

```
SettingsWindow (UI 配置)
    ↓ 读写
SettingsConfig (持久化字段)
    ↓ 读取
MainWindow.xaml.cs (事件订阅处)
    ↓ 调用
FeishuService (新建)
    ↓ HTTP POST
飞书 Webhook API
```

## 改动清单

### 1. SettingsConfig 新增字段 (`Services/SettingsService.cs`)

| 字段 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| `FeishuEnabled` | `bool` | `false` | 是否启用飞书通知 |
| `FeishuWebhookUrl` | `string` | `""` | Webhook URL |
| `FeishuNotifyOnWork` | `bool` | `true` | 工作完成时通知 |
| `FeishuNotifyOnRest` | `bool` | `true` | 休息完成时通知 |

### 2. 新建 FeishuService (`Services/FeishuService.cs`)

- 单一方法 `async Task<bool> SendAsync(string title, string content)`
- 优先级：`FEISHU_WEBHOOK_URL` 环境变量 > `SettingsConfig.FeishuWebhookUrl`
- HTTP POST，Content-Type: application/json
- Payload 格式：
  ```json
  {
    "msg_type": "post",
    "content": {
      "post": {
        "zh_cn": {
          "title": "<title>",
          "content": [[{"tag": "text", "text": "<content>"}]]
        }
      }
    }
  }
  ```
- 超时 10 秒，不阻塞 UI 线程
- 发送失败静默忽略（不弹错误提示，不打断用户）

### 3. SettingsWindow 新增 UI (`Views/SettingsWindow.xaml` + `.cs`)

在"提示音"区域下方新增"🔗 飞书通知"区块：

- 启用开关（CheckBox，绑定 `FeishuEnabled`）
- Webhook URL 输入框（TextBox，只有启用时才可编辑）
- "工作完成时通知"复选框（绑定 `FeishuNotifyOnWork`）
- "休息完成时通知"复选框（绑定 `FeishuNotifyOnRest`）

### 4. MainWindow.xaml.cs 集成

在 `WorkCompleted` 回调中：检查 `FeishuEnabled && FeishuNotifyOnWork`，调用 `FeishuService.SendAsync()`
在 `RestCompleted` 回调中：检查 `FeishuEnabled && FeishuNotifyOnRest`，调用 `FeishuService.SendAsync()`

消息内容：
- 工作完成：标题 `"{计时器名称} 时间到！"`，正文 `"你已完成专注工作，要休息一下吗？"`
- 休息完成：标题 `"{计时器名称} 休息结束！"`，正文 `"精力充沛了吗？开始新一轮专注吧！"`

## 风险与边界

- 飞书请求失败不影响弹窗、提示音等现有通知流程
- 不引入第三方 NuGet 包，用 `HttpClient` 即可
- Webhook URL 明文存储于 `settings.json`，无加密（与现有设计一致）
