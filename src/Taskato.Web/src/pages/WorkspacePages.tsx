import {
  useCallback,
  useEffect,
  useMemo,
  useRef,
  useState,
  type FormEvent,
  type MouseEvent,
  type ReactNode,
} from "react";
import {
  Bell,
  ArrowCounterClockwise,
  CalendarBlank,
  CaretDown,
  CaretLeft,
  CaretRight,
  Check,
  CheckCircle,
  ClockCounterClockwise,
  Copy,
  DotsThree,
  DownloadSimple,
  GearSix,
  ListChecks,
  MagnifyingGlass,
  Minus,
  NotePencil,
  Pause,
  Play,
  Plus,
  SidebarSimple,
  Sparkle,
  Stop,
  Timer,
  Trash,
  X,
} from "@phosphor-icons/react";
import {
  bridge,
  type SettingsDto,
  type GlobalSearchResult,
  type SummaryResult,
  type TaskActivityDto,
  type TaskDto,
  type TaskListResult,
  type TimerListResult,
} from "../bridge";

type PageId = "tasks" | "summary" | "history" | "settings";
type TaskNode = TaskDto & { children: TaskNode[] };

const navItems = [
  { id: "tasks" as const, label: "任务", icon: ListChecks },
  { id: "summary" as const, label: "每日总结", icon: CalendarBlank },
  { id: "history" as const, label: "历史记录", icon: ClockCounterClockwise },
  { id: "settings" as const, label: "设置", icon: GearSix },
];

function toLocalDateInput(date = new Date()): string {
  const year = date.getFullYear();
  const month = String(date.getMonth() + 1).padStart(2, "0");
  const day = String(date.getDate()).padStart(2, "0");
  return `${year}-${month}-${day}`;
}

function applyTheme(
  settings: Pick<SettingsDto, "themePrimaryColor" | "themeSecondaryColor">,
): void {
  document.documentElement.style.setProperty(
    "--primary",
    settings.themePrimaryColor,
  );
  document.documentElement.style.setProperty(
    "--secondary",
    settings.themeSecondaryColor,
  );
}

function IconButton({
  label,
  children,
  onClick,
  className = "",
  disabled = false,
}: {
  label: string;
  children: ReactNode;
  onClick?: () => void;
  className?: string;
  disabled?: boolean;
}) {
  return (
    <button
      className={`icon-button ${className}`}
      aria-label={label}
      title={label}
      onClick={onClick}
      disabled={disabled}
    >
      {children}
    </button>
  );
}

function useBridgeRefresh(eventName: string, refresh: () => void): void {
  useEffect(() => bridge.subscribe(eventName, refresh), [eventName, refresh]);
}

function buildTree(tasks: TaskDto[]): TaskNode[] {
  const nodes = new Map(
    tasks.map((task) => [task.id, { ...task, children: [] } as TaskNode]),
  );
  const roots: TaskNode[] = [];
  nodes.forEach((node) => {
    const parent = node.parentId == null ? null : nodes.get(node.parentId);
    if (parent) parent.children.push(node);
    else roots.push(node);
  });
  return roots;
}

function AppNav({
  activePage,
  onNavigate,
  collapsed,
  onToggle,
}: {
  activePage: PageId;
  onNavigate(page: PageId): void;
  collapsed: boolean;
  onToggle(): void;
}) {
  return (
    <aside className={`app-nav ${collapsed ? "collapsed" : ""}`}>
      <div className="brand-row">
        <div className="brand-mark">
          <Sparkle size={19} weight="fill" />
        </div>
        {!collapsed && <strong>Taskato</strong>}
        <IconButton
          label={collapsed ? "展开导航" : "收起导航"}
          onClick={onToggle}
          className="nav-collapse"
        >
          <SidebarSimple size={19} />
        </IconButton>
      </div>
      <nav aria-label="主要导航">
        {navItems.map(({ id, label, icon: Icon }) => (
          <button
            key={id}
            className={`nav-item ${activePage === id ? "active" : ""}`}
            onClick={() => onNavigate(id)}
            title={collapsed ? label : undefined}
          >
            <Icon size={20} weight={activePage === id ? "fill" : "regular"} />
            {!collapsed && <span>{label}</span>}
          </button>
        ))}
      </nav>
      <div className="nav-spacer" />
      <div className="focus-summary">
        <Timer size={20} />
        {!collapsed && (
          <div>
            <strong>本地专注</strong>
            <span>C# 计时器</span>
          </div>
        )}
      </div>
      <div className="profile-row">
        <span className="avatar">T</span>
        {!collapsed && (
          <span className="profile-copy">
            <strong>本地用户</strong>
            <small>{bridge.isNative ? "数据已连接" : "浏览器预览"}</small>
          </span>
        )}
      </div>
    </aside>
  );
}

function TopBar({
  activePage,
  onSearch,
  onNotifications,
}: {
  activePage: PageId;
  onSearch(): void;
  onNotifications(): void;
}) {
  const title =
    navItems.find((item) => item.id === activePage)?.label ?? "任务";
  const drag = (event: MouseEvent<HTMLElement>) => {
    if ((event.target as HTMLElement).closest("button,input")) return;
    void bridge.request("window.drag");
  };
  return (
    <header className="top-bar" onMouseDown={drag}>
      <div>
        <span className="eyebrow">
          {new Intl.DateTimeFormat("zh-CN", { dateStyle: "full" }).format(
            new Date(),
          )}
        </span>
        <h1>{title}</h1>
      </div>
      <div className="top-actions">
        <button className="search-command" onClick={onSearch}>
          <MagnifyingGlass size={17} />
          <span>搜索任务与记录</span>
          <kbd>Ctrl K</kbd>
        </button>
        <IconButton label="通知设置" onClick={onNotifications}>
          <Bell size={19} />
        </IconButton>
        <span className="window-divider" />
        <IconButton
          label="最小化"
          onClick={() => void bridge.request("window.minimize")}
        >
          <Minus size={18} />
        </IconButton>
        <IconButton
          label="最大化或还原"
          onClick={() => void bridge.request("window.maximize")}
        >
          <SidebarSimple size={18} />
        </IconButton>
        <IconButton
          label="关闭到托盘"
          onClick={() => void bridge.request("window.close")}
        >
          <X size={18} />
        </IconButton>
      </div>
    </header>
  );
}

