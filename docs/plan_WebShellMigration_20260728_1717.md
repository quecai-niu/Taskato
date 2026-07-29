# Taskato React + WebView2 迁移计划

## 1. 决策状态

- [x] 保留现有 C#、SQLite、番茄钟、托盘和通知逻辑。
- [x] 新 UI 使用 React + TypeScript + Vite。
- [x] 桌面容器使用 WPF + WebView2，不引入 Electron 或 Tauri。
- [x] 视觉基准复用 `docs/previews/preview_TaskatoWorkspace_20260728_1421/`。
- [x] Web UI 默认启用，现有 WPF 工作区暂时保留为可回退入口。
- [x] 用户确认本计划后开始实现。

## 2. 最终结构

```text
WPF 桌面壳层
├─ 窗口、托盘、单例运行和系统通知
├─ WebView2
│  └─ React + TypeScript 工作区
└─ C# WebBridge
   ├─ Task API → DatabaseService
   ├─ Summary/History API → 现有查询逻辑
   ├─ Pomodoro API → PomodoroService
   └─ Settings API → SettingsService
```

生产环境不启动本地 HTTP 服务器。WPF 使用 WebView2 虚拟域名映射，把前端构建产物映射为 `https://app.taskato.local/`。开发环境可选择连接 Vite 开发服务器，以获得热更新。

## 3. 通信协议

不使用 `AddHostObjectToScript` 直接暴露 C# 对象。统一通过 `CoreWebView2.WebMessageReceived` 和 `PostWebMessageAsJson` 交换版本化 JSON：

```json
{ "id": "request-id", "method": "tasks.list", "params": {} }
{ "id": "request-id", "ok": true, "result": {} }
{ "event": "pomodoro.tick", "payload": {} }
```

约束：

- [x] 每个请求带唯一 `id`，响应必须回传相同 `id`。
- [x] C# 只注册白名单方法，未知方法返回结构化错误。
- [x] 前端只接收 DTO，不直接序列化 SQLite 实体或 ViewModel。
- [x] UI 更新通过事件推送，不能依赖高频轮询数据库。
- [x] 所有业务写操作继续进入现有 Service，React 不直接接触数据库。

## 4. 目录与文件规划

### 4.1 前端

- [x] `src/Taskato.Web/package.json`：React、TypeScript、Vite 和图标依赖。
- [x] 应用壳层位于 `App.tsx`/`components/AppChrome.tsx`，四个业务页面分别位于 `pages/TaskPage.tsx`、`SummaryPage.tsx`、`HistoryPage.tsx` 和 `SettingsPage.tsx`。
- [x] 任务树、当前任务和番茄钟工作区。
- [x] 每日总结。
- [x] 历史记录与事件时间线。
- [x] 完整设置页面。
- [x] `src/Taskato.Web/src/bridge/`：请求、响应、事件和浏览器预览模拟器。
- [x] `src/Taskato.Web/src/styles.css`：从已确认原型迁移的令牌、布局和响应式规则。
- [x] `src/Taskato.Web/tests/`：桥接协议与关键交互测试。

原型只作为视觉和交互来源，不直接把 `node_modules`、Sites worker 或演示数据复制进生产目录。

### 4.2 C# 宿主

- [x] `src/Taskato/Views/WebShellWindow.xaml(.cs)`：WebView2 容器和窗口行为。
- [x] `src/Taskato/Services/WebBridge/WebBridgeService.cs`：消息接收、路由和响应。
- [x] `src/Taskato/Services/WebBridge/WebBridgeContracts.cs`：请求、响应和事件协议。
- [x] 任务查询与写操作处理（首轮集中在 `WebBridgeService`）。
- [x] 每日总结查询与补充任务处理（首轮集中在 `WebBridgeService`）。
- [x] 历史查询、事件投影与导出处理（首轮集中在 `WebBridgeService`）。
- [x] 计时器命令和事件推送处理（首轮集中在 `WebBridgeService`）。
- [x] 设置读取、校验和保存处理（首轮集中在 `WebBridgeService`）。
- [x] `src/Taskato/Models/BridgeDtos/`：前端专用 DTO。

### 4.3 构建与资源

- [x] `Taskato.csproj` 添加 `Microsoft.Web.WebView2`。
- [x] 前端 `npm run build` 输出到独立 `dist`，再由项目作为 Content 复制到发布目录。
- [x] Release 构建检查前端产物存在；缺失时给出明确错误，不生成空白应用。
- [x] 开发模式允许通过环境变量连接 Vite，默认仍加载本地产物。
- [x] 发布包包含 WebView2 运行时检测和可读错误页。

## 5. 实施阶段

### 阶段 0：建立可回退宿主

