import {
  useCallback,
  useEffect,
  useMemo,
  useRef,
  useState,
  type FormEvent,
  type KeyboardEvent,
  type MouseEvent,
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
  type ProgressRecordDto,
  type SettingsDto,
  type SummaryResult,
  type TaskActivityDto,
  type TaskDto,
  type TaskListResult,
  type TaskProgressResult,
  type TimerListResult,
} from "../bridge";
import {
  applyTheme,
  IconButton,
  toLocalDateInput,
  useBridgeRefresh,
} from "../ui";
import { TaskPicker } from "../components/TaskPicker";

type TaskNode = TaskDto & { children: TaskNode[] };

const TASK_EXPANSION_STORAGE_KEY = "taskato.expandedTaskIds";
const PROGRESS_RECORD_PREVIEW_COUNT = 3;

function loadStoredTaskExpansion(): {
  hasStoredValue: boolean;
  taskIds: Set<number>;
} {
  const rawValue = window.localStorage.getItem(TASK_EXPANSION_STORAGE_KEY);
  if (rawValue == null) return { hasStoredValue: false, taskIds: new Set() };
  try {
    const parsed = JSON.parse(rawValue) as unknown;
    if (!Array.isArray(parsed))
      return { hasStoredValue: false, taskIds: new Set() };
    return {
      hasStoredValue: true,
      taskIds: new Set(
        parsed.filter(
          (taskId): taskId is number =>
            Number.isInteger(taskId) && taskId > 0,
        ),
      ),
    };
  } catch {
    return { hasStoredValue: false, taskIds: new Set() };
  }
}

function taskCompletionTimestamp(task: TaskDto): number {
  const timestamp = task.completedAt ? Date.parse(task.completedAt) : NaN;
  // 旧数据可能缺少完成时间；放到已完成分组末尾，不能挡住用户刚完成的任务。
  return Number.isFinite(timestamp) ? timestamp : 0;
}

function compareSubtaskDisplayOrder(left: TaskDto, right: TaskDto): number {
  const completionGroupOrder =
    Number(left.isCompleted) - Number(right.isCompleted);
  if (completionGroupOrder !== 0) return completionGroupOrder;
  if (!left.isCompleted) return 0;
  return taskCompletionTimestamp(right) - taskCompletionTimestamp(left);
}

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
  nodes.forEach((node) => node.children.sort(compareSubtaskDisplayOrder));
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
const PRIORITY_CONTEXT_MENU_WIDTH = 176;
const PRIORITY_CONTEXT_MENU_HEIGHT = 190;
const CONTEXT_MENU_VIEWPORT_GAP = 8;

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
  caption = "优先级",
  className = "",
  disabled = false,
}: {
  value: number;
  onChange(value: number): void;
  label: string;
  caption?: string;
  className?: string;
  disabled?: boolean;
}) {
  return (
    <div
      className={`priority-selector ${className}`.trim()}
      role="radiogroup"
      aria-label={label}
      aria-busy={disabled}
    >
      <span>{caption}</span>
      {priorityLabels.map((priority, index) => (
        <button
          key={priority}
          type="button"
          role="radio"
          aria-label={`${label}：${priority}`}
          aria-checked={value === index}
          disabled={disabled}
          className={`priority-option priority-${index} ${value === index ? "selected" : ""}`}
          onClick={() => onChange(index)}
        >
          {priority}
        </button>
      ))}
    </div>
  );
}

