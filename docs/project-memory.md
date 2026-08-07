# Taskato 项目记忆

更新时间：2026-08-07

## 当前架构决策

1. C# Service/ViewModel 是任务、计时器和设置的唯一业务事实来源；React 只能调用 `WebBridgeService` 白名单协议。
2. 每日总结只读取仍然有效的完成记录。任务完成后取消会保留在活动日志，但不继续出现在每日总结。
3. `TaskActivityRecords` 是只追加时间线。写任务与写活动必须在同一 SQLite 事务中完成；旧库只回填可证明事实，并保留迁移边界。
4. 数据恢复不在运行中的连接上覆盖 SQLite；只暂存已校验内容，在下次启动连接数据库前替换，并先保存回滚副本。
5. 番茄钟使用绝对截止时间计算剩余时长。DispatcherTimer 只负责刷新 UI，系统休眠和 UI 卡顿不能改变业务耗时。
6. WebView2 是唯一工作区 UI；故障时只显示可诊断错误和 retry（重试）入口，不再维护旧 WPF 工作区回退。
7. WebView2 用户数据默认位于 `%LOCALAPPDATA%/Taskato/WebView2`，自动烟测可覆盖为隔离目录；浏览器缓存不得写入或打包进便携发布目录。
8. 专注统计只读取新增 `PomodoroFocusSessions`/`PomodoroFocusSegments`：Working 与 Resting 的 active（有效）区间计入，Paused 与 CompletionPending 等待不计入；旧历史不推算。

## 发布边界

- 当前版本：2.23.0；Git 提交版本号按仓库规则使用 `v2.23`。
- 发布包使用 .NET 8 `win-x64` 自包含模式；安装包负责检测 WebView2 Evergreen Runtime。
- 自动静默更新和代码签名尚未启用。正式公众分发必须在受控 CI 中使用发布者证书签名。

## 已验证基线

- Debug 隔离构建 0 错误；干净还原后保留 `SQLitePCLRaw` 的现有 `NETSDK1206` RID 警告。
- C# 回归程序覆盖 XAML、层级任务、只追加日志、迁移、备份恢复、桥接、休眠模拟、双屏窗口归位、24 小时加速计时和通知去重/释放。
- 前端 TypeScript、3 个测试文件共 30 项 Vitest 和生产构建通过；新增专注统计天/周/月格子数量与切换回归测试。
- 2026-08-07：Debug/Release 隔离编译、TestProject（含专注统计跨午夜/暂停恢复/完成等待测试）通过；保留既有 `NETSDK1206` RID warning（警告）。
- 2.22.0 修复 Web 窗口拖动、任务三栏缩放和树层级显示，设置页恢复旧版六类完整能力；发布流水线继续自动验证真实 WebView2 宿主。
- 2.23.0 按“清晰工作台”重构任务层级和交互反馈：任务组与未归类任务分区、父任务树形选择、标题栏与设置页同步日夜模式、启动居中和 WebView2-only 工作区。