- [x] 保留当前 WPF 工作区，不删除已有页面。
- [x] 新增 `UseLegacyWorkspace` 设置，默认 `false`。
- [x] WebView2 初始化失败时显示可操作错误页，并允许切换回旧 WPF 工作区。
- [x] 托盘、首次引导和退出逻辑继续由 C# 管理。

验收：无前端产物或 WebView2 不可用时，用户仍能进入旧界面处理任务。

### 阶段 1：迁移视觉原型

- [x] 把已确认原型整理为 TypeScript 组件，移除演示专用状态。
- [x] 保留左侧导航、三栏任务页、总结、历史和设置页面结构。
- [x] 实现 1440、1100、960 三档响应式布局。
- [x] 先接入 bridge mock，使浏览器预览和组件测试不依赖 WPF。

验收：浏览器中与已确认原型视觉一致，所有控件都有真实交互状态。

### 阶段 2：任务桥接

- [x] 查询任务树、选中任务、新增顶级任务和子任务。
- [x] 完成、取消、删除分支、提升子任务和移动任务。
- [x] 保持最多五层、父级级联完成和完成耗时对话规则。
- [x] 子任务输入使用独立 React 状态，不得写入主任务输入状态。
- [x] 数据变化后由 C# 推送 `tasks.changed`，前端刷新必要区域。

验收：现有任务树回归测试通过；Web UI 可完成全部日常任务操作。

### 阶段 3：番茄钟与桌面能力

- [x] 推送主计时器和附加计时器状态。
- [x] 支持开始、暂停、继续、停止、提前休息和多计时器切换。
- [x] Toast、飞书和声音继续在 C# 侧执行。
- [x] 网页标题栏按钮映射到 WPF 最小化、最大化、关闭到托盘和拖动窗口。

验收：隐藏到托盘后计时不中断，每次完成只触发一次桌面通知。

### 阶段 4：总结、历史和设置

- [x] 每日总结使用当前有效完成过滤，保留“补充任务”。
- [x] 历史查询支持日期、关键词、事件状态、排序、导出和删除。
- [x] 设置覆盖旧设置窗体全部用户配置字段，而不是只迁移常用项。
- [x] 主题更新即时推送到 React CSS 变量。
- [x] 文件选择、声音试听和导出路径继续通过 C# 系统对话框完成。

验收：重启应用后设置兼容旧 JSON；总结复制/导出与页面一致。

### 阶段 5：默认切换与清理

- [x] Web UI 设为默认入口，保留明确的旧界面回退开关。
- [ ] 连续稳定使用并完成手工 QA 后，再删除旧 WPF 大页面。
- [x] 保留真正适合原生弹窗的流程：任务完成、确认、Toast 和系统文件选择。
- [x] 更新 README、交接文档、项目记忆和 `REPO_MAP.md`。

验收：常规使用只有一个 WebView2 主窗口，退出和托盘行为无回归。

## 6. 测试方案

- [x] `npm run build`：TypeScript 和生产前端构建。
- [x] `npm test`：桥接客户端、任务树组件和关键状态测试。
- [x] `dotnet build Taskato.sln`：C# 与 WPF 宿主构建。
- [x] `dotnet run --project src/TestProject/TestProject.csproj`：现有数据库业务回归。
- [x] C# 桥接契约测试：正常响应、未知方法、无效参数和异常序列化。
- [x] WebView2 启动冒烟：发布流水线自动启动真实宿主，验证导航完成、React 根节点、入口页面和 JS/C# 双向消息成功。
- [x] 窗口 QA：1440 × 1024、1100 × 800、960 × 640。
- [x] 手工流程（本次可独立验证范围）：浏览器覆盖任务输入隔离、任务新增、番茄钟、总结、历史和设置；真实 Release WebView2 覆盖启动握手、创建任务、详情/笔记、全局搜索和关闭到托盘后进程存活。另以自动化覆盖通知去重/释放、休眠时间跳变、24 小时加速计时和双屏坐标算法；真实休眠硬件和长期驻留仍保留为发布候选版观察项。

## 7. 风险与控制

1. WebView2 与 `AllowsTransparency=True` 的 WPF 窗口组合不稳定。
   - 宿主改为不透明窗口；圆角和阴影由网页与 Windows 11 窗口能力表达。
2. JS 与 C# 同时维护业务状态会产生双重事实来源。
   - C# 是唯一业务事实来源；React 只保存界面状态和 C# 返回的快照。
3. 高频番茄钟消息可能造成渲染压力。
   - 每秒只推送必要字段，前端局部更新，不整页重新查询。
4. 前端构建失败可能生成空白桌面应用。
   - Release 构建校验入口文件；运行时提供原生错误页和旧界面回退。
5. WebView2 Runtime 在目标机器缺失。
   - 启动时检测并给出安装指引；发布阶段决定 Evergreen 安装策略。

