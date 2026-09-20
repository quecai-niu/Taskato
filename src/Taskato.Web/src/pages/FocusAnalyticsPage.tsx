import { useCallback, useEffect, useMemo, useState } from "react";
import {
  CalendarDots,
  CaretLeft,
  CaretRight,
  Clock,
  Coffee,
  Pause,
  Play,
  Timer,
} from "@phosphor-icons/react";
import {
  bridge,
  type FocusAnalyticsCellDto,
  type FocusAnalyticsResult,
} from "../bridge";
import { IconButton, toLocalDateInput, useBridgeRefresh } from "../ui";

type FocusPeriod = "day" | "week" | "month";

const periodLabels: Record<FocusPeriod, string> = {
  day: "天",
  week: "周",
  month: "月",
};

export function FocusAnalyticsPage({
  reportError,
}: {
  reportError(error: unknown): void;
}) {
  const [period, setPeriod] = useState<FocusPeriod>("day");
  const [anchorDate, setAnchorDate] = useState(toLocalDateInput());
  const [analytics, setAnalytics] = useState<FocusAnalyticsResult | null>(null);
  const [loading, setLoading] = useState(true);
  const load = useCallback(async () => {
    try {
      setLoading(true);
      setAnalytics(
        await bridge.request<FocusAnalyticsResult>("focus.analytics", {
          period,
          anchorDate,
        }),
      );
    } catch (error) {
      reportError(error);
    } finally {
      setLoading(false);
    }
  }, [anchorDate, period, reportError]);

  useEffect(() => {
    void load();
  }, [load]);
  useBridgeRefresh("focus.changed", () => void load());

  const shiftPeriod = (direction: -1 | 1) => {
    const value = new Date(`${anchorDate}T12:00:00`);
    if (period === "day") value.setDate(value.getDate() + direction);
    else if (period === "week") value.setDate(value.getDate() + direction * 7);
    else value.setMonth(value.getMonth() + direction);
    setAnchorDate(toLocalDateInput(value));
  };

  return (
    <div className="full-page focus-analytics-page page-enter">
      <div className="page-heading-row focus-page-heading">
        <div>
          <span className="section-kicker">看见你的专注节律</span>
          <h2>专注统计</h2>
          <p>统计番茄钟的专注、休息与暂停时长；暂停计入有效总时长，完成等待不计入。</p>
        </div>
        <div className="focus-period-tabs" role="tablist" aria-label="统计周期">
          {(Object.keys(periodLabels) as FocusPeriod[]).map((value) => (
            <button
              key={value}
              role="tab"
              aria-selected={period === value}
              className={period === value ? "active" : ""}
              onClick={() => setPeriod(value)}
            >
              {periodLabels[value]}
            </button>
          ))}
        </div>
      </div>

      <div className="focus-period-toolbar">
        <IconButton label="上一周期" onClick={() => shiftPeriod(-1)}>
          <CaretLeft size={18} />
        </IconButton>
        <div>
          <CalendarDots size={18} />
          <strong>{analytics?.rangeLabel ?? "正在加载"}</strong>
        </div>
        <button className="focus-today-button" onClick={() => setAnchorDate(toLocalDateInput())}>
          回到今天
        </button>
        <IconButton label="下一周期" onClick={() => shiftPeriod(1)}>
          <CaretRight size={18} />
        </IconButton>
      </div>

      <section className="focus-stat-strip" aria-label="当前周期统计摘要">
        <Stat icon={<Timer size={18} />} label="有效总时长" value={formatDuration(analytics?.totalSeconds ?? 0)} />
        <Stat icon={<Play size={18} />} label="专注时长" value={formatDuration(analytics?.workSeconds ?? 0)} />
        <Stat icon={<Coffee size={18} />} label="休息时长" value={formatDuration(analytics?.restSeconds ?? 0)} />
        <Stat icon={<Pause size={18} />} label="暂停时长" value={formatDuration(analytics?.pauseSeconds ?? 0)} />
        <Stat icon={<Clock size={18} />} label="启动次数" value={`${analytics?.workStartCount ?? 0} 次`} />
        <Stat label="最长单次" value={formatDuration(analytics?.longestSessionSeconds ?? 0)} />
        <Stat label="活跃天数" value={`${analytics?.activeDays ?? 0} 天`} />
      </section>

      <section className={`focus-heatmap-card ${loading ? "loading" : ""}`}>
        <header>
          <div>
            <span className="section-kicker">时间分布</span>
            <h3>{periodLabels[period]}视图</h3>
          </div>
          <div className="focus-legend" aria-label="时长颜色图例">
            <span>少</span>
            {[0, 1, 2, 3, 4, 5].map((level) => (
              <i key={level} className={`focus-legend-cell heat-level-${level}`} />
            ))}
            <span>多</span>
            <b><i className="focus-start-sample" />启动</b>
          </div>
        </header>
        {analytics && period === "day" && <DayHeatmap cells={analytics.cells} />}
        {analytics && period === "week" && <WeekHeatmap cells={analytics.cells} />}
        {analytics && period === "month" && <MonthHeatmap cells={analytics.cells} />}
        {!loading && analytics?.cells.every(
          (cell) => cell.workSeconds + cell.restSeconds + cell.pauseSeconds === 0,
        ) && (
          <p className="focus-empty-hint">这个周期还没有新的番茄钟记录。</p>
        )}
      </section>
    </div>
  );
}

