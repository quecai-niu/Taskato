import { act } from "react";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, beforeEach, describe, expect, it } from "vitest";
import {
  HistoryPage,
  SettingsPage,
  SummaryPage,
  TaskPage,
} from "../src/pages/WorkspacePages";
import { buildTree, filterTasksWithAncestors } from "../src/pages/TaskPage";
import type { TaskDto } from "../src/bridge";

describe("独立业务页面", () => {
  let host: HTMLDivElement;
  let root: Root;

  beforeEach(() => {
    (
      globalThis as typeof globalThis & {
        IS_REACT_ACT_ENVIRONMENT?: boolean;
      }
    ).IS_REACT_ACT_ENVIRONMENT = true;
    host = document.createElement("div");
    document.body.append(host);
    root = createRoot(host);
  });

  afterEach(async () => {
    await act(async () => root.unmount());
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
    expect(host.textContent).toContain("任务笔记");
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
    expect(host.textContent).toContain("备份与恢复");
    expect(host.textContent).toContain("立即切换到旧版界面");
  });
});
