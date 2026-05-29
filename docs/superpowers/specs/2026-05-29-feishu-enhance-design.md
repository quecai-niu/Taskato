# 飞书通知增强 — 设计文档

> 日期：2026-05-29 | 状态：待实现

## 目标

1. 工作/休息结束时飞书消息间隔发送 3 次（间隔 3 秒）
2. 休息过半时发送飞书提醒
3. 两项均可独立开关，默认开启，持久化到 settings.json

## 改动清单

### SettingsConfig 新增字段

| 字段 | 类型 | 默认值 |
|------|------|--------|
| `FeishuRepeatEnabled` | `bool` | `true` |
| `FeishuRestHalfwayEnabled` | `bool` | `true` |

### FeishuService 新增方法

`SendRepeatedAsync(title, content, count=3, intervalSeconds=3)` — 循环调用 SendAsync，间隔 await Task.Delay

### PomodoroService 新增事件

- `RestHalfway` — 休息过半时触发
- 检测条件：`CurrentState == Resting && _remainingSeconds == _totalSeconds / 2`

### MainWindow.xaml.cs

- 4 处回调：`SendAsync` 替换为 `SendRepeatedAsync`
- 新增：订阅 `RestHalfway` 事件，检查 `FeishuRestHalfwayEnabled` 后发送飞书

### SettingsWindow

- 新增两行：重复发送开关 + 休息过半提醒开关