function GlobalSearchDialog({
  open,
  onClose,
  onOpenTask,
  onOpenHistory,
}: {
  open: boolean;
  onClose(): void;
  onOpenTask(taskId: number): void;
  onOpenHistory(): void;
}) {
  const [query, setQuery] = useState("");
  const [result, setResult] = useState<GlobalSearchResult>({
    tasks: [],
    activities: [],
  });
  const [status, setStatus] = useState<"idle" | "loading" | "ready" | "error">(
    "idle",
  );

  useEffect(() => {
    if (!open) {
      setQuery("");
      setResult({ tasks: [], activities: [] });
      setStatus("idle");
      return;
    }
    if (query.trim().length < 2) {
      setResult({ tasks: [], activities: [] });
      setStatus("idle");
      return;
    }
    const timer = window.setTimeout(() => {
      setStatus("loading");
      void bridge
        .request<GlobalSearchResult>("search.global", {
          keyword: query.trim(),
        })
        .then((value) => {
          setResult(value);
          setStatus("ready");
        })
        .catch(() => setStatus("error"));
    }, 220);
    return () => window.clearTimeout(timer);
  }, [open, query]);

  useEffect(() => {
    if (!open) return;
    const closeOnEscape = (event: KeyboardEvent) => {
      if (event.key === "Escape") onClose();
    };
    window.addEventListener("keydown", closeOnEscape);
    return () => window.removeEventListener("keydown", closeOnEscape);
  }, [onClose, open]);

  if (!open) return null;
  const hasResults = result.tasks.length > 0 || result.activities.length > 0;
  return (
    <div className="modal-backdrop command-backdrop" onMouseDown={onClose}>
      <section
        className="command-palette"
        role="dialog"
        aria-modal="true"
        aria-label="全局搜索"
        onMouseDown={(event) => event.stopPropagation()}
      >
        <label className="command-input">
          <MagnifyingGlass size={19} />
          <input
            autoFocus
            type="search"
            placeholder="搜索任务标题、笔记和历史记录…"
            value={query}
            onChange={(event) => setQuery(event.target.value)}
          />
          <kbd>Esc</kbd>
        </label>
        <div className="command-results">
          {result.tasks.length > 0 && (
            <section>
              <h3>任务</h3>
              {result.tasks.map((task) => (
                <button key={task.id} onClick={() => onOpenTask(task.id)}>
                  <ListChecks size={17} />
                  <span>
                    <strong>{task.title}</strong>
                    <small>{task.notes || `第 ${task.depth + 1} 层任务`}</small>
                  </span>
                  <CaretRight size={15} />
                </button>
              ))}
            </section>
          )}
          {result.activities.length > 0 && (
            <section>
              <h3>历史记录</h3>
              {result.activities.map((activity) => (
                <button key={activity.id} onClick={onOpenHistory}>
                  <ClockCounterClockwise size={17} />
                  <span>
                    <strong>{activity.title}</strong>
                    <small>
                      {activity.eventLabel} · {activity.detail}
                    </small>
                  </span>
                  <CaretRight size={15} />
                </button>
              ))}
            </section>
          )}
          {status === "idle" && <p>输入至少两个字符开始搜索。</p>}
          {status === "loading" && <p>正在搜索本地任务和记录…</p>}
          {status === "error" && <p>搜索失败，请稍后重试。</p>}
          {status === "ready" && !hasResults && <p>没有找到匹配内容。</p>}
        </div>
      </section>
    </div>
  );
}

function NotificationCenter({
  open,
  onClose,
  onOpenHistory,
  onOpenSettings,
}: {
  open: boolean;
  onClose(): void;
  onOpenHistory(): void;
  onOpenSettings(): void;
}) {
  const [items, setItems] = useState<TaskActivityDto[]>([]);
  useEffect(() => {
    if (!open) return;
    const today = toLocalDateInput();
    void bridge
      .request<TaskActivityDto[]>("history.events", {
        startDate: today,
        endDate: today,
        eventFilter: "all",
        sortDesc: true,
      })
      .then((events) =>
        setItems(
          events
            .filter((item) =>
              ["completed", "reopened", "deleted"].includes(item.eventType),
            )
            .slice(0, 5),
        ),
      );
  }, [open]);
  if (!open) return null;
  return (
    <div className="notification-popover" role="dialog" aria-label="最近通知">
      <header>
        <div>
          <strong>最近通知</strong>
          <span>任务状态变化与桌面提醒</span>
        </div>
        <IconButton label="关闭通知中心" onClick={onClose}>
          <X size={16} />
        </IconButton>
      </header>
      <div className="notification-list">
        {items.length ? (
          items.map((item) => (
            <button key={item.id} onClick={onOpenHistory}>
              <CheckCircle size={17} />
              <span>
                <strong>{item.eventLabel} · {item.title}</strong>
                <small>{item.detail}</small>
              </span>
            </button>
          ))
        ) : (
          <p>今天还没有需要关注的任务通知。</p>
        )}
      </div>
      <footer>
        <button className="text-action" onClick={onOpenHistory}>
          查看完整历史
        </button>
        <button className="text-action" onClick={onOpenSettings}>
          提醒设置
        </button>
      </footer>
    </div>
  );
}

function TaskTreeRow({
  task,
  selectedId,
  expanded,
  onToggle,
  onSelect,
  onComplete,
}: {
  task: TaskNode;
  selectedId: number | null;
  expanded: Set<number>;
  onToggle(id: number): void;
  onSelect(id: number): void;
  onComplete(task: TaskDto): void;
}) {
  const hasChildren = task.children.length > 0;
  const isExpanded = expanded.has(task.id);
  return (
    <>
      <div
        className={`tree-row ${selectedId === task.id ? "selected" : ""}`}
        style={{ "--depth": task.depth } as React.CSSProperties}
      >
        <button
          className="tree-expand"
          aria-label={
            hasChildren
              ? isExpanded
                ? "收起子任务"
                : "展开子任务"
              : "无子任务"
          }
          onClick={() => hasChildren && onToggle(task.id)}
        >
          {hasChildren ? (
            <CaretRight className={isExpanded ? "rotated" : ""} size={14} />
          ) : (
            <span />
          )}
        </button>
        <button
          className={`task-check ${task.isCompleted ? "checked" : ""}`}
          aria-label={task.isCompleted ? "取消完成" : "标记完成"}
          onClick={() => onComplete(task)}
        >
          {task.isCompleted && <Check size={12} weight="bold" />}
        </button>
        <button className="tree-title" onClick={() => onSelect(task.id)}>
          {task.title}
        </button>
        {task.priority >= 2 && (
          <span className="priority-mark" title="高优先级" />
        )}
        {hasChildren && (
          <span className="tree-progress">
            {task.completedLeafCount}/{task.totalLeafCount}
          </span>
        )}
      </div>
      {hasChildren &&
        isExpanded &&
        task.children.map((child) => (
          <TaskTreeRow
            key={child.id}
            task={child}
            selectedId={selectedId}
            expanded={expanded}
            onToggle={onToggle}
            onSelect={onSelect}
            onComplete={onComplete}
          />
        ))}
    </>
  );
}

function CompletionDialog({
  task,
  onClose,
  onComplete,
}: {
  task: TaskDto;
  onClose(): void;
  onComplete(duration: number | null): void;
}) {
  const [duration, setDuration] = useState("");
  return (
    <div className="modal-backdrop">
      <section className="modal" role="dialog" aria-modal="true">
        <header>
          <div>
            <span className="section-kicker">完成叶子任务</span>
            <h2>{task.title}</h2>
          </div>
          <IconButton label="关闭" onClick={onClose}>
            <X size={19} />
          </IconButton>
        </header>
        <p>可以记录本次实际专注时长；全部子任务完成后，父级成果会自动生成。</p>
        <div className="modal-fields">
          <label>
            <span>专注时长（分钟，可选）</span>
            <input
              autoFocus
              type="number"
              min="1"
              value={duration}
              onChange={(event) => setDuration(event.target.value)}
            />
          </label>
        </div>
        <footer>
          <button className="secondary-button" onClick={onClose}>
            取消
          </button>
          <button
            className="primary-button"
            onClick={() => onComplete(duration ? Number(duration) : null)}
          >
            <Check size={17} />
            确认完成
          </button>
        </footer>
      </section>
    </div>
  );
}

