import type {
  BridgeApi,
  FocusAnalyticsResult,
  SettingsDto,
  SummaryResult,
  TaskActivityDto,
  TaskDto,
  TaskListResult,
  TimerDto,
  TimerListResult,
} from "./types";

const now = new Date().toISOString();

function recalculateTaskStats(items: TaskDto[]): TaskDto[] {
  const childrenByParent = new Map<number, TaskDto[]>();
  items.forEach((task) => {
    if (task.parentId == null) return;
    const children = childrenByParent.get(task.parentId) ?? [];
    children.push(task);
    childrenByParent.set(task.parentId, children);
  });
  const counts = new Map<number, { completed: number; total: number }>();
  const countLeaves = (task: TaskDto): { completed: number; total: number } => {
    const cached = counts.get(task.id);
    if (cached) return cached;
    const children = childrenByParent.get(task.id) ?? [];
    const result = children.length
      ? children
          .map(countLeaves)
          .reduce(
            (sum, child) => ({
              completed: sum.completed + child.completed,
              total: sum.total + child.total,
            }),
            { completed: 0, total: 0 },
          )
      : { completed: task.isCompleted ? 1 : 0, total: 1 };
    counts.set(task.id, result);
    return result;
  };

  items.forEach(countLeaves);
  return items.map((task) => {
    const children = childrenByParent.get(task.id) ?? [];
    const count = counts.get(task.id)!;
    return {
      ...task,
      hasChildren: children.length > 0,
      completedLeafCount: count.completed,
      totalLeafCount: count.total,
    };
  });
}

function reopenTaskAndAncestors(items: TaskDto[], taskId: number): TaskDto[] {
  const byId = new Map(items.map((task) => [task.id, task]));
  const reopenedIds = new Set<number>();
  let current = byId.get(taskId);
  while (current) {
    reopenedIds.add(current.id);
    current = current.parentId == null ? undefined : byId.get(current.parentId);
  }
  return items.map((task) =>
    reopenedIds.has(task.id)
      ? {
          ...task,
          isCompleted: false,
          completedAt: null,
          completionDurationMinutes: null,
        }
      : task,
  );
}

let tasks: TaskDto[] = [
  {
    id: 1,
    title: "发布 Taskato 3.0\n发布前完成关键功能验证和交付说明。",
    parentId: null,
    depth: 0,
    isCompleted: false,
    priority: 2,
    hasChildren: true,
    canAddChild: true,
    completedLeafCount: 1,
    totalLeafCount: 3,
    createdAt: now,
    completedAt: null,
    completionDurationMinutes: null,
  },
  {
    id: 2,
    title: "确定新版信息架构",
    parentId: 1,
    depth: 1,
    isCompleted: true,
    priority: 2,
    hasChildren: false,
    canAddChild: true,
    completedLeafCount: 1,
    totalLeafCount: 1,
    createdAt: now,
    completedAt: now,
    completionDurationMinutes: 28,
  },
  {
    id: 3,
    title: "重构任务工作区\n重点检查深层嵌套、选中状态和子任务输入焦点。",
    parentId: 1,
    depth: 1,
    isCompleted: false,
    priority: 2,
    hasChildren: true,
    canAddChild: true,
    completedLeafCount: 0,
    totalLeafCount: 2,
    createdAt: now,
    completedAt: null,
    completionDurationMinutes: null,
  },
  {
    id: 4,
    title: "实现详情编辑面板",
    parentId: 3,
    depth: 2,
    isCompleted: false,
    priority: 2,
    hasChildren: false,
    canAddChild: true,
    completedLeafCount: 0,
    totalLeafCount: 1,
    createdAt: now,
    completedAt: null,
    completionDurationMinutes: null,
  },
  {
    id: 5,
    title: "补充键盘快捷键",
    parentId: 3,
    depth: 2,
    isCompleted: false,
    priority: 1,
    hasChildren: false,
    canAddChild: true,
    completedLeafCount: 0,
    totalLeafCount: 1,
    createdAt: now,
    completedAt: null,
    completionDurationMinutes: null,
  },
];

