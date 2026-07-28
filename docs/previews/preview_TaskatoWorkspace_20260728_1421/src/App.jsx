import { useEffect, useMemo, useState } from "react";
import {
  ArrowsOut, Bell, CalendarBlank, CaretDown, CaretLeft, CaretRight, CaretUpDown,
  Check, CheckCircle, Circle, ClockCounterClockwise, Command, Copy, DotsThree,
  DownloadSimple, GearSix, House, ListChecks, MagnifyingGlass, Minus, NotePencil,
  Pause, Play, Plus, SidebarSimple, SlidersHorizontal, Sparkle, Stop, Tag, Target,
  Timer, X,
} from "@phosphor-icons/react";

const navItems = [
  { id: "tasks", label: "任务", icon: ListChecks },
  { id: "summary", label: "每日总结", icon: CalendarBlank },
  { id: "history", label: "历史记录", icon: ClockCounterClockwise },
  { id: "settings", label: "设置", icon: GearSix },
];

const taskTree = [
  { id: 1, title: "发布 Taskato 3.0", progress: "3/6", priority: "high", children: [
    { id: 2, title: "确定新版信息架构", completed: true },
    { id: 3, title: "重构任务工作区", progress: "1/3", priority: "high", children: [
      { id: 4, title: "完成任务树交互", completed: true },
      { id: 5, title: "实现详情编辑面板" },
      { id: 6, title: "补充键盘快捷键" },
    ] },
    { id: 7, title: "整理发布说明" },
  ] },
  { id: 8, title: "个人学习计划", progress: "2/4", children: [
    { id: 9, title: "阅读 WPF 数据绑定章节", completed: true },
    { id: 10, title: "整理 MVVM 学习笔记" },
  ] },
  { id: 11, title: "周末采购", progress: "0/3" },
];

const taskLookup = new Map();
function indexTasks(tasks, parent = null) {
  tasks.forEach((task) => {
    taskLookup.set(task.id, { ...task, parent });
    if (task.children) indexTasks(task.children, task.title);
  });
}
indexTasks(taskTree);

const initialSubtasks = [
  { id: 4, title: "完成任务树交互", completed: true, duration: "42 分钟" },
  { id: 5, title: "实现详情编辑面板", completed: false, duration: "进行中" },
  { id: 6, title: "补充键盘快捷键", completed: false, duration: "未开始" },
];

const summaryGroups = [
  { title: "发布 Taskato 3.0", meta: "产品开发 · 3 项成果", items: [
    { title: "完成任务树交互", time: "10:18", type: "叶子任务", depth: 2, duration: "42 分钟" },
    { title: "重构任务工作区", time: "10:18", type: "父级成果", depth: 1 },
    { title: "确定新版信息架构", time: "09:05", type: "叶子任务", depth: 1, duration: "28 分钟" },
  ] },
  { title: "个人学习计划", meta: "成长计划 · 1 项成果", items: [
    { title: "阅读 WPF 数据绑定章节", time: "13:42", type: "叶子任务", depth: 1, duration: "55 分钟" },
  ] },
];

function IconButton({ label, children, onClick, className = "" }) {
  return <button className={`icon-button ${className}`} aria-label={label} title={label} onClick={onClick}>{children}</button>;
}

function AppNav({ activePage, onNavigate, collapsed, onToggle }) {
  return (
    <aside className={`app-nav ${collapsed ? "collapsed" : ""}`}>
      <div className="brand-row">
        <div className="brand-mark"><Sparkle size={19} weight="fill" /></div>
        {!collapsed && <strong>Taskato</strong>}
        <IconButton label={collapsed ? "展开导航" : "收起导航"} onClick={onToggle} className="nav-collapse"><SidebarSimple size={19} /></IconButton>
      </div>
      <nav aria-label="主要导航">
        {navItems.map(({ id, label, icon: Icon }) => (
          <button key={id} className={`nav-item ${activePage === id ? "active" : ""}`} onClick={() => onNavigate(id)} title={collapsed ? label : undefined}>
            <Icon size={20} weight={activePage === id ? "fill" : "regular"} />
            {!collapsed && <span>{label}</span>}
          </button>
        ))}
      </nav>
      <div className="nav-spacer" />
      <div className="focus-summary"><Timer size={20} />{!collapsed && <div><strong>42 分钟</strong><span>今日专注</span></div>}</div>
      <button className="profile-row" title={collapsed ? "用户菜单" : undefined}>
        <span className="avatar">T</span>
        {!collapsed && <><span className="profile-copy"><strong>本地用户</strong><small>数据已保存</small></span><CaretUpDown size={15} /></>}
      </button>
    </aside>
  );
}