export function TaskPage({
  reportError,
  requestedTaskId,
}: {
  reportError(error: unknown): void;
  requestedTaskId: number | null;
}) {
  const [tasks, setTasks] = useState<TaskDto[]>([]);
  const [selectedId, setSelectedId] = useState<number | null>(null);
  const [expanded, setExpanded] = useState(new Set<number>());
  const [filter, setFilter] = useState("");
  const [rootTitle, setRootTitle] = useState("");
  const [childTitle, setChildTitle] = useState("");
  const [titleDraft, setTitleDraft] = useState("");
  const [notesDraft, setNotesDraft] = useState("");
  const [notesStatus, setNotesStatus] = useState<
    "saved" | "unsaved" | "saving" | "error"
  >("saved");
  const [taskMenuOpen, setTaskMenuOpen] = useState(false);
  const [undoAction, setUndoAction] = useState<{
    message: string;
    run(): Promise<void>;
  } | null>(null);
  const notesRequestVersion = useRef(0);
  const [completionTask, setCompletionTask] = useState<TaskDto | null>(null);
  const [parentCandidates, setParentCandidates] = useState<TaskDto[]>([]);
  const [timerState, setTimerState] = useState<TimerListResult>({
    activeId: "",
    multipleEnabled: false,
    timers: [],
  });

  const load = useCallback(async () => {
    try {
      const result = await bridge.request<TaskListResult>("tasks.list");
      setTasks(result.tasks);
      setSelectedId((current) =>
        result.tasks.some((task) => task.id === current)
          ? current
          : (result.selectedTaskId ?? result.tasks[0]?.id ?? null),
      );
      setExpanded((current) =>
        current.size
          ? current
          : new Set(
              result.tasks
                .filter((task) => task.hasChildren)
                .map((task) => task.id),
            ),
      );
    } catch (error) {
      reportError(error);
    }
  }, [reportError]);
  const loadTimers = useCallback(async () => {
    try {
      setTimerState(await bridge.request<TimerListResult>("pomodoro.list"));
    } catch (error) {
      reportError(error);
    }
  }, [reportError]);
  useEffect(() => {
    void load();
    void loadTimers();
  }, [load, loadTimers]);
  useBridgeRefresh("tasks.changed", () => void load());
  useBridgeRefresh("pomodoro.changed", () => void loadTimers());
  useEffect(() => {
    if (requestedTaskId == null) return;
    setSelectedId(requestedTaskId);
    requestAnimationFrame(() =>
      document
        .querySelector<HTMLElement>(".task-title-input")
        ?.scrollIntoView({ block: "center" }),
    );
  }, [requestedTaskId]);
  useEffect(() => {
    if (selectedId == null) {
      setParentCandidates([]);
      return;
    }
    void bridge
      .request<TaskDto[]>("tasks.parentCandidates", { taskId: selectedId })
      .then(setParentCandidates)
      .catch(reportError);
  }, [reportError, selectedId, tasks]);

  const filtered = useMemo(
    () =>
      filter
        ? tasks.filter((task) =>
            task.title.toLowerCase().includes(filter.toLowerCase()),
          )
        : tasks,
    [filter, tasks],
  );
  const roots = useMemo(() => buildTree(filtered), [filtered]);
  const selected = tasks.find((task) => task.id === selectedId) ?? null;
  const children = tasks.filter((task) => task.parentId === selectedId);
  useEffect(
    () => {
      setTitleDraft(selected?.title ?? "");
      setNotesDraft(selected?.notes ?? "");
      setNotesStatus("saved");
    },
    [selected?.id, selected?.notes, selected?.title],
  );
  const activeTimer =
    timerState.timers.find((timer) => timer.id === timerState.activeId) ??
    timerState.timers[0];

  const add = async (event: FormEvent, parentId: number | null) => {
    event.preventDefault();
    const title = parentId == null ? rootTitle.trim() : childTitle.trim();
    if (!title) return;
    try {
      await bridge.request("tasks.add", { title, parentId });
      if (parentId == null) setRootTitle("");
      else setChildTitle("");
      await load();
    } catch (error) {
      reportError(error);
    }
  };
  const offerUndo = (message: string, run: () => Promise<void>) => {
    setUndoAction({ message, run });
    window.setTimeout(() => {
      setUndoAction((current) => (current?.message === message ? null : current));
    }, 6000);
  };
  const toggleComplete = (task: TaskDto) => {
    if (!task.isCompleted && task.hasChildren) {
      reportError(new Error("父任务会在全部叶子任务完成后自动达成。"));
      return;
    }
    if (task.isCompleted)
      return void bridge
        .request("tasks.toggle", { taskId: task.id, completed: false })
        .then(async () => {
          await load();
          offerUndo(`已取消完成“${task.title}”`, async () => {
            await bridge.request("tasks.toggle", {
              taskId: task.id,
              completed: true,
              durationMinutes: task.completionDurationMinutes,
            });
            await load();
          });
        })
        .catch(reportError);
    if (bridge.isNative)
      return void bridge
        .request<{ completed: boolean; cancelled: boolean }>(
          "tasks.completeDialog",
          { taskId: task.id },
        )
        .then(async (result) => {
          if (result.completed) {
            await load();
            offerUndo(`已完成“${task.title}”`, async () => {
              await bridge.request("tasks.toggle", {
                taskId: task.id,
                completed: false,
              });
              await load();
            });
          }
        })
        .catch(reportError);
    setCompletionTask(task);
  };
  const complete = async (durationMinutes: number | null) => {
    if (!completionTask) return;
    try {
      await bridge.request("tasks.toggle", {
        taskId: completionTask.id,
        completed: true,
        durationMinutes,
      });
      setCompletionTask(null);
      await load();
      const completedTask = completionTask;
      offerUndo(`已完成“${completedTask.title}”`, async () => {
        await bridge.request("tasks.toggle", {
          taskId: completedTask.id,
          completed: false,
        });
        await load();
      });
    } catch (error) {
      reportError(error);
    }
  };
  const updateTask = async (
    changes: { title?: string; priority?: number; notes?: string },
    undoChanges?: { title?: string; priority?: number; notes?: string },
    successMessage?: string,
  ) => {
    if (!selected) return;
    try {
      await bridge.request("tasks.update", { taskId: selected.id, ...changes });
      await load();
      if (undoChanges && successMessage) {
        const taskId = selected.id;
        offerUndo(successMessage, async () => {
          await bridge.request("tasks.update", { taskId, ...undoChanges });
          await load();
        });
      }
    } catch (error) {
      reportError(error);
    }
  };
  const deleteTask = async (mode: "deleteBranch" | "promoteChildren") => {
    if (
      !selected ||
      !window.confirm(`确定处理“${selected.title}”吗？完成历史会保留。`)
    )
      return;
    try {
      await bridge.request("tasks.delete", { taskId: selected.id, mode });
      await load();
    } catch (error) {
      reportError(error);
    }
  };
  const moveTask = async (parentId: number | null) => {
    if (!selected) return;
    const previousParentId = selected.parentId;
    const taskId = selected.id;
    try {
      await bridge.request("tasks.move", { taskId, parentId });
      await load();
      offerUndo(`已移动“${selected.title}”`, async () => {
        await bridge.request("tasks.move", {
          taskId,
          parentId: previousParentId,
        });
        await load();
      });
    } catch (error) {
      reportError(error);
    }
  };
  const timerCommand = async (command: string) => {
    try {
      setTimerState(
        await bridge.request<TimerListResult>("pomodoro.command", {
          command,
          timerId: activeTimer?.id,
        }),
      );
    } catch (error) {
      reportError(error);
    }
  };

  useEffect(() => {
    if (!selected || notesDraft === selected.notes) {
      setNotesStatus("saved");
      return;
    }
    setNotesStatus("unsaved");
    const version = ++notesRequestVersion.current;
    const timer = window.setTimeout(() => {
      setNotesStatus("saving");
      void bridge
        .request("tasks.update", {
          taskId: selected.id,
          notes: notesDraft,
        })
        .then(async () => {
          if (notesRequestVersion.current !== version) return;
          setNotesStatus("saved");
          await load();
        })
        .catch((error) => {
          if (notesRequestVersion.current !== version) return;
          setNotesStatus("error");
          reportError(error);
        });
    }, 700);
    return () => window.clearTimeout(timer);
  }, [load, notesDraft, reportError, selected?.id, selected?.notes]);

  return (
    <div className="task-workspace page-enter">
      <section className="tree-pane">
        <div className="pane-heading">
          <div>
            <span className="section-kicker">工作空间</span>
            <h2>项目与任务</h2>
          </div>
        </div>
        <label className="pane-search">
          <MagnifyingGlass size={16} />
          <input
            type="search"
            aria-label="筛选任务"
            placeholder="筛选任务"
            value={filter}
            onChange={(event) => setFilter(event.target.value)}
          />
        </label>
        <div className="tree-filter-row">
          <button className="filter-chip active">全部</button>
          <span>{roots.length} 个项目</span>
        </div>
        <div className="task-tree">
          {roots.map((task) => (
            <TaskTreeRow
              key={task.id}
              task={task}
              selectedId={selectedId}
              expanded={expanded}
              onToggle={(id) =>
                setExpanded((value) => {
                  const next = new Set(value);
                  next.has(id) ? next.delete(id) : next.add(id);
                  return next;
                })
              }
              onSelect={setSelectedId}
              onComplete={toggleComplete}
            />
          ))}
        </div>
        <form
          className="inline-add"
          onSubmit={(event) => void add(event, null)}
        >
          <button type="submit" aria-label="新建顶级任务">
            <Plus size={16} />
          </button>
          <input
            value={rootTitle}
            onChange={(event) => setRootTitle(event.target.value)}
            placeholder="新建顶级任务"
          />
        </form>
      </section>
      <main className="task-canvas">
        {selected ? (
          <>
            <div className="canvas-header">
              <div className="breadcrumbs">
                <span>任务</span>
                <CaretRight size={13} />
                <span>{selected.title}</span>
              </div>
              <IconButton
                label="更多操作"
                onClick={() => setTaskMenuOpen((value) => !value)}
              >
                <DotsThree size={20} />
              </IconButton>
              {taskMenuOpen && (
                <div className="task-action-menu" role="menu">
                  <button
                    role="menuitem"
                    onClick={() => {
                      setTaskMenuOpen(false);
                      document
                        .querySelector<HTMLInputElement>(".task-title-input")
                        ?.focus();
                    }}
                  >
                    <NotePencil size={16} />
                    编辑标题
                  </button>
                  <button
                    role="menuitem"
                    onClick={() => {
                      setTaskMenuOpen(false);
                      document
                        .getElementById("task-notes")
                        ?.scrollIntoView({ behavior: "smooth", block: "center" });
                    }}
                  >
                    <NotePencil size={16} />
                    编辑任务笔记
                  </button>
                  <button
                    role="menuitem"
                    onClick={() => {
                      setTaskMenuOpen(false);
                      document
                        .getElementById("task-operations")
                        ?.scrollIntoView({ behavior: "smooth", block: "center" });
                    }}
                  >
                    <ListChecks size={16} />
                    移动或调整层级
                  </button>
                  <button
                    className="danger"
                    role="menuitem"
                    onClick={() => {
                      setTaskMenuOpen(false);
                      void deleteTask("deleteBranch");
                    }}
                  >
                    <Trash size={16} />
                    删除任务分支
                  </button>
                </div>
              )}
            </div>
            <div className="task-title-block">
              <button
                className={`task-check large ${selected.isCompleted ? "checked" : ""}`}
                onClick={() => toggleComplete(selected)}
              >
                {selected.isCompleted && <Check size={15} />}
              </button>
              <div>
                <input
                  className="task-title-input"
                  aria-label="任务标题"
                  maxLength={500}
                  value={titleDraft}
                  onChange={(event) => setTitleDraft(event.target.value)}
                  onBlur={() => {
                    const title = titleDraft.trim();
                    if (title && title !== selected.title)
                      void updateTask(
                        { title },
                        { title: selected.title },
                        `已重命名为“${title}”`,
                      );
                    else setTitleDraft(selected.title);
                  }}
                  onKeyDown={(event) => {
                    if (event.key === "Enter") event.currentTarget.blur();
                    if (event.key === "Escape") {
                      setTitleDraft(selected.title);
                      event.currentTarget.blur();
                    }
                  }}
                />
                <p>
                  {selected.hasChildren
                    ? "父级任务由叶子任务的完成状态自动维护。"
                    : "专注完成当前任务，并按需记录实际耗时。"}
                </p>
              </div>
            </div>
            <div className="meta-strip">
              <button
                title="点击切换优先级"
                onClick={() =>
                  void updateTask(
                    { priority: (selected.priority + 1) % 4 },
                    { priority: selected.priority },
                    "已调整任务优先级",
                  )
                }
              >
                优先级 {selected.priority}
              </button>
              <span className="meta-item">
                <CalendarBlank size={16} />
                {new Date(selected.createdAt).toLocaleDateString()}
              </span>
            </div>
            <section className="content-section">
              <div className="section-title-row">
                <div>
                  <h3>直属子任务</h3>
                  <span>
                    {children.filter((item) => item.isCompleted).length}/
                    {children.length} 已完成
                  </span>
                </div>
              </div>
              <div className="progress-track">
                <span
                  style={{
                    width: `${children.length ? (children.filter((item) => item.isCompleted).length / children.length) * 100 : 0}%`,
                  }}
                />
              </div>
              <div className="subtask-list">
                {children.map((item) => (
                  <div className="subtask-row" key={item.id}>
                    <button
                      className={`task-check ${item.isCompleted ? "checked" : ""}`}
                      onClick={() => toggleComplete(item)}
                    >
                      {item.isCompleted && <Check size={12} />}
                    </button>
                    <span className={item.isCompleted ? "done" : ""}>
                      {item.title}
                    </span>
                    <small>
                      {item.completionDurationMinutes
                        ? `${item.completionDurationMinutes} 分钟`
                        : ""}
                    </small>
                  </div>
                ))}
              </div>
              {selected.canAddChild && (
                <form
                  className="inline-add"
                  onSubmit={(event) => void add(event, selected.id)}
                >
                  <button type="submit">
                    <Plus size={16} />
                  </button>
                  <input
                    value={childTitle}
                    onChange={(event) => setChildTitle(event.target.value)}
                    placeholder="添加子任务，按 Enter 保存"
                  />
                </form>
              )}
            </section>
            <section id="task-notes" className="content-section notes-section">
              <div className="section-title-row">
                <h3>任务笔记</h3>
                <span
                  className={`save-status ${notesStatus}`}
                  role={notesStatus === "error" ? "alert" : "status"}
                >
                  {notesStatus === "saving"
                    ? "正在保存…"
                    : notesStatus === "unsaved"
                      ? "等待自动保存"
                      : notesStatus === "error"
                        ? "保存失败，继续输入可重试"
                        : "已保存"}
                </span>
              </div>
              <textarea
                aria-label="任务笔记"
                maxLength={4000}
                placeholder="记录上下文、验收要点或下一步；停止输入后自动保存。"
                value={notesDraft}
                onChange={(event) => setNotesDraft(event.target.value)}
              />
            </section>
            <section id="task-operations" className="content-section">
              <div className="section-title-row">
                <h3>任务操作</h3>
              </div>
              <div className="setting-row compact-setting-row">
                <div>
                  <strong>所属父任务</strong>
                  <span>移动后由 C# 校验循环引用和五层限制</span>
                </div>
                <select
                  className="select-button"
                  value={selected.parentId ?? ""}
                  onChange={(event) =>
                    void moveTask(
                      event.target.value ? Number(event.target.value) : null,
                    )
                  }
                >
                  <option value="">顶级任务</option>
                  {parentCandidates.map((candidate) => (
                    <option key={candidate.id} value={candidate.id}>
                      {"　".repeat(candidate.depth)}
                      {candidate.title}
                    </option>
                  ))}
                </select>
              </div>
              <div className="timer-controls">
                <button
                  className="secondary-button"
                  onClick={() => void deleteTask("deleteBranch")}
                >
                  删除任务分支
                </button>
                {selected.hasChildren && (
                  <button
                    className="secondary-button"
                    onClick={() => void deleteTask("promoteChildren")}
                  >
                    仅删父级并提升子任务
                  </button>
                )}
              </div>
            </section>
          </>
        ) : (
          <div className="empty-state">
            <h2>还没有任务</h2>
            <p>从左侧创建一个顶级任务开始。</p>
          </div>
        )}
      </main>
      <aside className="inspector-pane">
        <section className="focus-panel">
          <div className="focus-heading">
            <div>
              <Timer size={19} />
              <span>当前专注</span>
            </div>
            <span className="live-label">
              {activeTimer?.status ?? "未连接"}
            </span>
          </div>
          <select
            className="timer-select"
            value={timerState.activeId}
            onChange={(event) =>
              void bridge
                .request("pomodoro.command", {
                  command: "select",
                  timerId: event.target.value,
                })
                .then(loadTimers)
                .catch(reportError)
            }
          >
            {timerState.timers.map((timer) => (
              <option key={timer.id} value={timer.id}>
                {timer.name}
              </option>
            ))}
          </select>
          <strong className="timer-value">
            {activeTimer?.display ?? "25:00"}
          </strong>
          <p>{selected?.title ?? "选择任务后开始专注"}</p>
          <div className="timer-progress">
            <span style={{ width: `${(activeTimer?.progress ?? 1) * 100}%` }} />
          </div>
          <div className="timer-controls">
            <button
              className="primary-control"
              disabled={
                activeTimer?.isStarted
                  ? !activeTimer.canPause
                  : !activeTimer?.canStart
              }
              onClick={() =>
                void timerCommand(activeTimer?.isStarted ? "pause" : "start")
              }
            >
              {activeTimer?.isStarted ? (
                <Pause size={18} />
              ) : (
                <Play size={18} />
              )}
              {activeTimer?.isStarted ? "暂停/继续" : "开始"}
            </button>
            <IconButton
              label="停止计时"
              disabled={!activeTimer?.canStop}
              onClick={() => void timerCommand("stop")}
            >
              <Stop size={17} />
            </IconButton>
          </div>
          <div className="timer-secondary-controls">
            <button
              disabled={!activeTimer?.canRest}
              onClick={() => void timerCommand("rest")}
            >
              提前休息
            </button>
            {timerState.multipleEnabled && (
              <button onClick={() => void timerCommand("add")}>
                添加计时器
              </button>
            )}
            {timerState.multipleEnabled && activeTimer?.id !== "timer-0" && (
              <button onClick={() => void timerCommand("remove")}>
                移除当前计时器
              </button>
            )}
          </div>
        </section>
        <section className="details-panel">
          <div className="section-title-row">
            <h3>任务详情</h3>
            <IconButton
              label="编辑任务标题"
              onClick={() =>
                document
                  .querySelector<HTMLInputElement>(".task-title-input")
                  ?.focus()
              }
            >
              <NotePencil size={16} />
            </IconButton>
          </div>
          <dl>
            <div>
              <dt>状态</dt>
              <dd>
                <span
                  className={`status-dot ${selected && !selected.isCompleted ? "active" : ""}`}
                />
                {selected?.isCompleted ? "已完成" : "进行中"}
              </dd>
            </div>
            <div>
              <dt>层级</dt>
              <dd>{selected ? `${selected.depth + 1} / 5` : "—"}</dd>
            </div>
            <div>
              <dt>累计专注</dt>
              <dd>
                {selected?.completionDurationMinutes
                  ? `${selected.completionDurationMinutes} 分钟`
                  : "未记录"}
              </dd>
            </div>
          </dl>
        </section>
      </aside>
      {completionTask && (
        <CompletionDialog
          task={completionTask}
          onClose={() => setCompletionTask(null)}
          onComplete={(duration) => void complete(duration)}
        />
      )}
      {undoAction && (
        <div className="undo-toast" role="status">
          <span>{undoAction.message}</span>
          <button
            onClick={() => {
              const action = undoAction;
              setUndoAction(null);
              void action.run().catch(reportError);
            }}
          >
            <ArrowCounterClockwise size={16} />
            撤销
          </button>
          <IconButton label="关闭撤销提示" onClick={() => setUndoAction(null)}>
            <X size={15} />
          </IconButton>
        </div>
      )}
    </div>
  );
}

