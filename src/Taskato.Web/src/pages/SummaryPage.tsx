import { taskTitle, taskPath, parentTaskPath } from "../taskText";
import {
  useCallback,
  useEffect,
  useRef,
  useState,
  type FormEvent,
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
  type TimerListResult,
} from "../bridge";
import {
  applyTheme,
  IconButton,
  toLocalDateInput,
  useBridgeRefresh,
} from "../ui";
import { TaskPicker } from "../components/TaskPicker";

const PROGRESS_CONTEXT_MENU_WIDTH = 184;
const PROGRESS_CONTEXT_MENU_HEIGHT = 82;
const CONTEXT_MENU_VIEWPORT_GAP = 8;
const SUMMARY_PRIORITY_LABELS = ["无", "低", "中", "高"] as const;

function getSummaryPriority(priority: number): {
  value: number;
  label: string;
} {
  const value = priority >= 0 && priority < SUMMARY_PRIORITY_LABELS.length
    ? priority
    : 0;
  return { value, label: SUMMARY_PRIORITY_LABELS[value] };
}

type ProgressContextMenuState = {
  record: ProgressRecordDto;
  left: number;
  top: number;
};

type PendingProgressDelete = {
  mode: "single" | "batch";
  records: ProgressRecordDto[];
};

function fitProgressContextMenu(clientX: number, clientY: number) {
  const maximumLeft = Math.max(
    CONTEXT_MENU_VIEWPORT_GAP,
    window.innerWidth - PROGRESS_CONTEXT_MENU_WIDTH - CONTEXT_MENU_VIEWPORT_GAP,
  );
  const maximumTop = Math.max(
    CONTEXT_MENU_VIEWPORT_GAP,
    window.innerHeight - PROGRESS_CONTEXT_MENU_HEIGHT - CONTEXT_MENU_VIEWPORT_GAP,
  );
  return {
    left: Math.min(Math.max(CONTEXT_MENU_VIEWPORT_GAP, clientX), maximumLeft),
    top: Math.min(Math.max(CONTEXT_MENU_VIEWPORT_GAP, clientY), maximumTop),
  };
}

function ProgressContextMenu({
  state,
  onDelete,
  onClose,
}: {
  state: ProgressContextMenuState;
  onDelete(record: ProgressRecordDto): void;
  onClose(): void;
}) {
  const menuRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    const closeOnOutsideClick = (event: globalThis.MouseEvent) => {
      if (!menuRef.current?.contains(event.target as Node)) onClose();
    };
    const closeOnEscape = (event: globalThis.KeyboardEvent) => {
      if (event.key !== "Escape") return;
      event.preventDefault();
      onClose();
    };
    const focusTimer = window.setTimeout(() => {
      menuRef.current?.querySelector<HTMLButtonElement>('[role="menuitem"]')?.focus();
    });
    window.addEventListener("mousedown", closeOnOutsideClick);
    window.addEventListener("keydown", closeOnEscape);
    window.addEventListener("resize", onClose);
    window.addEventListener("blur", onClose);
    return () => {
      window.clearTimeout(focusTimer);
      window.removeEventListener("mousedown", closeOnOutsideClick);
      window.removeEventListener("keydown", closeOnEscape);
      window.removeEventListener("resize", onClose);
      window.removeEventListener("blur", onClose);
    };
  }, [onClose]);

  return (
    <div
      ref={menuRef}
      className="progress-context-menu"
      role="menu"
      aria-label={`管理推进记录“${state.record.note || "未填写"}”`}
      style={{ left: state.left, top: state.top }}
      onContextMenu={(event) => event.preventDefault()}
    >
      <span className="progress-context-menu-title" role="presentation">
        推进记录
      </span>
      <button
        type="button"
        role="menuitem"
        className="progress-context-delete"
        onClick={() => onDelete(state.record)}
      >
        <Trash size={15} />
        删除记录
      </button>
    </div>
  );
}