function TopBar({ activePage, onFullscreen }) {
  const title = navItems.find((item) => item.id === activePage)?.label ?? "任务";
  return (
    <header className="top-bar">
      <div><span className="eyebrow">2026 年 7 月 28 日 · 星期二</span><h1>{title}</h1></div>
      <div className="top-actions">
        <button className="search-command"><MagnifyingGlass size={17} /><span>搜索任务与记录</span><kbd>Ctrl K</kbd></button>
        <IconButton label="通知"><Bell size={19} /></IconButton><span className="window-divider" />
        <IconButton label="最小化"><Minus size={18} /></IconButton>
        <IconButton label="全屏预览" onClick={onFullscreen}><ArrowsOut size={18} /></IconButton>
        <IconButton label="关闭预览"><X size={18} /></IconButton>
      </div>
    </header>
  );
}

function TaskTreeRow({ task, depth, selectedId, expanded, onToggle, onSelect }) {
  const hasChildren = Boolean(task.children?.length);
  const isExpanded = expanded.has(task.id);
  return <>
    <div className={`tree-row ${selectedId === task.id ? "selected" : ""}`} style={{ "--depth": depth }}>
      <button className="tree-expand" aria-label={hasChildren ? (isExpanded ? "收起子任务" : "展开子任务") : "无子任务"} onClick={() => hasChildren && onToggle(task.id)}>
        {hasChildren ? <CaretRight className={isExpanded ? "rotated" : ""} size={14} /> : <span />}
      </button>
      <button className={`task-check ${task.completed ? "checked" : ""}`} aria-label={task.completed ? "取消完成" : "标记完成"}>{task.completed && <Check size={12} weight="bold" />}</button>
      <button className="tree-title" onClick={() => onSelect(task.id)}>{task.title}</button>
      {task.priority === "high" && <span className="priority-mark" title="高优先级" />}
      {task.progress && <span className="tree-progress">{task.progress}</span>}
    </div>
    {hasChildren && isExpanded && task.children.map((child) => <TaskTreeRow key={child.id} task={child} depth={depth + 1} selectedId={selectedId} expanded={expanded} onToggle={onToggle} onSelect={onSelect} />)}
  </>;
}