export function SummaryPage({
  reportError,
  onOpenTask,
}: {
  reportError(error: unknown): void;
  onOpenTask(taskId: number): void;
}) {
  const [date, setDate] = useState(toLocalDateInput());
  const [summary, setSummary] = useState<SummaryResult | null>(null);
  const [backfill, setBackfill] = useState(false);
  const [operationStatus, setOperationStatus] = useState("");
  const load = useCallback(async () => {
    try {
      setSummary(await bridge.request<SummaryResult>("summary.get", { date }));
    } catch (error) {
      reportError(error);
    }
  }, [date, reportError]);
  useEffect(() => {
    void load();
  }, [load]);
  useBridgeRefresh("tasks.changed", () => void load());
  const shift = (days: number) => {
    const value = new Date(`${date}T00:00:00`);
    value.setDate(value.getDate() + days);
    if (value <= new Date()) setDate(toLocalDateInput(value));
  };
  const runSummaryOperation = async (
    method: "summary.copy" | "summary.export",
    successMessage: string,
  ) => {
    try {
      setOperationStatus("处理中…");
      const result = await bridge.request<{ copied?: boolean; exported?: boolean }>(
        method,
        { date },
      );
      if (result.copied || result.exported) setOperationStatus(successMessage);
      else setOperationStatus("已取消");
    } catch (error) {
      setOperationStatus("操作失败");
      reportError(error);
    }
  };
  return (
    <div className="full-page summary-page page-enter">
      <div className="page-heading-row">
        <div>
          <span className="section-kicker">回顾仍然有效的成果</span>
          <h2>每日总结</h2>
          <p>取消完成只保留在历史中，不计入这里。</p>
        </div>
        <div className="heading-actions">
          <button
            className="secondary-button"
            onClick={() => void runSummaryOperation("summary.copy", "已复制")}
          >
            <Copy size={17} />
            复制总结
          </button>
          <button className="primary-button" onClick={() => setBackfill(true)}>
            <Plus size={17} />
            补充任务
          </button>
        </div>
      </div>
      <div className="date-toolbar">
        <IconButton label="前一天" onClick={() => shift(-1)}>
          <CaretLeft size={18} />
        </IconButton>
        <div className="date-picker">
          <CalendarBlank size={18} />
          <strong>{date}</strong>
          {date === toLocalDateInput() && <span>今天</span>}
        </div>
        <IconButton label="后一天" onClick={() => shift(1)}>
          <CaretRight size={18} />
        </IconButton>
      </div>
      <div className="summary-layout">
        <section className="summary-main">
          <div className="summary-stats">
            <div>
              <span>有效完成</span>
              <strong>{summary?.completedCount ?? 0}</strong>
              <small>{summary?.leafCompletedCount ?? 0} 个叶子任务</small>
            </div>
            <div>
              <span>父级成果</span>
              <strong>{summary?.parentAchievementCount ?? 0}</strong>
              <small>自动汇总达成</small>
            </div>
            <div>
              <span>专注时长</span>
              <strong>{summary?.totalDurationDisplay ?? "0分钟"}</strong>
              <small>来自完成记录</small>
            </div>
          </div>
          <div className="achievement-list">
            {summary?.groups.map((group) => (
              <article className="achievement-group" key={group.topLevelTaskId}>
                <header>
                  <div>
                    <CaretDown size={16} />
                    <div>
                      <h3>{group.title}</h3>
                      <p>{group.records.length} 项成果</p>
                    </div>
                  </div>
                </header>
                {group.records.map((item) => (
                  <button
                    className="achievement-row"
                    key={`${item.taskId}-${item.completedAt}`}
                    style={
                      { "--indent": item.depthSnapshot } as React.CSSProperties
                    }
                    disabled={!item.exists}
                    title={
                      item.exists
                        ? "打开任务详情"
                        : "任务已删除，仅保留完成快照"
                    }
                    onClick={() => item.exists && onOpenTask(item.taskId)}
                  >
                    <CheckCircle size={18} weight="fill" />
                    <div>
                      <strong>{item.titleSnapshot}</strong>
                      <span>{item.kindDisplay}</span>
                    </div>
                    <small>{item.durationDisplay}</small>
                    <time>
                      {new Date(item.completedAt).toLocaleTimeString([], {
                        hour: "2-digit",
                        minute: "2-digit",
                      })}
                    </time>
                  </button>
                ))}
              </article>
            ))}
          </div>
          {summary && summary.groups.length === 0 && (
            <div className="history-empty">
              这一天还没有有效完成；取消完成的内容仍可在历史记录中查看。
            </div>
          )}
        </section>
        <aside className="summary-side">
          <section>
            <span className="section-kicker">一句话总结</span>
            <h3>{summary?.summaryLine ?? "正在加载"}</h3>
            <p>统计、复制和导出使用同一份有效完成数据。</p>
          </section>
          <details className="summary-method">
            <summary>查看统计口径</summary>
            <p>
              当前仍存在的任务必须保持完成状态且完成时间匹配；已删除任务保留完成快照；取消完成仅进入历史日志。
            </p>
          </details>
          {operationStatus && (
            <span className="summary-operation-status" role="status">
              {operationStatus}
            </span>
          )}
          <button
            className="export-button"
            onClick={() => void runSummaryOperation("summary.export", "已导出")}
          >
            <DownloadSimple size={18} />
            导出今日总结
          </button>
        </aside>
      </div>
      {backfill && (
        <BackfillDialog
          date={date}
          reportError={reportError}
          onClose={() => setBackfill(false)}
          onDone={load}
        />
      )}
    </div>
  );
}