## 8. 回滚方案

- 当前 WPF 工作区在 Web UI 全量验收前保持可编译。
- `UseLegacyWorkspace=true` 可立即切回旧界面，不改变 SQLite 数据。
- Bridge 只调用现有 Service，不修改数据库格式即可完成前四阶段。
- 前端静态资源独立打包，回退时不需要还原用户数据或设置文件。

## 9. 推荐执行顺序

1. WebView2 宿主、回退开关和最小 `app.ready` 握手。
2. 从已确认原型建立 React + TypeScript 生产前端。
3. 任务 DTO 与任务桥接。
4. 番茄钟事件和窗口控制桥接。
5. 每日总结、历史和完整设置桥接。
6. 双端自动化、真实窗口 QA、默认入口切换。
7. 稳定后再清理旧 WPF 页面。

## 10. 当前下一步

- [x] 用户已确认并完成阶段 0–4 与默认入口切换。
- [ ] 保留旧 WPF 页面进入稳定观察期；确认托盘、通知和原生短流程长期无回归后再删除。

## 11. 实施调整记录

### 11.1 前端文件组织

- 原计划：按应用壳、页面和样式职责拆分目录。
- 实际调整：首轮曾把页面集中实现；成熟度收口时已将桌面框架迁入 `components/AppChrome.tsx`，四个业务页面分别迁入独立文件，`WorkspacePages.tsx` 仅保留稳定导出，样式继续集中在 `styles.css`。
- 原因：保留现有桥接和页面内部状态边界，同时消除 2000 行以上聚合模块及重复应用框架代码，使页面可以独立渲染测试。
- 验证：TypeScript 严格检查、3 个测试文件共 10 项测试、Vite 生产构建和真实 WebView2 自动烟测通过。

### 11.2 C# Bridge 处理器组织

- 原计划：按 Task/Summary/History/Pomodoro/Settings 建立五个 Handler 文件。
- 实际调整：首轮使用一个白名单 `WebBridgeService` 集中分发，协议契约与 DTO 仍独立文件维护。
- 原因：所有处理器复用同一数据库、主 ViewModel、设置服务和 WebView2 生命周期；集中实现避免首轮迁移引入额外依赖注入层。
- 验证：C# 契约测试覆盖握手、请求 id、未知方法、无效参数、DTO 查询和取消完成时间线。

### 11.3 取消完成历史

- 原计划：历史页增加可靠事件时间线，同时每日总结只保留当前有效完成。
- 实际调整：新增只追加 `TaskActivityRecords`，在同一 SQLite 事务中记录创建、编辑、移动、完成、取消完成和删除；旧数据仅回填可证明事实并写入迁移边界。
- 原因：`LastModifiedAt` 无法证明取消完成的真实发生时间，继续投影会损害历史可信度；幂等迁移则兼顾旧库兼容。
- 验证：数据库回归断言重复初始化不增加回填数量、取消完成保留独立事件且不再进入每日总结。

## 12. 2026-07-28 验证证据

- `npm test`：3 个测试文件、10 个测试全部通过。

### 2026-07-29 成熟度收口

- `App.tsx` 已改为只负责应用级导航、全局浮层和错误边界；桌面框架移入 `components/AppChrome.tsx`，共享 UI 移入 `ui.tsx`，四个业务页面已拆成独立文件并由 `pages/WorkspacePages.tsx` 统一导出。
- `WebBridgeService` 已按核心协议、总结/历史和设置/数据副作用拆分，白名单路由仍保持唯一入口。
- 历史页已切换为只追加活动日志，不再使用 `LastModifiedAt` 推导取消完成。
- 新增备份恢复、崩溃诊断、休眠同步计时、发布脚本和安装定义；完整证据见成熟度审计实施记录。
- `npm run build`：TypeScript 检查与 Vite 生产构建通过。
- `dotnet build Taskato.sln -c Release`：0 错误；保留现有第三方 `SQLitePCLRaw` 的 1 项 `NETSDK1206` RID 警告。
- `dotnet run --project src/TestProject/TestProject.csproj -c Release --no-build`：整窗 XAML、任务树、级联完成、有效总结、历史、桥接契约、层级限制和删除提升全部通过。
- Release 输出中的 `Web/index.html` 已引用本次最新哈希资源。
- 浏览器 1440 × 1024、1100 × 800、960 × 640 均无横向溢出，控制台无错误。
- `scripts/build-release.ps1 -Version 2.20.0`：前后端检查、C# 回归、自包含发布、真实 WebView2 自动烟测、ZIP、SHA-256 与清单均通过。
- 隔离数据目录启动 Release `Taskato.exe`，自动断言 React 根节点、任务入口、WebView2 消息对象与无错误 Toast；WebView2 用户数据不会污染发布目录。
