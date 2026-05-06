# CLAUDE.md — Taskato

## 项目概要
Taskato 是一个 Windows WPF (.NET 8) 番茄钟 + 任务管理桌面应用，采用 MVVM 架构。

## 编译与运行
```bash
dotnet build src/Taskato/Taskato.csproj
dotnet run --project src/Taskato/Taskato.csproj
```

## 架构要点
- `Models/` — 数据模型 (TaskItem 等)
- `ViewModels/` — MVVM 视图模型 (MainViewModel, PomodoroTimerViewModel, HistoryViewModel)
- `Views/` — XAML 窗体 (MainWindow, SettingsWindow, HistoryWindow, TaskDetailWindow, ToastWindow, ConfirmDialog)
- `Services/` — 业务服务 (DatabaseService/SQLite, PomodoroService 计时核心, TrayService 托盘)
- `Converters/` — XAML 值转换器
- `Assets/` — 应用图标 (icon.png, icon.ico)

## 基础规则（详见 .agents/rules/BaseRules.md）
- 复杂需求先出方案，获确认后再执行；简单修改直接做
- 代码必须中文注释，UI 修改严格检查对齐
- 临时文件放 `.agents/preview/`，命名带时间戳
- 终端优先用 pwsh；编译 → dotnet run → 极速冒烟（2~3秒后 Ctrl+C，WPF 放宽至 10~15 秒）
- 每次回合最后调用 desktop-notification 弹窗提醒
- 禁止修改与任务无关的文件
