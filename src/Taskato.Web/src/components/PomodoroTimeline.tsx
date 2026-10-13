import { useCallback, useEffect, useRef, useState } from "react";
import { CaretRight, ClockCounterClockwise } from "@phosphor-icons/react";
import { bridge, type PomodoroTimelineDto } from "../bridge";
import { taskTitle } from "../taskText";
import { useBridgeRefresh } from "../ui";

const phaseEvents: Record<string, { title: string; className: string }> = {
  "开始专注": { title: "开始专注", className: "start-work" },
  "开始休息": { title: "开始休息", className: "start-rest" },
  "专注时间到": { title: "结束专注", className: "end-work" },
  "休息时间到": { title: "结束休息", className: "end-rest" },
  "停止专注": { title: "提前结束专注", className: "early-work" },
  "提前完成专注": { title: "提前结束专注", className: "early-work" },
  "停止休息": { title: "提前结束休息", className: "early-rest" },
};

function elapsedLabel(seconds: number): string {
  const duration = Math.max(0, Math.floor(seconds));
  const hours = Math.floor(duration / 3600);
  const minutes = Math.floor((duration % 3600) / 60);
  const remainder = duration % 60;
  return `${hours > 0 ? `${hours} 小时 ` : ""}${minutes > 0 ? `${minutes} 分 ` : ""}${remainder} 秒`;
}

export function PomodoroTimeline({ timerId }: { timerId?: string }) {
  const [expanded, setExpanded] = useState(false);
  const [mode, setMode] = useState<"simple" | "detailed">("simple");
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

  const visibleEntries = mode === "detailed" ? entries : entries.filter((entry) =>
    phaseEvents[entry.title]);

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
          <div className="segmented pomodoro-timeline-mode" role="group" aria-label="时间链显示模式">
            <button type="button" className={mode === "simple" ? "active" : ""} aria-pressed={mode === "simple"} onClick={() => setMode("simple")}>简略</button>
            <button type="button" className={mode === "detailed" ? "active" : ""} aria-pressed={mode === "detailed"} onClick={() => setMode("detailed")}>详细</button>
          </div>
          <p className="pomodoro-timeline-caption">当前计时器 · 最近 100 条 · 从本功能启用后记录</p>
          {loading && entries.length === 0 && <p role="status">正在加载记录…</p>}
          {error && <div role="alert"><p>{error}</p><button type="button" onClick={() => void load()}>重试</button></div>}
          {!loading && !error && visibleEntries.length === 0 && <p>{mode === "simple" ? "暂无专注或休息的开始、结束记录。" : "暂无记录。开始专注后，阶段变化、提醒和你的选择会保存在这里。"}</p>}
          <ol className="pomodoro-timeline-events">
            {visibleEntries.map((entry, index) => {
              const phaseEvent = phaseEvents[entry.title];
              const previous = visibleEntries[index + 1];
              const elapsed = previous
                ? (new Date(entry.occurredAt).getTime() - new Date(previous.occurredAt).getTime()) / 1000
                : null;
              const isPhaseEvent = /^(开始|恢复|暂停|停止)(专注|休息)$/.test(entry.title)
                || ["提前完成专注", "专注时间到", "休息时间到"].includes(entry.title);
              let durationLabel = entry.durationLabel;
              if (phaseEvent) {
                if (entry.title.startsWith("开始")) durationLabel = "暂停时长";
                else durationLabel = entry.title.includes("专注") ? "专注时长" : "休息时长";
              }
              return (
                <li key={entry.id} className={`pomodoro-timeline-event ${entry.kind} ${phaseEvent?.className ?? ""}`}>
                  <div className="pomodoro-timeline-event-heading">
                    <strong>{phaseEvent ? phaseEvent.title : entry.title}</strong>
                    <time dateTime={entry.occurredAt}>{new Date(entry.occurredAt).toLocaleString("zh-CN", {
                      year: "numeric", month: "2-digit", day: "2-digit",
                      hour: "2-digit", minute: "2-digit", second: "2-digit", hour12: false,
                    })}</time>
                  </div>
                  {entry.durationSeconds != null && <small>{durationLabel} {elapsedLabel(entry.durationSeconds)}</small>}
                  {mode === "detailed" && entry.detail && <p>{entry.detail}</p>}
                  {mode === "detailed" && entry.taskTitle && <p className="pomodoro-timeline-task">{taskTitle(entry.taskTitle)}</p>}
                  {mode === "detailed" && entry.durationSeconds == null && !isPhaseEvent && elapsed !== null && <small>距上一事件 {elapsedLabel(elapsed)}</small>}
                </li>
              );
            })}
          </ol>
        </div>
      )}
    </section>
  );
}
