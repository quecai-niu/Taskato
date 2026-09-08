import { taskTitle } from "../taskText";
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
            <strong>{taskTitle(item.title)}</strong>
            <span>
              {item.detail}
              {item.isLegacyBackfill && <small className="legacy-badge">历史回填</small>}
            </span>
            {item.canOpenTask && item.taskId != null ? (
              <span className="history-actions">
                <IconButton
                  label={`打开 ${taskTitle(item.title)}`}
                  onClick={() => onOpenTask(item.taskId!)}
                >
                  <CaretRight size={16} />
                </IconButton>
                <IconButton
                  label={`删除当前任务 ${taskTitle(item.title)}`}
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
                <h2>删除“{taskTitle(pendingDelete.title)}”及其子任务？</h2>
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
