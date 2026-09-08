import { act } from "react";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import {
  FocusAnalyticsPage,
  HistoryPage,
  SettingsPage,
  SummaryPage,
  TaskPage,
} from "../src/pages/WorkspacePages";
import { buildTree, filterTasksWithAncestors } from "../src/pages/TaskPage";
import { sortBackfillTasks } from "../src/pages/SummaryPage";
import {
  calculateTaskPickerPlacement,
  filterAndSortTaskOptions,
  TaskPicker,
} from "../src/components/TaskPicker";
import {
  bridge,
  type ProgressRecordDto,
  type SummaryResult,
  type TaskDto,
  type TaskListResult,
  type TaskProgressResult,
} from "../src/bridge";

function createTask(overrides: Partial<TaskDto> & Pick<TaskDto, "id" | "title">): TaskDto {
  return {
    parentId: null,
    depth: 0,
    isCompleted: false,
    status: "notStarted",
    priority: 0,
    hasChildren: false,
    canAddChild: true,
    completedLeafCount: 0,
    totalLeafCount: 1,
    createdAt: new Date().toISOString(),
    completedAt: null,
    completionDurationMinutes: null,
    ...overrides,
  };
}

function createProgressRecord(id: number, note: string): ProgressRecordDto {
  const completedAt = new Date(Date.UTC(2026, 7, 28, 12, 30 - id));
  return {
    id,
    taskId: 1,
    taskTitle: "发布 Taskato 3.0",
    taskPath: "发布 Taskato 3.0",
    note,
    startedAt: new Date(completedAt.getTime() - 25 * 60_000).toISOString(),
    completedAt: completedAt.toISOString(),
    createdAt: completedAt.toISOString(),
    originalDurationMinutes: 25,
    durationMinutes: 25,
    isDurationAdjusted: false,
    endReason: "completed",
    endReasonDisplay: "自然结束",
    source: "pomodoro",
    canOpenTask: true,
  };
}

