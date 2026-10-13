import { useCallback, useEffect, useState } from "react";
import { X } from "@phosphor-icons/react";
import { bridge, type SettingsDto } from "./bridge";
import {
  AppNav,
  GlobalSearchDialog,
  NotificationCenter,
  TopBar,
} from "./components/AppChrome";
import {
  HistoryPage,
  FocusAnalyticsPage,
  SettingsPage,
  SummaryPage,
  TaskPage,
} from "./pages/WorkspacePages";
import {
  applyTheme,
  IconButton,
  navItems,
  type PageId,
} from "./ui";

/// 只负责应用级导航和错误边界；各业务页面与桌面框架分别位于独立模块。
export function App() {
  const [storedFocusTargets] = useState(() => {
    const rawValue = window.localStorage.getItem("taskato.focusTargets");
    if (rawValue === null) return null;
    try {
      const value = JSON.parse(rawValue);
      if (
        !Number.isInteger(value?.selectedTaskId) || value.selectedTaskId <= 0 ||
        !Array.isArray(value.taskIds) ||
        !value.taskIds.every((id: unknown) => typeof id === "number" && Number.isInteger(id) && id > 0)
      ) {
        throw new Error("专注目标存储格式无效");
      }
      return value as { selectedTaskId: number; taskIds: number[] };
    } catch (error) {
      console.error("无法恢复专注目标", error);
      return null;
    }
  });
  const [activePage, setActivePage] = useState<PageId>("tasks");
  const [error, setError] = useState<string | null>(null);
  const [pageRefreshVersion, setPageRefreshVersion] = useState(0);
  const [searchOpen, setSearchOpen] = useState(false);
  const [notificationsOpen, setNotificationsOpen] = useState(false);
  const [requestedTaskId, setRequestedTaskId] = useState<number | null>(null);
  const [sessionSelectedTaskId, setSessionSelectedTaskId] = useState<
    number | null
  >(storedFocusTargets?.selectedTaskId ?? null);
  const [sessionProgressTargetTaskIds, setSessionProgressTargetTaskIds] = useState<number[]>(storedFocusTargets?.taskIds ?? []);
  const [sessionTaskViewMode, setSessionTaskViewMode] = useState<
    "detail" | "create"
  >("detail");
  const [appearanceMode, setAppearanceMode] = useState<"dark" | "light">(
    "dark",
  );
  const reportError = useCallback((value: unknown) => {
    setError(value instanceof Error ? value.message : String(value));
  }, []);
  useEffect(() => {
    try {
      // 所属任务必须随标记一起恢复，否则默认选中其他任务会清空标记。
      if (sessionSelectedTaskId === null || sessionProgressTargetTaskIds.length === 0) {
        window.localStorage.removeItem("taskato.focusTargets");
      } else {
        window.localStorage.setItem("taskato.focusTargets", JSON.stringify({
          selectedTaskId: sessionSelectedTaskId,
          taskIds: sessionProgressTargetTaskIds,
        }));
      }
    } catch (error) {
      reportError(error);
    }
  }, [reportError, sessionSelectedTaskId, sessionProgressTargetTaskIds]);
  const openTask = useCallback((taskId: number) => {
    setActivePage("tasks");
    setSessionTaskViewMode("detail");
    setSearchOpen(false);
    setNotificationsOpen(false);
    setSessionProgressTargetTaskIds([]);
    setSessionSelectedTaskId(taskId);
    setRequestedTaskId(null);
    requestAnimationFrame(() => setRequestedTaskId(taskId));
  }, []);
  const handleRequestedTaskHandled = useCallback(
    () => setRequestedTaskId(null),
    [],
  );

  useEffect(() => {
    void bridge
      .request("workspace.context.set", {
        activePage,
        taskViewMode: sessionTaskViewMode,
        selectedTaskId: sessionSelectedTaskId,
        progressTargetTaskId: null,
      })
      .catch(reportError);
  }, [
    activePage,
    reportError,
    sessionSelectedTaskId,
    sessionTaskViewMode,
  ]);

  useEffect(() => {
    void bridge
      .request<{ protocolVersion: number }>("app.ready")
      .then((result) => {
        if (result.protocolVersion !== 1)
          throw new Error(`不支持的本地桥接协议：${result.protocolVersion}`);
      })
      .catch(reportError);
    void bridge
      .request<SettingsDto>("settings.get")
      .then((settings) => {
        setAppearanceMode(settings.appearanceMode);
        applyTheme(settings);
      })
      .catch(reportError);
    const unsubscribeNavigation = bridge.subscribe(
      "app.navigate",
      (payload) => {
        const page = (payload as { page?: PageId }).page;
        if (page && navItems.some((item) => item.id === page))
          setActivePage(page);
      },
    );
    const unsubscribeSettings = bridge.subscribe(
      "settings.changed",
      (payload) => {
        const settings = payload as SettingsDto;
        setAppearanceMode(settings.appearanceMode);
        applyTheme(settings);
      },
    );
    return () => {
      unsubscribeNavigation();
      unsubscribeSettings();
    };
  }, [reportError]);

  useEffect(() => {
    const handler = (event: KeyboardEvent) => {
      if (event.ctrlKey && event.key.toLowerCase() === "k") {
        event.preventDefault();
        setSearchOpen(true);
      }
    };
    window.addEventListener("keydown", handler);
    return () => window.removeEventListener("keydown", handler);
  }, []);

  return (
    <div className="app-shell nav-is-collapsed">
      <AppNav
        activePage={activePage}
        onNavigate={(page) => {
          setActivePage(page);
          setNotificationsOpen(false);
        }}
      />
      <div className="app-main">
        <TopBar
          reportError={reportError}
          activePage={activePage}
          appearanceMode={appearanceMode}
          onSearch={() => setSearchOpen(true)}
          onNotifications={() => setNotificationsOpen((value) => !value)}
          onToggleAppearance={() => {
            const next = appearanceMode === "dark" ? "light" : "dark";
            void bridge
              .request<SettingsDto>("settings.save", { appearanceMode: next })
              .then((settings) => {
                setAppearanceMode(settings.appearanceMode);
                applyTheme(settings);
              })
              .catch(reportError);
          }}
        />
        {activePage === "tasks" && (
          <TaskPage
            key={`tasks-${pageRefreshVersion}`}
            reportError={reportError}
            requestedTaskId={requestedTaskId}
            sessionSelectedTaskId={sessionSelectedTaskId}
            sessionProgressTargetTaskIds={sessionProgressTargetTaskIds}
            sessionTaskViewMode={sessionTaskViewMode}
            onSelectedTaskIdChange={setSessionSelectedTaskId}
            onProgressTargetTaskIdsChange={setSessionProgressTargetTaskIds}
            onTaskViewModeChange={setSessionTaskViewMode}
            onRequestedTaskHandled={handleRequestedTaskHandled}
          />
        )}
        {activePage === "summary" && (
          <SummaryPage
            key={`summary-${pageRefreshVersion}`}
            reportError={reportError}
            onOpenTask={openTask}
          />
        )}
        {activePage === "focus" && (
          <FocusAnalyticsPage
            key={`focus-${pageRefreshVersion}`}
            reportError={reportError}
          />
        )}
        {activePage === "history" && (
          <HistoryPage
            key={`history-${pageRefreshVersion}`}
            reportError={reportError}
            onOpenTask={openTask}
          />
        )}
        {activePage === "settings" && (
          <SettingsPage
            key={`settings-${pageRefreshVersion}`}
            reportError={reportError}
          />
        )}
      </div>
      <GlobalSearchDialog
        open={searchOpen}
        onClose={() => setSearchOpen(false)}
        onOpenTask={openTask}
        onOpenHistory={() => {
          setSearchOpen(false);
          setActivePage("history");
        }}
      />
      <NotificationCenter
        open={notificationsOpen}
        onClose={() => setNotificationsOpen(false)}
        onOpenHistory={() => {
          setNotificationsOpen(false);
          setActivePage("history");
        }}
        onOpenSettings={() => {
          setNotificationsOpen(false);
          setActivePage("settings");
        }}
      />
      {error && (
        <div className="error-toast" role="alert">
          <span>{error}</span>
          <button
            className="text-action"
            onClick={() => {
              setError(null);
              setPageRefreshVersion((value) => value + 1);
            }}
          >
            重试当前页面
          </button>
          <IconButton label="关闭错误" onClick={() => setError(null)}>
            <X size={16} />
          </IconButton>
        </div>
      )}
    </div>
  );
}
