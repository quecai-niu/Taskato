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
              <span>总投入时长</span>
              <strong>{summary?.totalDurationDisplay ?? "0分钟"}</strong>
              <small>
                任务 {summary?.taskDurationDisplay ?? "0分钟"} · 推进{
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
            </div>
            <div className="summary-progress-list">
              {(summary?.progressRecords ?? []).map((record) => (
                <article className="summary-progress-row" key={record.id}>
                  <div>
                    <strong>{record.note || "未填写"}</strong>
                    <span>{record.taskPath}</span>
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
                  <IconButton
                    label="编辑推进所属和时长"
                    onClick={() => setEditingProgress(record)}
                  >
                    <NotePencil size={16} />
                  </IconButton>
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
