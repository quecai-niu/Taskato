# Taskato 仓库地图

更新时间：2026-07-29

## 应用入口

- `src/Taskato/App.xaml.cs`：进程生命周期、依赖组装、托盘、工作区选择、恢复前置处理与系统休眠事件。
- `src/Taskato/Views/WebShellWindow.xaml.cs`：WebView2 初始化、静态资源映射、进程失败反馈和旧 WPF 回退。
- `src/Taskato.Web/src/App.tsx`：React 应用级导航、全局浮层和错误边界。

## 前端工作区

- `src/Taskato.Web/src/components/AppChrome.tsx`：导航栏、标题栏、全局搜索和通知中心。
- `src/Taskato.Web/src/pages/WorkspacePages.tsx`：任务、每日总结、历史和设置页面。
- `src/Taskato.Web/src/ui.tsx`：页面标识、导航元数据、主题和共享按钮/刷新工具。
- `src/Taskato.Web/src/bridge/`：WebView2 客户端、浏览器 Mock 与 TypeScript 契约。
- `src/Taskato.Web/tests/`：桥接和应用框架组件测试。

## C# 业务与桥接

- `src/Taskato/Services/DatabaseService.cs`：数据库初始化、兼容迁移和基础任务操作。
- `src/Taskato/Services/DatabaseService.TaskTree.cs`：层级任务、级联完成、移动与删除。
- `src/Taskato/Services/DatabaseService.Activity.cs`：只追加活动日志、旧数据幂等回填和查询。
- `src/Taskato/Services/WebBridge/WebBridgeService.cs`：协议入口、白名单分发、任务与番茄钟命令。
- `WebBridgeService.Reporting.cs`：每日总结、历史和导出。
- `WebBridgeService.Settings.cs`：设置、原生文件对话框、备份恢复和本地目录能力。

## 数据安全与稳定性

- `src/Taskato/Services/DataBackupService.cs`：备份归档、校验、暂存恢复和回滚副本。
- `src/Taskato/Services/DiagnosticsService.cs`：本地崩溃日志和保留期清理。
- `src/Taskato/Services/PomodoroService.cs`：基于绝对截止时间的工作/休息计时。
- `src/Taskato/Models/TaskActivityRecord.cs`：不可变活动类型和事件快照。

## 验证与发布

- `src/TestProject/Program.cs`：XAML、任务树、日志、迁移、备份、桥接和休眠回归测试。
- `scripts/build-release.ps1`：前后端验证、Windows x64 发布、便携包、校验和与清单。
- `installer/Taskato.iss`：Inno Setup 安装定义和 WebView2 Evergreen 检测。
- `docs/release_Taskato_20260729.md`：运行时、升级、签名和回滚边界。