function BackfillDialog({
  date,
  reportError,
  onClose,
  onDone,
}: {
  date: string;
  reportError(error: unknown): void;
  onClose(): void;
  onDone(): Promise<void>;
}) {
  const [tasks, setTasks] = useState<TaskDto[]>([]);
  const [taskId, setTaskId] = useState<number | null>(null);
  const [duration, setDuration] = useState("");
  const [time, setTime] = useState(new Date().toTimeString().slice(0, 5));
  useEffect(() => {
    void bridge
      .request<TaskDto[]>("summary.candidates")
      .then((items) => {
        setTasks(items);
        setTaskId(items[0]?.id ?? null);
      })
      .catch(reportError);
  }, [reportError]);
  const submit = async () => {
    if (taskId == null) return;
    try {
      await bridge.request("summary.backfill", {
        taskId,
        date,
        time,
        durationMinutes: duration ? Number(duration) : null,
      });
      await onDone();
      onClose();
    } catch (error) {
      reportError(error);
    }
  };
  return (
    <div
      className="modal-backdrop"
      onMouseDown={(event) => event.target === event.currentTarget && onClose()}
    >
      <section className="modal" role="dialog" aria-modal="true">
        <header>
          <div>
            <span className="section-kicker">{date}</span>
            <h2>补充任务</h2>
          </div>
          <IconButton label="关闭" onClick={onClose}>
            <X size={19} />
          </IconButton>
        </header>
        <p>选择未完成叶子任务；由此达成的父级成果会自动记录。</p>
        <div className="task-choice-list">
          {tasks.map((task) => (
            <button
              key={task.id}
              className={taskId === task.id ? "selected" : ""}
              onClick={() => setTaskId(task.id)}
            >
              <span className="radio-mark">
                {taskId === task.id && <Check size={10} />}
              </span>
              <div>
                <strong>{task.title}</strong>
                <small>第 {task.depth + 1} 层</small>
              </div>
            </button>
          ))}
          {tasks.length === 0 && (
            <p className="dialog-empty">没有可补充的未完成叶子任务。</p>
          )}
        </div>
        <div className="modal-fields">
          <label>
            <span>完成时间</span>
            <input
              value={time}
              onChange={(event) => setTime(event.target.value)}
            />
          </label>
          <label>
            <span>专注时长（分钟）</span>
            <input
              type="number"
              min="1"
              value={duration}
              onChange={(event) => setDuration(event.target.value)}
            />
          </label>
        </div>
        <footer>
          <button className="secondary-button" onClick={onClose}>
            取消
          </button>
          <button
            className="primary-button"
            disabled={taskId == null}
            onClick={() => void submit()}
          >
            <Check size={17} />
            确认补充
          </button>
        </footer>
      </section>
    </div>
  );
}

