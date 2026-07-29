import {
  useCallback,
  useEffect,
  useMemo,
  useRef,
  useState,
  type FormEvent,
  type KeyboardEvent,
  type PointerEvent,
} from "react";
import {
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
  NotePencil,
  Pause,
  Play,
  Plus,
  Stop,
  Timer,
  Trash,
  X,
} from "@phosphor-icons/react";
import {
  bridge,
  type SettingsDto,
  type SummaryResult,
  type TaskActivityDto,
  type TaskDto,
  type TaskListResult,
  type TimerListResult,
} from "../bridge";
import {
  applyTheme,
  IconButton,
  toLocalDateInput,
  useBridgeRefresh,
} from "../ui";

type TaskNode = TaskDto & { children: TaskNode[] };

export function buildTree(tasks: TaskDto[]): TaskNode[] {
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

export function filterTasksWithAncestors(
  tasks: TaskDto[],
  keyword: string,
): TaskDto[] {
  const query = keyword.trim().toLocaleLowerCase();
  if (!query) return tasks;
  const byId = new Map(tasks.map((task) => [task.id, task]));
  const visibleIds = new Set<number>();
  for (const task of tasks) {
    if (!task.title.toLocaleLowerCase().includes(query)) continue;
    let current: TaskDto | undefined = task;
    while (current && !visibleIds.has(current.id)) {
      visibleIds.add(current.id);
      current = current.parentId == null ? undefined : byId.get(current.parentId);
    }
  }
  // 搜索结果保留祖先链，避免命中的子任务被错误提升成顶级任务。
  return tasks.filter((task) => visibleIds.has(task.id));
}

const TREE_PANE_DEFAULT = 300;
const TREE_PANE_MIN = 220;
const TREE_PANE_MAX = 560;
const INSPECTOR_PANE_DEFAULT = 310;
const INSPECTOR_PANE_MIN = 250;
const INSPECTOR_PANE_MAX = 560;
const CANVAS_PANE_MIN = 360;
const RESIZER_WIDTH = 24;

function storedPaneWidth(key: string, fallback: number): number {
  const value = Number(window.localStorage.getItem(key));
  return Number.isFinite(value) && value > 0 ? value : fallback;
}

function clamp(value: number, minimum: number, maximum: number): number {
  return Math.min(Math.max(value, minimum), Math.max(minimum, maximum));
}

const priorityLabels = ["无", "低", "中", "高"] as const;

function PrioritySelector({
  value,
  onChange,
  label,
}: {
  value: number;
  onChange(value: number): void;
  label: string;
}) {
  return (
    <div className="priority-selector" role="radiogroup" aria-label={label}>
      <span>优先级</span>
      {priorityLabels.map((priority, index) => (
        <button
          key={priority}
          type="button"
          role="radio"
          aria-label={`${label}：${priority}`}
          aria-checked={value === index}
          className={`priority-option priority-${index} ${value === index ? "selected" : ""}`}
          onClick={() => onChange(index)}
        >
          {priority}
        </button>
      ))}
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
    <div className={`tree-branch ${hasChildren ? "has-children" : ""}`}>
      <div
        className={`tree-row ${hasChildren ? "parent-node" : "leaf-node"} ${
          selectedId === task.id ? "selected" : ""
        }`}
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
        {hasChildren ? (
          <button
            className={`group-state ${task.isCompleted ? "completed" : ""}`}
            aria-label={task.isCompleted ? "取消完成父任务" : "完成父任务"}
            title="所有叶子任务完成后，可手动完成父任务"
            onClick={() => onComplete(task)}
          >
            {task.isCompleted ? (
              <Check size={12} weight="bold" />
            ) : (
              <ListChecks size={13} />
            )}
          </button>
        ) : (
          <button
            className={`task-check ${task.isCompleted ? "checked" : ""}`}
            aria-label={task.isCompleted ? "取消完成" : "标记完成"}
            onClick={() => onComplete(task)}
          >
            {task.isCompleted && <Check size={12} weight="bold" />}
          </button>
        )}
        <button className="tree-title" onClick={() => onSelect(task.id)}>
          {task.title}
        </button>
        {task.priority > 0 && (
          <span className={`priority-badge priority-${task.priority}`}>
            {priorityLabels[task.priority]}
          </span>
        )}
        {hasChildren && (
          <span className="tree-parent-progress">
            <i>
              <b
                style={{
                  width: `${task.totalLeafCount ? (task.completedLeafCount / task.totalLeafCount) * 100 : 0}%`,
                }}
              />
            </i>
            <span>
              {task.completedLeafCount}/{task.totalLeafCount}
            </span>
          </span>
        )}
      </div>
      {hasChildren && isExpanded && (
        <div className="tree-children">
          {task.children.map((child) => (
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
        </div>
      )}
    </div>
  );
}

function ParentCandidateRow({
  task,
  currentParentId,
  onSelect,
}: {
  task: TaskNode;
  currentParentId: number | null;
  onSelect(id: number): void;
}) {
  return (
    <>
      <button
        className={`parent-option ${currentParentId === task.id ? "selected" : ""}`}
        style={
          { "--candidate-depth": `${task.depth * 18}px` } as React.CSSProperties
        }
        role="treeitem"
        aria-selected={currentParentId === task.id}
        onClick={() => onSelect(task.id)}
      >
        <span className="parent-option-marker" />
        <span>{task.title}</span>
        {task.hasChildren && (
          <small>
            {task.completedLeafCount}/{task.totalLeafCount}
          </small>
        )}
      </button>
      {task.children.map((child) => (
        <ParentCandidateRow
          key={child.id}
          task={child}
          currentParentId={currentParentId}
          onSelect={onSelect}
        />
      ))}
    </>
  );
}

function ParentTaskPicker({
  task,
  candidates,
  currentParentTitle,
  onMove,
}: {
  task: TaskDto;
  candidates: TaskDto[];
  currentParentTitle: string | null;
  onMove(parentId: number | null): void;
}) {
  const [open, setOpen] = useState(false);
  const [query, setQuery] = useState("");
  const controlRef = useRef<HTMLDivElement>(null);
  const candidateTree = useMemo(
    () => buildTree(filterTasksWithAncestors(candidates, query)),
    [candidates, query],
  );

  useEffect(() => {
    if (!open) return;
    const close = (event: globalThis.MouseEvent) => {
      if (!controlRef.current?.contains(event.target as Node)) setOpen(false);
    };
    const closeOnEscape = (event: globalThis.KeyboardEvent) => {
      if (event.key === "Escape") setOpen(false);
    };
    window.addEventListener("mousedown", close);
    window.addEventListener("keydown", closeOnEscape);
    return () => {
      window.removeEventListener("mousedown", close);
      window.removeEventListener("keydown", closeOnEscape);
    };
  }, [open]);

  const choose = (parentId: number | null) => {
    setOpen(false);
    setQuery("");
    if (parentId !== task.parentId) onMove(parentId);
  };

  return (
    <div className="parent-picker" ref={controlRef}>
      <button
        className="parent-picker-trigger"
        aria-haspopup="tree"
        aria-expanded={open}
        onClick={() => setOpen((value) => !value)}
      >
        <span>
          <small>当前父任务</small>
          <strong>{currentParentTitle ?? "未归类（顶级任务）"}</strong>
        </span>
        <span className="parent-picker-action">更改</span>
      </button>
      {open && (
        <div
          className="parent-picker-popover"
          role="dialog"
          aria-label="选择父任务"
        >
          <label className="parent-picker-search">
            <MagnifyingGlass size={15} />
            <input
              autoFocus
              type="search"
              placeholder="搜索父任务"
              value={query}
              onChange={(event) => setQuery(event.target.value)}
            />
          </label>
          <div className="parent-picker-tree" role="tree">
            <button
              className={`parent-option root-option ${task.parentId == null ? "selected" : ""}`}
              role="treeitem"
              aria-selected={task.parentId == null}
              onClick={() => choose(null)}
            >
              <span className="parent-option-marker" />
              <span>设为顶级任务</span>
              <small>移入未归类任务</small>
            </button>
            {candidateTree.map((candidate) => (
              <ParentCandidateRow
                key={candidate.id}
                task={candidate}
                currentParentId={task.parentId}
                onSelect={choose}
              />
            ))}
            {candidateTree.length === 0 && <p>没有匹配的可用父任务。</p>}
          </div>
        </div>
      )}
    </div>
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
            <span className="section-kicker">
              {task.hasChildren ? "完成父任务" : "完成叶子任务"}
            </span>
            <h2>{task.title}</h2>
          </div>
          <IconButton label="关闭" onClick={onClose}>
            <X size={19} />
          </IconButton>
        </header>
        <p>
          {task.hasChildren
            ? "所有叶子任务已经完成；确认后才会记录这项父级成果。"
            : "可以记录本次实际专注时长；父任务仍需由你手动确认完成。"}
        </p>
        {!task.hasChildren && (
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
        )}
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
  const [rootPriority, setRootPriority] = useState(0);
  const [childTitle, setChildTitle] = useState("");
  const [childPriority, setChildPriority] = useState(0);
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
  const workspaceRef = useRef<HTMLDivElement>(null);
  const paneDrag = useRef<{
    side: "tree" | "inspector";
    pointerId: number;
    startX: number;
    startWidth: number;
  } | null>(null);
  const [treePaneWidth, setTreePaneWidth] = useState(() =>
    storedPaneWidth("taskato.treePaneWidth", TREE_PANE_DEFAULT),
  );
  const [inspectorPaneWidth, setInspectorPaneWidth] = useState(() =>
    storedPaneWidth("taskato.inspectorPaneWidth", INSPECTOR_PANE_DEFAULT),
  );
  useEffect(() => {
    window.localStorage.setItem("taskato.treePaneWidth", String(treePaneWidth));
  }, [treePaneWidth]);
  useEffect(() => {
    window.localStorage.setItem(
      "taskato.inspectorPaneWidth",
      String(inspectorPaneWidth),
    );
  }, [inspectorPaneWidth]);
  useEffect(() => {
    const fitPanesToWindow = () => {
      const total = workspaceRef.current?.getBoundingClientRect().width ?? 0;
      if (!total) return;
      const available = total - CANVAS_PANE_MIN - RESIZER_WIDTH;
      if (treePaneWidth + inspectorPaneWidth <= available) return;
      // 缩小窗口时优先保住中央任务内容，并让左右栏都保持可操作的最小宽度。
      const nextTree = clamp(available - INSPECTOR_PANE_MIN, TREE_PANE_MIN, TREE_PANE_MAX);
      const nextInspector = clamp(available - nextTree, INSPECTOR_PANE_MIN, INSPECTOR_PANE_MAX);
      setTreePaneWidth(nextTree);
      setInspectorPaneWidth(nextInspector);
    };
    fitPanesToWindow();
    window.addEventListener("resize", fitPanesToWindow);
    return () => window.removeEventListener("resize", fitPanesToWindow);
  }, [inspectorPaneWidth, treePaneWidth]);
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
    () => filterTasksWithAncestors(tasks, filter),
    [filter, tasks],
  );
  const roots = useMemo(() => buildTree(filtered), [filtered]);
  const groupedRoots = roots.filter((task) => task.children.length > 0);
  const ungroupedRoots = roots.filter((task) => task.children.length === 0);
  const toggleExpanded = (id: number) =>
    setExpanded((value) => {
      const next = new Set(value);
      next.has(id) ? next.delete(id) : next.add(id);
      return next;
    });
  const selected = tasks.find((task) => task.id === selectedId) ?? null;
  const children = tasks.filter((task) => task.parentId === selectedId);
  const selectedPath = useMemo(() => {
    if (!selected) return [];
    const byId = new Map(tasks.map((task) => [task.id, task]));
    const path: TaskDto[] = [];
    let current: TaskDto | undefined = selected;
    while (current) {
      path.unshift(current);
      current = current.parentId == null ? undefined : byId.get(current.parentId);
    }
    return path;
  }, [selected, tasks]);
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
  const isCompletionPending = activeTimer?.status === "等待处理";

  const add = async (event: FormEvent, parentId: number | null) => {
    event.preventDefault();
    const title = parentId == null ? rootTitle.trim() : childTitle.trim();
    const priority = parentId == null ? rootPriority : childPriority;
    if (!title) return;
    try {
      await bridge.request("tasks.add", { title, parentId, priority });
      if (parentId == null) {
        setRootTitle("");
        setRootPriority(0);
      } else {
        setChildTitle("");
        setChildPriority(0);
      }
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
      const remaining = Math.max(0, task.totalLeafCount - task.completedLeafCount);
      if (remaining > 0) {
        reportError(new Error(`父任务还有 ${remaining} 个叶子任务未完成。`));
        return;
      }
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

  const availableWidth = () =>
    workspaceRef.current?.getBoundingClientRect().width ?? window.innerWidth;
  const resizePane = (side: "tree" | "inspector", width: number) => {
    const total = availableWidth();
    if (side === "tree") {
      setTreePaneWidth(
        clamp(
          width,
          TREE_PANE_MIN,
          Math.min(
            TREE_PANE_MAX,
            total - inspectorPaneWidth - CANVAS_PANE_MIN - RESIZER_WIDTH,
          ),
        ),
      );
    } else {
      setInspectorPaneWidth(
        clamp(
          width,
          INSPECTOR_PANE_MIN,
          Math.min(
            INSPECTOR_PANE_MAX,
            total - treePaneWidth - CANVAS_PANE_MIN - RESIZER_WIDTH,
          ),
        ),
      );
    }
  };
  const beginPaneResize =
    (side: "tree" | "inspector") =>
    (event: PointerEvent<HTMLDivElement>) => {
      paneDrag.current = {
        side,
        pointerId: event.pointerId,
        startX: event.clientX,
        startWidth: side === "tree" ? treePaneWidth : inspectorPaneWidth,
      };
      event.currentTarget.setPointerCapture(event.pointerId);
    };
  const movePaneResize = (event: PointerEvent<HTMLDivElement>) => {
    const drag = paneDrag.current;
    if (!drag || drag.pointerId !== event.pointerId) return;
    const delta = event.clientX - drag.startX;
    resizePane(
      drag.side,
      drag.startWidth + (drag.side === "tree" ? delta : -delta),
    );
  };
  const finishPaneResize = (event: PointerEvent<HTMLDivElement>) => {
    if (paneDrag.current?.pointerId !== event.pointerId) return;
    event.currentTarget.releasePointerCapture(event.pointerId);
    paneDrag.current = null;
  };
  const resizePaneByKeyboard =
    (side: "tree" | "inspector") =>
    (event: KeyboardEvent<HTMLDivElement>) => {
      if (event.key !== "ArrowLeft" && event.key !== "ArrowRight") return;
      event.preventDefault();
      const delta = event.key === "ArrowRight" ? 12 : -12;
      resizePane(
        side,
        (side === "tree" ? treePaneWidth : inspectorPaneWidth) +
          (side === "tree" ? delta : -delta),
      );
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
    <div
      className="task-workspace page-enter"
      ref={workspaceRef}
      style={
        {
          "--tree-pane-width": `${treePaneWidth}px`,
          "--inspector-pane-width": `${inspectorPaneWidth}px`,
        } as React.CSSProperties
      }
    >
      <section className="tree-pane">
        <div className="pane-heading">
          <div>
            <span className="section-kicker">工作空间</span>
            <h2>项目与任务</h2>
          </div>
        </div>
        <form
          className="root-task-composer"
          onSubmit={(event) => void add(event, null)}
        >
          <div className="composer-input-row">
            <input
              aria-label="新建顶级任务"
              value={rootTitle}
              onChange={(event) => setRootTitle(event.target.value)}
              onKeyDown={(event) => {
                if (event.key !== "Enter" || event.nativeEvent.isComposing) return;
                event.preventDefault();
                event.currentTarget.form?.requestSubmit();
              }}
              placeholder="输入任务标题，按 Enter 新增"
            />
            <button
              type="submit"
              aria-label="添加顶级任务"
              disabled={!rootTitle.trim()}
            >
              <Plus size={16} />
              新增
            </button>
          </div>
          <PrioritySelector
            label="新任务优先级"
            value={rootPriority}
            onChange={setRootPriority}
          />
        </form>
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
          <span>{filtered.length} 个任务</span>
        </div>
        <div className="task-tree">
          <div className="tree-section-label">
            <span>任务组</span>
            <small>{groupedRoots.length}</small>
          </div>
          {groupedRoots.map((task) => (
            <TaskTreeRow
              key={task.id}
              task={task}
              selectedId={selectedId}
              expanded={expanded}
              onToggle={toggleExpanded}
              onSelect={setSelectedId}
              onComplete={toggleComplete}
            />
          ))}
          <div className="tree-section-label ungrouped">
            <span>未归类任务</span>
            <small>{ungroupedRoots.length}</small>
          </div>
          {ungroupedRoots.map((task) => (
            <TaskTreeRow
              key={task.id}
              task={task}
              selectedId={selectedId}
              expanded={expanded}
              onToggle={toggleExpanded}
              onSelect={setSelectedId}
              onComplete={toggleComplete}
            />
          ))}
        </div>
      </section>
      <div
        className="pane-resizer"
        role="separator"
        aria-label="调整任务树宽度"
        aria-orientation="vertical"
        tabIndex={0}
        onPointerDown={beginPaneResize("tree")}
        onPointerMove={movePaneResize}
        onPointerUp={finishPaneResize}
        onPointerCancel={finishPaneResize}
        onKeyDown={resizePaneByKeyboard("tree")}
      />
      <main className="task-canvas">
        {selected ? (
          <>
            <div className="canvas-header">
              <div className="breadcrumbs">
                <span>任务</span>
                {selectedPath.map((item) => (
                  <span className="breadcrumb-part" key={item.id}>
                    <CaretRight size={13} />
                    <span>{item.title}</span>
                  </span>
                ))}
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
                    ? "所有叶子任务完成后，由你手动确认父任务完成。"
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
                优先级：{priorityLabels[selected.priority]}
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
                  className="inline-add subtask-composer"
                  onSubmit={(event) => void add(event, selected.id)}
                >
                  <div className="subtask-input-row">
                    <button type="submit" aria-label="添加子任务">
                      <Plus size={16} />
                    </button>
                    <input
                      aria-label="新建子任务"
                      value={childTitle}
                      onChange={(event) => setChildTitle(event.target.value)}
                      placeholder="添加子任务，按 Enter 保存"
                    />
                  </div>
                  <PrioritySelector
                    label="子任务优先级"
                    value={childPriority}
                    onChange={setChildPriority}
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
                <ParentTaskPicker
                  task={selected}
                  candidates={parentCandidates}
                  currentParentTitle={
                    tasks.find((task) => task.id === selected.parentId)?.title ??
                    null
                  }
                  onMove={(parentId) => void moveTask(parentId)}
                />
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
      <div
        className="pane-resizer"
        role="separator"
        aria-label="调整任务详情宽度"
        aria-orientation="vertical"
        tabIndex={0}
        onPointerDown={beginPaneResize("inspector")}
        onPointerMove={movePaneResize}
        onPointerUp={finishPaneResize}
        onPointerCancel={finishPaneResize}
        onKeyDown={resizePaneByKeyboard("inspector")}
      />
      <aside className="inspector-pane">
        <section className="focus-panel timer-card">
          <div className="focus-heading">
            <div>
              <Timer size={19} />
              <span>当前专注</span>
            </div>
            <span className="live-label timer-status-pill">
              {activeTimer?.status ?? "未连接"}
            </span>
          </div>
          <label className="timer-picker-row">
            <span>计时器</span>
            <select
              className="timer-select"
              aria-label="选择番茄钟"
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
          </label>
          <div className="timer-display-stage">
            <span>专注倒计时</span>
            <strong className="timer-value">
              {activeTimer?.display ?? "25:00"}
            </strong>
            <p>{selected?.title ?? "选择任务后开始专注"}</p>
            <div className="timer-progress" aria-label="当前计时进度">
              <span style={{ width: `${(activeTimer?.progress ?? 1) * 100}%` }} />
            </div>
          </div>
          <div className="timer-duration-controls" aria-label="调整番茄钟时长">
            <button
              type="button"
              aria-label="减少五分钟"
              disabled={activeTimer?.isStarted ?? true}
              onClick={() => void timerCommand("decrease")}
            >
              −5 分钟
            </button>
            <span>{activeTimer?.workMinutes ?? 25} 分钟专注</span>
            <button
              type="button"
              aria-label="增加五分钟"
              disabled={activeTimer?.isStarted ?? true}
              onClick={() => void timerCommand("increase")}
            >
              ＋5 分钟
            </button>
          </div>
          <div className="timer-controls">
            <button
              className="primary-control"
              disabled={
                isCompletionPending || (activeTimer?.isStarted
                  ? !activeTimer.canPause
                  : !activeTimer?.canStart)
              }
              onClick={() =>
                void timerCommand(activeTimer?.isStarted ? "pause" : "start")
              }
            >
              {isCompletionPending ? (
                <Timer size={18} />
              ) : activeTimer?.isStarted ? (
                <Pause size={18} />
              ) : (
                <Play size={18} />
              )}
              {isCompletionPending
                ? "请处理完成提醒"
                : activeTimer?.isStarted
                  ? "暂停 / 继续"
                  : "开始专注"}
            </button>
          </div>
          <div className="timer-secondary-controls timer-action-row">
            <button
              disabled={!activeTimer?.canStop}
              onClick={() => void timerCommand("stop")}
            >
              <Stop size={15} />
              停止
            </button>
            <button
              disabled={!activeTimer?.canRest}
              onClick={() => void timerCommand("rest")}
            >
              <Timer size={15} />
              提前休息
            </button>
          </div>
          <div className="timer-secondary-controls timer-management-row">
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
