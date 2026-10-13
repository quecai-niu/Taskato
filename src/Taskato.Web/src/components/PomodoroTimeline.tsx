import { useCallback, useEffect, useRef, useState } from "react";
import { CaretRight, ClockCounterClockwise } from "@phosphor-icons/react";
import { bridge, type PomodoroTimelineDto, type SettingsDto, type TimerDto } from "../bridge";
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

type TimelineDisplayEntry = PomodoroTimelineDto & { startedAt?: string };

function simpleTimelineEntries(entries: PomodoroTimelineDto[], pauseThresholdMinutes: number): TimelineDisplayEntry[] {
  const originalPhaseEntries = entries.filter((entry) => phaseEvents[entry.title]);
  const phaseEntries: TimelineDisplayEntry[] = [];
  for (let index = 0; index < originalPhaseEntries.length; index++) {
    const entry = originalPhaseEntries[index];
    const start = originalPhaseEntries[index + 1];
    const isCompletion = !entry.title.startsWith("开始");
    // 记录按时间倒序排列；只有紧邻的同阶段开始、结束才能配对。
    const matchesStart = start?.title === (entry.title.includes("专注") ? "开始专注" : "开始休息");
    if (isCompletion && matchesStart && entry.durationSeconds != null
      && start.durationSeconds != null && start.durationSeconds < pauseThresholdMinutes * 60) {
      phaseEntries.push({ ...entry, startedAt: start.occurredAt });
      index++;
    } else {
      phaseEntries.push(entry);
    }
  }

  const result: TimelineDisplayEntry[] = [];
  let index = 0;
  while (index < phaseEntries.length) {
    const entry = phaseEntries[index];
    if (!entry.title.includes("专注") || entry.title === "开始专注") {
      result.push(entry);
      index++;
      continue;
    }

    const completedEntries: TimelineDisplayEntry[] = [];
    // 保留下来的开始节点代表长暂停、进行中的轮次或旧记录，不能跨越它合并。
    while (index < phaseEntries.length && phaseEntries[index].title.includes("专注")
      && phaseEntries[index].title !== "开始专注") {
      completedEntries.push(phaseEntries[index]);
      index++;
    }
    // 未进入休息的多轮专注合并展示；缺少配对开始或统计时长的旧记录保留原样。
    if (completedEntries.length < 2 || completedEntries.some((item) => item.durationSeconds == null || item.startedAt == null)) {
      result.push(...completedEntries);
      continue;
    }

    const latestCompletion = completedEntries[0];
    let focusedSeconds = 0;
    for (const completedEntry of completedEntries) {
      focusedSeconds += completedEntry.durationSeconds!;
    }
    result.push({
      ...latestCompletion,
      title: "专注时间到",
      durationSeconds: focusedSeconds,
      startedAt: completedEntries[completedEntries.length - 1].startedAt,
    });
  }
  return result;
}

export function PomodoroTimeline({ timerId, timer }: { timerId?: string; timer?: TimerDto }) {
  const [expanded, setExpanded] = useState(true);
  const [mode, setMode] = useState<"simple" | "detailed">("simple");
  const [entries, setEntries] = useState<PomodoroTimelineDto[]>([]);
  const [pauseThresholdMinutes, setPauseThresholdMinutes] = useState(1);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState("");
  const requestVersion = useRef(0);

  const load = useCallback(async () => {
    if (!timerId || !expanded) return;
    const version = ++requestVersion.current;
    setLoading(true);
    setError("");
    try {
      const [result, settings] = await Promise.all([
        bridge.request<PomodoroTimelineDto[]>("pomodoro.timeline", { timerId }),
        bridge.request<SettingsDto>("settings.get"),
      ]);
      if (version === requestVersion.current) {
        setEntries(result);
        setPauseThresholdMinutes(settings.timelinePauseThresholdMinutes);
      }
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
  useEffect(() => bridge.subscribe("settings.changed", (payload) => {
    setPauseThresholdMinutes((payload as SettingsDto).timelinePauseThresholdMinutes);
  }), []);

  const visibleEntries: TimelineDisplayEntry[] = mode === "detailed" ? entries : simpleTimelineEntries(entries, pauseThresholdMinutes);

  return (
    <section className={`pomodoro-timeline${expanded ? " is-expanded" : ""}`} aria-label="番茄钟时间链">
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
          {timer?.currentPhaseElapsedSeconds != null && (
            <p className="pomodoro-timeline-caption">
              <strong>当前{timer.phase === "work" ? "专注" : "休息"}已进行 {elapsedLabel(timer.currentPhaseElapsedSeconds)}</strong>
              {timer.status === "已暂停" && " · 已暂停"}
            </p>
          )}
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
              const isPauseDuration = durationLabel === "暂停时长" || durationLabel === "暂停了";
              const showDuration = entry.durationSeconds != null
                && (!isPauseDuration || entry.durationSeconds >= pauseThresholdMinutes * 60);
              return (
                <li key={entry.id} className={`pomodoro-timeline-event ${entry.kind} ${phaseEvent?.className ?? ""}`}>
                  <div className="pomodoro-timeline-event-heading">
                    <strong>{phaseEvent ? phaseEvent.title : entry.title}</strong>
                    <time dateTime={entry.occurredAt}>{new Date(entry.occurredAt).toLocaleString("zh-CN", {
                      year: "numeric", month: "2-digit", day: "2-digit",
                      hour: "2-digit", minute: "2-digit", second: "2-digit", hour12: false,
                    })}</time>
                  </div>
                  {entry.startedAt && <small>起止 {new Date(entry.startedAt).toLocaleString("zh-CN", { hour12: false })} — {new Date(entry.occurredAt).toLocaleString("zh-CN", { hour12: false })}</small>}
                  {showDuration && <small>{durationLabel} {elapsedLabel(entry.durationSeconds!)}</small>}
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
