import { useEffect, useState, type MouseEvent } from "react";
import {
  Bell,
  CaretRight,
  CheckCircle,
  ClockCounterClockwise,
  ListChecks,
  MagnifyingGlass,
  Minus,
  Moon,
  SidebarSimple,
  Sparkle,
  Sun,
  Timer,
  X,
} from "@phosphor-icons/react";
import {
  bridge,
  type GlobalSearchResult,
  type TaskActivityDto,
} from "../bridge";
import {
  IconButton,
  navItems,
  toLocalDateInput,
  type PageId,
} from "../ui";

export function AppNav({
  activePage,
  onNavigate,
  collapsed,
  onToggle,
}: {
  activePage: PageId;
  onNavigate(page: PageId): void;
  collapsed: boolean;
  onToggle(): void;
}) {
  return (
    <aside className={`app-nav ${collapsed ? "collapsed" : ""}`}>
      <div className="brand-row">
        <div className="brand-mark">
          <Sparkle size={19} weight="fill" />
        </div>
        {!collapsed && <strong>Taskato</strong>}
        <IconButton
          label={collapsed ? "展开导航" : "收起导航"}
          onClick={onToggle}
          className="nav-collapse"
        >
          <SidebarSimple size={19} />
        </IconButton>
      </div>
      <nav aria-label="主要导航">
        {navItems.map(({ id, label, icon: Icon }) => (
          <button
            key={id}
            className={`nav-item ${activePage === id ? "active" : ""}`}
            onClick={() => onNavigate(id)}
            title={collapsed ? label : undefined}
          >
            <Icon size={20} weight={activePage === id ? "fill" : "regular"} />
            {!collapsed && <span>{label}</span>}
          </button>
        ))}
      </nav>
      <div className="nav-spacer" />
      <div className="focus-summary">
        <Timer size={20} />
        {!collapsed && (
          <div>
            <strong>本地专注</strong>
            <span>C# 计时器</span>
          </div>
        )}
      </div>
      <div className="profile-row">
        <span className="avatar">T</span>
        {!collapsed && (
          <span className="profile-copy">
            <strong>本地用户</strong>
            <small>{bridge.isNative ? "数据已连接" : "浏览器预览"}</small>
          </span>
        )}
      </div>
    </aside>
  );
}

export function TopBar({
  activePage,
  appearanceMode,
  onSearch,
  onNotifications,
  onToggleAppearance,
}: {
  activePage: PageId;
  appearanceMode: "dark" | "light";
  onSearch(): void;
  onNotifications(): void;
  onToggleAppearance(): void;
}) {
  const title =
    navItems.find((item) => item.id === activePage)?.label ?? "任务";
  const drag = (event: MouseEvent<HTMLElement>) => {
    if (event.button !== 0) return;
    if ((event.target as HTMLElement).closest("button,input")) return;
    void bridge.request("window.drag");
  };
  return (
    <header className="top-bar" onMouseDown={drag}>
      <div className="top-bar-copy">
        <span className="eyebrow">
          {new Intl.DateTimeFormat("zh-CN", { dateStyle: "full" }).format(
            new Date(),
          )}
        </span>
        <h1>{title}</h1>
      </div>
      <button className="search-command" onClick={onSearch}>
        <MagnifyingGlass size={17} />
        <span>搜索任务与记录</span>
        <kbd>Ctrl K</kbd>
      </button>
      <div className="top-actions">
        <button
          className="appearance-toggle"
          aria-label={
            appearanceMode === "dark" ? "切换到日间模式" : "切换到夜间模式"
          }
          title={
            appearanceMode === "dark" ? "切换到日间模式" : "切换到夜间模式"
          }
          onClick={onToggleAppearance}
        >
          {appearanceMode === "dark" ? <Sun size={17} /> : <Moon size={17} />}
          <span>{appearanceMode === "dark" ? "日间" : "夜间"}</span>
        </button>
        <IconButton label="通知中心" onClick={onNotifications}>
          <Bell size={19} />
        </IconButton>
        <span className="window-divider" />
        <IconButton
          label="最小化"
          onClick={() => void bridge.request("window.minimize")}
        >
          <Minus size={18} />
        </IconButton>
        <IconButton
          label="最大化或还原"
          onClick={() => void bridge.request("window.maximize")}
        >
          <SidebarSimple size={18} />
        </IconButton>
        <IconButton
          label="关闭到托盘"
          onClick={() => void bridge.request("window.close")}
        >
          <X size={18} />
        </IconButton>
      </div>
    </header>
  );
}