describe("独立业务页面", () => {
  let host: HTMLDivElement;
  let root: Root;

  beforeEach(() => {
    (
      globalThis as typeof globalThis & {
        IS_REACT_ACT_ENVIRONMENT?: boolean;
      }
    ).IS_REACT_ACT_ENVIRONMENT = true;
    window.localStorage.clear();
    host = document.createElement("div");
    document.body.append(host);
    root = createRoot(host);
  });

  afterEach(async () => {
    await act(async () => root.unmount());
    vi.restoreAllMocks();
    host.remove();
  });

  async function renderAndFlush(node: React.ReactNode): Promise<void> {
    await act(async () => {
      root.render(node);
      await new Promise((resolve) => window.setTimeout(resolve, 0));
    });
  }

  async function openContextMenu(
    element: HTMLElement,
    clientX = 120,
    clientY = 120,
  ): Promise<MouseEvent> {
    const event = new MouseEvent("contextmenu", {
      bubbles: true,
      cancelable: true,
      button: 2,
      clientX,
      clientY,
    });
    await act(async () => {
      element.dispatchEvent(event);
      await new Promise((resolve) => window.setTimeout(resolve, 0));
    });
    return event;
  }

  it("任务页可独立加载任务树、详情与计时器", async () => {
    await renderAndFlush(
      <TaskPage reportError={() => undefined} requestedTaskId={null} />,
    );
    expect(host.textContent).toContain("项目与任务");
    expect(host.textContent).toContain("任务组");
    expect(host.textContent).toContain("未归类任务");
    expect(host.textContent).toContain("当前专注");
    expect(host.textContent).toContain("任务内容");
    expect(host.querySelector(".open-task-create")).not.toBeNull();
    expect(host.querySelector('[aria-label="新建顶级任务"]')).toBeNull();
    expect(host.querySelector('[aria-label="完成父任务"]')).not.toBeNull();
    expect(host.querySelector('[aria-label="新子任务优先级"]')).not.toBeNull();
    expect(host.querySelector('[aria-label="减少五分钟"]')).not.toBeNull();
    expect(host.querySelector('[aria-label="增加五分钟"]')).not.toBeNull();
    expect(host.querySelector(".timer-display-stage")).not.toBeNull();
    expect(host.querySelectorAll('[role="separator"]')).toHaveLength(2);
    expect(host.querySelector('[aria-label="所属父任务"]')).not.toBeNull();
    expect(host.querySelector("#task-operations select")).toBeNull();
    expect(
      host.querySelector("#task-operations .parent-task-operation > .task-picker"),
    ).not.toBeNull();
    expect(host.textContent).toContain("仅可选择有效父任务，最多支持 5 层");
    expect(host.textContent).not.toContain("移动后由 C# 校验");
  });

  it("顶级任务组默认折叠已完成的直属任务并允许展开", async () => {
    await renderAndFlush(
      <TaskPage reportError={() => undefined} requestedTaskId={1} />,
    );

    const subtaskList = host.querySelector<HTMLElement>(
      ".task-canvas .subtask-list",
    )!;
    expect(subtaskList.textContent).toContain("重构任务工作区");
    const details = subtaskList.querySelector<HTMLElement>(".subtask-details");
    expect(details?.textContent).toBe(
      "重点检查深层嵌套、选中状态和子任务输入焦点。",
    );
    expect(details?.closest(".subtask-row")?.getAttribute("aria-label")).toContain(
      details?.textContent,
    );
    expect(subtaskList.textContent).not.toContain("确定新版信息架构");

    const toggle = subtaskList.querySelector<HTMLButtonElement>(
      ".completed-subtasks-toggle",
    )!;
    expect(toggle.getAttribute("aria-expanded")).toBe("false");
    expect(toggle.textContent).toContain("已完成任务");
    expect(toggle.textContent).toContain("1 项");

    await act(async () => toggle.click());

    expect(toggle.getAttribute("aria-expanded")).toBe("true");
    expect(subtaskList.textContent).toContain("确定新版信息架构");
  });

  it("直属子任务可作为专注目标，手动推进仍默认归属详情任务", async () => {
    const progressTargetChanged = vi.fn();
    await renderAndFlush(
      <TaskPage
        reportError={() => undefined}
        requestedTaskId={1}
        onProgressTargetTaskIdChange={progressTargetChanged}
      />,
    );

    const subtaskList = host.querySelector<HTMLElement>(
      ".task-canvas .subtask-list",
    )!;
    const activeRow = [...subtaskList.querySelectorAll<HTMLElement>(".subtask-row")]
      .find((row) => row.textContent?.includes("重构任务工作区"))!;
    await act(async () => activeRow.click());

    expect(activeRow.classList.contains("progress-target")).toBe(true);
    expect(activeRow.getAttribute("aria-current")).toBe("true");
    expect(activeRow.textContent).toContain("专注目标");
    expect(progressTargetChanged).toHaveBeenLastCalledWith(3);
    expect(host.querySelector(".timer-display-stage p")?.textContent).toContain(
      "重构任务工作区",
    );

    await act(async () =>
      activeRow.querySelector<HTMLButtonElement>(".task-check")!.click()
    );
    expect(activeRow.classList.contains("progress-target")).toBe(true);
    const completionClose = host.querySelector<HTMLButtonElement>(
      '.modal [aria-label="关闭"]',
    );
    if (completionClose)
      await act(async () => completionClose.click());

    const createProgress = [...host.querySelectorAll<HTMLButtonElement>("button")]
      .find((button) => button.textContent?.includes("记录推进"))!;
    await act(async () => createProgress.click());
    let selectedOption = host.querySelector<HTMLElement>(
      '.progress-editor-modal [role="option"][aria-selected="true"]',
    )!;
    expect(selectedOption.textContent).toContain("发布 Taskato 3.0");
    expect(host.querySelector<HTMLInputElement>(".progress-current-task input")?.checked).toBe(true);
    await act(async () =>
      host
        .querySelector<HTMLButtonElement>(
          '.progress-editor-modal [aria-label="关闭"]',
        )!
        .click()
    );

    await act(async () =>
      subtaskList
        .querySelector<HTMLButtonElement>(".completed-subtasks-toggle")!
        .click()
    );
    const completedRow = [...subtaskList.querySelectorAll<HTMLElement>(
      ".subtask-row",
    )].find((row) => row.textContent?.includes("确定新版信息架构"))!;
    await act(async () => completedRow.click());
    expect(completedRow.classList.contains("progress-target")).toBe(true);
    expect(progressTargetChanged).toHaveBeenLastCalledWith(2);

    await act(async () => createProgress.click());
    expect(
      host
        .querySelector(".progress-editor-modal .task-picker-completed-filter")
        ?.getAttribute("aria-pressed"),
    ).toBe("false");
    selectedOption = host.querySelector<HTMLElement>(
      '.progress-editor-modal [role="option"][aria-selected="true"]',
    )!;
    expect(selectedOption.textContent).toContain("发布 Taskato 3.0");
    await act(async () =>
      host
        .querySelector<HTMLButtonElement>(
          '.progress-editor-modal [aria-label="关闭"]',
        )!
        .click()
    );

    await act(async () =>
      host
        .querySelector<HTMLElement>(".task-canvas")!
        .dispatchEvent(new MouseEvent("click", { bubbles: true }))
    );
    expect(host.querySelector(".subtask-row.progress-target")).toBeNull();
    expect(progressTargetChanged).toHaveBeenLastCalledWith(null);
    expect(host.querySelector(".timer-display-stage p")?.textContent).toContain(
      "发布 Taskato 3.0",
    );

    await act(async () => createProgress.click());
    selectedOption = host.querySelector<HTMLElement>(
      '.progress-editor-modal [role="option"][aria-selected="true"]',
    )!;
    expect(selectedOption.textContent).toContain("发布 Taskato 3.0");
  });

  it("任务详情优先级选择会立即更新并写入任务", async () => {
    await renderAndFlush(
      <TaskPage reportError={() => undefined} requestedTaskId={4} />,
    );
    const requestSpy = vi.spyOn(bridge, "request");
    const highPriority = host.querySelector<HTMLButtonElement>(
      '[aria-label="任务优先级：高"]',
    )!;

    await act(async () => {
      highPriority.click();
      await new Promise((resolve) => window.setTimeout(resolve, 0));
    });

    expect(requestSpy).toHaveBeenCalledWith("tasks.update", {
      taskId: 4,
      priority: 3,
    });
    expect(highPriority.getAttribute("aria-checked")).toBe("true");
    expect(host.querySelector(".tree-row.selected .priority-3")).not.toBeNull();
  });

  it("任务组详情只为有优先级的直属子任务显示标签", async () => {
    const parent = createTask({
      id: 9200,
      title: "优先级展示测试",
      hasChildren: true,
      totalLeafCount: 2,
    });
    const prioritizedChild = createTask({
      id: 9201,
      title: "高优先级子任务",
      parentId: parent.id,
      depth: 1,
      priority: 3,
    });
    const unprioritizedChild = createTask({
      id: 9202,
      title: "无优先级子任务",
      parentId: parent.id,
      depth: 1,
      priority: 0,
    });
    const originalRequest = bridge.request.bind(bridge);
    vi.spyOn(bridge, "request").mockImplementation((method, params) => {
      if (method === "tasks.list")
        return Promise.resolve({
          tasks: [parent, prioritizedChild, unprioritizedChild],
          detailTasks: [parent, prioritizedChild, unprioritizedChild],
          selectedTaskId: parent.id,
          maximumDepth: 5,
        } satisfies TaskListResult);
      return originalRequest(method, params);
    });

    await renderAndFlush(
      <TaskPage reportError={() => undefined} requestedTaskId={parent.id} />,
    );

    const detailList = host.querySelector<HTMLElement>(
      ".task-canvas .subtask-list",
    )!;
    const priorityBadges = detailList.querySelectorAll(
      ".subtask-priority-badge",
    );
    expect(priorityBadges).toHaveLength(1);
    expect(priorityBadges[0].textContent).toBe("高");
    expect(priorityBadges[0].getAttribute("title")).toBe("优先级：高");
  });

  it("任务树整行可以右击切换优先级且不会切换当前详情", async () => {
    await renderAndFlush(
      <TaskPage reportError={() => undefined} requestedTaskId={4} />,
    );
    const selectedTitle = host.querySelector(".tree-row.selected .tree-title")
      ?.textContent;
    const groupRow = [...host.querySelectorAll<HTMLElement>(".tree-row")].find(
      (row) => row.textContent?.includes("发布 Taskato 3.0"),
    )!;

    const contextEvent = await openContextMenu(groupRow, 5000, 5000);
    expect(contextEvent.defaultPrevented).toBe(true);
    let menu = host.querySelector<HTMLElement>(".priority-context-menu")!;
    expect(menu).not.toBeNull();
    expect(menu.getAttribute("aria-label")).toContain("发布 Taskato 3.0");
    expect(
      menu
        .querySelector('[aria-label="切换为中优先级"]')
        ?.getAttribute("aria-checked"),
    ).toBe("true");
    expect(Number.parseFloat(menu.style.left)).toBeLessThan(window.innerWidth);
    expect(Number.parseFloat(menu.style.top)).toBeLessThan(window.innerHeight);

    await act(async () => {
      window.dispatchEvent(new KeyboardEvent("keydown", { key: "Escape" }));
    });
    expect(host.querySelector(".priority-context-menu")).toBeNull();

    await openContextMenu(groupRow);
    await act(async () => {
      document.body.dispatchEvent(new MouseEvent("mousedown", { bubbles: true }));
    });
    expect(host.querySelector(".priority-context-menu")).toBeNull();

    await openContextMenu(groupRow);
    menu = host.querySelector<HTMLElement>(".priority-context-menu")!;
    const requestSpy = vi.spyOn(bridge, "request");
    await act(async () => {
      menu
        .querySelector<HTMLButtonElement>('[aria-label="切换为高优先级"]')!
        .click();
      await new Promise((resolve) => window.setTimeout(resolve, 0));
    });

    expect(requestSpy).toHaveBeenCalledWith("tasks.update", {
      taskId: 1,
      priority: 3,
    });
    expect(host.querySelector(".tree-row.selected .tree-title")?.textContent).toBe(
      selectedTitle,
    );
  });

  it("直属子任务整行可以右击切换优先级", async () => {
    await renderAndFlush(
      <TaskPage reportError={() => undefined} requestedTaskId={3} />,
    );
    const subtaskRow = [
      ...host.querySelectorAll<HTMLElement>(".task-canvas .subtask-row"),
    ].find((row) => row.textContent?.includes("补充键盘快捷键"))!;

    await openContextMenu(subtaskRow);
    const menu = host.querySelector<HTMLElement>(".priority-context-menu")!;
    expect(
      menu
        .querySelector('[aria-label="切换为低优先级"]')
        ?.getAttribute("aria-checked"),
    ).toBe("true");
    const requestSpy = vi.spyOn(bridge, "request");
    await act(async () => {
      menu
        .querySelector<HTMLButtonElement>('[aria-label="切换为无优先级"]')!
        .click();
      await new Promise((resolve) => window.setTimeout(resolve, 0));
    });

    expect(requestSpy).toHaveBeenCalledWith("tasks.update", {
      taskId: 5,
      priority: 0,
    });
    const updatedSubtaskRow = [
      ...host.querySelectorAll<HTMLElement>(".task-canvas .subtask-row"),
    ].find((row) => row.textContent?.includes("补充键盘快捷键"))!;
    expect(updatedSubtaskRow.querySelector(".subtask-priority-badge")).toBeNull();
  });

  it("专注统计页支持天、周、月热力图切换", async () => {
    await renderAndFlush(<FocusAnalyticsPage reportError={() => undefined} />);
    expect(host.textContent).toContain("专注统计");
    expect(host.querySelectorAll(".focus-day-chart .focus-heat-cell")).toHaveLength(48);

    const weekTab = host.querySelector<HTMLButtonElement>('[role="tab"]:not([aria-selected="true"])');
    expect(weekTab).not.toBeNull();
    await act(async () => {
      weekTab?.click();
      await new Promise((resolve) => window.setTimeout(resolve, 0));
    });
    expect(host.querySelectorAll(".focus-week-row")).toHaveLength(7);
    expect(host.querySelectorAll(".focus-week-chart .focus-heat-cell")).toHaveLength(7 * 48);

    const monthTab = [...host.querySelectorAll<HTMLButtonElement>('[role="tab"]')]
      .find((button) => button.textContent === "月");
    expect(monthTab).not.toBeNull();
    await act(async () => {
      monthTab?.click();
      await new Promise((resolve) => window.setTimeout(resolve, 0));
    });
    expect(host.querySelectorAll(".focus-month-chart .month-cell").length).toBeGreaterThanOrEqual(28);
  });

  it("任务详情显示精确任务的推进记录并支持手动补录", async () => {
    const originalRequest = bridge.request.bind(bridge);
    const requestSpy = vi.spyOn(bridge, "request").mockImplementation(
      async <T,>(method: string, params?: Record<string, unknown>) => {
        if (method === "progress.create") return createProgressRecord(99, "测试推进") as T;
        return originalRequest<T>(method, params);
      },
    );
    await renderAndFlush(
      <TaskPage reportError={() => undefined} requestedTaskId={4} />,
    );
    expect(host.textContent).toContain("推进记录");
    expect(host.textContent).toContain("完成推进记录区域的信息结构");
    const createProgress = [...host.querySelectorAll<HTMLButtonElement>("button")]
      .find((button) => button.textContent?.includes("记录推进"));
    await act(async () => createProgress?.click());
    expect(host.querySelector(".progress-editor-modal")).not.toBeNull();
    expect(host.querySelector('[aria-label="搜索任务"]')).not.toBeNull();
    const belongsToCurrent = host.querySelector<HTMLInputElement>(".progress-current-task input")!;
    expect(belongsToCurrent.checked).toBe(true);
    await act(async () => belongsToCurrent.click());
    expect(belongsToCurrent.checked).toBe(false);
    const unassigned = host.querySelector('.progress-editor-modal [role="option"][aria-selected="true"]');
    expect(unassigned?.textContent).toContain("无所属");
    await act(async () => belongsToCurrent.click());
    const saveButton = [...host.querySelectorAll<HTMLButtonElement>(".progress-editor-modal button")]
      .find((button) => button.textContent === "保存推进")!;
    await act(async () => saveButton.click());
    expect(requestSpy.mock.calls.filter(([method]) => method === "progress.create")).toHaveLength(1);
    expect(requestSpy).toHaveBeenCalledWith("progress.create", expect.objectContaining({ taskId: 4 }));
  });

  it("任务详情默认只显示最新三条推进记录并允许展开", async () => {
    const records = Array.from({ length: 5 }, (_, index) =>
      createProgressRecord(index + 1, `推进记录 ${index + 1}`),
    );
    const progressResult: TaskProgressResult = {
      records,
      ownCount: records.length,
      allCount: records.length,
      ownDurationMinutes: 125,
      allDurationMinutes: 125,
      lastProgressAt: records[0].completedAt,
    };
    const originalRequest = bridge.request.bind(bridge);
    vi.spyOn(bridge, "request").mockImplementation(
      async <T,>(method: string, params?: Record<string, unknown>) => {
        if (method === "progress.list") return progressResult as T;
        return originalRequest<T>(method, params);
      },
    );

    await renderAndFlush(
      <TaskPage reportError={() => undefined} requestedTaskId={1} />,
    );

    expect(host.querySelectorAll(".progress-record-row")).toHaveLength(3);
    expect(host.textContent).toContain("推进记录 1");
    expect(host.textContent).toContain("推进记录 3");
    expect(host.textContent).not.toContain("推进记录 4");

    const expand = host.querySelector<HTMLButtonElement>(
      '[aria-label="展开全部推进记录"]',
    )!;
    expect(expand.getAttribute("aria-expanded")).toBe("false");
    await act(async () => expand.click());
    expect(host.querySelectorAll(".progress-record-row")).toHaveLength(5);
    expect(host.textContent).toContain("推进记录 5");

    const allProgressTab = [...host.querySelectorAll<HTMLButtonElement>('[role="tab"]')]
      .find((button) => button.textContent?.includes("全部推进"))!;
    await act(async () => {
      allProgressTab.click();
      await new Promise((resolve) => window.setTimeout(resolve, 0));
    });
    expect(host.querySelectorAll(".progress-record-row")).toHaveLength(3);
    expect(
      host.querySelector('[aria-label="展开全部推进记录"]'),
    ).not.toBeNull();
  });

  it("统一任务选择器默认隐藏已完成任务并支持按完整路径搜索", () => {
    const source: TaskDto[] = [
      createTask({ id: 1, title: "项目", parentId: null, depth: 0 }),
      createTask({ id: 2, title: "加载窗口", parentId: 1, depth: 1 }),
      createTask({ id: 3, title: "已完成项", isCompleted: true }),
    ];
    expect(filterAndSortTaskOptions(source, "项目 > 加载", false, "newest")
      .map((task) => task.id)).toEqual([2]);
    expect(filterAndSortTaskOptions(source, "", false, "newest")
      .map((task) => task.id)).not.toContain(3);
    expect(filterAndSortTaskOptions(source, "", true, "newest")
      .map((task) => task.id)).toContain(3);
  });

  it("任务选择器记住最后排序方式并对非法存储值安全回退", async () => {
    const tasks = [
      createTask({ id: 1, title: "旧任务", createdAt: "2026-08-01T09:00:00" }),
      createTask({ id: 2, title: "高优先级任务", priority: 3 }),
    ];
    window.localStorage.setItem("taskato.taskPickerSortMode", "unknown");

    await renderAndFlush(
      <TaskPicker
        tasks={tasks}
        value={null}
        onChange={() => undefined}
        embedded
      />,
    );
    let sortSelect = host.querySelector<HTMLSelectElement>(
      '[aria-label="任务排序方式"]',
    )!;
    expect(sortSelect.value).toBe("newest");

    await act(async () => {
      sortSelect.value = "priority";
      sortSelect.dispatchEvent(new Event("change", { bubbles: true }));
    });
    expect(window.localStorage.getItem("taskato.taskPickerSortMode")).toBe(
      "priority",
    );

    await renderAndFlush(<div />);
    await renderAndFlush(
      <TaskPicker
        tasks={tasks}
        value={null}
        onChange={() => undefined}
        embedded
      />,
    );
    sortSelect = host.querySelector<HTMLSelectElement>(
      '[aria-label="任务排序方式"]',
    )!;
    expect(sortSelect.value).toBe("priority");
    expect(
      host.querySelector<HTMLButtonElement>(".task-picker-completed-filter")
        ?.getAttribute("aria-pressed"),
    ).toBe("false");
  });

  it("任务选择器中的不同层级任务保持左侧对齐", async () => {
    const tasks = [
      createTask({ id: 1, title: "顶级任务", depth: 0 }),
      createTask({ id: 2, title: "三级任务", parentId: 1, depth: 3 }),
    ];

    await renderAndFlush(
      <TaskPicker
        tasks={tasks}
        value={null}
        onChange={() => undefined}
        embedded
        allowUnassigned={false}
      />,
    );

    const optionRows = host.querySelectorAll<HTMLElement>(
      ".task-picker-list [role=option]",
    );
    expect(optionRows).toHaveLength(2);
    expect(optionRows[0].style.paddingLeft).toBe("");
    expect(optionRows[1].style.paddingLeft).toBe("");
  });

  it("任务选择器靠近视口底部时改为向上展开", () => {
    const placement = calculateTaskPickerPlacement(
      { top: 680, right: 1180, bottom: 730 },
      1200,
      760,
    );
    expect(placement.opensAbove).toBe(true);
    expect(placement.style.bottom).toBe(87);
    expect(placement.style.maxHeight).toBe(410);
  });

  it("任务筛选保留祖先链，不会把子任务提升成顶级", () => {
    const makeTask = (id: number, title: string, parentId: number | null, depth: number) =>
      ({ id, title, parentId, depth } as TaskDto);
    const tasks = [
      makeTask(1, "父任务", null, 0),
      makeTask(2, "命中的子任务", 1, 1),
      makeTask(3, "无关任务", null, 0),
    ];
    const filtered = filterTasksWithAncestors(tasks, "命中");
    const roots = buildTree(filtered);
    expect(filtered.map((task) => task.id)).toEqual([1, 2]);
    expect(roots).toHaveLength(1);
    expect(roots[0].children[0].id).toBe(2);
  });

  it("同级子任务先显示未完成任务，已完成任务按完成时间倒序排列", () => {
    const rootTask = {
      id: 9100,
      title: "父任务",
      parentId: null,
      isCompleted: false,
    } as TaskDto;
    const completedChild = {
      id: 9101,
      title: "较早完成的子任务",
      parentId: rootTask.id,
      isCompleted: true,
      completedAt: "2026-08-05T10:00:00+08:00",
    } as TaskDto;
    const openChild = {
      id: 9102,
      title: "未完成子任务",
      parentId: rootTask.id,
      isCompleted: false,
    } as TaskDto;
    const newlyCompletedChild = {
      id: 9103,
      title: "刚完成的子任务",
      parentId: rootTask.id,
      isCompleted: true,
      completedAt: "2026-08-05T11:00:00+08:00",
    } as TaskDto;
    const legacyCompletedChild = {
      id: 9104,
      title: "缺少完成时间的旧子任务",
      parentId: rootTask.id,
      isCompleted: true,
      completedAt: null,
    } as TaskDto;

    const roots = buildTree([
      rootTask,
      completedChild,
      legacyCompletedChild,
      openChild,
      newlyCompletedChild,
    ]);

    expect(roots[0].children.map((task) => task.id)).toEqual([
      9102,
      9103,
      9101,
      9104,
    ]);
  });

  it("未完成子任务按优先级和创建时间排序，刷新后仍保持一致", () => {
    const parent = createTask({ id: 9200, title: "父任务" });
    const older = createTask({ id: 9201, title: "旧任务", parentId: parent.id, priority: 3, createdAt: "2026-09-01T10:00:00Z" });
    const newest = createTask({ id: 9202, title: "新任务", parentId: parent.id, createdAt: "2026-09-07T10:00:00Z" });
    const sameTime = createTask({ id: 9203, title: "同时间后创建", parentId: parent.id, createdAt: newest.createdAt });
    const completed = createTask({ id: 9204, title: "已完成", parentId: parent.id, isCompleted: true });
    const newestHighPriority = createTask({ id: 9205, title: "较新的高优先级任务", parentId: parent.id, priority: 3, createdAt: "2026-09-07T10:00:00Z" });
    for (const input of [[parent, older, completed, newest, sameTime, newestHighPriority], [parent, sameTime, newestHighPriority, newest, completed, older]]) {
      expect(buildTree(input)[0].children.map((task) => task.id)).toEqual([9205, 9201, 9203, 9202, 9204]);
    }
  });

  it("新增直属子任务使用独立卡片并显示在列表顶部", async () => {
    await renderAndFlush(
      <TaskPage reportError={() => undefined} requestedTaskId={null} />,
    );

    const composer = host.querySelector(".subtask-composer")!;
    const list = host.querySelector(".subtask-list")!;

    expect(composer).not.toBeNull();
    expect(list).not.toBeNull();
    expect(composer.textContent).toContain("新增直属子任务");
    expect(composer.textContent).toContain("将创建在当前任务下");
    expect(
      composer.querySelector('[aria-label="新子任务优先级"]'),
    ).not.toBeNull();
    expect(composer.querySelector(".subtask-composer-heading")).not.toBeNull();
    expect(composer.querySelector(".subtask-composer-body")).not.toBeNull();
    expect(composer.compareDocumentPosition(list)).toBe(
      Node.DOCUMENT_POSITION_FOLLOWING,
    );
  });

  it("左侧隐藏的历史子任务仍会显示在父任务详情中", async () => {
    const visibleRoot: TaskDto = {
      id: 9001,
      title: "仅在左侧显示父任务",
      parentId: null,
      depth: 0,
      isCompleted: true,
      priority: 3,
      hasChildren: true,
      canAddChild: true,
      completedLeafCount: 1,
      totalLeafCount: 1,
      createdAt: new Date().toISOString(),
      completedAt: new Date().toISOString(),
      completionDurationMinutes: null,
    };
    const hiddenHistoricalChild: TaskDto = {
      ...visibleRoot,
      id: 9002,
      title: "左侧隐藏但详情保留的历史子任务",
      parentId: visibleRoot.id,
      depth: 1,
      hasChildren: false,
      canAddChild: true,
      completedLeafCount: 1,
      totalLeafCount: 1,
    };
    const originalRequest = bridge.request.bind(bridge);
    vi.spyOn(bridge, "request").mockImplementation((method, params) => {
      if (method === "tasks.list")
        return Promise.resolve({
          tasks: [visibleRoot],
          detailTasks: [visibleRoot, hiddenHistoricalChild],
          selectedTaskId: visibleRoot.id,
          maximumDepth: 5,
        } satisfies TaskListResult);
      return originalRequest(method, params);
    });

    await renderAndFlush(
      <TaskPage reportError={() => undefined} requestedTaskId={null} />,
    );

    expect(host.querySelector(".task-tree")?.textContent).not.toContain(
      hiddenHistoricalChild.title,
    );
    const detailList = host.querySelector<HTMLElement>(".subtask-list")!;
    expect(detailList.textContent).not.toContain(hiddenHistoricalChild.title);
    const completedToggle = detailList.querySelector<HTMLButtonElement>(
      ".completed-subtasks-toggle",
    )!;
    await act(async () => completedToggle.click());
    expect(detailList.textContent).toContain(hiddenHistoricalChild.title);
    expect(host.querySelector(".content-section")?.textContent).toContain(
      "1/1 已完成",
    );
  });

  it("中央新增页面可创建任务，番茄钟可在空闲时调整时长", async () => {
    await renderAndFlush(
      <TaskPage reportError={() => undefined} requestedTaskId={null} />,
    );

    await act(async () =>
      host.querySelector<HTMLButtonElement>(".open-task-create")!.click(),
    );
    expect(host.textContent).toContain("番茄钟专注计时不关联任务");
    const input = host.querySelector<HTMLTextAreaElement>(
      '[aria-label="新任务内容"]',
    )!;
    const highPriority = host.querySelector<HTMLButtonElement>(
      '[aria-label="新任务优先级：高"]',
    )!;
    await act(async () => highPriority.click());
    expect(highPriority.getAttribute("aria-checked")).toBe("true");
    await act(async () => {
      const valueSetter = Object.getOwnPropertyDescriptor(
        HTMLTextAreaElement.prototype,
        "value",
      )!.set!;
      valueSetter.call(input, "从中央新增页面创建的任务");
      input.dispatchEvent(new Event("input", { bubbles: true }));
    });
    const addButton = Array.from(host.querySelectorAll<HTMLButtonElement>("button"))
      .find((button) => button.textContent?.includes("创建任务"))!;
    expect(addButton.disabled).toBe(false);
    await act(async () => {
      addButton.click();
      await new Promise((resolve) => window.setTimeout(resolve, 0));
    });
    expect(host.textContent).toContain("从中央新增页面创建的任务");
    expect(host.querySelector('[aria-label="新任务内容"]')).toBeNull();

    const increaseButton = host.querySelector<HTMLButtonElement>(
      '[aria-label="增加五分钟"]',
    )!;
    await act(async () => {
      increaseButton.click();
      await new Promise((resolve) => window.setTimeout(resolve, 0));
    });
    expect(host.textContent).toContain("30 分钟专注");
  });

  it("任务树可一键收起展开，新增任务不会改变当前展开状态", async () => {
    await renderAndFlush(
      <TaskPage reportError={() => undefined} requestedTaskId={null} />,
    );

    const collapseButton = host.querySelector<HTMLButtonElement>(
      '[aria-label="全部收起"]',
    )!;
    expect(collapseButton).not.toBeNull();
    expect(host.querySelector(".tree-children")).not.toBeNull();

    await act(async () => collapseButton.click());
    expect(host.querySelector('[aria-label="全部展开"]')).not.toBeNull();
    expect(host.querySelector(".tree-children")).toBeNull();

    await act(async () =>
      host.querySelector<HTMLButtonElement>(".open-task-create")!.click(),
    );
    const rootInput = host.querySelector<HTMLTextAreaElement>(
      '[aria-label="新任务内容"]',
    )!;
    await act(async () => {
      const valueSetter = Object.getOwnPropertyDescriptor(
        HTMLTextAreaElement.prototype,
        "value",
      )!.set!;
      valueSetter.call(rootInput, "收起时新增的顶级任务");
      rootInput.dispatchEvent(new Event("input", { bubbles: true }));
    });
    await act(async () => {
      host
        .querySelector<HTMLButtonElement>(".task-create-actions .primary-button")!
        .click();
      await new Promise((resolve) => window.setTimeout(resolve, 0));
    });
    expect(host.textContent).toContain("收起时新增的顶级任务");
    expect(host.querySelector('[aria-label="全部展开"]')).not.toBeNull();
    expect(host.querySelector(".tree-children")).toBeNull();

    const childInput = host.querySelector<HTMLInputElement>(
      '[aria-label="新建子任务"]',
    )!;
    await act(async () => {
      const valueSetter = Object.getOwnPropertyDescriptor(
        HTMLInputElement.prototype,
        "value",
      )!.set!;
      valueSetter.call(childInput, "收起时新增的子任务");
      childInput.dispatchEvent(new Event("input", { bubbles: true }));
    });
    await act(async () => {
      host
        .querySelector<HTMLButtonElement>('[aria-label="添加子任务"]')!
        .click();
      await new Promise((resolve) => window.setTimeout(resolve, 0));
    });
    expect(host.querySelector('[aria-label="全部展开"]')).not.toBeNull();
    expect(host.querySelector(".tree-children")).toBeNull();

    await act(async () =>
      host
        .querySelector<HTMLButtonElement>('[aria-label="全部展开"]')!
        .click(),
    );
    expect(host.querySelector('[aria-label="全部收起"]')).not.toBeNull();
    expect(host.querySelector(".tree-children")).not.toBeNull();
    expect(host.textContent).toContain("收起时新增的子任务");
  });

  it("切换菜单后保留任务树的收起状态", async () => {
    await renderAndFlush(
      <TaskPage reportError={() => undefined} requestedTaskId={null} />,
    );
    await act(async () =>
      host
        .querySelector<HTMLButtonElement>('[aria-label="全部收起"]')!
        .click(),
    );
    expect(host.querySelector(".tree-children")).toBeNull();

    await renderAndFlush(
      <HistoryPage reportError={() => undefined} onOpenTask={() => undefined} />,
    );
    await renderAndFlush(
      <TaskPage reportError={() => undefined} requestedTaskId={null} />,
    );

    expect(host.querySelector('[aria-label="全部展开"]')).not.toBeNull();
    expect(host.querySelector(".tree-children")).toBeNull();
  });

  it("双击任务组子任务会选中任务并聚焦任务内容", async () => {
    await renderAndFlush(
      <TaskPage reportError={() => undefined} requestedTaskId={null} />,
    );

    const childTitle = [...host.querySelectorAll<HTMLButtonElement>(".tree-title")]
      .find((button) => button.textContent?.includes("确定新版信息架构"))!;
    expect(childTitle).not.toBeNull();

    await act(async () => {
      childTitle.dispatchEvent(new MouseEvent("dblclick", { bubbles: true }));
      await new Promise((resolve) => window.setTimeout(resolve, 20));
    });

    expect(
      host.querySelector<HTMLTextAreaElement>('[aria-label="任务内容"]')?.value,
    ).toBe("确定新版信息架构");
    expect(document.activeElement).toBe(
      host.querySelector<HTMLTextAreaElement>('[aria-label="任务内容"]'),
    );
  });

  it("任务内容编辑器更新唯一内容字段，不再发送 notes", async () => {
    const requestSpy = vi.spyOn(bridge, "request");
    await renderAndFlush(
      <TaskPage reportError={() => undefined} requestedTaskId={null} />,
    );
    const editor = host.querySelector<HTMLTextAreaElement>(
      '[aria-label="任务内容"]',
    )!;
    const valueSetter = Object.getOwnPropertyDescriptor(
      HTMLTextAreaElement.prototype,
      "value",
    )!.set!;

    await act(async () => {
      valueSetter.call(editor, "统一标题\n完整详细说明");
      editor.dispatchEvent(new Event("input", { bubbles: true }));
      await new Promise((resolve) => window.setTimeout(resolve, 800));
    });

    expect(requestSpy).toHaveBeenCalledWith(
      "tasks.update",
      expect.objectContaining({ title: "统一标题\n完整详细说明" }),
    );
    expect(host.querySelector(".task-title-display")?.textContent).toBe("统一标题");
    expect(host.querySelector(".timer-display-stage p")?.textContent).toBe("统一标题");
    expect(editor.value).toBe("统一标题\n完整详细说明");
    expect([...host.querySelectorAll(".tree-title")].some((node) => node.textContent === "统一标题")).toBe(true);
    expect(requestSpy).not.toHaveBeenCalledWith(
      "tasks.update",
      expect.objectContaining({ notes: expect.anything() }),
    );
  });

  it("任务内容失去焦点时立即保存，不等待自动保存延迟", async () => {
    const requestSpy = vi.spyOn(bridge, "request");
    await renderAndFlush(
      <TaskPage reportError={() => undefined} requestedTaskId={null} />,
    );
    const editor = host.querySelector<HTMLTextAreaElement>(
      '[aria-label="任务内容"]',
    )!;
    const valueSetter = Object.getOwnPropertyDescriptor(
      HTMLTextAreaElement.prototype,
      "value",
    )!.set!;

    await act(async () => {
      editor.focus();
      valueSetter.call(editor, "切换任务前必须保存的内容");
      editor.dispatchEvent(new Event("input", { bubbles: true }));
      editor.blur();
      await new Promise((resolve) => window.setTimeout(resolve, 20));
    });

    expect(requestSpy).toHaveBeenCalledWith("tasks.update", {
      taskId: expect.any(Number),
      title: "切换任务前必须保存的内容",
    });
  });

  it("子任务面包屑可以返回父任务", async () => {
    await renderAndFlush(
      <TaskPage reportError={() => undefined} requestedTaskId={null} />,
    );
    const childTitle = [...host.querySelectorAll<HTMLButtonElement>(".tree-title")]
      .find((button) => button.textContent?.includes("确定新版信息架构"))!;
    await act(async () => {
      childTitle.click();
      await new Promise((resolve) => window.setTimeout(resolve, 20));
    });
    const parentLink = host.querySelector<HTMLButtonElement>(".breadcrumb-link")!;
    expect(parentLink).not.toBeNull();
    const parentContent = parentLink.textContent ?? "";

    await act(async () => {
      parentLink.click();
      await new Promise((resolve) => window.setTimeout(resolve, 20));
    });

    expect(
      host.querySelector<HTMLTextAreaElement>('[aria-label="任务内容"]')?.value,
    ).toBe(parentContent);
    expect(host.querySelector('[aria-current="page"]')?.textContent).toBe(
      parentContent,
    );
  });

  it("左右面板会从本地存储恢复拖拽宽度", async () => {
    window.localStorage.setItem("taskato.treePaneWidth", "420");
    window.localStorage.setItem("taskato.inspectorPaneWidth", "360");

    await renderAndFlush(
      <TaskPage reportError={() => undefined} requestedTaskId={null} />,
    );

    const workspace = host.querySelector<HTMLElement>(".task-workspace")!;
    expect(workspace.style.getPropertyValue("--tree-pane-width")).toBe("420px");
    expect(workspace.style.getPropertyValue("--inspector-pane-width")).toBe(
      "360px",
    );
  });

  it("强提示开关点击后立即持久化", async () => {
    const requestSpy = vi.spyOn(bridge, "request");
    await renderAndFlush(<SettingsPage reportError={() => undefined} />);
    const toggle = host.querySelector<HTMLButtonElement>(
      '[aria-label="提示时打开软件界面"]',
    )!;

    await act(async () => {
      toggle.click();
      await new Promise((resolve) => window.setTimeout(resolve, 20));
    });

    expect(requestSpy).toHaveBeenCalledWith("settings.save", {
      showMainWindowOnTimerNotification: true,
    });
    expect(toggle.getAttribute("aria-pressed")).toBe("true");

    // 恢复 mock 的默认值，避免把跨测试共享的预览设置带入后续用例。
    await act(async () => {
      toggle.click();
      await new Promise((resolve) => window.setTimeout(resolve, 20));
    });
  });

  it("总结、历史和设置页可分别独立加载", async () => {
    await renderAndFlush(
      <SummaryPage reportError={() => undefined} onOpenTask={() => undefined} />,
    );
    expect(host.textContent).toContain("每日总结");
    expect(host.textContent).toContain("专注时长");
    expect(host.textContent).toContain("任务 28分钟 · 推进 25分钟");
    expect(host.querySelector(".summary-stats")?.textContent).not.toContain("53分钟");

    await renderAndFlush(
      <HistoryPage reportError={() => undefined} onOpenTask={() => undefined} />,
    );
    expect(host.textContent).toContain("只追加的任务活动日志");

    await renderAndFlush(<SettingsPage reportError={() => undefined} />);
    expect(host.textContent).toContain("应用设置");
    expect(host.textContent).toContain("主题颜色");
    expect(host.textContent).toContain("外观模式");
    expect(host.textContent).toContain("番茄钟时长");
    expect(host.textContent).toContain("提示音");
    expect(host.textContent).toContain("飞书通知");
    expect(
      host
        .querySelector<HTMLButtonElement>('[aria-label="提示时打开软件界面"]')
        ?.getAttribute("aria-pressed"),
    ).toBe("false");
    expect(
      host.querySelector<HTMLInputElement>('[aria-label="工作结束通知次数"]')
        ?.value,
    ).toBe("0");
    expect(
      host.querySelector<HTMLInputElement>('[aria-label="休息过半通知次数"]')
        ?.value,
    ).toBe("3");
    expect(
      host.querySelector<HTMLInputElement>('[aria-label="休息结束通知次数"]')
        ?.value,
    ).toBe("6");
    expect(
      host.querySelector<HTMLInputElement>('[aria-label="测试消息发送次数"]')
        ?.value,
    ).toBe("1");
    expect(host.textContent).toContain("备份与恢复");
    expect(host.textContent).toContain("数据与维护");
    expect(host.textContent).not.toContain("立即切换到旧版界面");
  });

  it("每日总结任务显示所属路径和固定的四档优先级", async () => {
    const originalRequest = bridge.request.bind(bridge);
    const completedAt = new Date().toISOString();
    const priorityLabels = ["无", "低", "中", "高"];
    const summaryFixture: SummaryResult = {
      date: "2026-08-31",
      summaryLine: "优先级展示测试",
      completedCount: 4,
      leafCompletedCount: 4,
      parentAchievementCount: 0,
      highPriorityCompleted: 2,
      taskDurationMinutes: 40,
      taskDurationDisplay: "40分钟",
      totalDurationMinutes: 40,
      totalDurationDisplay: "40分钟",
      progressCount: 0,
      progressDurationMinutes: 0,
      progressDurationDisplay: "0分钟",
      progressRecords: [],
      groups: [
        {
          topLevelTaskId: 100,
          title: "任务所属展示",
          records: priorityLabels.map((label, priority) => ({
            taskId: 100 + priority,
            titleSnapshot: `${label}优先级任务`,
            pathSnapshot:
              priority === 3
                ? "长期项目 > 第一阶段 > 第二阶段 > 第三阶段 > 一个用于检查省略显示但仍保留完整提示的长路径任务"
                : `任务所属展示 > ${label}优先级任务`,
            depthSnapshot: 1,
            prioritySnapshot: priority,
            kindDisplay: "子任务完成",
            completedAt,
            durationDisplay: "10分钟",
            exists: true,
          })),
        },
      ],
    };
    vi.spyOn(bridge, "request").mockImplementation(
      ((method: string, params?: Record<string, unknown>) => {
        if (method === "summary.get") return Promise.resolve(summaryFixture);
        return originalRequest(method, params);
      }) as typeof bridge.request,
    );

    await renderAndFlush(
      <SummaryPage reportError={() => undefined} onOpenTask={() => undefined} />,
    );

    const rows = [...host.querySelectorAll<HTMLElement>(".achievement-row")];
    expect(rows).toHaveLength(4);
    expect(
      rows.map(
        (row) =>
          row.querySelector<HTMLElement>(".achievement-priority-badge")
            ?.textContent,
      ),
    ).toEqual(priorityLabels);
    expect(
      rows.map(
        (row) =>
          row.querySelector<HTMLElement>(".achievement-task-path")?.textContent,
      ),
    ).toEqual(["任务所属展示", "任务所属展示", "任务所属展示", "长期项目 / 第一阶段 / 第二阶段 / 第三阶段"]);
    expect(
      rows[3]
        .querySelector<HTMLElement>(".achievement-task-path")
        ?.getAttribute("title"),
    ).toBe("长期项目 / 第一阶段 / 第二阶段 / 第三阶段");
  });

  it("补充任务支持按发布时间和优先级排序", () => {
    const makeTask = (
      id: number,
      priority: number,
      createdAt: string,
    ) => ({ id, priority, createdAt } as TaskDto);
    const tasks = [
      makeTask(1, 1, "2026-08-09T09:00:00"),
      makeTask(2, 2, "2026-08-08T09:00:00"),
      makeTask(3, 2, "2026-08-10T09:00:00"),
    ];

    expect(sortBackfillTasks(tasks, "newest").map((task) => task.id)).toEqual([
      3, 1, 2,
    ]);
    expect(sortBackfillTasks(tasks, "oldest").map((task) => task.id)).toEqual([
      2, 1, 3,
    ]);
    expect(sortBackfillTasks(tasks, "priority").map((task) => task.id)).toEqual([
      3, 2, 1,
    ]);
    expect(tasks.map((task) => task.id)).toEqual([1, 2, 3]);
  });

  it("补充任务弹窗默认使用最新发布排序", async () => {
    await renderAndFlush(
      <SummaryPage reportError={() => undefined} onOpenTask={() => undefined} />,
    );
    const openBackfill = [...host.querySelectorAll<HTMLButtonElement>("button")]
      .find((button) => button.textContent?.includes("补充任务"));

    await act(async () => {
      openBackfill?.click();
      await new Promise((resolve) => window.setTimeout(resolve, 0));
    });

    const sortSelect = host.querySelector<HTMLSelectElement>(
      '[aria-label="任务排序方式"]',
    );
    expect(sortSelect?.value).toBe("newest");
    expect(sortSelect?.options).toHaveLength(3);
    expect(host.querySelector(".backfill-modal .task-choice-list")).not.toBeNull();

    await act(async () => {
      if (sortSelect) {
        sortSelect.value = "priority";
        sortSelect.dispatchEvent(new Event("change", { bubbles: true }));
      }
    });
    expect(sortSelect?.value).toBe("priority");
  });

  it("每日总结推进记录支持通过右键菜单确认删除", async () => {
    const requestSpy = vi.spyOn(bridge, "request");
    await renderAndFlush(
      <SummaryPage reportError={() => undefined} onOpenTask={() => undefined} />,
    );
    let progressRow = host.querySelector<HTMLElement>(".summary-progress-row")!;

    const firstContextEvent = await openContextMenu(progressRow, 5000, 5000);
    expect(firstContextEvent.defaultPrevented).toBe(true);
    let menu = host.querySelector<HTMLElement>(".progress-context-menu")!;
    expect(menu).not.toBeNull();
    expect(Number.parseFloat(menu.style.left)).toBeLessThan(window.innerWidth);
    expect(Number.parseFloat(menu.style.top)).toBeLessThan(window.innerHeight);

    await act(async () => {
      window.dispatchEvent(new KeyboardEvent("keydown", { key: "Escape" }));
    });
    expect(host.querySelector(".progress-context-menu")).toBeNull();

    await openContextMenu(progressRow);
    menu = host.querySelector<HTMLElement>(".progress-context-menu")!;
    await act(async () => {
      menu.querySelector<HTMLButtonElement>('[role="menuitem"]')!.click();
      await new Promise((resolve) => window.setTimeout(resolve, 0));
    });
    let confirmDialog = host.querySelector<HTMLElement>(
      ".progress-delete-backdrop .progress-delete-modal",
    )!;
    expect(confirmDialog).not.toBeNull();
    expect(confirmDialog.textContent).toContain("删除这条推进记录？");
    await act(async () => {
      [...confirmDialog.querySelectorAll<HTMLButtonElement>("button")]
        .find((button) => button.textContent?.trim() === "取消")!
        .click();
    });
    expect(requestSpy).not.toHaveBeenCalledWith("progress.delete", {
      recordId: 1,
    });
    expect(host.querySelector(".progress-delete-modal")).toBeNull();

    progressRow = host.querySelector<HTMLElement>(".summary-progress-row")!;
    await openContextMenu(progressRow);
    menu = host.querySelector<HTMLElement>(".progress-context-menu")!;
    await act(async () => {
      menu.querySelector<HTMLButtonElement>('[role="menuitem"]')!.click();
    });
    confirmDialog = host.querySelector<HTMLElement>(".progress-delete-modal")!;
    await act(async () => {
      confirmDialog
        .querySelector<HTMLButtonElement>(".progress-delete-confirm-button")!
        .click();
      await new Promise((resolve) => window.setTimeout(resolve, 20));
    });
    expect(requestSpy).toHaveBeenCalledWith("progress.delete", { recordId: 1 });
    expect(host.querySelector(".summary-progress-row")).toBeNull();
  });

  it("每日总结推进管理支持全选、取消全选、退出和批量删除", async () => {
    for (let index = 1; index <= 3; index += 1) {
      await bridge.request("progress.create", {
        taskId: null,
        note: `批量管理记录 ${index}`,
        durationMinutes: index,
        completedAt: new Date().toISOString(),
      });
    }
    const requestSpy = vi.spyOn(bridge, "request");
    await renderAndFlush(
      <SummaryPage reportError={() => undefined} onOpenTask={() => undefined} />,
    );
    const findButton = (label: string) =>
      [...host.querySelectorAll<HTMLButtonElement>("button")].find(
        (button) => button.textContent?.trim() === label,
      )!;

    await act(async () => findButton("管理").click());
    let checkboxes = [
      ...host.querySelectorAll<HTMLInputElement>(".summary-progress-checkbox"),
    ];
    expect(checkboxes).toHaveLength(3);
    expect(findButton("删除已选").disabled).toBe(true);

    await act(async () => checkboxes[0].click());
    expect(checkboxes[0].checked).toBe(true);
    await act(async () => findButton("完成").click());
    expect(host.querySelector(".summary-progress-checkbox")).toBeNull();

    await act(async () => findButton("管理").click());
    expect(findButton("删除已选").disabled).toBe(true);
    await act(async () => findButton("全选").click());
    checkboxes = [
      ...host.querySelectorAll<HTMLInputElement>(".summary-progress-checkbox"),
    ];
    expect(checkboxes.every((checkbox) => checkbox.checked)).toBe(true);
    expect(findButton("全选").disabled).toBe(true);

    await act(async () => findButton("取消全选").click());
    expect(checkboxes.every((checkbox) => !checkbox.checked)).toBe(true);
    expect(host.querySelector(".summary-progress-checkbox")).not.toBeNull();

    await act(async () => {
      checkboxes[0].click();
      checkboxes[1].click();
    });
    await act(async () => findButton("删除已选（2）").click());
    let confirmDialog = host.querySelector<HTMLElement>(
      ".progress-delete-modal",
    )!;
    expect(confirmDialog.textContent).toContain("删除选中的 2 条推进记录？");
    await act(async () => {
      [...confirmDialog.querySelectorAll<HTMLButtonElement>("button")]
        .find((button) => button.textContent?.trim() === "取消")!
        .click();
    });
    expect(
      [...host.querySelectorAll<HTMLInputElement>(".summary-progress-checkbox")]
        .filter((checkbox) => checkbox.checked),
    ).toHaveLength(2);
    expect(requestSpy).not.toHaveBeenCalledWith(
      "progress.deleteBatch",
      expect.anything(),
    );

    await act(async () => findButton("删除已选（2）").click());
    confirmDialog = host.querySelector<HTMLElement>(".progress-delete-modal")!;
    await act(async () => {
      confirmDialog
        .querySelector<HTMLButtonElement>(".progress-delete-confirm-button")!
        .click();
      await new Promise((resolve) => window.setTimeout(resolve, 20));
    });
    expect(requestSpy).toHaveBeenCalledWith("progress.deleteBatch", {
      recordIds: expect.any(Array),
    });
    expect(host.querySelector(".summary-progress-checkbox")).toBeNull();
    expect(host.querySelectorAll(".summary-progress-row")).toHaveLength(1);
  });
});