function TaskPage() {
  const [selectedId, setSelectedId] = useState(3);
  const [expanded, setExpanded] = useState(new Set([1, 3, 8]));
  const [newTask, setNewTask] = useState("");
  const [items, setItems] = useState(initialSubtasks);
  const [timerRunning, setTimerRunning] = useState(true);
  const [seconds, setSeconds] = useState(19 * 60 + 36);
  const selected = taskLookup.get(selectedId) ?? taskLookup.get(3);
  useEffect(() => {
    if (!timerRunning) return undefined;
    const timerId = window.setInterval(() => setSeconds((value) => Math.max(0, value - 1)), 1000);
    return () => window.clearInterval(timerId);
  }, [timerRunning]);
  const time = `${String(Math.floor(seconds / 60)).padStart(2, "0")}:${String(seconds % 60).padStart(2, "0")}`;
  const toggleExpanded = (id) => setExpanded((current) => { const next = new Set(current); next.has(id) ? next.delete(id) : next.add(id); return next; });
  const addSubtask = (event) => {
    event.preventDefault();
    if (!newTask.trim()) return;
    setItems((current) => [...current, { id: Date.now(), title: newTask.trim(), completed: false, duration: "未开始" }]);
    setNewTask("");
  };
  return (
    <div className="task-workspace page-enter" data-testid="tasks-page">
      <section className="tree-pane">
        <div className="pane-heading"><div><span className="section-kicker">工作空间</span><h2>项目与任务</h2></div><IconButton label="新建顶级任务"><Plus size={18} /></IconButton></div>
        <label className="pane-search"><MagnifyingGlass size={16} /><input aria-label="筛选任务" placeholder="筛选任务" /></label>
        <div className="tree-filter-row"><button className="filter-chip active">进行中</button><button className="filter-chip">全部</button><span>3 个项目</span></div>
        <div className="task-tree">{taskTree.map((task) => <TaskTreeRow key={task.id} task={task} depth={0} selectedId={selectedId} expanded={expanded} onToggle={toggleExpanded} onSelect={setSelectedId} />)}</div>
        <button className="subtle-action"><Plus size={15} />新建项目</button>
      </section>
      <main className="task-canvas">
        <div className="canvas-header"><div className="breadcrumbs"><span>任务</span><CaretRight size={13} /><span>{selected.parent || selected.title}</span></div><IconButton label="更多操作"><DotsThree size={20} /></IconButton></div>
        <div className="task-title-block"><button className="task-check large" aria-label="标记当前任务完成" /><div><h2>{selected.title}</h2><p>让任务结构在大屏空间里清晰呈现，同时保持快速录入与专注执行。</p></div></div>
        <div className="meta-strip"><button><Target size={16} />高优先级</button><button><CalendarBlank size={16} />今天</button><button><Tag size={16} />产品开发</button></div>
        <section className="content-section">
          <div className="section-title-row"><div><h3>子任务</h3><span>{items.filter((item) => item.completed).length}/{items.length} 已完成</span></div><button className="text-action"><SlidersHorizontal size={16} />排序</button></div>
          <div className="progress-track"><span style={{ width: `${items.length ? (items.filter((item) => item.completed).length / items.length) * 100 : 0}%` }} /></div>
          <div className="subtask-list">{items.map((item) => <div className="subtask-row" key={item.id}>
            <button className={`task-check ${item.completed ? "checked" : ""}`} onClick={() => setItems((current) => current.map((entry) => entry.id === item.id ? { ...entry, completed: !entry.completed } : entry))} aria-label={item.completed ? "取消完成" : "标记完成"}>{item.completed && <Check size={12} weight="bold" />}</button>
            <span className={item.completed ? "done" : ""}>{item.title}</span><small>{item.duration}</small><IconButton label="子任务菜单"><DotsThree size={18} /></IconButton>
          </div>)}</div>
          <form className="inline-add" onSubmit={addSubtask}><button type="submit" aria-label="保存子任务"><Plus size={16} /></button><input value={newTask} onChange={(event) => setNewTask(event.target.value)} placeholder="添加子任务，按 Enter 保存" aria-label="添加子任务" /></form>
        </section>
        <section className="content-section notes-section"><div className="section-title-row"><h3>任务笔记</h3><span>自动保存</span></div><textarea defaultValue="重点检查任务树在深层嵌套时的缩进、选中状态，以及新增子任务输入框的焦点归属。" aria-label="任务笔记" /></section>
      </main>
      <aside className="inspector-pane">
        <section className="focus-panel"><div className="focus-heading"><div><Timer size={19} /><span>当前专注</span></div><span className="live-label">运行中</span></div><strong className="timer-value">{time}</strong><p>实现详情编辑面板</p><div className="timer-progress"><span style={{ width: `${(seconds / 1500) * 100}%` }} /></div><div className="timer-controls"><button className="primary-control" onClick={() => setTimerRunning((value) => !value)}>{timerRunning ? <Pause size={18} weight="fill" /> : <Play size={18} weight="fill" />}{timerRunning ? "暂停" : "继续"}</button><IconButton label="停止计时" onClick={() => { setTimerRunning(false); setSeconds(25 * 60); }}><Stop size={17} weight="fill" /></IconButton></div></section>
        <section className="details-panel"><div className="section-title-row"><h3>任务详情</h3><button className="text-action"><NotePencil size={16} />编辑</button></div><dl><div><dt>状态</dt><dd><span className="status-dot active" />进行中</dd></div><div><dt>优先级</dt><dd>高</dd></div><div><dt>计划日期</dt><dd>2026-07-28</dd></div><div><dt>累计专注</dt><dd>1 小时 26 分</dd></div><div><dt>创建时间</dt><dd>7 月 24 日 14:36</dd></div></dl></section>
        <section className="activity-panel"><div className="section-title-row"><h3>最近动态</h3><button className="text-action">查看全部</button></div><div className="activity-item"><CheckCircle size={17} /><div><span>完成“任务树交互”</span><small>42 分钟前</small></div></div><div className="activity-item"><NotePencil size={17} /><div><span>更新任务说明</span><small>今天 09:48</small></div></div></section>
      </aside>
    </div>
  );
}

