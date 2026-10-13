export interface TaskDto {
  id: number;
  title: string;
  parentId: number | null;
  depth: number;
  isCompleted: boolean;
  status?: "notStarted" | "inProgress" | "completed";
  priority: number;
  orderIndex: number;
  hasChildren: boolean;
  canAddChild: boolean;
  completedLeafCount: number;
  totalLeafCount: number;
  createdAt: string;
  completedAt: string | null;
  completionDurationMinutes: number | null;
}

export interface ProgressRecordDto {
  id: number;
  taskId: number | null;
  taskTitle: string;
  taskPath: string;
  note: string;
  startedAt: string;
  completedAt: string;
  createdAt: string;
  originalDurationMinutes: number;
  durationMinutes: number;
  isDurationAdjusted: boolean;
  endReason: string;
  endReasonDisplay: string;
  source: "pomodoro" | "manual" | "legacypomodorotask";
  canOpenTask: boolean;
}

export interface TaskProgressResult {
  records: ProgressRecordDto[];
  ownCount: number;
  allCount: number;
  ownDurationMinutes: number;
  allDurationMinutes: number;
  lastProgressAt: string | null;
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
  phase: "work" | "rest";
  remainingSeconds: number;
  minimumAdjustableSeconds: number;
  maximumAdjustableSeconds: number;
  adjustmentStepSeconds: number;
  canAdjustRemaining: boolean;
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

export interface PomodoroTimelineDto {
  id: number;
  occurredAt: string;
  kind: "action" | "completion" | "reminder" | "choice";
  title: string;
  detail: string;
  taskTitle: string;
  durationSeconds?: number | null;
  durationLabel?: string;
}

export interface FocusStartDto {
  time: string;
  timerName: string;
  taskId: number | null;
  taskTitle: string;
}

export interface FocusAnalyticsCellDto {
  key: string;
  date: string;
  label: string;
  startsAt: string;
  endsAt: string;
  workSeconds: number;
  restSeconds: number;
  pauseSeconds: number;
  workStarts: FocusStartDto[];
}

export interface FocusAnalyticsResult {
  period: "day" | "week" | "month";
  anchorDate: string;
  rangeLabel: string;
  rangeStart: string;
  rangeEndExclusive: string;
  workSeconds: number;
  restSeconds: number;
  pauseSeconds: number;
  totalSeconds: number;
  workStartCount: number;
  longestSessionSeconds: number;
  activeDays: number;
  cells: FocusAnalyticsCellDto[];
}

export interface SummaryRecord {
  taskId: number;
  titleSnapshot: string;
  pathSnapshot: string;
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
  taskDurationMinutes: number;
  taskDurationDisplay: string;
  focusDurationMinutes?: number;
  focusDurationDisplay?: string;
  totalDurationMinutes: number;
  totalDurationDisplay: string;
  progressCount?: number;
  progressDurationMinutes?: number;
  progressDurationDisplay?: string;
  progressRecords?: ProgressRecordDto[];
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

export interface TaskImageDto {
  id: number;
  fileName: string;
  thumbnailUrl: string;
}