function ProgressDeleteDialog({
  request,
  deleting,
  onCancel,
  onConfirm,
}: {
  request: PendingProgressDelete;
  deleting: boolean;
  onCancel(): void;
  onConfirm(): void;
}) {
  const confirmButtonRef = useRef<HTMLButtonElement>(null);
  const recordCount = request.records.length;

  useEffect(() => {
    const closeOnEscape = (event: globalThis.KeyboardEvent) => {
      if (event.key !== "Escape" || deleting) return;
      event.preventDefault();
      onCancel();
    };
    const focusTimer = window.setTimeout(() => confirmButtonRef.current?.focus());
    window.addEventListener("keydown", closeOnEscape);
    return () => {
      window.clearTimeout(focusTimer);
      window.removeEventListener("keydown", closeOnEscape);
    };
  }, [deleting, onCancel]);

  return (
    <div
      className="modal-backdrop progress-delete-backdrop"
      onMouseDown={(event) => {
        if (event.target === event.currentTarget && !deleting) onCancel();
      }}
    >
      <section
        className="modal progress-delete-modal"
        role="dialog"
        aria-modal="true"
        aria-labelledby="progress-delete-title"
      >
        <header>
          <div>
            <span className="section-kicker">今日推进</span>
            <h2 id="progress-delete-title">
              {recordCount === 1
                ? "删除这条推进记录？"
                : `删除选中的 ${recordCount} 条推进记录？`}
            </h2>
          </div>
          <IconButton label="关闭" onClick={onCancel} disabled={deleting}>
            <X size={19} />
          </IconButton>
        </header>
        <p>删除后无法撤销，所属任务的状态不会自动回退。</p>
        <footer>
          <button
            type="button"
            className="secondary-button"
            disabled={deleting}
            onClick={onCancel}
          >
            取消
          </button>
          <button
            ref={confirmButtonRef}
            type="button"
            className="progress-delete-confirm-button"
            disabled={deleting}
            onClick={onConfirm}
          >
            <Trash size={16} />
            {deleting ? "正在删除…" : `确认删除 ${recordCount} 条`}
          </button>
        </footer>
      </section>
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
  const [taskOptions, setTaskOptions] = useState<TaskDto[]>([]);
  const [editingProgress, setEditingProgress] = useState<ProgressRecordDto | null>(
    null,
  );
  const [progressContextMenu, setProgressContextMenu] =
    useState<ProgressContextMenuState | null>(null);
  const [isManagingProgress, setIsManagingProgress] = useState(false);
  const [selectedProgressIds, setSelectedProgressIds] = useState<Set<number>>(
    () => new Set(),
  );
  const [pendingProgressDelete, setPendingProgressDelete] =
    useState<PendingProgressDelete | null>(null);
  const [deletingProgress, setDeletingProgress] = useState(false);
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
  useEffect(() => {
    void bridge
      .request<TaskDto[]>("tasks.options")
      .then(setTaskOptions)
      .catch(reportError);
  }, [reportError]);
  useBridgeRefresh("tasks.changed", () => void load());
  useBridgeRefresh("focus.changed", () => void load());
  useEffect(() => {
    const validRecordIds = new Set(
      (summary?.progressRecords ?? []).map((record) => record.id),
    );
    setSelectedProgressIds((current) => {
      const validSelectedIds = new Set(
        [...current].filter((recordId) => validRecordIds.has(recordId)),
      );
      return validSelectedIds.size === current.size ? current : validSelectedIds;
    });
  }, [summary?.progressRecords]);
  const closeProgressContextMenu = useCallback(
    () => setProgressContextMenu(null),
    [],
  );
  const progressRecords = summary?.progressRecords ?? [];
  const toggleProgressSelection = (recordId: number) => {
    setSelectedProgressIds((current) => {
      const next = new Set(current);
      if (next.has(recordId)) next.delete(recordId);
      else next.add(recordId);
      return next;
    });
  };
  const toggleProgressManagement = () => {
    closeProgressContextMenu();
    setSelectedProgressIds(new Set());
    setIsManagingProgress((current) => !current);
  };
  const requestBatchProgressDelete = () => {
    const selectedRecords = progressRecords.filter((record) =>
      selectedProgressIds.has(record.id),
    );
    if (selectedRecords.length === 0) return;
    setPendingProgressDelete({ mode: "batch", records: selectedRecords });
  };
  const confirmProgressDelete = async () => {
    if (!pendingProgressDelete) return;
    try {
      setDeletingProgress(true);
      if (pendingProgressDelete.mode === "single") {
        await bridge.request("progress.delete", {
          recordId: pendingProgressDelete.records[0].id,
        });
      } else {
        await bridge.request("progress.deleteBatch", {
          recordIds: pendingProgressDelete.records.map((record) => record.id),
        });
      }
      await load();
      if (pendingProgressDelete.mode === "batch") {
        setIsManagingProgress(false);
        setSelectedProgressIds(new Set());
      }
      setPendingProgressDelete(null);
    } catch (error) {
      reportError(error);
    } finally {
      setDeletingProgress(false);
    }
  };
  const shift = (days: number) => {
    const value = new Date(`${date}T00:00:00`);
    value.setDate(value.getDate() + days);
    if (value <= new Date()) {
      closeProgressContextMenu();
      setIsManagingProgress(false);
      setSelectedProgressIds(new Set());
      setPendingProgressDelete(null);
      setDate(toLocalDateInput(value));
    }
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
              <strong>{summary?.focusDurationDisplay ?? "0分钟"}</strong>
              <small>
                记录耗时：任务 {summary?.taskDurationDisplay ?? "0分钟"} · 推进{
                  " "
                }{summary?.progressDurationDisplay ?? "0分钟"}
              </small>
            </div>
          </div>
          <div className="achievement-list">
            {summary?.groups.map((group) => (
              <article className="achievement-group" key={group.topLevelTaskId}>
                <header>
                  <div>
                    <CaretDown size={16} />
                    <div>
                      <h3>{taskTitle(group.title)}</h3>
                      <p>{group.records.length} 项成果</p>
                    </div>
                  </div>
                </header>
                {group.records.map((item) => {
                  const priority = getSummaryPriority(item.prioritySnapshot);
                  return (
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
                      <div className="achievement-task-copy">
                        <div className="achievement-task-heading">
                          <strong>{taskTitle(item.titleSnapshot)}</strong>
                        </div>
                        <span
                          className="achievement-task-path"
                          title={parentTaskPath(item.pathSnapshot) || "顶级任务"}
                        >
                          {parentTaskPath(item.pathSnapshot) || "顶级任务"}
                        </span>
                      </div>
                      <div className="achievement-metadata">
                        <span
                          className={`priority-badge achievement-priority-badge priority-${priority.value}`}
                          title={`优先级：${priority.label}`}
                        >
                          {priority.label}
                        </span>
                        <span className="achievement-kind">{item.kindDisplay}</span>
                        <small>{item.durationDisplay}</small>
                        <time>
                          {new Date(item.completedAt).toLocaleTimeString([], {
                            hour: "2-digit",
                            minute: "2-digit",
                          })}
                        </time>
                      </div>
                    </button>
                  );
                })}
              </article>
            ))}
          </div>
          <section className="summary-progress-section">
            <div className="section-title-row">
              <div>
                <h3>今日推进</h3>
                <span>
                  {summary?.progressCount ?? 0} 次 ·{
                    " "
                  }{summary?.progressDurationDisplay ?? "0分钟"}
                </span>
              </div>
              <div className="summary-progress-actions">
                {isManagingProgress ? (
                  <>
                    <button
                      type="button"
                      disabled={
                        progressRecords.length === 0 ||
                        selectedProgressIds.size === progressRecords.length
                      }
                      onClick={() =>
                        setSelectedProgressIds(
                          new Set(progressRecords.map((record) => record.id)),
                        )
                      }
                    >
                      全选
                    </button>
                    <button
                      type="button"
                      disabled={selectedProgressIds.size === 0}
                      onClick={() => setSelectedProgressIds(new Set())}
                    >
                      取消全选
                    </button>
                    <button
                      type="button"
                      className="summary-progress-batch-delete"
                      disabled={selectedProgressIds.size === 0}
                      onClick={requestBatchProgressDelete}
                    >
                      删除已选
                      {selectedProgressIds.size > 0 &&
                        `（${selectedProgressIds.size}）`}
                    </button>
                    <button type="button" onClick={toggleProgressManagement}>
                      完成
                    </button>
                  </>
                ) : (
                  <button
                    type="button"
                    disabled={progressRecords.length === 0}
                    onClick={toggleProgressManagement}
                  >
                    管理
                  </button>
                )}
              </div>
            </div>
            <div className="summary-progress-list">
              {progressRecords.map((record) => (
                <article
                  className={`summary-progress-row ${
                    isManagingProgress ? "managing" : ""
                  } ${selectedProgressIds.has(record.id) ? "selected" : ""}`.trim()}
                  key={record.id}
                  title={
                    isManagingProgress
                      ? "点击勾选这条推进记录"
                      : "右键可删除这条推进记录"
                  }
                  onClick={() =>
                    isManagingProgress && toggleProgressSelection(record.id)
                  }
                  onContextMenu={(event) => {
                    event.preventDefault();
                    if (isManagingProgress) return;
                    setProgressContextMenu({
                      record,
                      ...fitProgressContextMenu(event.clientX, event.clientY),
                    });
                  }}
                >
                  {isManagingProgress && (
                    <input
                      type="checkbox"
                      className="summary-progress-checkbox"
                      aria-label={`选择推进记录：${record.note || "未填写"}`}
                      checked={selectedProgressIds.has(record.id)}
                      onClick={(event) => event.stopPropagation()}
                      onChange={() => toggleProgressSelection(record.id)}
                    />
                  )}
                  <div>
                    <strong>{record.note || "未填写"}</strong>
                    <span>{taskPath(record.taskPath)}</span>
                  </div>
                  <small>
                    {record.durationMinutes} 分钟
                    {record.isDurationAdjusted ? " · 已调整" : ""}
                  </small>
                  <time>
                    {new Date(record.completedAt).toLocaleTimeString([], {
                      hour: "2-digit",
                      minute: "2-digit",
                    })}
                  </time>
                  {!isManagingProgress && (
                    <IconButton
                      label="编辑推进所属和时长"
                      onClick={() => {
                        closeProgressContextMenu();
                        setEditingProgress(record);
                      }}
                    >
                      <NotePencil size={16} />
                    </IconButton>
                  )}
                </article>
              ))}
              {(summary?.progressRecords?.length ?? 0) === 0 && (
                <div className="history-empty">这一天还没有推进记录。</div>
              )}
            </div>
          </section>
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
              当前仍存在的任务必须保持完成状态且完成时间匹配；已删除任务保留完成快照；取消完成仅进入历史日志。专注时长与专注统计的有效总时长一致，包含番茄钟专注、休息和暂停区间，完成等待不计入；任务与推进耗时单独展示，不与专注时长相加。推进只在任务详情页手动记录。
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
      {progressContextMenu && (
        <ProgressContextMenu
          state={progressContextMenu}
          onDelete={(record) => {
            closeProgressContextMenu();
            setPendingProgressDelete({ mode: "single", records: [record] });
          }}
          onClose={closeProgressContextMenu}
        />
      )}
      {pendingProgressDelete && (
        <ProgressDeleteDialog
          request={pendingProgressDelete}
          deleting={deletingProgress}
          onCancel={() => setPendingProgressDelete(null)}
          onConfirm={() => void confirmProgressDelete()}
        />
      )}
      {backfill && (
        <BackfillDialog
          date={date}
          reportError={reportError}
          onClose={() => setBackfill(false)}
          onDone={load}
        />
      )}
      {editingProgress && (
        <SummaryProgressEditor
          record={editingProgress}
          tasks={taskOptions}
          reportError={reportError}
          onClose={() => setEditingProgress(null)}
          onSaved={load}
        />
      )}
    </div>
  );
}

export type BackfillSortMode = "newest" | "oldest" | "priority";

function SummaryProgressEditor({
  record,
  tasks,
  reportError,
  onClose,
  onSaved,
}: {
  record: ProgressRecordDto;
  tasks: TaskDto[];
  reportError(error: unknown): void;
  onClose(): void;
  onSaved(): Promise<void>;
}) {
  const [taskId, setTaskId] = useState(record.taskId);
  const [note, setNote] = useState(record.note);
  const [duration, setDuration] = useState(String(record.durationMinutes));
  const [saving, setSaving] = useState(false);
  const durationMinutes = Number(duration);
  const validDuration = Number.isInteger(durationMinutes) &&
    durationMinutes >= 1 && durationMinutes <= 1440;

  const save = async () => {
    if (!validDuration) return;
    try {
      setSaving(true);
      await bridge.request("progress.update", {
        recordId: record.id,
        taskId,
        note,
        durationMinutes,
      });
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
            <span className="section-kicker">今日推进</span>
            <h2>修改所属和时长</h2>
          </div>
          <IconButton label="关闭" onClick={onClose}>
            <X size={19} />
          </IconButton>
        </header>
        <label className="progress-note-field">
          <span>推进内容（可选）</span>
          <textarea
            maxLength={5000}
            value={note}
            onChange={(event) => setNote(event.target.value)}
          />
        </label>
        <span className="field-label">所属任务</span>
        <TaskPicker tasks={tasks} value={taskId} onChange={setTaskId} embedded />
        <div className="modal-fields progress-fields">
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
        {record.originalDurationMinutes > 0 &&
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
          <button className="secondary-button" onClick={onClose}>取消</button>
          <button
            className="primary-button"
            disabled={!validDuration || saving}
            onClick={() => void save()}
          >
            {saving ? "正在保存…" : "保存修改"}
          </button>
        </footer>
      </section>
    </div>
  );
}

export function sortBackfillTasks(
  tasks: TaskDto[],
  sortMode: BackfillSortMode,
): TaskDto[] {
  const compareCreatedAt = (left: TaskDto, right: TaskDto) =>
    new Date(left.createdAt).getTime() - new Date(right.createdAt).getTime();

  return [...tasks].sort((left, right) => {
    if (sortMode === "oldest") {
      return compareCreatedAt(left, right) || left.id - right.id;
    }

    if (sortMode === "priority") {
      return (
        right.priority - left.priority ||
        compareCreatedAt(right, left) ||
        right.id - left.id
      );
    }

    return compareCreatedAt(right, left) || right.id - left.id;
  });
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
        setTaskId(sortBackfillTasks(items, "newest")[0]?.id ?? null);
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
      <section className="modal backfill-modal" role="dialog" aria-modal="true">
        <header>
          <div>
            <span className="section-kicker">{date}</span>
            <h2>补充任务</h2>
          </div>
          <IconButton label="关闭" onClick={onClose}>
            <X size={19} />
          </IconButton>
        </header>
        <p>选择未完成叶子任务；补充完成后，父任务仍需手动确认。</p>
        <span className="field-label">选择任务</span>
        <TaskPicker
          tasks={tasks}
          value={taskId}
          onChange={setTaskId}
          embedded
          allowUnassigned={false}
        />
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
