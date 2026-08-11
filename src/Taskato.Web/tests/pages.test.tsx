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
import { bridge, type TaskDto, type TaskListResult } from "../src/bridge";

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

  it("任务页可独立加载任务树、详情与计时器", async () => {
    await renderAndFlush(
      <TaskPage reportError={() => undefined} requestedTaskId={null} />,
    );
    expect(host.textContent).toContain("项目与任务");
    expect(host.textContent).toContain("任务组");
    expect(host.textContent).toContain("未归类任务");
    expect(host.textContent).toContain("当前专注");
    expect(host.textContent).toContain("任务内容");
    expect(host.querySelector('[aria-label="新建顶级任务"]')).not.toBeNull();
    expect(host.querySelector('[aria-label="添加顶级任务"]')).not.toBeNull();
    expect(host.querySelector('[aria-label="完成父任务"]')).not.toBeNull();
    expect(host.querySelector('[aria-label="新任务优先级"]')).not.toBeNull();
    expect(host.querySelector('[aria-label="子任务优先级"]')).not.toBeNull();
    expect(host.querySelector('[aria-label="减少五分钟"]')).not.toBeNull();
    expect(host.querySelector('[aria-label="增加五分钟"]')).not.toBeNull();
    expect(host.querySelector(".timer-display-stage")).not.toBeNull();
    expect(host.querySelectorAll('[role="separator"]')).toHaveLength(2);
    expect(host.querySelector('[aria-haspopup="tree"]')).not.toBeNull();
    expect(host.querySelector("#task-operations select")).toBeNull();
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

  it("添加子任务输入栏显示在子任务列表顶部", async () => {
    await renderAndFlush(
      <TaskPage reportError={() => undefined} requestedTaskId={null} />,
    );

    const composer = host.querySelector(".subtask-composer")!;
    const list = host.querySelector(".subtask-list")!;

    expect(composer).not.toBeNull();
    expect(list).not.toBeNull();
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
    expect(host.querySelector(".subtask-list")?.textContent).toContain(
      hiddenHistoricalChild.title,
    );
    expect(host.querySelector(".content-section")?.textContent).toContain(
      "1/1 已完成",
    );
  });

  it("常驻输入框可新增顶级任务，番茄钟可在空闲时调整时长", async () => {
    await renderAndFlush(
      <TaskPage reportError={() => undefined} requestedTaskId={null} />,
    );

    const input = host.querySelector<HTMLInputElement>(
      '[aria-label="新建顶级任务"]',
    )!;
    const highPriority = host.querySelector<HTMLButtonElement>(
      '[aria-label="新任务优先级：高"]',
    )!;
    await act(async () => highPriority.click());
    expect(highPriority.getAttribute("aria-checked")).toBe("true");
    await act(async () => {
      const valueSetter = Object.getOwnPropertyDescriptor(
        HTMLInputElement.prototype,
        "value",
      )!.set!;
      valueSetter.call(input, "从常驻入口新增的任务");
      input.dispatchEvent(new Event("input", { bubbles: true }));
    });
    const addButton = host.querySelector<HTMLButtonElement>(
      '[aria-label="添加顶级任务"]',
    )!;
    expect(addButton.disabled).toBe(false);
    await act(async () => {
      addButton.click();
      await new Promise((resolve) => window.setTimeout(resolve, 0));
    });
    expect(host.textContent).toContain("从常驻入口新增的任务");
    expect(
      host
        .querySelector('[aria-label="新任务优先级：无"]')
        ?.getAttribute("aria-checked"),
    ).toBe("true");

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

    const rootInput = host.querySelector<HTMLInputElement>(
      '[aria-label="新建顶级任务"]',
    )!;
    await act(async () => {
      const valueSetter = Object.getOwnPropertyDescriptor(
        HTMLInputElement.prototype,
        "value",
      )!.set!;
      valueSetter.call(rootInput, "收起时新增的顶级任务");
      rootInput.dispatchEvent(new Event("input", { bubbles: true }));
    });
    await act(async () => {
      host
        .querySelector<HTMLButtonElement>('[aria-label="添加顶级任务"]')!
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
      valueSetter.call(editor, "唯一的任务内容");
      editor.dispatchEvent(new Event("input", { bubbles: true }));
      await new Promise((resolve) => window.setTimeout(resolve, 800));
    });

    expect(requestSpy).toHaveBeenCalledWith(
      "tasks.update",
      expect.objectContaining({ title: "唯一的任务内容" }),
    );
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
      '[aria-label="补充任务排序方式"]',
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
});