export function HistoryPage({
  reportError,
  onOpenTask,
}: {
  reportError(error: unknown): void;
  onOpenTask(taskId: number): void;
}) {
  const today = toLocalDateInput();
  const initialStart = toLocalDateInput(new Date(Date.now() - 30 * 86400000));
  const [rows, setRows] = useState<TaskActivityDto[]>([]);
  const [startDate, setStartDate] = useState(initialStart);
  const [endDate, setEndDate] = useState(today);
  const [keyword, setKeyword] = useState("");
  const [eventFilter, setEventFilter] = useState("all");
  const [sortDesc, setSortDesc] = useState(true);
  const [pendingDelete, setPendingDelete] = useState<TaskActivityDto | null>(
    null,
  );
  const params = useMemo(
    () => ({
      startDate,
      endDate,
      keyword,
      eventFilter,
      sortDesc,
    }),
    [endDate, eventFilter, keyword, sortDesc, startDate],
  );
  const load = useCallback(async () => {
    try {
      setRows(
        await bridge.request<TaskActivityDto[]>("history.events", params),
      );
    } catch (error) {
      reportError(error);
    }
  }, [params, reportError]);
  useEffect(() => {
    const timeout = setTimeout(() => void load(), 180);
    return () => clearTimeout(timeout);
  }, [load]);
  const deleteTask = async () => {
    if (pendingDelete?.taskId == null) return;
    try {
      await bridge.request("tasks.delete", {
        taskId: pendingDelete.taskId,
        mode: "deleteBranch",
      });
      setPendingDelete(null);
      await load();
    } catch (error) {
      reportError(error);
    }
  };
  return (
    <div className="full-page history-page page-enter">
      <div className="page-heading-row">
        <div>
          <span className="section-kicker">只追加的任务活动日志</span>
          <h2>历史记录</h2>
          <p>升级前可靠事实会标记为历史回填；升级后的编辑、移动和删除均按发生时间记录。</p>
        </div>
        <button
          className="secondary-button"
          onClick={() =>
            void bridge.request("history.export", params).catch(reportError)
          }
        >
          <DownloadSimple size={17} />
          导出记录
        </button>
      </div>
      <div className="history-toolbar">
        <label className="wide-search">
          <MagnifyingGlass size={17} />
          <input
            type="search"
            placeholder="搜索任务"
            value={keyword}
            onChange={(event) => setKeyword(event.target.value)}
          />
        </label>
        <label className="date-range-field">
          <span>从</span>
          <input
            type="date"
            value={startDate}
            max={endDate}
            onChange={(event) => setStartDate(event.target.value)}
          />
        </label>
        <label className="date-range-field">
          <span>到</span>
          <input
            type="date"
            value={endDate}
            min={startDate}
            max={today}
            onChange={(event) => setEndDate(event.target.value)}
          />
        </label>
        <select
          className="select-button"
          value={eventFilter}
          onChange={(event) => setEventFilter(event.target.value)}
        >
          <option value="all">全部事件</option>
          <option value="created">创建任务</option>
          <option value="updated">编辑任务</option>
          <option value="moved">移动任务</option>
          <option value="completed">完成任务</option>
          <option value="reopened">取消完成</option>
          <option value="deleted">删除任务</option>
          <option value="migrationBoundary">日志边界</option>
        </select>
        <select
          className="select-button"
          value={sortDesc ? "desc" : "asc"}
          onChange={(event) => setSortDesc(event.target.value === "desc")}
        >
          <option value="desc">最新优先</option>
          <option value="asc">最早优先</option>
        </select>
      </div>
      <section className="history-table">
        <header>
          <span>发生时间</span>
          <span>事件</span>
          <span>任务</span>
          <span>说明</span>
          <span />
        </header>
        {rows.map((item) => (
          <div className="history-row" key={item.id}>
            <time>{new Date(item.occurredAt).toLocaleString()}</time>
            <span className={`event-label ${item.tone}`}>
              {item.eventLabel}
            </span>
            <strong>{item.title}</strong>
            <span>
              {item.detail}
              {item.isLegacyBackfill && <small className="legacy-badge">历史回填</small>}
            </span>
            {item.canOpenTask && item.taskId != null ? (
              <span className="history-actions">
                <IconButton
                  label={`打开 ${item.title}`}
                  onClick={() => onOpenTask(item.taskId!)}
                >
                  <CaretRight size={16} />
                </IconButton>
                <IconButton
                  label={`删除当前任务 ${item.title}`}
                  onClick={() => setPendingDelete(item)}
                >
                  <Trash size={15} />
                </IconButton>
              </span>
            ) : (
              <span />
            )}
          </div>
        ))}
        {rows.length === 0 && (
          <div className="history-empty">所选范围内没有匹配的记录</div>
        )}
      </section>
      {pendingDelete && (
        <div className="modal-backdrop">
          <section className="modal" role="dialog" aria-modal="true">
            <header>
              <div>
                <span className="section-kicker">历史记录操作</span>
                <h2>删除“{pendingDelete.title}”及其子任务？</h2>
              </div>
              <IconButton label="关闭" onClick={() => setPendingDelete(null)}>
                <X size={19} />
              </IconButton>
            </header>
            <p>任务会从当前工作区删除；已经生成的不可变完成事实仍会保留。</p>
            <footer>
              <button
                className="secondary-button"
                onClick={() => setPendingDelete(null)}
              >
                取消
              </button>
              <button
                className="primary-button"
                onClick={() => void deleteTask()}
              >
                确认删除
              </button>
            </footer>
          </section>
        </div>
      )}
    </div>
  );
}

