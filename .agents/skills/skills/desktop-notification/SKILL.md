---
name: desktop-notification
description: Provides a standard method for the agent to send native Windows desktop popup notifications to the user upon completing a task. Handles toggle configuration logic.
---

# Desktop Notification Skill

Use this skill at the end of your execution turns to notify the user that you have finished your tasks.

## 1. 状态检查与配置 (Toggle Logic)
用户的通知偏好存储在以下文件：
`.agents\skills\skills\desktop-notification\resources\notification_config.txt`

*   **前置检查**：在执行弹窗命令前，使用读取工具检查该文件。如果文件内容为 `false`，则**跳过弹窗操作**并结束对话。如果文件不存在或内容为 `true`，则执行弹窗。
*   **状态切换**：当用户在对话中明确要求“关闭弹窗”或“开启弹窗”时，请主动修改该文件的内容（写入 `false` 或 `true`）。

## 2. 弹窗执行命令 (Execution Command)
使用以下 `pwsh` 命令触发置顶弹窗。
**注意：** 必须在 `run_command` 工具中将 `WaitMsBeforeAsync` 设为一个极小值（如 `200`），让其在后台挂起等待用户点击，而不要阻塞你的对话进程。

```powershell
pwsh -NoProfile -WindowStyle Hidden -File ".agents\skills\skills\desktop-notification\scripts\notify.ps1"
```
*(技术细节：通过传入带有 TopMost 属性的匿名 Form，强制 MessageBox 穿透其他窗口置顶显示。)*
