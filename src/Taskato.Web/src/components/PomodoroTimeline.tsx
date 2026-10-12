import { useCallback, useEffect, useRef, useState } from "react";
import { CaretRight, ClockCounterClockwise } from "@phosphor-icons/react";
import { bridge, type PomodoroTimelineDto } from "../bridge";
import { taskTitle } from "../taskText";
import { useBridgeRefresh } from "../ui";

function elapsedLabel(seconds: number): string {
  const duration = Math.max(0, Math.floor(seconds));
  const hours = Math.floor(duration / 3600);
  const minutes = Math.floor((duration % 3600) / 60);
  const remainder = duration % 60;
  return `${hours > 0 ? `${hours} 小时 ` : ""}${minutes > 0 ? `${minutes} 分 ` : ""}${remainder} 秒`;
}

export function PomodoroTimeline({ timerId }: { timerId?: string }) {
  const [expanded, setExpanded] = useState(false);
  const [entries, setEntries] = useState<PomodoroTimelineDto[]>([]);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState("");
  const requestVersion = useRef(0);

  const load = useCallback(async () => {
    if (!timerId || !expanded) return;
    const version = ++requestVersion.current;
    setLoading(true);
    setError("");
    try {
      const result = await bridge.request<PomodoroTimelineDto[]>("pomodoro.timeline", { timerId });
      if (version === requestVersion.current) setEntries(result);
    } catch (failure) {
      if (version === requestVersion.current) {
        setError(failure instanceof Error ? failure.message : String(failure));
      }
    } finally {
      if (version === requestVersion.current) setLoading(false);
    }
  }, [expanded, timerId]);

  useEffect(() => {
    setEntries([]);
    void load();
    return () => { requestVersion.current++; };
  }, [load]);
  useBridgeRefresh("focus.changed", () => void load());

  return (
    <section className="pomodoro-timeline" aria-label="番茄钟时间链">
      <button
        type="button"
        className="pomodoro-timeline-toggle"
        aria-expanded={expanded}
        aria-controls="pomodoro-timeline-content"
        onClick={() => setExpanded((current) => !current)}
      >
        <ClockCounterClockwise size={18} />
        <strong>番茄钟时间链</strong>
        <span>{expanded ? "收起" : "展开"}</span>
        <CaretRight size={14} className={expanded ? "expanded" : ""} />
      </button>
      {expanded && (
        <div id="pomodoro-timeline-content" className="pomodoro-timeline-content">
          <p className="pomodoro-timeline-caption">当前计时器 · 最近 100 条 · 从本功能启用后记录</p>
          {loading && entries.length === 0 && <p role="status">正在加载记录…</p>}
          {error && <div role="alert"><p>{error}</p><button type="button" onClick={() => void load()}>重试</button></div>}
          {!loading && !error && entries.length === 0 && <p>暂无记录。开始专注后，阶段变化、提醒和你的选择会保存在这里。</p>}
          <ol className="pomodoro-timeline-events">
            {entries.map((entry, index) => {
              const previous = entries[index + 1];
              const elapsed = previous
                ? (new Date(entry.occurredAt).getTime() - new Date(previous.occurredAt).getTime()) / 1000
                : null;
              return (
                <li key={entry.id} className={`pomodoro-timeline-event ${entry.kind}`}>
                  <time dateTime={entry.occurredAt}>{new Date(entry.occurredAt).toLocaleString("zh-CN", {
                    year: "numeric", month: "2-digit", day: "2-digit",
                    hour: "2-digit", minute: "2-digit", second: "2-digit", hour12: false,
                  })}</time>
                  <strong>{entry.title}</strong>
                  {entry.detail && <p>{entry.detail}</p>}
                  {entry.taskTitle && <p className="pomodoro-timeline-task">{taskTitle(entry.taskTitle)}</p>}
                  {elapsed !== null && <small>距上一事件 {elapsedLabel(elapsed)}</small>}
                </li>
              );
            })}
          </ol>
        </div>
      )}
    </section>
  );
}
