import {
  useCallback,
  useEffect,
  useMemo,
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
        <p>选择未完成叶子任务；补充完成后，父任务仍需手动确认。</p>
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
