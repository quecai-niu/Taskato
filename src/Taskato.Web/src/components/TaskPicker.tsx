import { taskTitle as firstLine } from "../taskText";
import {
  useEffect,
  useLayoutEffect,
  useMemo,
  useRef,
  useState,
  type CSSProperties,
} from "react";
import { createPortal } from "react-dom";
import { CaretDown, Check, MagnifyingGlass } from "@phosphor-icons/react";
import type { TaskDto } from "../bridge";

export type TaskPickerSortMode = "newest" | "oldest" | "priority";

const TASK_PICKER_SORT_STORAGE_KEY = "taskato.taskPickerSortMode";
const TASK_PICKER_SORT_CHANGED_EVENT = "taskato:task-picker-sort-changed";

function isTaskPickerSortMode(value: string | null): value is TaskPickerSortMode {
  return value === "newest" || value === "oldest" || value === "priority";
}

function loadStoredTaskPickerSortMode(): TaskPickerSortMode {
  const storedValue = window.localStorage.getItem(TASK_PICKER_SORT_STORAGE_KEY);
  return isTaskPickerSortMode(storedValue) ? storedValue : "newest";
}

function saveTaskPickerSortMode(sortMode: TaskPickerSortMode): void {
  window.localStorage.setItem(TASK_PICKER_SORT_STORAGE_KEY, sortMode);
  window.dispatchEvent(new Event(TASK_PICKER_SORT_CHANGED_EVENT));
}

type TaskPickerPlacement = {
  style: CSSProperties;
  opensAbove: boolean;
};

export function calculateTaskPickerPlacement(
  anchor: Pick<DOMRect, "top" | "right" | "bottom">,
  viewportWidth: number,
  viewportHeight: number,
): TaskPickerPlacement {
  const viewportGap = 12;
  const triggerGap = 7;
  const preferredWidth = 520;
  const minimumUsableHeight = 220;
  const width = Math.min(preferredWidth, viewportWidth - viewportGap * 2);
  const left = Math.min(
    Math.max(viewportGap, anchor.right - width),
    viewportWidth - width - viewportGap,
  );
  const spaceAbove = Math.max(0, anchor.top - viewportGap - triggerGap);
  const spaceBelow = Math.max(
    0,
    viewportHeight - anchor.bottom - viewportGap - triggerGap,
  );
  const opensAbove =
    spaceBelow < minimumUsableHeight && spaceAbove > spaceBelow;
  const availableHeight = opensAbove ? spaceAbove : spaceBelow;

  return {
    opensAbove,
    style: {
      left,
      width,
      maxHeight: Math.max(120, Math.min(410, availableHeight)),
      ...(opensAbove
        ? { bottom: viewportHeight - anchor.top + triggerGap }
        : { top: anchor.bottom + triggerGap }),
    },
  };
}

export function buildTaskPath(tasks: TaskDto[], taskId: number): string {
  const byId = new Map(tasks.map((task) => [task.id, task]));
  const parts: string[] = [];
  const visited = new Set<number>();
  let current = byId.get(taskId);
  while (current && visited.add(current.id)) {
    parts.unshift(firstLine(current.title));
    current = current.parentId == null ? undefined : byId.get(current.parentId);
  }
  return parts.join(" > ");
}

export function filterAndSortTaskOptions(
  tasks: TaskDto[],
  query: string,
  includeCompleted: boolean,
  sortMode: TaskPickerSortMode,
): TaskDto[] {
  const normalizedQuery = query.trim().toLocaleLowerCase();
  const filtered = tasks.filter((task) => {
    if (!includeCompleted && task.isCompleted) return false;
    if (!normalizedQuery) return true;
    return buildTaskPath(tasks, task.id)
      .toLocaleLowerCase()
      .includes(normalizedQuery);
  });
  return [...filtered].sort((left, right) => {
    const createdDelta =
      new Date(left.createdAt).getTime() - new Date(right.createdAt).getTime();
    if (sortMode === "oldest") return createdDelta || left.id - right.id;
    if (sortMode === "priority") {
      return right.priority - left.priority || -createdDelta || right.id - left.id;
    }
    return -createdDelta || right.id - left.id;
  });
}

