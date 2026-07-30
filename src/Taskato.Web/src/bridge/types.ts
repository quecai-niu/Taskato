export interface TaskDto {
  id: number;
  title: string;
  parentId: number | null;
  depth: number;
  isCompleted: boolean;
  priority: number;
  hasChildren: boolean;
  canAddChild: boolean;
  completedLeafCount: number;
  totalLeafCount: number;
  createdAt: string;
  completedAt: string | null;
  completionDurationMinutes: number | null;
}

export interface TaskListResult {
  tasks: TaskDto[];
  detailTasks: TaskDto[];
  selectedTaskId: number | null;
  maximumDepth: number;
}

export interface TaskActivityDto {
  id: string;
  taskId: number | null;
  title: string;
  eventType:
    | "created"
    | "updated"
    | "moved"
    | "completed"
    | "reopened"
    | "deleted"
    | "migrationboundary";
  eventLabel: string;
  occurredAt: string;
  detail: string;
  tone: "neutral" | "success" | "warning";
  canOpenTask: boolean;
  isLegacyBackfill: boolean;
}

export interface GlobalSearchResult {
  tasks: TaskDto[];
  activities: TaskActivityDto[];
}

export interface TimerDto {
  id: string;
  name: string;
  display: string;
  status: string;
  progress: number;
  workMinutes: number;
  restMinutes: number;
  isStarted: boolean;
  canStart: boolean;
  canPause: boolean;
  canStop: boolean;
  canRest: boolean;
}

export interface TimerListResult {
  activeId: string;
  multipleEnabled: boolean;
  timers: TimerDto[];
}

export interface SummaryRecord {
  taskId: number;
  titleSnapshot: string;
  depthSnapshot: number;
  prioritySnapshot: number;
  kindDisplay: string;
  completedAt: string;
  durationDisplay: string;
  exists: boolean;
}

export interface SummaryResult {
  date: string;
  summaryLine: string;
  completedCount: number;
  leafCompletedCount: number;
  parentAchievementCount: number;
  highPriorityCompleted: number;
  totalDurationMinutes: number;
  totalDurationDisplay: string;
  groups: Array<{
    topLevelTaskId: number;
    title: string;
    records: SummaryRecord[];
  }>;
}

export interface SettingsDto {
  autoStartEnabled: boolean;
  workMinutes: number;
  restMinutes: number;
  appearanceMode: "dark" | "light";
  themePrimaryColor: string;
  themeSecondaryColor: string;
  selectedColorIndex: number;
  autoStartNextPomodoro: boolean;
  enableToastTimer: boolean;
  toastActionDelaySeconds: number;
  showMainWindowOnTimerNotification: boolean;
  enableMultiplePomodoros: boolean;
  notificationSoundChoice: number;
  customSoundPath: string;
  feishuEnabled: boolean;
  feishuWebhookUrl: string;
  feishuNotifyOnWork: boolean;
  feishuNotifyOnRest: boolean;
  feishuRepeatEnabled: boolean;
  feishuRestHalfwayEnabled: boolean;
  feishuWorkNotificationCount: number;
  feishuRestHalfwayNotificationCount: number;
  feishuRestCompletedNotificationCount: number;
  feishuTestNotificationCount: number;
  autoShowDailySummary: boolean;
  useLegacyWorkspace: boolean;
}

export interface BridgeEvent<T = unknown> {
  event: string;
  payload: T;
}

export interface BridgeApi {
  readonly isNative: boolean;
  request<T>(method: string, params?: Record<string, unknown>): Promise<T>;
  subscribe(
    eventName: string,
    listener: (payload: unknown) => void,
  ): () => void;
}