function Toggle({
  value,
  onChange,
  label,
}: {
  value: boolean;
  onChange(value: boolean): void;
  label: string;
}) {
  return (
    <button
      className={`switch ${value ? "on" : ""}`}
      onClick={() => onChange(!value)}
      aria-label={label}
      aria-pressed={value}
    >
      <span />
    </button>
  );
}
export function SettingsPage({ reportError }: { reportError(error: unknown): void }) {
  const [settings, setSettings] = useState<SettingsDto | null>(null);
  const [savedSettings, setSavedSettings] = useState<SettingsDto | null>(null);
  const [saveStatus, setSaveStatus] = useState<
    "clean" | "dirty" | "saving" | "saved" | "error"
  >("clean");
  const [dataStatus, setDataStatus] = useState("");
  const [settingsSection, setSettingsSection] = useState<
    "timer" | "notifications" | "theme"
  >("timer");
  useEffect(() => {
    void bridge
      .request<SettingsDto>("settings.get")
      .then((value) => {
        setSettings(value);
        setSavedSettings(value);
        setSaveStatus("clean");
      })
      .catch(reportError);
  }, [reportError]);
  if (!settings)
    return (
      <div className="full-page">
        <p>正在加载设置…</p>
      </div>
    );
  const patch = <K extends keyof SettingsDto>(
    key: K,
    value: SettingsDto[K],
  ) => {
    setSettings({ ...settings, [key]: value });
    setSaveStatus("dirty");
  };
  const save = async () => {
    if (
      settings.workMinutes < 1 ||
      settings.workMinutes > 240 ||
      settings.restMinutes < 1 ||
      settings.restMinutes > 120
    ) {
      setSaveStatus("error");
      reportError(new Error("请检查番茄钟时长，数值超出允许范围。"));
      return;
    }
    try {
      setSaveStatus("saving");
      const result = await bridge.request<SettingsDto>(
        "settings.save",
        settings as unknown as Record<string, unknown>,
      );
      setSettings(result);
      setSavedSettings(result);
      applyTheme(result);
      setSaveStatus("saved");
    } catch (error) {
      setSaveStatus("error");
      reportError(error);
    }
  };
  const chooseSound = async () => {
    try {
      const result = await bridge.request<{ selected: boolean; path: string }>(
        "settings.chooseSound",
      );
      if (result.selected) {
        setSettings({
          ...settings,
          notificationSoundChoice: 5,
          customSoundPath: result.path,
        });
        setSaveStatus("dirty");
      }
    } catch (error) {
      reportError(error);
    }
  };
  const previewSound = () =>
    bridge
      .request("settings.previewSound", {
        notificationSoundChoice: settings.notificationSoundChoice,
        customSoundPath: settings.customSoundPath,
      })
      .catch(reportError);
  const createBackup = async () => {
    try {
      setDataStatus("正在创建一致性备份…");
      const result = await bridge.request<{
        created: boolean;
        fileName?: string;
      }>("app.backupData");
      setDataStatus(
        result.created ? `备份已创建：${result.fileName}` : "已取消备份",
      );
    } catch (error) {
      setDataStatus("备份失败");
      reportError(error);
    }
  };
  const stageRestore = async () => {
    if (
      !window.confirm(
        "恢复会在下次启动前替换当前数据，并自动保留回滚副本。继续选择备份吗？",
      )
    )
      return;
    try {
      setDataStatus("正在校验备份…");
      const result = await bridge.request<{
        staged: boolean;
        restartRequired?: boolean;
      }>("app.restoreData");
      setDataStatus(
        result.staged
          ? "备份已校验并暂存，请退出并重新启动 Taskato 完成恢复。"
          : "已取消恢复",
      );
    } catch (error) {
      setDataStatus("恢复校验失败，当前数据未改变");
      reportError(error);
    }
  };
  const navigateSettings = (section: "timer" | "notifications" | "theme") => {
    setSettingsSection(section);
    requestAnimationFrame(() =>
      document
        .getElementById(`settings-${section}`)
        ?.scrollIntoView({ behavior: "smooth", block: "start" }),
    );
  };
  return (
    <div className="full-page settings-page page-enter">
      <div className="page-heading-row">
        <div>
          <span className="section-kicker">完整应用设置</span>
          <h2>设置</h2>
          <p>设置继续保存在原有 settings.json，并由 C# 校验。</p>
        </div>
        <div className="heading-actions">
          <span className={`save-status ${saveStatus}`} role="status">
            {saveStatus === "saving"
              ? "正在保存…"
              : saveStatus === "dirty"
                ? "有未保存更改"
                : saveStatus === "error"
                  ? "保存失败"
                  : saveStatus === "saved"
                    ? "已保存"
                    : "设置已同步"}
          </span>
          <button
            className="secondary-button"
            disabled={!savedSettings || saveStatus === "saving"}
            onClick={() => {
              if (!savedSettings) return;
              setSettings(savedSettings);
              applyTheme(savedSettings);
              setSaveStatus("clean");
            }}
          >
            恢复已保存值
          </button>
          <button
            className="primary-button"
            disabled={saveStatus === "saving" || saveStatus === "clean"}
            onClick={() => void save()}
          >
            保存设置
          </button>
        </div>
      </div>
      <div className="settings-layout">
        <aside className="settings-nav">
          <button
            className={settingsSection === "timer" ? "active" : ""}
            onClick={() => navigateSettings("timer")}
          >
            <Timer size={17} />
            番茄钟
          </button>
          <button
            className={settingsSection === "notifications" ? "active" : ""}
            onClick={() => navigateSettings("notifications")}
          >
            <Bell size={17} />
            提醒与总结
          </button>
          <button
            className={settingsSection === "theme" ? "active" : ""}
            onClick={() => navigateSettings("theme")}
          >
            <Sparkle size={17} />
            主题与回退
          </button>
        </aside>
        <main className="settings-content">
          <section id="settings-timer">
            <div>
              <h3>番茄钟</h3>
              <p>修改空闲计时器的默认时长。</p>
            </div>
            <div className="setting-row">
              <div>
                <strong>开机自动启动</strong>
                <span>登录 Windows 后启动 Taskato</span>
              </div>
              <Toggle
                label="开机自动启动"
                value={settings.autoStartEnabled}
                onChange={(value) => patch("autoStartEnabled", value)}
              />
            </div>
            <div className="setting-row">
              <div>
                <strong>专注分钟</strong>
                <span>1–240</span>
              </div>
              <input
                className="number-input"
                type="number"
                min="1"
                max="240"
                value={settings.workMinutes}
                onChange={(event) =>
                  patch("workMinutes", Number(event.target.value))
                }
              />
            </div>
            <div className="setting-row">
              <div>
                <strong>休息分钟</strong>
                <span>1–120</span>
              </div>
              <input
                className="number-input"
                type="number"
                min="1"
                max="120"
                value={settings.restMinutes}
                onChange={(event) =>
                  patch("restMinutes", Number(event.target.value))
                }
              />
            </div>
            <div className="setting-row">
              <div>
                <strong>多组番茄钟</strong>
                <span>允许添加附加计时器</span>
              </div>
              <Toggle
                label="多组番茄钟"
                value={settings.enableMultiplePomodoros}
                onChange={(value) => patch("enableMultiplePomodoros", value)}
              />
            </div>
            <div className="setting-row">
              <div>
                <strong>自动开始下一轮</strong>
                <span>继续工作时自动倒计时</span>
              </div>
              <Toggle
                label="自动开始下一轮"
                value={settings.autoStartNextPomodoro}
                onChange={(value) => patch("autoStartNextPomodoro", value)}
              />
            </div>
          </section>
          <section id="settings-notifications">
            <div>
              <h3>提醒与总结</h3>
              <p>桌面 Toast、飞书和跨天总结仍由 C# 执行。</p>
            </div>
            <div className="setting-row">
              <div>
                <strong>Toast 显示计时</strong>
              </div>
              <Toggle
                label="Toast 显示计时"
                value={settings.enableToastTimer}
                onChange={(value) => patch("enableToastTimer", value)}
              />
            </div>
            <div className="setting-row">
              <div>
                <strong>Toast 操作延迟（秒）</strong>
                <span>防止通知按钮被连续误触</span>
              </div>
              <input
                className="number-input"
                type="number"
                min="0"
                max="60"
                step="0.5"
                value={settings.toastActionDelaySeconds}
                onChange={(event) =>
                  patch("toastActionDelaySeconds", Number(event.target.value))
                }
              />
            </div>
            <div className="setting-row">
              <div>
                <strong>通知声音</strong>
                <span>0 为静音，5 为自定义文件</span>
              </div>
              <div className="setting-actions">
                <select
                  className="select-button"
                  value={settings.notificationSoundChoice}
                  onChange={(event) =>
                    patch("notificationSoundChoice", Number(event.target.value))
                  }
                >
                  <option value={0}>静音</option>
                  <option value={1}>系统通知</option>
                  <option value={2}>系统提示</option>
                  <option value={3}>系统背景</option>
                  <option value={4}>系统风铃</option>
                  <option value={5}>自定义</option>
                </select>
                <button className="secondary-button" onClick={previewSound}>
                  试听
                </button>
              </div>
            </div>
            <div className="setting-row nested">
              <div>
                <strong>自定义声音</strong>
                <span className="setting-path">
                  {settings.customSoundPath || "尚未选择文件"}
                </span>
              </div>
              <button
                className="secondary-button"
                onClick={() => void chooseSound()}
              >
                选择文件
              </button>
            </div>
            <div className="setting-row">
              <div>
                <strong>自动显示昨日总结</strong>
              </div>
              <Toggle
                label="自动显示昨日总结"
                value={settings.autoShowDailySummary}
                onChange={(value) => patch("autoShowDailySummary", value)}
              />
            </div>
            <div className="setting-row">
              <div>
                <strong>飞书通知</strong>
              </div>
              <Toggle
                label="飞书通知"
                value={settings.feishuEnabled}
                onChange={(value) => patch("feishuEnabled", value)}
              />
            </div>
            <div className="setting-row">
              <div>
                <strong>Webhook URL</strong>
                <span>环境变量仍可覆盖</span>
              </div>
              <input
                className="text-input"
                value={settings.feishuWebhookUrl}
                onChange={(event) =>
                  patch("feishuWebhookUrl", event.target.value)
                }
              />
            </div>
            <div className="setting-row nested">
              <div>
                <strong>工作结束通知</strong>
              </div>
              <Toggle
                label="工作结束通知"
                value={settings.feishuNotifyOnWork}
                onChange={(value) => patch("feishuNotifyOnWork", value)}
              />
            </div>
            <div className="setting-row nested">
              <div>
                <strong>休息结束通知</strong>
              </div>
              <Toggle
                label="休息结束通知"
                value={settings.feishuNotifyOnRest}
                onChange={(value) => patch("feishuNotifyOnRest", value)}
              />
            </div>
            <div className="setting-row nested">
              <div>
                <strong>重复提醒</strong>
              </div>
              <Toggle
                label="重复提醒"
                value={settings.feishuRepeatEnabled}
                onChange={(value) => patch("feishuRepeatEnabled", value)}
              />
            </div>
            <div className="setting-row nested">
              <div>
                <strong>休息过半提醒</strong>
              </div>
              <Toggle
                label="休息过半提醒"
                value={settings.feishuRestHalfwayEnabled}
                onChange={(value) => patch("feishuRestHalfwayEnabled", value)}
              />
            </div>
          </section>
          <section id="settings-theme">
            <div>
              <h3>主题与回退</h3>
              <p>主题保存后立即映射为网页 CSS 变量。</p>
            </div>
            <div className="setting-row">
              <div>
                <strong>主色</strong>
              </div>
              <input
                className="text-input color-input"
                value={settings.themePrimaryColor}
                onChange={(event) => {
                  patch("themePrimaryColor", event.target.value);
                  document.documentElement.style.setProperty(
                    "--primary",
                    event.target.value,
                  );
                }}
              />
            </div>
            <div className="setting-row">
              <div>
                <strong>辅色</strong>
              </div>
              <input
                className="text-input color-input"
                value={settings.themeSecondaryColor}
                onChange={(event) => {
                  patch("themeSecondaryColor", event.target.value);
                  document.documentElement.style.setProperty(
                    "--secondary",
                    event.target.value,
                  );
                }}
              />
            </div>
            <div className="setting-row">
              <div>
                <strong>下次启动使用旧 WPF 界面</strong>
                <span>WebView2 故障时的持久回退开关</span>
              </div>
              <Toggle
                label="使用旧界面"
                value={settings.useLegacyWorkspace}
                onChange={(value) => {
                  if (
                    value &&
                    !window.confirm(
                      "旧界面只用于 WebView2 故障回退。确定下次启动切换吗？",
                    )
                  )
                    return;
                  patch("useLegacyWorkspace", value);
                }}
              />
            </div>
            <div className="setting-row">
              <div>
                <strong>本地数据目录</strong>
                <span>数据库、设置、恢复回滚和诊断日志均保存在本机</span>
              </div>
              <div className="setting-actions">
                <button
                  className="secondary-button"
                  onClick={() =>
                    void bridge.request("app.openDataFolder").catch(reportError)
                  }
                >
                  打开数据位置
                </button>
                <button
                  className="secondary-button"
                  onClick={() =>
                    void bridge.request("app.openLogsFolder").catch(reportError)
                  }
                >
                  打开诊断日志
                </button>
              </div>
            </div>
            <div className="setting-row">
              <div>
                <strong>备份与恢复</strong>
                <span>备份包含 SQLite 数据和设置；恢复前会校验并保留回滚副本</span>
              </div>
              <div className="setting-actions">
                <button
                  className="secondary-button"
                  onClick={() => void createBackup()}
                >
                  创建备份
                </button>
                <button
                  className="secondary-button"
                  onClick={() => void stageRestore()}
                >
                  从备份恢复
                </button>
              </div>
            </div>
            {dataStatus && (
              <p className="data-operation-status" role="status">
                {dataStatus}
              </p>
            )}
          </section>
        </main>
      </div>
    </div>
  );
}
