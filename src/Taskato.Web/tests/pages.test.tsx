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
    expect(host.textContent).toContain("当前专注");
    expect(host.textContent).toContain("任务笔记");
    expect(host.querySelectorAll('[role="separator"]')).toHaveLength(2);
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
    expect(host.textContent).toContain("番茄钟时长");
    expect(host.textContent).toContain("提示音");
    expect(host.textContent).toContain("飞书通知");
    expect(host.textContent).toContain("备份与恢复");
  });
});