function Stat({
  icon,
  label,
  value,
}: {
  icon?: React.ReactNode;
  label: string;
  value: string;
}) {
  return (
    <div>
      <span>{icon}{label}</span>
      <strong>{value}</strong>
    </div>
  );
}

function DayHeatmap({ cells }: { cells: FocusAnalyticsCellDto[] }) {
  return (
    <div className="focus-day-chart" aria-label="一天半小时专注分布">
      <TimeAxis />
      <div className="focus-cell-row day-cell-row">
        {cells.map((cell) => <HeatCell key={cell.key} cell={cell} period="day" />)}
      </div>
    </div>
  );
}

function WeekHeatmap({ cells }: { cells: FocusAnalyticsCellDto[] }) {
  const days = useMemo(() => {
    const grouped = new Map<string, FocusAnalyticsCellDto[]>();
    cells.forEach((cell) => grouped.set(cell.date, [...(grouped.get(cell.date) ?? []), cell]));
    return [...grouped.entries()];
  }, [cells]);
  return (
    <div className="focus-week-chart" aria-label="一周半小时专注分布">
      <div className="week-axis-spacer" />
      <TimeAxis />
      {days.map(([date, dayCells]) => (
        <div className="focus-week-row" key={date}>
          <span>{weekdayLabel(date)}<small>{date.slice(5)}</small></span>
          <div className="focus-cell-row week-cell-row">
            {dayCells.map((cell) => <HeatCell key={cell.key} cell={cell} period="week" />)}
          </div>
        </div>
      ))}
    </div>
  );
}

function MonthHeatmap({ cells }: { cells: FocusAnalyticsCellDto[] }) {
  const leading = cells.length
    ? (new Date(`${cells[0].date}T12:00:00`).getDay() + 6) % 7
    : 0;
  return (
    <div className="focus-month-chart" aria-label="月度日历专注分布">
      <div className="month-weekdays">
        {['一', '二', '三', '四', '五', '六', '日'].map((day) => <span key={day}>周{day}</span>)}
      </div>
      <div className="month-grid">
        {Array.from({ length: leading }, (_, index) => <i key={`blank-${index}`} />)}
        {cells.map((cell) => <HeatCell key={cell.key} cell={cell} period="month" />)}
      </div>
    </div>
  );
}

function TimeAxis() {
  return (
    <div className="focus-time-axis" aria-hidden="true">
      {[0, 4, 8, 12, 16, 20, 24].map((hour) => <span key={hour}>{String(hour).padStart(2, '0')}:00</span>)}
    </div>
  );
}

function HeatCell({ cell, period }: { cell: FocusAnalyticsCellDto; period: FocusPeriod }) {
  const total = cell.workSeconds + cell.restSeconds + cell.pauseSeconds;
  const level = heatLevel(total, period);
  const date = new Date(`${cell.date}T12:00:00`);
  const label = period === "month" ? String(date.getDate()) : cell.label;
  return (
    <button
      className={`focus-heat-cell heat-level-${level} ${period === "month" ? "month-cell" : ""}`}
      aria-label={`${cell.date} ${cell.label}，专注 ${formatDuration(cell.workSeconds)}，休息 ${formatDuration(cell.restSeconds)}，暂停 ${formatDuration(cell.pauseSeconds)}`}
    >
      {period === "month" && <span>{label}</span>}
      {cell.workStarts.length > 0 && <i className="focus-start-marker" />}
      <span className="focus-cell-tooltip" role="tooltip">
        <strong>{period === "month" ? cell.date : `${cell.date} ${cell.label}`}</strong>
        <em>专注 {formatDuration(cell.workSeconds)}</em>
        <em>休息 {formatDuration(cell.restSeconds)}</em>
        <em>暂停 {formatDuration(cell.pauseSeconds)}</em>
        <em>合计 {formatDuration(total)}</em>
        {cell.workStarts.length > 0 && (
          <small>
            启动 {cell.workStarts.map((start) => `${start.time}${start.taskTitle ? ` · ${start.taskTitle}` : ""}`).join("；")}
          </small>
        )}
      </span>
    </button>
  );
}

function heatLevel(seconds: number, period: FocusPeriod): number {
  if (seconds <= 0) return 0;
  const thresholds = period === "month"
    ? [30 * 60, 60 * 60, 2 * 60 * 60, 4 * 60 * 60]
    : [7.5 * 60, 15 * 60, 22.5 * 60, 30 * 60];
  const index = thresholds.findIndex((threshold) => seconds <= threshold);
  return index < 0 ? 5 : index + 1;
}

function formatDuration(seconds: number): string {
  const roundedMinutes = Math.round(seconds / 60);
  if (roundedMinutes < 60) return `${roundedMinutes} 分钟`;
  const hours = Math.floor(roundedMinutes / 60);
  const minutes = roundedMinutes % 60;
  return minutes ? `${hours} 小时 ${minutes} 分` : `${hours} 小时`;
}

function weekdayLabel(value: string): string {
  return new Intl.DateTimeFormat("zh-CN", { weekday: "short" }).format(new Date(`${value}T12:00:00`));
}