function SummaryPage({ onOpenBackfill }) {
  return <div className="full-page summary-page page-enter" data-testid="summary-page">
    <div className="page-heading-row"><div><span className="section-kicker">回顾今天的有效成果</span><h2>每日总结</h2><p>已取消完成的任务仅保留在历史记录中，不计入这里。</p></div><div className="heading-actions"><button className="secondary-button"><Copy size={17} />复制总结</button><button className="primary-button" onClick={onOpenBackfill}><Plus size={17} />补充任务</button></div></div>
    <div className="date-toolbar"><IconButton label="前一天"><CaretLeft size={18} /></IconButton><button className="date-picker"><CalendarBlank size={18} /><strong>2026 年 7 月 28 日</strong><span>今天</span></button><IconButton label="后一天"><CaretRight size={18} /></IconButton></div>
    <div className="summary-layout"><section className="summary-main">
      <div className="summary-stats"><div><span>有效完成</span><strong>4</strong><small>3 个叶子任务</small></div><div><span>父级成果</span><strong>1</strong><small>自动汇总达成</small></div><div><span>专注时长</span><strong>2h 05m</strong><small>比昨日多 18 分钟</small></div></div>
      <div className="achievement-list">{summaryGroups.map((group) => <article className="achievement-group" key={group.title}><header><div><CaretDown size={16} /><div><h3>{group.title}</h3><p>{group.meta}</p></div></div><IconButton label="成果菜单"><DotsThree size={19} /></IconButton></header><div className="achievement-rows">{group.items.map((item) => <div className="achievement-row" key={`${group.title}-${item.title}`} style={{ "--indent": item.depth }}><CheckCircle size={18} weight="fill" /><div><strong>{item.title}</strong><span>{item.type}</span></div>{item.duration && <small>{item.duration}</small>}<time>{item.time}</time></div>)}</div></article>)}</div>
    </section><aside className="summary-side"><section><span className="section-kicker">一句话总结</span><h3>今天推进了核心重构</h3><p>任务结构与工作区设计已经进入实现阶段，学习计划也保持了稳定进度。</p></section><section><div className="section-title-row"><h3>专注分布</h3><span>125 分钟</span></div><div className="focus-bars"><div><span>产品开发</span><i><b style={{ width: "68%" }} /></i><small>85m</small></div><div><span>学习成长</span><i><b style={{ width: "32%" }} /></i><small>40m</small></div></div></section><button className="export-button"><DownloadSimple size={18} />导出今日总结</button></aside></div>
  </div>;
}

function HistoryPage() {
  const rows = [
    ["14:06", "取消完成", "实现详情编辑面板", "状态已恢复为进行中", "neutral"],
    ["13:42", "完成任务", "阅读 WPF 数据绑定章节", "专注 55 分钟", "success"],
    ["11:26", "更新任务", "重构任务工作区", "修改计划日期", "neutral"],
    ["10:18", "完成任务", "完成任务树交互", "同时达成父级成果", "success"],
    ["09:05", "完成任务", "确定新版信息架构", "专注 28 分钟", "success"],
  ];
  return <div className="full-page history-page page-enter" data-testid="history-page"><div className="page-heading-row"><div><span className="section-kicker">完整操作轨迹</span><h2>历史记录</h2><p>完成、取消、编辑和层级变化都会保留在日志中。</p></div><button className="secondary-button"><DownloadSimple size={17} />导出记录</button></div><div className="history-toolbar"><label className="wide-search"><MagnifyingGlass size={17} /><input placeholder="搜索任务或操作" /></label><button className="select-button">全部类型<CaretDown size={14} /></button><button className="select-button"><CalendarBlank size={16} />今天<CaretDown size={14} /></button></div><section className="history-table"><header><span>时间</span><span>事件</span><span>任务</span><span>说明</span><span /></header>{rows.map(([time, event, task, detail, tone]) => <div className="history-row" key={`${time}-${task}`}><time>{time}</time><span className={`event-label ${tone}`}>{event}</span><strong>{task}</strong><span>{detail}</span><IconButton label="查看详情"><CaretRight size={16} /></IconButton></div>)}</section></div>;
}