const settings: SettingsDto = {
  autoStartEnabled: false,
  workMinutes: 25,
  restMinutes: 5,
  appearanceMode: "dark",
  themePrimaryColor: "#7775F5",
  themeSecondaryColor: "#A855F7",
  selectedColorIndex: 0,
  autoStartNextPomodoro: false,
  enableToastTimer: true,
  toastActionDelaySeconds: 1.5,
  showMainWindowOnTimerNotification: false,
  enableMultiplePomodoros: false,
  notificationSoundChoice: 3,
  customSoundPath: "",
  feishuEnabled: false,
  feishuWebhookUrl: "",
  feishuNotifyOnWork: true,
  feishuNotifyOnRest: true,
  feishuRepeatEnabled: true,
  feishuRestHalfwayEnabled: true,
  feishuWorkNotificationCount: 0,
  feishuRestHalfwayNotificationCount: 3,
  feishuRestCompletedNotificationCount: 6,
  feishuTestNotificationCount: 1,
  autoShowDailySummary: false,
};

export class MockBridge implements BridgeApi {
  readonly isNative = false;
  private activeTimerId = "timer-0";
  private timers: TimerDto[] = [
    {
      id: "timer-0",
      name: "主番茄钟",
      display: "25:00",
      status: "空闲",
      progress: 1,
      workMinutes: 25,
      restMinutes: 5,
      isStarted: false,
      canStart: true,
      canPause: false,
      canStop: false,
      canRest: false,
    },
  ];
  private readonly listeners = new Map<
    string,
    Set<(payload: unknown) => void>
  >();

