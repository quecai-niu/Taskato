# Taskato 方案一设计 QA

- source visual truth: `docs/previews/preview_TaskatoUiDirections_20260729_1313.html` 的“1 · 清晰工作台”
- source capture: `docs/audits/review_TaskatoUiOption1_20260729_1336/source-normalized.png`
- implementation: `http://127.0.0.1:4173/`
- implementation capture: `docs/audits/review_TaskatoUiOption1_20260729_1336/implementation-normalized.png`
- combined comparison: `docs/audits/review_TaskatoUiOption1_20260729_1336/comparison.png`
- viewport: 浏览器外框 1280 × 820；页面 CSS 可视区 1280 × 720
- pixels and density: 常规截图 1280 × 720；规范化对比图均为 1280 × 766；浏览器报告 `devicePixelRatio=1.5`，截图接口已归一化为 CSS 像素尺寸
- state: 任务页、夜间模式、父任务组展开、首个父任务选中

## Full-view comparison evidence

- 信息架构一致：左侧应用导航、任务树、中央详情、右侧专注面板均保持四区工作台关系。
- 正式实现将根任务进一步分为“任务组”和“未归类任务”，这是为解决真实数据中独立顶级任务与父任务混排而做的业务增强，没有改变父子数据。
- 标题栏搜索框相对标题栏中心偏差为 3px；日夜切换、通知和窗口按钮位于右侧。
- 默认强调色继续使用用户保存的主题色，而不是强制覆盖为设计稿珊瑚色，这是保留原有六套主题功能的有意差异。

## Focused-region comparison evidence

- 父任务选择器：`implementation-parent-picker.png`。原生大下拉框已替换为宽度受控、可搜索的树形浮层；测试非空任务时显示 5 个选项，输入“发布”后缩减为 2 个选项。
- 日间模式：`implementation-light.png`。使用米黄色背景、深褐灰文字和非纯白内容面板。
- 设置页：`implementation-settings-fixed.png`。外观分段按钮、主题色、番茄钟步进器保持可见且横向排列。
- 图像与图标：页面无内容型图片；导航和操作图标继续使用 Phosphor 图标组件，未用临时占位图形替代。

## Findings

当前没有遗留的 P0、P1 或 P2 问题。

### Typography

- 中文使用系统无衬线字体栈，标题、父任务、正文和辅助文字形成明确字重层级。
- 长任务名称使用省略或受控换行，当前 1280px 视口未出现文字相互覆盖。

### Spacing and layout rhythm

- 三栏继续支持可拖动分隔线；中央区域保留 360px 最小宽度。
- 任务组、未归类任务、子任务引导线和内容分区均有独立间距，不再依靠同一种背景表达全部状态。

### Colors and visual tokens

- 夜间文本与结构边界对比度高于旧界面；选中状态同时使用强调色竖线、背景和文字。
- 日间模式避免纯白大面积背景，控件仍通过边框、深色文字和聚焦环保持辨识度。

### Copy and content

- “任务组”“未归类任务”“当前父任务”“设为顶级任务”分别表达不同业务含义。
- 优先级由无说明红点改为“低/中/高”文字标签。

## Comparison history

1. 首轮发现设置页 `.setting-row > div` 规则把外观分段按钮和番茄钟步进器挤成纵向，判定为 P2。
2. 增加设置控件定向布局规则，复核截图 `implementation-settings-fixed.png` 显示两个控件均恢复横向排列。
3. 第二轮发现标题栏切换到日间后，已打开的设置页仍显示“夜间”选中，判定为 P2。
4. 设置页订阅 `settings.changed`，外观模式改为即时单字段保存；浏览器实测标题栏切换后设置页显示“日间”，设置页切回夜间后标题栏恢复“切换到日间模式”。

## Primary interactions and console

- 已测试：任务层级展开、任务选择、父任务树打开、父任务搜索、标题栏日夜切换、设置页日夜切换和设置页导航。
- 浏览器控制台错误：0。

## Follow-up polish

- P3：可在后续根据用户长期使用反馈微调任务树默认宽度，但当前宽度已经可以拖动并持久化。

final result: passed