function SettingsPage() {
  const [launch, setLaunch] = useState(true);
  const [sound, setSound] = useState(true);
  return <div className="full-page settings-page page-enter" data-testid="settings-page"><div className="page-heading-row"><div><span className="section-kicker">个性化工作方式</span><h2>设置</h2><p>调整外观、番茄钟和应用行为。</p></div></div><div className="settings-layout"><aside className="settings-nav"><button className="active"><House size={17} />通用</button><button><Timer size={17} />番茄钟</button><button><Sparkle size={17} />外观主题</button><button><Command size={17} />快捷键</button></aside><main className="settings-content"><section><div><h3>应用行为</h3><p>控制 Taskato 启动和提醒方式。</p></div><div className="setting-row"><div><strong>开机后自动启动</strong><span>登录系统后在后台打开 Taskato</span></div><button className={`switch ${launch ? "on" : ""}`} onClick={() => setLaunch((value) => !value)} aria-label="开机后自动启动" aria-pressed={launch}><span /></button></div><div className="setting-row"><div><strong>完成提示音</strong><span>任务完成和番茄钟结束时播放提示音</span></div><button className={`switch ${sound ? "on" : ""}`} onClick={() => setSound((value) => !value)} aria-label="完成提示音" aria-pressed={sound}><span /></button></div></section><section><div><h3>默认任务视图</h3><p>启动应用时优先进入的任务范围。</p></div><div className="segmented"><button className="active">进行中</button><button>今天</button><button>全部任务</button></div></section><section><div><h3>数据存储</h3><p>所有数据都保存在当前设备中。</p></div><div className="setting-row"><div><strong>本地数据库</strong><span>上次保存：今天 14:18</span></div><button className="secondary-button">打开数据位置</button></div></section></main></div></div>;
}

function BackfillDialog({ onClose }) {
  const [selected, setSelected] = useState("实现详情编辑面板");
  return <div className="modal-backdrop" role="presentation" onMouseDown={(event) => event.target === event.currentTarget && onClose()}><section className="modal" role="dialog" aria-modal="true" aria-labelledby="backfill-title"><header><div><span className="section-kicker">2026 年 7 月 28 日</span><h2 id="backfill-title">补充任务</h2></div><IconButton label="关闭" onClick={onClose}><X size={19} /></IconButton></header><p>选择一个未完成的叶子任务，将它补充为当天完成。父级成果会根据子任务状态自动更新。</p><label className="field-label">选择任务</label><div className="task-choice-list">{["实现详情编辑面板", "补充键盘快捷键", "整理发布说明"].map((title) => <button key={title} className={selected === title ? "selected" : ""} onClick={() => setSelected(title)}><span className="radio-mark">{selected === title && <Circle size={8} weight="fill" />}</span><div><strong>{title}</strong><small>发布 Taskato 3.0</small></div></button>)}</div><div className="modal-fields"><label><span>完成时间</span><input defaultValue="14:21" /></label><label><span>专注时长（分钟）</span><input defaultValue="35" /></label></div><footer><button className="secondary-button" onClick={onClose}>取消</button><button className="primary-button" onClick={onClose}><Check size={17} />确认补充</button></footer></section></div>;
}

export function App() {
  const [activePage, setActivePage] = useState("tasks");
  const [navCollapsed, setNavCollapsed] = useState(false);
  const [showBackfill, setShowBackfill] = useState(false);
  const page = useMemo(() => {
    if (activePage === "summary") return <SummaryPage onOpenBackfill={() => setShowBackfill(true)} />;
    if (activePage === "history") return <HistoryPage />;
    if (activePage === "settings") return <SettingsPage />;
    return <TaskPage />;
  }, [activePage]);
  const toggleFullscreen = async () => {
    if (!document.fullscreenElement) await document.documentElement.requestFullscreen?.();
    else await document.exitFullscreen?.();
  };
  return <div className={`app-shell ${navCollapsed ? "nav-is-collapsed" : ""}`}><AppNav activePage={activePage} onNavigate={setActivePage} collapsed={navCollapsed} onToggle={() => setNavCollapsed((value) => !value)} /><div className="app-main"><TopBar activePage={activePage} onFullscreen={toggleFullscreen} />{page}</div>{showBackfill && <BackfillDialog onClose={() => setShowBackfill(false)} />}</div>;
}