export function TaskPicker({
  tasks,
  value,
  onChange,
  embedded = false,
  allowUnassigned = true,
  unassignedLabel = "无所属",
  label = "选择任务",
}: {
  tasks: TaskDto[];
  value: number | null;
  onChange(taskId: number | null): void;
  embedded?: boolean;
  allowUnassigned?: boolean;
  unassignedLabel?: string;
  label?: string;
}) {
  const [open, setOpen] = useState(embedded);
  const [query, setQuery] = useState("");
  const [includeCompleted, setIncludeCompleted] = useState(
    () => tasks.find((task) => task.id === value)?.isCompleted ?? false,
  );
  const [sortMode, setSortMode] = useState<TaskPickerSortMode>(
    loadStoredTaskPickerSortMode,
  );
  const [placement, setPlacement] = useState<TaskPickerPlacement | null>(null);
  const controlRef = useRef<HTMLDivElement>(null);
  const popoverRef = useRef<HTMLDivElement>(null);
  const selected = tasks.find((task) => task.id === value);
  const options = useMemo(
    () => filterAndSortTaskOptions(tasks, query, includeCompleted, sortMode),
    [includeCompleted, query, sortMode, tasks],
  );

  useEffect(() => {
    if (embedded || !open) return;
    const close = (event: globalThis.MouseEvent) => {
      const target = event.target as Node;
      if (
        !controlRef.current?.contains(target) &&
        !popoverRef.current?.contains(target)
      ) {
        setOpen(false);
      }
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
  }, [embedded, open]);

  useEffect(() => {
    const restoreSortMode = (event: Event) => {
      if (
        event instanceof StorageEvent &&
        event.key !== TASK_PICKER_SORT_STORAGE_KEY
      ) {
        return;
      }
      setSortMode(loadStoredTaskPickerSortMode());
    };
    window.addEventListener("storage", restoreSortMode);
    window.addEventListener(TASK_PICKER_SORT_CHANGED_EVENT, restoreSortMode);
    return () => {
      window.removeEventListener("storage", restoreSortMode);
      window.removeEventListener(TASK_PICKER_SORT_CHANGED_EVENT, restoreSortMode);
    };
  }, []);

  useLayoutEffect(() => {
    if (embedded || !open) return;
    const updatePlacement = () => {
      const anchor = controlRef.current?.getBoundingClientRect();
      if (!anchor) return;
      setPlacement(
        calculateTaskPickerPlacement(
          anchor,
          window.innerWidth,
          window.innerHeight,
        ),
      );
    };
    updatePlacement();
    window.addEventListener("resize", updatePlacement);
    window.addEventListener("scroll", updatePlacement, true);
    return () => {
      window.removeEventListener("resize", updatePlacement);
      window.removeEventListener("scroll", updatePlacement, true);
    };
  }, [embedded, open]);

  const choose = (taskId: number | null) => {
    onChange(taskId);
    if (!embedded) setOpen(false);
  };
  const pickerBody = (
    <div className="task-picker-body">
      <div className="task-choice-heading">
        <label className="task-picker-search">
          <MagnifyingGlass size={15} />
          <input
            type="search"
            aria-label="搜索任务"
            placeholder="搜索任务名称或完整路径"
            value={query}
            onChange={(event) => setQuery(event.target.value)}
          />
        </label>
        <select
          className="backfill-sort-select"
          aria-label="任务排序方式"
          value={sortMode}
          onChange={(event) => {
            const nextSortMode = event.target.value as TaskPickerSortMode;
            setSortMode(nextSortMode);
            saveTaskPickerSortMode(nextSortMode);
          }}
        >
          <option value="newest">最新发布</option>
          <option value="oldest">最早发布</option>
          <option value="priority">优先级</option>
        </select>
      </div>
      <button
        type="button"
        className={`task-picker-completed-filter ${includeCompleted ? "active" : ""}`}
        aria-pressed={includeCompleted}
        onClick={() => setIncludeCompleted((current) => !current)}
      >
        包含已完成任务
      </button>
      <div className="task-choice-list task-picker-list" role="listbox">
        {allowUnassigned && (
          <button
            type="button"
            className={value == null ? "selected" : ""}
            role="option"
            aria-selected={value == null}
            onClick={() => choose(null)}
          >
            <span className="radio-mark">{value == null && <Check size={10} />}</span>
            <div>
              <strong>{unassignedLabel}</strong>
              <small>不归入任何任务</small>
            </div>
          </button>
        )}
        {options.map((task) => (
          <button
            type="button"
            key={task.id}
            className={value === task.id ? "selected" : ""}
            role="option"
            aria-selected={value === task.id}
            onClick={() => choose(task.id)}
          >
            <span className="radio-mark">
              {value === task.id && <Check size={10} />}
            </span>
            <div>
              <strong>{firstLine(task.title)}</strong>
              <small>{buildTaskPath(tasks, task.id)}</small>
            </div>
            {task.isCompleted && <span className="task-picker-state">已完成</span>}
          </button>
        ))}
        {options.length === 0 && (
          <p className="dialog-empty">没有匹配的任务。</p>
        )}
      </div>
    </div>
  );

  if (embedded) return <div className="task-picker embedded">{pickerBody}</div>;
  return (
    <div className="task-picker" ref={controlRef}>
      <button
        type="button"
        className="task-picker-trigger"
        aria-label={label}
        aria-haspopup="dialog"
        aria-expanded={open}
        onClick={() => setOpen((current) => !current)}
      >
        <span>
          <small>{label}</small>
          <strong>
            {selected ? buildTaskPath(tasks, selected.id) : unassignedLabel}
          </strong>
        </span>
        <CaretDown size={15} />
      </button>
      {open &&
        placement &&
        createPortal(
          <div
            className={`task-picker-popover ${placement.opensAbove ? "opens-above" : "opens-below"}`}
            ref={popoverRef}
            style={placement.style}
          >
            {pickerBody}
          </div>,
          document.body,
        )}
    </div>
  );
}