function PriorityContextMenu({
  task,
  left,
  top,
  disabled,
  onChange,
  onClose,
}: {
  task: TaskDto;
  left: number;
  top: number;
  disabled: boolean;
  onChange(priority: number): void;
  onClose(): void;
}) {
  const menuRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    const closeOnOutsideClick = (event: globalThis.MouseEvent) => {
      if (!menuRef.current?.contains(event.target as Node)) onClose();
    };
    const handleKeyDown = (event: globalThis.KeyboardEvent) => {
      if (event.key === "Escape") {
        event.preventDefault();
        onClose();
      }
    };
    const focusTimer = window.setTimeout(() => {
      menuRef.current
        ?.querySelector<HTMLButtonElement>('[aria-checked="true"]')
        ?.focus();
    });
    window.addEventListener("mousedown", closeOnOutsideClick);
    window.addEventListener("keydown", handleKeyDown);
    window.addEventListener("resize", onClose);
    window.addEventListener("blur", onClose);
    return () => {
      window.clearTimeout(focusTimer);
      window.removeEventListener("mousedown", closeOnOutsideClick);
      window.removeEventListener("keydown", handleKeyDown);
      window.removeEventListener("resize", onClose);
      window.removeEventListener("blur", onClose);
    };
  }, [onClose]);

  const moveFocus = (event: KeyboardEvent<HTMLDivElement>) => {
    if (!["ArrowDown", "ArrowUp", "Home", "End"].includes(event.key)) return;
    event.preventDefault();
    const items = [
      ...(menuRef.current?.querySelectorAll<HTMLButtonElement>(
        '[role="menuitemradio"]:not(:disabled)',
      ) ?? []),
    ];
    if (items.length === 0) return;
    const currentIndex = items.indexOf(document.activeElement as HTMLButtonElement);
    if (event.key === "Home") items[0].focus();
    else if (event.key === "End") items.at(-1)?.focus();
    else {
      const offset = event.key === "ArrowDown" ? 1 : -1;
      const nextIndex = (currentIndex + offset + items.length) % items.length;
      items[nextIndex].focus();
    }
  };

  return (
    <div
      ref={menuRef}
      className="priority-context-menu"
      role="menu"
      aria-label={`修改“${task.title}”的优先级`}
      style={{ left, top }}
      onContextMenu={(event) => event.preventDefault()}
      onKeyDown={moveFocus}
    >
      <span className="priority-context-menu-title" role="presentation">
        切换优先级
      </span>
      {priorityLabels.map((priority, index) => (
        <button
          key={priority}
          type="button"
          role="menuitemradio"
          aria-label={`切换为${priority}优先级`}
          aria-checked={task.priority === index}
          disabled={disabled}
          className={`priority-context-option priority-${index}`}
          onClick={() => onChange(index)}
        >
          <span>{priority}</span>
          {task.priority === index && <Check size={14} weight="bold" />}
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
  onEditContent,
  onComplete,
  onOpenPriorityMenu,
}: {
  task: TaskNode;
  selectedId: number | null;
  expanded: Set<number>;
  onToggle(id: number): void;
  onSelect(id: number): void;
  onEditContent(id: number): void;
  onComplete(task: TaskDto): void;
  onOpenPriorityMenu(task: TaskDto, clientX: number, clientY: number): void;
}) {
  const isParent = task.hasChildren;
  const hasVisibleChildren = task.children.length > 0;
  const isExpanded = expanded.has(task.id);
  return (
    <div className={`tree-branch ${hasVisibleChildren ? "has-children" : ""}`}>
      <div
        className={`tree-row ${isParent ? "parent-node" : "leaf-node"} ${
          selectedId === task.id ? "selected" : ""
        }`}
        onContextMenu={(event) => {
          event.preventDefault();
          onOpenPriorityMenu(task, event.clientX, event.clientY);
        }}
      >
        <button
          className="tree-expand"
          aria-label={
            hasVisibleChildren
              ? isExpanded
                ? "收起子任务"
                : "展开子任务"
              : "无子任务"
          }
          onClick={() => hasVisibleChildren && onToggle(task.id)}
        >
          {hasVisibleChildren ? (
            <CaretRight className={isExpanded ? "rotated" : ""} size={14} />
          ) : (
            <span />
          )}
        </button>
        {isParent ? (
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
        <button
          className="tree-title"
          title={task.parentId == null ? undefined : "双击编辑任务内容"}
          onClick={() => onSelect(task.id)}
          onDoubleClick={() => task.parentId != null && onEditContent(task.id)}
        >
          {task.title}
        </button>
        {task.priority > 0 && (
          <span className={`priority-badge priority-${task.priority}`}>
            {priorityLabels[task.priority]}
          </span>
        )}
        {isParent && (
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
      {hasVisibleChildren && isExpanded && (
        <div className="tree-children">
          {task.children.map((child) => (
            <TaskTreeRow
              key={child.id}
              task={child}
              selectedId={selectedId}
              expanded={expanded}
              onToggle={onToggle}
              onSelect={onSelect}
              onEditContent={onEditContent}
              onComplete={onComplete}
              onOpenPriorityMenu={onOpenPriorityMenu}
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

function toDateTimeFields(value: string | Date): { date: string; time: string } {
  const date = new Date(value);
  return {
    date: toLocalDateInput(date),
    time: date.toTimeString().slice(0, 5),
  };
}

function formatProgressDuration(minutes: number): string {
  const hours = Math.floor(minutes / 60);
  const remainder = minutes % 60;
  if (hours && remainder) return `${hours} 小时 ${remainder} 分钟`;
  if (hours) return `${hours} 小时`;
  return `${minutes} 分钟`;
}

function ProgressEditorDialog({
  tasks,
  initialTaskId,
  record,
  reportError,
  onClose,
  onSaved,
}: {
  tasks: TaskDto[];
  initialTaskId: number | null;
  record?: ProgressRecordDto | null;
  reportError(error: unknown): void;
  onClose(): void;
  onSaved(): Promise<void>;
}) {
  const initialDateTime = toDateTimeFields(record?.completedAt ?? new Date());
  const [taskId, setTaskId] = useState(record?.taskId ?? initialTaskId);
  const [note, setNote] = useState(record?.note ?? "");
  const [duration, setDuration] = useState(
    String(record?.durationMinutes ?? 25),
  );
  const [date, setDate] = useState(initialDateTime.date);
  const [time, setTime] = useState(initialDateTime.time);
  const [saving, setSaving] = useState(false);
  const durationMinutes = Number(duration);
  const validDuration = Number.isInteger(durationMinutes) &&
    durationMinutes >= 1 && durationMinutes <= 1440;

  const save = async () => {
    if (!validDuration) return;
    try {
      setSaving(true);
      if (record) {
        await bridge.request("progress.update", {
          recordId: record.id,
          taskId,
          note,
          durationMinutes,
        });
      } else {
        await bridge.request("progress.create", {
          taskId,
          note,
          durationMinutes,
          completedAt: new Date(`${date}T${time}:00`).toISOString(),
        });
      }
      await onSaved();
      onClose();
    } catch (error) {
      reportError(error);
    } finally {
      setSaving(false);
    }
  };
  const remove = async () => {
    if (!record || !window.confirm("删除这条推进记录吗？任务状态不会自动回退。")) return;
    try {
      setSaving(true);
      await bridge.request("progress.delete", { recordId: record.id });
      await onSaved();
      onClose();
    } catch (error) {
      reportError(error);
    } finally {
      setSaving(false);
    }
  };

  return (
    <div
      className="modal-backdrop"
      onMouseDown={(event) => event.target === event.currentTarget && onClose()}
    >
      <section className="modal progress-editor-modal" role="dialog" aria-modal="true">
        <header>
          <div>
            <span className="section-kicker">
              {record ? "编辑推进记录" : "记录当前进展"}
            </span>
            <h2>{record ? "修改推进" : "新增推进"}</h2>
          </div>
          <IconButton label="关闭" onClick={onClose}>
            <X size={19} />
          </IconButton>
        </header>
        <label className="progress-note-field">
          <span>推进内容（可选）</span>
          <textarea
            autoFocus
            maxLength={5000}
            placeholder="记录这次完成或推进了什么"
            value={note}
            onChange={(event) => setNote(event.target.value)}
          />
        </label>
        <span className="field-label">所属任务</span>
        <TaskPicker tasks={tasks} value={taskId} onChange={setTaskId} embedded />
        <div className="modal-fields progress-fields">
          {!record && (
            <>
              <label>
                <span>日期</span>
                <input
                  type="date"
                  max={toLocalDateInput()}
                  value={date}
                  onChange={(event) => setDate(event.target.value)}
                />
              </label>
              <label>
                <span>完成时间</span>
                <input
                  type="time"
                  value={time}
                  onChange={(event) => setTime(event.target.value)}
                />
              </label>
            </>
          )}
          <label>
            <span>统计时长（分钟）</span>
            <input
              type="number"
              min="1"
              max="1440"
              value={duration}
              onChange={(event) => setDuration(event.target.value)}
            />
          </label>
        </div>
        {record && record.originalDurationMinutes > 0 &&
          record.originalDurationMinutes !== durationMinutes && (
            <div className="adjusted-duration-note">
              <span>原始计时：{record.originalDurationMinutes} 分钟</span>
              <button
                type="button"
                onClick={() => setDuration(String(record.originalDurationMinutes))}
              >
                恢复原始计时
              </button>
            </div>
          )}
        <footer>
          {record && (
            <button className="danger-text-button" onClick={() => void remove()}>
              删除记录
            </button>
          )}
          <span className="modal-footer-spacer" />
          <button className="secondary-button" onClick={onClose}>取消</button>
          <button
            className="primary-button"
            disabled={!validDuration || saving}
            onClick={() => void save()}
          >
            {saving ? "正在保存…" : "保存推进"}
          </button>
        </footer>
      </section>
    </div>
  );
}

export function TaskPage({
  reportError,
  requestedTaskId,
  sessionSelectedTaskId = null,
  sessionProgressTargetTaskId = null,
  sessionTaskViewMode = "detail",
  onSelectedTaskIdChange,
  onProgressTargetTaskIdChange,
  onTaskViewModeChange,
  onRequestedTaskHandled,
}: {
  reportError(error: unknown): void;
  requestedTaskId: number | null;
  sessionSelectedTaskId?: number | null;
  sessionProgressTargetTaskId?: number | null;
  sessionTaskViewMode?: "detail" | "create";
  onSelectedTaskIdChange?(taskId: number | null): void;
  onProgressTargetTaskIdChange?(taskId: number | null): void;
  onTaskViewModeChange?(mode: "detail" | "create"): void;
  onRequestedTaskHandled?(): void;
}) {
  const [tasks, setTasks] = useState<TaskDto[]>([]);
  const [detailTasks, setDetailTasks] = useState<TaskDto[]>([]);
  const [selectedId, setSelectedId] = useState<number | null>(
    sessionSelectedTaskId,
  );
  const [progressTargetTaskId, setProgressTargetTaskId] = useState<
    number | null
  >(sessionProgressTargetTaskId);
  const progressTargetOwnerId = useRef<number | null>(sessionSelectedTaskId);
  const [taskViewMode, setTaskViewMode] = useState<"detail" | "create">(
    sessionTaskViewMode,
  );
  const [storedTaskExpansion] = useState(loadStoredTaskExpansion);
  const [expanded, setExpanded] = useState(
    () => new Set(storedTaskExpansion.taskIds),
  );
  const expansionInitialized = useRef(false);
  const [filter, setFilter] = useState("");
  const [rootTitle, setRootTitle] = useState("");
  const [rootPriority, setRootPriority] = useState(0);
  const [newTaskParentId, setNewTaskParentId] = useState<number | null>(null);
  const [childTitle, setChildTitle] = useState("");
  const [childPriority, setChildPriority] = useState(0);
  const [contentDraft, setContentDraft] = useState("");
  const [contentStatus, setContentStatus] = useState<
    "saved" | "unsaved" | "saving" | "error"
  >("saved");
  const [taskMenuOpen, setTaskMenuOpen] = useState(false);
  const [priorityContextMenu, setPriorityContextMenu] = useState<{
    task: TaskDto;
    left: number;
    top: number;
  } | null>(null);
  const [undoAction, setUndoAction] = useState<{
    message: string;
    run(): Promise<void>;
  } | null>(null);
  const contentRequestVersion = useRef(0);
  const contentFocusTaskId = useRef<number | null>(null);
  const [completionTask, setCompletionTask] = useState<TaskDto | null>(null);
  const [progressScope, setProgressScope] = useState<"own" | "all">("own");
  const [progressResult, setProgressResult] = useState<TaskProgressResult | null>(null);
  const [progressEditor, setProgressEditor] = useState<
    ProgressRecordDto | "new" | null
  >(null);
  const [progressRecordsExpanded, setProgressRecordsExpanded] = useState(false);
  const [prioritySaving, setPrioritySaving] = useState(false);
  const [completedChildrenExpanded, setCompletedChildrenExpanded] =
    useState(false);
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
    if (!expansionInitialized.current) return;
    window.localStorage.setItem(
      TASK_EXPANSION_STORAGE_KEY,
      JSON.stringify([...expanded]),
    );
  }, [expanded]);
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
      setDetailTasks(result.detailTasks);
      setSelectedId((current) => {
        if (result.detailTasks.some((task) => task.id === current)) return current;
        return result.detailTasks.some((task) => task.id === result.selectedTaskId)
          ? result.selectedTaskId
          : (result.tasks[0]?.id ?? null);
      });
      const expandableIds = new Set(
        result.tasks
          .map((task) => task.parentId)
          .filter((parentId): parentId is number => parentId != null),
      );
      if (!expansionInitialized.current) {
        // 第一次使用仍默认全展开；已有记录（包括空集合）必须原样恢复，避免切换菜单后复位。
        expansionInitialized.current = true;
        setExpanded(
          storedTaskExpansion.hasStoredValue
            ? new Set(
                [...storedTaskExpansion.taskIds].filter((taskId) =>
                  expandableIds.has(taskId),
                ),
              )
            : expandableIds,
        );
      } else {
        setExpanded(
          (current) =>
            new Set([...current].filter((taskId) => expandableIds.has(taskId))),
        );
      }
    } catch (error) {
      reportError(error);
    }
  }, [reportError, storedTaskExpansion]);
  const loadTimers = useCallback(async () => {
    try {
      setTimerState(await bridge.request<TimerListResult>("pomodoro.list"));
    } catch (error) {
      reportError(error);
    }
  }, [reportError]);
  const loadProgress = useCallback(async () => {
    if (selectedId == null || taskViewMode !== "detail") {
      setProgressResult(null);
      return;
    }
    try {
      setProgressResult(
        await bridge.request<TaskProgressResult>("progress.list", {
          taskId: selectedId,
          includeDescendants: progressScope === "all",
        }),
      );
    } catch (error) {
      reportError(error);
    }
  }, [progressScope, reportError, selectedId, taskViewMode]);
  useEffect(() => {
    void load();
    void loadTimers();
    void loadProgress();
  }, [load, loadProgress, loadTimers]);
  useBridgeRefresh("tasks.changed", () => {
    void load();
    void loadProgress();
  });
  useBridgeRefresh("pomodoro.changed", () => void loadTimers());
  useEffect(() => {
    // 选中项只提升到应用会话，不写入本地存储；关闭应用后仍按原规则选择默认任务。
    onSelectedTaskIdChange?.(selectedId);
  }, [onSelectedTaskIdChange, selectedId]);
  useEffect(() => {
    onProgressTargetTaskIdChange?.(progressTargetTaskId);
  }, [onProgressTargetTaskIdChange, progressTargetTaskId]);
  useEffect(() => {
    if (progressTargetOwnerId.current === selectedId) return;
    progressTargetOwnerId.current = selectedId;
    setProgressTargetTaskId(null);
  }, [selectedId]);
  useEffect(() => {
    onTaskViewModeChange?.(taskViewMode);
  }, [onTaskViewModeChange, taskViewMode]);
  useEffect(() => {
    if (requestedTaskId == null) return;
    setTaskViewMode("detail");
    setSelectedId(requestedTaskId);
    onRequestedTaskHandled?.();
    requestAnimationFrame(() =>
      document
        .querySelector<HTMLElement>(".task-title-display")
        ?.scrollIntoView?.({ block: "center" }),
    );
  }, [onRequestedTaskHandled, requestedTaskId]);
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
  const groupedRoots = roots.filter((task) => task.hasChildren);
  const ungroupedRoots = roots.filter((task) => !task.hasChildren);
  const expandableTaskIds = useMemo(
    () =>
      new Set(
        tasks
          .map((task) => task.parentId)
          .filter((parentId): parentId is number => parentId != null),
      ),
    [tasks],
  );
  const hasExpandedTasks = [...expandableTaskIds].some((taskId) =>
    expanded.has(taskId),
  );
  const toggleAllExpanded = () =>
    setExpanded(hasExpandedTasks ? new Set() : new Set(expandableTaskIds));
  const toggleExpanded = (id: number) =>
    setExpanded((value) => {
      const next = new Set(value);
      next.has(id) ? next.delete(id) : next.add(id);
      return next;
    });
  const selected = detailTasks.find((task) => task.id === selectedId) ?? null;
  const children = useMemo(
    () =>
      detailTasks
        .filter((task) => task.parentId === selectedId)
        .sort(compareSubtaskDisplayOrder),
    [detailTasks, selectedId],
  );
  const progressTarget =
    children.find((task) => task.id === progressTargetTaskId) ?? null;
  const incompleteChildren = children.filter((task) => !task.isCompleted);
  const completedChildren = children.filter((task) => task.isCompleted);
  const collapseCompletedChildren =
    selected?.depth === 0 && completedChildren.length > 0;
  const progressRecords = progressResult?.records ?? [];
  const hasHiddenProgressRecords =
    progressRecords.length > PROGRESS_RECORD_PREVIEW_COUNT;
  const visibleProgressRecords = progressRecordsExpanded
    ? progressRecords
    : progressRecords.slice(0, PROGRESS_RECORD_PREVIEW_COUNT);
  const selectedPath = useMemo(() => {
    if (!selected) return [];
    const byId = new Map(detailTasks.map((task) => [task.id, task]));
    const path: TaskDto[] = [];
    let current: TaskDto | undefined = selected;
    while (current) {
      path.unshift(current);
      current = current.parentId == null ? undefined : byId.get(current.parentId);
    }
    return path;
  }, [detailTasks, selected]);
  useEffect(
    () => {
      setContentDraft(selected?.title ?? "");
      setContentStatus("saved");
    },
    [selected?.id, selected?.title],
  );
  useEffect(() => setCompletedChildrenExpanded(false), [selected?.id]);
  useEffect(() => {
    if (collapseCompletedChildren && progressTarget?.isCompleted)
      setCompletedChildrenExpanded(true);
  }, [collapseCompletedChildren, progressTarget?.id, progressTarget?.isCompleted]);
  useEffect(
    () => setProgressRecordsExpanded(false),
    [progressScope, selected?.id],
  );
  useEffect(() => {
    if (!selected || progressTargetTaskId == null || progressTarget) return;
    setProgressTargetTaskId(null);
  }, [progressTarget, progressTargetTaskId, selected]);

  const focusTaskContentEditor = useCallback(() => {
    const editor = document.querySelector<HTMLTextAreaElement>(
      ".task-content-input",
    );
    editor?.scrollIntoView?.({ behavior: "smooth", block: "center" });
    editor?.focus();
  }, []);

  useEffect(() => {
    if (selectedId == null || contentFocusTaskId.current !== selectedId) return;
    contentFocusTaskId.current = null;
    focusTaskContentEditor();
  }, [focusTaskContentEditor, selectedId]);

  const editTaskContent = (taskId: number) => {
    setTaskViewMode("detail");
    if (selectedId === taskId) {
      focusTaskContentEditor();
      return;
    }
    contentFocusTaskId.current = taskId;
    setSelectedId(taskId);
  };

  const selectTask = (taskId: number) => {
    setTaskViewMode("detail");
    setSelectedId(taskId);
  };

  const saveTaskContent = useCallback(
    async (task: TaskDto, draft: string) => {
      const content = draft.trim();
      if (!content) {
        setContentStatus("error");
        return;
      }
      if (content === task.title) {
        setContentStatus("saved");
        return;
      }

      const version = ++contentRequestVersion.current;
      setContentStatus("saving");
      try {
        await bridge.request("tasks.update", {
          taskId: task.id,
          title: content,
        });
        if (contentRequestVersion.current !== version) return;
        setContentStatus("saved");
        await load();
      } catch (error) {
        if (contentRequestVersion.current !== version) return;
        setContentStatus("error");
        reportError(error);
      }
    },
    [load, reportError],
  );
  const activeTimer =
    timerState.timers.find((timer) => timer.id === timerState.activeId) ??
    timerState.timers[0];
  const isCompletionPending = activeTimer?.status === "等待处理";

  const add = async (
    event: FormEvent,
    parentId: number | null,
    source: "root" | "child",
  ) => {
    event.preventDefault();
    const title = source === "root" ? rootTitle.trim() : childTitle.trim();
    const priority = source === "root" ? rootPriority : childPriority;
    if (!title) return;
    try {
      const created = await bridge.request<TaskDto>("tasks.add", {
        title,
        parentId,
        priority,
      });
      if (source === "root") {
        setRootTitle("");
        setRootPriority(0);
        setNewTaskParentId(null);
        setSelectedId(created.id);
        setTaskViewMode("detail");
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
  const updateTaskPriority = async (task: TaskDto, priority: number) => {
    if (prioritySaving || priority === task.priority) return;
    const taskId = task.id;
    const previousPriority = task.priority;
    const applyPriority = (items: TaskDto[], value: number) =>
      items.map((item) =>
        item.id === taskId ? { ...item, priority: value } : item,
      );

    setTasks((current) => applyPriority(current, priority));
    setDetailTasks((current) => applyPriority(current, priority));
    setPrioritySaving(true);
    try {
      await bridge.request("tasks.update", { taskId, priority });
      await load();
      offerUndo(
        `已将“${task.title}”优先级设为“${priorityLabels[priority]}”`,
        async () => {
          await bridge.request("tasks.update", {
            taskId,
            priority: previousPriority,
          });
          await load();
        },
      );
    } catch (error) {
      await load();
      reportError(error);
    } finally {
      setPrioritySaving(false);
    }
  };
  const openPriorityContextMenu = (
    task: TaskDto,
    clientX: number,
    clientY: number,
  ) => {
    const left = clamp(
      clientX,
      CONTEXT_MENU_VIEWPORT_GAP,
      window.innerWidth -
        PRIORITY_CONTEXT_MENU_WIDTH -
        CONTEXT_MENU_VIEWPORT_GAP,
    );
    const top = clamp(
      clientY,
      CONTEXT_MENU_VIEWPORT_GAP,
      window.innerHeight -
        PRIORITY_CONTEXT_MENU_HEIGHT -
        CONTEXT_MENU_VIEWPORT_GAP,
    );
    setPriorityContextMenu({ task, left, top });
  };
  const closePriorityContextMenu = useCallback(
    () => setPriorityContextMenu(null),
    [],
  );
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

  const clearProgressTargetFromBlank = (event: MouseEvent<HTMLElement>) => {
    if (progressTargetTaskId == null) return;
    const target = event.target;
    if (!(target instanceof HTMLElement)) return;
    if (
      target === event.currentTarget ||
      target.classList.contains("canvas-header") ||
      target.classList.contains("content-section") ||
      target.classList.contains("subtask-list") ||
      target.classList.contains("progress-track")
    ) {
      setProgressTargetTaskId(null);
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
    if (!selected || contentDraft === selected.title) {
      setContentStatus("saved");
      return;
    }
    setContentStatus("unsaved");
    const timer = window.setTimeout(() => {
      void saveTaskContent(selected, contentDraft);
    }, 700);
    return () => window.clearTimeout(timer);
  }, [contentDraft, saveTaskContent, selected]);

  const renderSubtaskRow = (item: TaskDto) => {
    const isProgressTarget = item.id === progressTarget?.id;
    return (
      <div
        className={`subtask-row ${item.isCompleted ? "completed" : ""} ${isProgressTarget ? "progress-target" : ""}`.trim()}
        key={item.id}
        role="listitem"
        tabIndex={0}
        aria-current={isProgressTarget ? "true" : undefined}
        aria-label={`${item.title}${isProgressTarget ? "，当前推进目标" : "，单击设为推进目标"}`}
        onClick={() => setProgressTargetTaskId(item.id)}
        onKeyDown={(event) => {
          if (event.target !== event.currentTarget) return;
          if (event.key !== "Enter" && event.key !== " ") return;
          event.preventDefault();
          setProgressTargetTaskId(item.id);
        }}
        onContextMenu={(event) => {
          event.preventDefault();
          openPriorityContextMenu(item, event.clientX, event.clientY);
        }}
      >
        <button
          className={`task-check ${item.isCompleted ? "checked" : ""}`}
          aria-label={item.isCompleted ? "取消完成" : "标记完成"}
          onClick={(event) => {
            event.stopPropagation();
            toggleComplete(item);
          }}
        >
          {item.isCompleted && <Check size={12} />}
        </button>
        <span
          className={item.isCompleted ? "done" : ""}
          title="双击编辑任务内容"
          onDoubleClick={() => editTaskContent(item.id)}
        >
          {item.title}
        </span>
        <span className="subtask-row-badges">
          {item.priority > 0 && (
            <span
              className={`priority-badge subtask-priority-badge priority-${item.priority}`}
              title={`优先级：${priorityLabels[item.priority]}`}
            >
              {priorityLabels[item.priority]}
            </span>
          )}
          {isProgressTarget && (
            <span className="progress-target-badge">推进目标</span>
          )}
        </span>
        <small>
          {item.completionDurationMinutes
            ? `${item.completionDurationMinutes} 分钟`
            : ""}
        </small>
      </div>
    );
  };

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
        <button
          type="button"
          className={`open-task-create ${taskViewMode === "create" ? "active" : ""}`}
          onClick={() => setTaskViewMode("create")}
        >
          <Plus size={17} weight="bold" />
          新增任务
        </button>
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
          <div className="tree-filter-actions">
            <span>{filtered.length} 个任务</span>
            <button
              type="button"
              className="tree-expand-all"
              aria-label={hasExpandedTasks ? "全部收起" : "全部展开"}
              onClick={toggleAllExpanded}
            >
              {hasExpandedTasks ? "全部收起" : "全部展开"}
            </button>
          </div>
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
              selectedId={taskViewMode === "detail" ? selectedId : null}
              expanded={expanded}
              onToggle={toggleExpanded}
              onSelect={selectTask}
              onEditContent={editTaskContent}
              onComplete={toggleComplete}
              onOpenPriorityMenu={openPriorityContextMenu}
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
              selectedId={taskViewMode === "detail" ? selectedId : null}
              expanded={expanded}
              onToggle={toggleExpanded}
              onSelect={selectTask}
              onEditContent={editTaskContent}
              onComplete={toggleComplete}
              onOpenPriorityMenu={openPriorityContextMenu}
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
      <main className="task-canvas" onClick={clearProgressTargetFromBlank}>
        {taskViewMode === "create" ? (
          <section className="task-create-page" aria-labelledby="task-create-title">
            <div className="task-create-heading">
              <div>
                <span className="section-kicker">新增任务</span>
                <h2 id="task-create-title">记录接下来要完成的事情</h2>
                <p>停留在此页面时，番茄钟推进记录默认设为“无所属”。</p>
              </div>
              <button
                type="button"
                className="secondary-button"
                onClick={() => setTaskViewMode("detail")}
              >
                取消
              </button>
            </div>
            <form
              className="task-create-form"
              onSubmit={(event) => void add(event, newTaskParentId, "root")}
            >
              <label className="task-create-content">
                <span>任务内容</span>
                <textarea
                  autoFocus
                  maxLength={5000}
                  aria-label="新任务内容"
                  placeholder="写下任务内容；可以使用多行文字。"
                  value={rootTitle}
                  onChange={(event) => setRootTitle(event.target.value)}
                />
                <small>{rootTitle.length} / 5000</small>
              </label>
              <div className="task-create-options">
                <PrioritySelector
                  label="新任务优先级"
                  value={rootPriority}
                  onChange={setRootPriority}
                />
                <div className="task-create-parent">
                  <span>所属父任务</span>
                  <TaskPicker
                    tasks={detailTasks.filter((task) => task.canAddChild)}
                    value={newTaskParentId}
                    onChange={setNewTaskParentId}
                    unassignedLabel="未归类（顶级任务）"
                    label="新任务所属父任务"
                  />
                </div>
              </div>
              <div className="task-create-actions">
                <button
                  type="button"
                  className="secondary-button"
                  onClick={() => setTaskViewMode("detail")}
                >
                  返回任务详情
                </button>
                <button
                  type="submit"
                  className="primary-button"
                  disabled={!rootTitle.trim()}
                >
                  <Plus size={17} />
                  创建任务
                </button>
              </div>
            </form>
          </section>
        ) : selected ? (
          <>
            <div className="canvas-header">
              <div className="breadcrumbs">
                <span>任务</span>
                {selectedPath.map((item, index) => {
                  const isCurrentTask = index === selectedPath.length - 1;
                  return (
                    <span className="breadcrumb-part" key={item.id}>
                      <CaretRight size={13} />
                      {isCurrentTask ? (
                        <span aria-current="page">{item.title}</span>
                      ) : (
                        <button
                          className="breadcrumb-link"
                          aria-label={`返回父任务：${item.title}`}
                          onClick={() => selectTask(item.id)}
                        >
                          {item.title}
                        </button>
                      )}
                    </span>
                  );
                })}
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
                      focusTaskContentEditor();
                    }}
                  >
                    <NotePencil size={16} />
                    编辑任务
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
                <h2 className="task-title-display">{selected.title}</h2>
                <p>
                  {selected.hasChildren
                    ? "所有叶子任务完成后，由你手动确认父任务完成。"
                    : "专注完成当前任务，并按需记录实际耗时。"}
                </p>
              </div>
            </div>
            <div className="meta-strip">
              <PrioritySelector
                className="detail-priority-selector"
                label="任务优先级"
                value={selected.priority}
                disabled={prioritySaving}
                onChange={(priority) =>
                  void updateTaskPriority(selected, priority)
                }
              />
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
              {selected.canAddChild && (
                <form
                  className="subtask-composer subtask-composer-card"
                  aria-label="新增直属子任务"
                  onSubmit={(event) => void add(event, selected.id, "child")}
                >
                  <header className="subtask-composer-heading">
                    <span aria-hidden="true">
                      <Plus size={16} weight="bold" />
                    </span>
                    <div>
                      <strong>新增直属子任务</strong>
                      <small>将创建在当前任务下</small>
                    </div>
                  </header>
                  <div className="subtask-composer-body">
                    <div className="subtask-input-row">
                      <input
                        aria-label="新建子任务"
                        value={childTitle}
                        onChange={(event) => setChildTitle(event.target.value)}
                        placeholder="输入子任务内容，按 Enter 保存"
                      />
                      <button
                        type="submit"
                        aria-label="添加子任务"
                        disabled={!childTitle.trim()}
                      >
                        <Plus size={15} />
                        <span>添加</span>
                      </button>
                    </div>
                    <PrioritySelector
                      className="subtask-create-priority"
                      label="新子任务优先级"
                      caption="新子任务优先级"
                      value={childPriority}
                      onChange={setChildPriority}
                    />
                  </div>
                </form>
              )}
              <div className="subtask-list">
                {(collapseCompletedChildren ? incompleteChildren : children).map(
                  renderSubtaskRow,
                )}
                {collapseCompletedChildren && (
                  <button
                    type="button"
                    className="completed-subtasks-toggle"
                    aria-expanded={completedChildrenExpanded}
                    onClick={() =>
                      setCompletedChildrenExpanded((current) => !current)
                    }
                  >
                    <CaretRight
                      size={15}
                      className={completedChildrenExpanded ? "expanded" : ""}
                    />
                    <span>已完成任务</span>
                    <small>{completedChildren.length} 项</small>
                    <strong>
                      {completedChildrenExpanded ? "收起" : "展开"}
                    </strong>
                  </button>
                )}
                {collapseCompletedChildren &&
                  completedChildrenExpanded &&
                  completedChildren.map(renderSubtaskRow)}
              </div>
            </section>
            <section id="task-content" className="content-section task-content-section">
              <div className="section-title-row">
                <h3>任务内容</h3>
                <span
                  className={`save-status ${contentStatus}`}
                  role={contentStatus === "error" ? "alert" : "status"}
                >
                  {contentStatus === "saving"
                    ? "正在保存…"
                    : contentStatus === "unsaved"
                      ? "等待自动保存"
                      : contentStatus === "error"
                        ? "保存失败，继续输入可重试"
                        : "已保存"}
                </span>
              </div>
              <textarea
                className="task-content-input"
                aria-label="任务内容"
                maxLength={5000}
                placeholder="输入任务内容；停止输入后自动保存。"
                value={contentDraft}
                onChange={(event) => setContentDraft(event.target.value)}
                onBlur={() => {
                  if (selected) void saveTaskContent(selected, contentDraft);
                }}
              />
            </section>
            <section id="task-progress" className="content-section progress-section">
              <div className="section-title-row progress-section-heading">
                <div>
                  <h3>推进记录</h3>
                  <span>
                    本任务 {progressResult?.ownCount ?? 0} 次 · 全部投入{
                      " "
                    }
                    {formatProgressDuration(progressResult?.allDurationMinutes ?? 0)}
                  </span>
                </div>
                <button
                  className="secondary-button"
                  onClick={() => setProgressEditor("new")}
                >
                  <Plus size={16} />
                  记录推进
                </button>
              </div>
              {selected.hasChildren && (
                <div className="segmented progress-scope-tabs" role="tablist">
                  <button
                    className={progressScope === "own" ? "active" : ""}
                    role="tab"
                    aria-selected={progressScope === "own"}
                    onClick={() => setProgressScope("own")}
                  >
                    本任务 {progressResult?.ownCount ?? 0}
                  </button>
                  <button
                    className={progressScope === "all" ? "active" : ""}
                    role="tab"
                    aria-selected={progressScope === "all"}
                    onClick={() => setProgressScope("all")}
                  >
                    全部推进 {progressResult?.allCount ?? 0}
                  </button>
                </div>
              )}
              <div className="progress-record-list">
                {visibleProgressRecords.map((record) => (
                  <article className="progress-record-row" key={record.id}>
                    <div className="progress-record-time">
                      <strong>
                        {new Date(record.completedAt).toLocaleDateString([], {
                          month: "2-digit",
                          day: "2-digit",
                        })}
                      </strong>
                      <span>
                        {new Date(record.startedAt).toLocaleTimeString([], {
                          hour: "2-digit",
                          minute: "2-digit",
                        })}
                        {"–"}
                        {new Date(record.completedAt).toLocaleTimeString([], {
                          hour: "2-digit",
                          minute: "2-digit",
                        })}
                      </span>
                    </div>
                    <div className="progress-record-content">
                      <strong>{record.note || "未填写"}</strong>
                      <span>
                        {progressScope === "all" ? `${record.taskPath} · ` : ""}
                        {record.endReasonDisplay} · 记录于{
                          " "
                        }
                        {new Date(record.createdAt).toLocaleString([], {
                          month: "2-digit",
                          day: "2-digit",
                          hour: "2-digit",
                          minute: "2-digit",
                        })}
                      </span>
                    </div>
                    <div className="progress-record-duration">
                      <strong>{formatProgressDuration(record.durationMinutes)}</strong>
                      {record.isDurationAdjusted && <span>已调整</span>}
                    </div>
                    <IconButton
                      label="编辑推进记录"
                      onClick={() => setProgressEditor(record)}
                    >
                      <NotePencil size={16} />
                    </IconButton>
                  </article>
                ))}
                {progressResult && progressRecords.length === 0 && (
                  <div className="progress-empty">
                    还没有推进记录。完成一次番茄钟，或点击“记录推进”手动补录。
                  </div>
                )}
                {hasHiddenProgressRecords && (
                  <button
                    type="button"
                    className="progress-record-toggle"
                    aria-expanded={progressRecordsExpanded}
                    aria-label={
                      progressRecordsExpanded
                        ? "收起推进记录"
                        : "展开全部推进记录"
                    }
                    onClick={() =>
                      setProgressRecordsExpanded((current) => !current)
                    }
                  >
                    <CaretDown
                      size={15}
                      className={progressRecordsExpanded ? "expanded" : ""}
                    />
                    <span>
                      {progressRecordsExpanded
                        ? `收起，只显示最新 ${PROGRESS_RECORD_PREVIEW_COUNT} 条`
                        : `展开其余 ${progressRecords.length - PROGRESS_RECORD_PREVIEW_COUNT} 条`}
                    </span>
                  </button>
                )}
              </div>
            </section>
            <section id="task-operations" className="content-section">
              <div className="section-title-row">
                <h3>任务操作</h3>
              </div>
              <div className="setting-row compact-setting-row parent-task-operation">
                <div>
                  <strong>所属父任务</strong>
                  <span>仅可选择有效父任务，最多支持 5 层</span>
                </div>
                <TaskPicker
                  tasks={parentCandidates}
                  value={selected.parentId}
                  onChange={(parentId) => void moveTask(parentId)}
                  unassignedLabel="设为顶级任务"
                  label="所属父任务"
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
            <p>从左侧打开新增任务页面开始。</p>
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
            <p>
              {taskViewMode === "create"
                ? "新增任务页面 · 推进记录将设为无所属"
                : (progressTarget?.title ??
                  selected?.title ??
                  "选择任务后开始专注")}
            </p>
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
              提前完成
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
          {taskViewMode === "create" ? (
            <div className="create-context-panel">
              <Plus size={20} />
              <h3>正在新增任务</h3>
              <p>当前没有打开具体任务，番茄钟推进记录将设为“无所属”。</p>
            </div>
          ) : (
            <>
              <div className="section-title-row">
                <h3>任务详情</h3>
                <IconButton
                  label="编辑任务"
                  onClick={focusTaskContentEditor}
                >
                  <NotePencil size={16} />
                </IconButton>
              </div>
              <dl>
                <div>
                  <dt>状态</dt>
                  <dd>
                    <span
                      className={`status-dot ${selected?.status === "inProgress" ? "active" : ""}`}
                    />
                    {selected?.isCompleted || selected?.status === "completed"
                      ? "已完成"
                      : selected?.status === "inProgress"
                        ? "进行中"
                        : "未开始"}
                  </dd>
                </div>
                <div>
                  <dt>层级</dt>
                  <dd>{selected ? `${selected.depth + 1} / 5` : "—"}</dd>
                </div>
                <div>
                  <dt>累计专注</dt>
                  <dd>
                    {progressResult && progressResult.ownDurationMinutes > 0
                      ? formatProgressDuration(progressResult.ownDurationMinutes)
                      : "未记录"}
                  </dd>
                </div>
                <div>
                  <dt>{selected?.hasChildren ? "最近推进（含子任务）" : "最近推进"}</dt>
                  <dd>
                    {progressResult?.lastProgressAt
                      ? new Date(progressResult.lastProgressAt).toLocaleString([], {
                          month: "2-digit",
                          day: "2-digit",
                          hour: "2-digit",
                          minute: "2-digit",
                        })
                      : "未记录"}
                  </dd>
                </div>
              </dl>
            </>
          )}
        </section>
      </aside>
      {priorityContextMenu && (
        <PriorityContextMenu
          task={priorityContextMenu.task}
          left={priorityContextMenu.left}
          top={priorityContextMenu.top}
          disabled={prioritySaving}
          onClose={closePriorityContextMenu}
          onChange={(priority) => {
            const task = priorityContextMenu.task;
            closePriorityContextMenu();
            void updateTaskPriority(task, priority);
          }}
        />
      )}
      {completionTask && (
        <CompletionDialog
          task={completionTask}
          onClose={() => setCompletionTask(null)}
          onComplete={(duration) => void complete(duration)}
        />
      )}
      {progressEditor && selected && (
        <ProgressEditorDialog
          tasks={detailTasks}
          initialTaskId={progressTarget?.id ?? selected.id}
          record={progressEditor === "new" ? null : progressEditor}
          reportError={reportError}
          onClose={() => setProgressEditor(null)}
          onSaved={async () => {
            await load();
            await loadProgress();
          }}
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