  async request<T>(
    method: string,
    params: Record<string, unknown> = {},
  ): Promise<T> {
    let result: unknown;
    switch (method) {
      case "app.ready":
        result = { protocolVersion: 1, appVersion: "preview" };
        break;
      case "tasks.list":
        result = {
          tasks,
          detailTasks: tasks,
          selectedTaskId: tasks[0]?.id ?? null,
          maximumDepth: 5,
        } satisfies TaskListResult;
        break;
      case "tasks.add": {
        const parentId =
          typeof params.parentId === "number" ? params.parentId : null;
        const parent = tasks.find((task) => task.id === parentId);
        const created: TaskDto = {
          id: Math.max(0, ...tasks.map((task) => task.id)) + 1,
          title: String(params.title),
          parentId,
          depth: parent ? parent.depth + 1 : 0,
          isCompleted: false,
          priority: Number(params.priority ?? 0),
          hasChildren: false,
          canAddChild: true,
          completedLeafCount: 0,
          totalLeafCount: 1,
          createdAt: now,
          completedAt: null,
          completionDurationMinutes: null,
        };
        tasks = [...tasks, created];
        if (parentId != null) tasks = reopenTaskAndAncestors(tasks, parentId);
        tasks = recalculateTaskStats(tasks);
        result = created;
        this.emit("tasks.changed", {});
        break;
      }
      case "tasks.parentCandidates": {
        const taskId = Number(params.taskId);
        const descendantIds = new Set<number>();
        const pending = [taskId];
        while (pending.length) {
          const parentId = pending.shift()!;
          tasks
            .filter((task) => task.parentId === parentId)
            .forEach((task) => {
              if (descendantIds.has(task.id)) return;
              descendantIds.add(task.id);
              pending.push(task.id);
            });
        }
        result = tasks.filter(
          (task) => task.id !== taskId && !descendantIds.has(task.id),
        );
        break;
      }
      case "tasks.toggle": {
        const id = Number(params.taskId);
        const currentTask = tasks.find((task) => task.id === id);
        if (!currentTask) throw new Error("任务不存在。");
        const completed =
          typeof params.completed === "boolean"
            ? params.completed
            : !currentTask.isCompleted;
        if (
          completed &&
          currentTask.hasChildren &&
          currentTask.completedLeafCount < currentTask.totalLeafCount
        ) {
          throw new Error(
            `父任务还有 ${currentTask.totalLeafCount - currentTask.completedLeafCount} 个叶子任务未完成。`,
          );
        }
        tasks = completed
          ? tasks.map((task) =>
              task.id === id
                ? {
                    ...task,
                    isCompleted: true,
                    completedAt: now,
                    completionDurationMinutes:
                      typeof params.durationMinutes === "number"
                        ? params.durationMinutes
                        : null,
                  }
                : task,
            )
          : reopenTaskAndAncestors(tasks, id);
        tasks = recalculateTaskStats(tasks);
        result = {
          completed: tasks.find((task) => task.id === id)?.isCompleted,
        };
        this.emit("tasks.changed", {});
        break;
      }
      case "tasks.update": {
        const id = Number(params.taskId);
        tasks = tasks.map((task) =>
          task.id === id ? ({ ...task, ...params } as TaskDto) : task,
        );
        result = tasks.find((task) => task.id === id);
        this.emit("tasks.changed", {});
        break;
      }
      case "tasks.move": {
        const id = Number(params.taskId);
        const parentId =
          typeof params.parentId === "number" ? params.parentId : null;
        const parent = tasks.find((task) => task.id === parentId);
        tasks = tasks.map((task) =>
          task.id === id
            ? { ...task, parentId, depth: parent ? parent.depth + 1 : 0 }
            : task,
        );
        if (parentId != null) tasks = reopenTaskAndAncestors(tasks, parentId);
        tasks = recalculateTaskStats(tasks);
        result = { moved: true };
        this.emit("tasks.changed", {});
        break;
      }
      case "tasks.delete":
        tasks = tasks.filter((task) => task.id !== Number(params.taskId));
        tasks = recalculateTaskStats(tasks);
        result = { affected: 1 };
        this.emit("tasks.changed", {});
        break;
      case "summary.get":
        result = this.summary(String(params.date ?? now.slice(0, 10)));
        break;
      case "focus.analytics":
        result = buildMockFocusAnalytics(
          String(params.period ?? "day") as "day" | "week" | "month",
          String(params.anchorDate ?? toMockDate(new Date())),
        );
        break;
      case "summary.candidates":
        result = tasks.filter(
          (task) =>
            !task.isCompleted &&
            !tasks.some((candidate) => candidate.parentId === task.id),
        );
        break;
      case "summary.backfill":
        result = { completedTaskIds: [Number(params.taskId)] };
        break;
      case "summary.copy":
      case "summary.export":
      case "history.export":
        result = { exported: true };
        break;
      case "search.global": {
        const keyword = String(params.keyword ?? "").toLowerCase();
        const matchingTasks = tasks.filter((task) =>
          task.title.toLowerCase().includes(keyword),
        );
        result = {
          tasks: matchingTasks,
          activities: this.activities({ keyword }),
        };
        break;
      }
      case "history.search":
        result = tasks;
        break;
      case "history.events": {
        result = this.activities(params);
        break;
      }
      case "settings.get":
        result = { ...settings };
        break;
      case "settings.save":
        Object.assign(settings, params);
        result = { ...settings };
        this.emit("settings.changed", result);
        break;
      case "settings.chooseSound":
        result = { selected: false, path: settings.customSoundPath };
        break;
      case "settings.previewSound":
        result = { played: settings.notificationSoundChoice !== 0 };
        break;
      case "settings.testFeishu":
        result = Boolean(params.webhookUrl)
          ? { sent: true, errorCode: "", message: `测试消息已发送 ${Number(params.notificationCount ?? settings.feishuTestNotificationCount)} 条。` }
          : { sent: false, errorCode: "invalid_url", message: "请先填写有效的 Webhook URL。" };
        break;
      case "settings.openFeishuGuide":
        result = { shouldConfigure: true };
        break;
      case "app.backupData":
        result = { created: true, fileName: "Taskato_浏览器预览.taskato-backup" };
        break;
      case "app.restoreData":
        result = { staged: true, restartRequired: true };
        break;
      case "app.openLogsFolder":
        result = { opened: true };
        break;
      case "pomodoro.list":
        result = {
          activeId: this.activeTimerId,
          multipleEnabled: settings.enableMultiplePomodoros,
          timers: this.timers,
        } satisfies TimerListResult;
        break;
      case "pomodoro.command": {
        const command = String(params.command);
        const timerId = String(params.timerId || this.activeTimerId);
        if (command === "select") this.activeTimerId = timerId;
        else if (command === "add" && settings.enableMultiplePomodoros) {
          const id = `timer-${Date.now()}`;
          this.timers = [
            ...this.timers,
            { ...this.timers[0], id, name: `附加组 ${this.timers.length}` },
          ];
          this.activeTimerId = id;
        } else if (command === "remove" && timerId !== "timer-0") {
          this.timers = this.timers.filter((timer) => timer.id !== timerId);
          this.activeTimerId = "timer-0";
        } else {
          this.timers = this.timers.map((timer) => {
            if (timer.id !== timerId) return timer;
            if (command === "start")
              return {
                ...timer,
                status: "专注中",
                isStarted: true,
                canStart: false,
                canPause: true,
                canStop: true,
                canRest: true,
              };
            if (command === "pause")
              return {
                ...timer,
                status: timer.status === "已暂停" ? "专注中" : "已暂停",
              };
            if (command === "rest")
              return {
                ...timer,
                status: "休息中",
                display: "05:00",
                canRest: false,
              };
            if (command === "stop")
              return {
                ...timer,
                status: "空闲",
                display: "25:00",
                isStarted: false,
                canStart: true,
                canPause: false,
                canStop: false,
                canRest: false,
              };
            if (command === "increase" || command === "decrease") {
              const workMinutes = Math.min(
                240,
                Math.max(1, timer.workMinutes + (command === "increase" ? 5 : -5)),
              );
              return {
                ...timer,
                workMinutes,
                display: `${String(workMinutes).padStart(2, "0")}:00`,
              };
            }
            return timer;
          });
        }
        this.emit("pomodoro.changed", {});
        result = await this.request("pomodoro.list");
        break;
      }
      default:
        result = { accepted: true };
    }
    return result as T;
  }

