import { act } from "react";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, beforeEach, describe, expect, it } from "vitest";
import {
  HistoryPage,
  SettingsPage,
  SummaryPage,
  TaskPage,
} from "../src/pages/WorkspacePages";

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
    expect(host.textContent).toContain("备份与恢复");
  });
});
