# Taskato 专注统计与旧版界面清理执行计划

更新时间：2026-08-07 21:44（Asia/Singapore）

## 实施总览

一句话目标：以番茄钟真实运行阶段为唯一统计来源，新增天、周、月热力图，并删除已经停止维护的旧版 WPF 工作区。

当前阶段：实现与验证完成。

- [x] 建立专注/休息会话与有效区间数据模型。
- [x] 接入开始、暂停、恢复、停止、自然到期、提前休息和进程恢复边界。
- [x] 新增专注统计 Bridge 查询与 React 独立页面。
- [x] 删除旧版 WPF 工作区、回退入口和对应设置。
- [x] 补齐后端、前端、迁移、恢复和视觉回归验证。
- [x] 更新仓库地图与项目记忆。

当前不需要额外输入；已经确认从新版本起记录，不推算旧数据。

## 已确认行为

- 统计来源是番茄钟生命周期，不读取任务完成耗时。
- Working 与 Resting 都计入有效时长；Paused 与 CompletionPending 不计入。
- 任务只作为会话元数据，用于识别计时所属任务，不决定统计时长。
- 天视图为 48 个半小时间隔；周视图为 7 行 × 48 列；月视图使用自然日历网格。
- 格子颜色表示专注和休息的有效总时长；提示信息分别显示专注、休息、合计与启动时刻。
- 启动标记只代表新一轮工作专注，暂停恢复不重复标记。
- 旧数据留空；新增统计表不改写现有任务、总结或活动日志。
- 删除旧版 WPF 业务页面和回退路径；保留 WebView2 宿主、托盘、通知窗口和必要原生对话框。

## 实现范围

### 数据与生命周期

- [x] 新增番茄钟会话和连续活动区间模型，并建立必要索引。
- [x] 使用 SQLite 事务保存阶段转换，保证会话与区间状态一致。
- [x] 正确处理跨午夜、多个计时器并发和进程关闭期间的绝对截止时间。
- [x] 统计写入失败时记录诊断日志，但不阻断计时器主流程。

### Bridge 与网页

- [x] 新增按锚点日期和粒度查询的专注统计协议。
- [x] 增加独立导航项、摘要卡片、周期切换、热力格子、图例和悬停详情。
- [x] 支持日间与夜间主题，并在常规窗口和最大化窗口中保持可读。
- [x] 浏览器 Mock 与真实 Bridge 使用同一 TypeScript 契约。

### 旧版清理

- [x] 删除旧版主窗口、页面导航控件及只为旧工作区服务的代码。
- [x] 删除“切换旧版”设置、Bridge 命令和 WebView2 初始化失败时的旧版按钮。
- [x] 保留仍被 Web Bridge 或原生对话框使用的业务 ViewModel，不做无关重写。
- [x] WebView2 初始化失败时保留可诊断错误和重试入口。

## 验证计划

- [x] C#：数据库初始化幂等、工作/休息阶段、暂停恢复、停止、到期、跨日、进程恢复和多计时器。
- [x] React：导航、天/周/月标签、格子数量、日历布局、启动标记、提示详情和错误状态。
- [x] 执行 Web 测试与生产构建。
- [x] 执行 TestProject、Release 构建和 WebView2 烟测。
- [x] 执行差异检查和实际页面视觉验证。

## 实施调整与证据

1. 原计划只要求在 TestProject 中接入统计回归；实际增加了 `PomodoroFocusTracker.FlushAsync()` 的退出冲刷，并在 headless test（无界面测试）中创建最小 `Application` 容器，原因是统计写入是异步队列且设置桥接会更新 WPF 主题资源。验证：TestProject 运行通过。
2. 删除旧 XAML 后首次增量 build（增量构建）仍读到旧 `obj` 生成文件；先执行 `dotnet clean`，再用隔离 `OutputPath`/`IntermediateOutputPath` 构建，避免锁定正在运行的 Taskato 进程。验证：Debug/Release 编译通过，只有既有 `NETSDK1206` RID warning（警告）。
3. 浏览器视觉验证采用可测试的 DOM/layout smoke（DOM 与布局烟测）覆盖天/周/月格子数量和响应式 CSS；真实 WebView2 smoke 由 `scripts/test-webview-smoke.ps1` 完成。

## 最终验证证据（2026-08-07）

- `npm test -- --run`：3 个 test files（测试文件）、30 tests（测试）通过。
- `npm run build`：TypeScript check（类型检查）与 Vite production bundle（生产打包）通过。
- `dotnet build src/Taskato/Taskato.csproj -c Release`：0 errors（错误）、0 warnings（警告）。
- `dotnet run --project src/TestProject/TestProject.csproj -c Release`：任务树、桥接、通知生命周期、专注统计跨午夜/暂停恢复/完成等待，以及附加计时器删除后的稳定槽位回归全部通过；构建仅保留既有 `NETSDK1206` RID warning（警告）。
- `scripts/test-webview-smoke.ps1`：Release WebView2、React root（根节点）与 local bridge（本地桥接）通过。
- Browser visual check（浏览器视觉检查）：专注统计页天/周/月 tab、day 48 cells、week 7×48 cells、month 31 cells 与统计摘要可见；截屏确认暗色主题对比度和 card layout（卡片布局）正常。

## 回滚边界

新增表是增量结构；回滚网页入口和 Bridge 查询不会影响已有任务数据。已经写入的专注统计记录可安全保留。旧版 WPF 工作区删除后不提供运行时回退，WebView2 故障通过错误页、诊断日志和重试入口处理。