  subscribe(
    eventName: string,
    listener: (payload: unknown) => void,
  ): () => void {
    const group = this.listeners.get(eventName) ?? new Set();
    group.add(listener);
    this.listeners.set(eventName, group);
    return () => group.delete(listener);
  }

  private emit(eventName: string, payload: unknown): void {
    this.listeners.get(eventName)?.forEach((listener) => listener(payload));
  }
  private summary(date: string): SummaryResult {
    const completed = tasks.filter((task) => task.isCompleted);
    return {
      date,
      summaryLine: `今天完成了 ${completed.length} 项有效成果。`,
      completedCount: completed.length,
      leafCompletedCount: completed.length,
      parentAchievementCount: 0,
      highPriorityCompleted: completed.filter((task) => task.priority >= 2)
        .length,
      totalDurationMinutes: completed.reduce(
        (sum, task) => sum + (task.completionDurationMinutes ?? 0),
        0,
      ),
      totalDurationDisplay: "28分钟",
      groups: [
        {
          topLevelTaskId: 1,
          title: "发布 Taskato 3.0",
          records: completed.map((task) => ({
            taskId: task.id,
            titleSnapshot: task.title,
            depthSnapshot: task.depth,
            prioritySnapshot: task.priority,
            kindDisplay: "子任务完成",
            completedAt: task.completedAt ?? now,
            durationDisplay: task.completionDurationMinutes
              ? `${task.completionDurationMinutes}分钟`
              : "",
            exists: true,
          })),
        },
      ],
    };
  }

  private activities(params: Record<string, unknown>): TaskActivityDto[] {
    const keyword = String(params.keyword ?? "").toLowerCase();
    const eventFilter = String(params.eventFilter ?? "all");
    return tasks
      .map(
        (task) =>
          ({
            id: `created-${task.id}`,
            taskId: task.id,
            title: task.title,
            eventType: task.isCompleted ? "completed" : "created",
            eventLabel: task.isCompleted ? "完成任务" : "创建任务",
            occurredAt: task.completedAt ?? task.createdAt,
            detail: task.isCompleted ? "浏览器预览完成记录" : "创建任务",
            tone: task.isCompleted ? "success" : "neutral",
            canOpenTask: true,
            isLegacyBackfill: false,
          }) satisfies TaskActivityDto,
      )
      .filter(
        (item) => eventFilter === "all" || item.eventType === eventFilter,
      )
      .filter(
        (item) =>
          !keyword ||
          item.title.toLowerCase().includes(keyword) ||
          item.detail.toLowerCase().includes(keyword),
      )
      .sort(
        (left, right) =>
          (params.sortDesc === false ? 1 : -1) *
          left.occurredAt.localeCompare(right.occurredAt),
      );
  }
}

