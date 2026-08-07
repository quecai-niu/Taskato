# Taskato 仓库地图

更新时间：2026-08-07

## 应用入口

- `src/Taskato/App.xaml.cs`：进程生命周期、依赖组装、托盘、恢复前置处理与系统休眠事件。
- `src/Taskato/Views/WebShellWindow.xaml.cs`：WebView2 初始化、用户数据隔离、静态资源映射、原生窗口拖动、自动烟测和进程失败重试。
- `src/Taskato.Web/src/App.tsx`：React 应用级导航、全局浮层和错误边界。

## 前端工作区

- `src/Taskato.Web/src/components/AppChrome.tsx`：导航栏、标题栏、全局搜索和通知中心。
- `src/Taskato.Web/src/pages/WorkspacePages.tsx`：五个业务页面的稳定导出入口。
- `src/Taskato.Web/src/pages/TaskPage.tsx`：保留祖先链的层级任务树、可持久化三栏分隔线、任务详情和番茄钟侧栏。
- `src/Taskato.Web/src/pages/SettingsPage.tsx`：一比一承接旧版主题、番茄钟、系统、总结、提示音和飞书设置，并保留数据保护入口。
- `src/Taskato.Web/src/pages/SummaryPage.tsx`、`HistoryPage.tsx`：可独立渲染和测试的总结与历史页面。
- `src/Taskato.Web/src/pages/FocusAnalyticsPage.tsx`：按天、周、月展示番茄钟有效工作/休息时长热力格子与启动标记。
- `src/Taskato.Web/src/ui.tsx`：页面标识、导航元数据、主题和共享按钮/刷新工具。
- `src/Taskato.Web/src/bridge/`：WebView2 客户端、浏览器 Mock 与 TypeScript 契约。
- `src/Taskato.Web/tests/`：桥接、应用框架和独立业务页面测试。

## C# 业务与桥接

- `src/Taskato/Services/DatabaseService.cs`：数据库初始化、兼容迁移和基础任务操作。
- `src/Taskato/Services/DatabaseService.TaskTree.cs`：层级任务、级联完成、移动与删除。
- `src/Taskato/Services/DatabaseService.Activity.cs`：只追加活动日志、旧数据幂等回填和查询。
- `src/Taskato/Services/DatabaseService.FocusAnalytics.cs`：番茄钟会话/连续区间持久化、恢复校正与天周月聚合查询。
- `src/Taskato/Models/PomodoroFocusRecords.cs`、`src/Taskato/Services/PomodoroFocusTracker.cs`：阶段记录模型与串行写入队列；只把 Working/Resting 的有效区间计入统计。
- `src/Taskato/Models/BridgeDtos/FocusAnalyticsBridgeDto.cs`、`src/Taskato/Services/WebBridge/WebBridgeService.FocusAnalytics.cs`：专注统计 DTO 与 Bridge 查询协议。
- `src/Taskato/Services/WebBridge/WebBridgeService.cs`：协议入口、白名单分发、任务与番茄钟命令。
- `WebBridgeService.Reporting.cs`：每日总结、历史和导出。
- `WebBridgeService.Settings.cs`：完整设置、提示音/飞书测试、原生文件对话框、备份恢复和本地目录能力。

## 数据安全与稳定性

- `src/Taskato/Services/DataBackupService.cs`：备份归档、校验、暂存恢复和回滚副本。
- `src/Taskato/Services/DiagnosticsService.cs`：本地崩溃日志和保留期清理。
- `src/Taskato/Services/PomodoroService.cs`：基于绝对截止时间的工作/休息计时。
- `src/Taskato/Services/PomodoroNotificationCoordinator.cs`：通知订阅、阶段去重与生命周期释放。
- `src/Taskato/Services/WindowPlacementCalculator.cs`：多屏工作区选择、屏幕断开降级与窗口边界约束。
- `src/Taskato/Models/TaskActivityRecord.cs`：不可变活动类型和事件快照。

## 验证与发布

- `src/TestProject/Program.cs`：任务树、日志、迁移、备份、桥接、窗口归位、通知生命周期、长期计时与专注统计回归测试。
- `scripts/build-release.ps1`：前后端验证、自包含 Windows x64 发布、真实 WebView2 烟测、便携包/安装包、校验和与清单。
- `scripts/test-webview-smoke.ps1`：以隔离数据目录启动发布程序，验证 React 根节点与本地桥接后清理运行数据。
- `installer/Taskato.iss`：Inno Setup 安装定义和 WebView2 Evergreen 检测。
- `docs/release_Taskato_20260729.md`：运行时、升级、签名和回滚边界。