export function GlobalSearchDialog({
  open,
  onClose,
  onOpenTask,
  onOpenHistory,
}: {
  open: boolean;
  onClose(): void;
  onOpenTask(taskId: number): void;
  onOpenHistory(): void;
}) {
  const [query, setQuery] = useState("");
  const [result, setResult] = useState<GlobalSearchResult>({
    tasks: [],
    activities: [],
  });
  const [status, setStatus] = useState<"idle" | "loading" | "ready" | "error">(
    "idle",
  );

  useEffect(() => {
    if (!open) {
      setQuery("");
      setResult({ tasks: [], activities: [] });
      setStatus("idle");
      return;
    }
    if (query.trim().length < 2) {
      setResult({ tasks: [], activities: [] });
      setStatus("idle");
      return;
    }
    const timer = window.setTimeout(() => {
      setStatus("loading");
      void bridge
        .request<GlobalSearchResult>("search.global", {
          keyword: query.trim(),
        })
        .then((value) => {
          setResult(value);
          setStatus("ready");
        })
        .catch(() => setStatus("error"));
    }, 220);
    return () => window.clearTimeout(timer);
  }, [open, query]);

  useEffect(() => {
    if (!open) return;
    const closeOnEscape = (event: KeyboardEvent) => {
      if (event.key === "Escape") onClose();
    };
    window.addEventListener("keydown", closeOnEscape);
    return () => window.removeEventListener("keydown", closeOnEscape);
  }, [onClose, open]);

  if (!open) return null;
  const hasResults = result.tasks.length > 0 || result.activities.length > 0;
  return (
    <div className="modal-backdrop command-backdrop" onMouseDown={onClose}>
      <section
        className="command-palette"
        role="dialog"
        aria-modal="true"
        aria-label="全局搜索"
        onMouseDown={(event) => event.stopPropagation()}
      >
        <label className="command-input">
          <MagnifyingGlass size={19} />
          <input
            autoFocus
            type="search"
            placeholder="搜索任务内容和历史记录…"
            value={query}
            onChange={(event) => setQuery(event.target.value)}
          />
          <kbd>Esc</kbd>
        </label>
        <div className="command-results">
          {result.tasks.length > 0 && (
            <section>
              <h3>任务</h3>
              {result.tasks.map((task) => (
                <button key={task.id} onClick={() => onOpenTask(task.id)}>
                  <ListChecks size={17} />
                  <span>
                    <strong>{task.title}</strong>
                    <small>第 {task.depth + 1} 层任务</small>
                  </span>
                  <CaretRight size={15} />
                </button>
              ))}
            </section>
          )}
          {result.activities.length > 0 && (
            <section>
              <h3>历史记录</h3>
              {result.activities.map((activity) => (
                <button key={activity.id} onClick={onOpenHistory}>
                  <ClockCounterClockwise size={17} />
                  <span>
                    <strong>{activity.title}</strong>
                    <small>
                      {activity.eventLabel} · {activity.detail}
                    </small>
                  </span>
                  <CaretRight size={15} />
                </button>
              ))}
            </section>
          )}
          {status === "idle" && <p>输入至少两个字符开始搜索。</p>}
          {status === "loading" && <p>正在搜索本地任务和记录…</p>}
          {status === "error" && <p>搜索失败，请稍后重试。</p>}
          {status === "ready" && !hasResults && <p>没有找到匹配内容。</p>}
        </div>
      </section>
    </div>
  );
}

export function NotificationCenter({
  open,
  onClose,
  onOpenHistory,
  onOpenSettings,
}: {
  open: boolean;
  onClose(): void;
  onOpenHistory(): void;
  onOpenSettings(): void;
}) {
  const [items, setItems] = useState<TaskActivityDto[]>([]);
  useEffect(() => {
    if (!open) return;
    const today = toLocalDateInput();
    void bridge
      .request<TaskActivityDto[]>("history.events", {
        startDate: today,
        endDate: today,
        eventFilter: "all",
        sortDesc: true,
      })
      .then((events) =>
        setItems(
          events
            .filter((item) =>
              ["completed", "reopened", "deleted"].includes(item.eventType),
            )
            .slice(0, 5),
        ),
      );
  }, [open]);
  if (!open) return null;
  return (
    <div className="notification-popover" role="dialog" aria-label="最近通知">
      <header>
        <div>
          <strong>最近通知</strong>
          <span>任务状态变化与桌面提醒</span>
        </div>
        <IconButton label="关闭通知中心" onClick={onClose}>
          <X size={16} />
        </IconButton>
      </header>
      <div className="notification-list">
        {items.length ? (
          items.map((item) => (
            <button key={item.id} onClick={onOpenHistory}>
              <CheckCircle size={17} />
              <span>
                <strong>{item.eventLabel} · {item.title}</strong>
                <small>{item.detail}</small>
              </span>
            </button>
          ))
        ) : (
          <p>今天还没有需要关注的任务通知。</p>
        )}
      </div>
      <footer>
        <button className="text-action" onClick={onOpenHistory}>
          查看完整历史
        </button>
        <button className="text-action" onClick={onOpenSettings}>
          提醒设置
        </button>
      </footer>
    </div>
  );
}