function buildMockFocusAnalytics(
  period: "day" | "week" | "month",
  anchorText: string,
): FocusAnalyticsResult {
  const anchor = new Date(`${anchorText}T12:00:00`);
  const start = new Date(anchor);
  if (period === "week") {
    start.setDate(start.getDate() - ((start.getDay() + 6) % 7));
  } else if (period === "month") {
    start.setDate(1);
  }
  start.setHours(0, 0, 0, 0);
  const end = new Date(start);
  if (period === "day") end.setDate(end.getDate() + 1);
  else if (period === "week") end.setDate(end.getDate() + 7);
  else end.setMonth(end.getMonth() + 1);

  const stepMinutes = period === "month" ? 24 * 60 : 30;
  const cells = [];
  for (let cursor = new Date(start), index = 0; cursor < end; index += 1) {
    const cellStart = new Date(cursor);
    cursor = new Date(cursor.getTime() + stepMinutes * 60_000);
    const active = period === "month"
      ? [1, 2, 4, 7, 8, 11, 14, 15, 19, 22, 23, 27].includes(index)
      : index % 17 === 0 || index % 29 === 0 || (index > 17 && index < 24);
    const workSeconds = active
      ? period === "month" ? ((index % 4) + 1) * 38 * 60 : 18 * 60
      : 0;
    const restSeconds = active && index % 3 === 0
      ? period === "month" ? 18 * 60 : 5 * 60
      : 0;
    cells.push({
      key: period === "month"
        ? toMockDate(cellStart)
        : `${toMockDate(cellStart)}T${toMockTime(cellStart)}`,
      date: toMockDate(cellStart),
      label: period === "month"
        ? `${cellStart.getMonth() + 1}月${cellStart.getDate()}日`
        : toMockTime(cellStart),
      startsAt: cellStart.toISOString(),
      endsAt: cursor.toISOString(),
      workSeconds,
      restSeconds,
      workStarts: active && index % 2 === 0
        ? [{
            time: toMockTime(cellStart),
            timerName: "主番茄钟",
            taskId: 1,
            taskTitle: "发布 Taskato 3.0",
          }]
        : [],
    });
  }
  const workSeconds = cells.reduce((sum, cell) => sum + cell.workSeconds, 0);
  const restSeconds = cells.reduce((sum, cell) => sum + cell.restSeconds, 0);
  const activeDays = new Set(
    cells
      .filter((cell) => cell.workSeconds + cell.restSeconds > 0)
      .map((cell) => cell.date),
  ).size;
  return {
    period,
    anchorDate: anchorText,
    rangeLabel: period === "month"
      ? `${start.getFullYear()}年${start.getMonth() + 1}月`
      : period === "week"
        ? `${start.getMonth() + 1}月${start.getDate()}日 – ${new Date(end.getTime() - 86_400_000).getMonth() + 1}月${new Date(end.getTime() - 86_400_000).getDate()}日`
        : `${start.getFullYear()}年${start.getMonth() + 1}月${start.getDate()}日`,
    rangeStart: toMockDate(start),
    rangeEndExclusive: toMockDate(end),
    workSeconds,
    restSeconds,
    totalSeconds: workSeconds + restSeconds,
    workStartCount: cells.reduce((sum, cell) => sum + cell.workStarts.length, 0),
    longestSessionSeconds: 48 * 60,
    activeDays,
    cells,
  };
}

function toMockDate(value: Date): string {
  return `${value.getFullYear()}-${String(value.getMonth() + 1).padStart(2, "0")}-${String(value.getDate()).padStart(2, "0")}`;
}

function toMockTime(value: Date): string {
  return `${String(value.getHours()).padStart(2, "0")}:${String(value.getMinutes()).padStart(2, "0")}`;
}
