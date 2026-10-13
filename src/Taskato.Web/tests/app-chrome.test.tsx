import { act } from "react";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import {
  GlobalSearchDialog,
  NotificationCenter,
  TopBar,
} from "../src/components/AppChrome";
import { bridge } from "../src/bridge";
import { App } from "../src/App";

describe("应用框架交互", () => {
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
    host.remove();
    vi.restoreAllMocks();
  });

  it("全局搜索是可访问的模态框，并支持 Escape 关闭", async () => {
    const close = vi.fn();
    await act(async () => {
      root.render(
        <GlobalSearchDialog
          open
          onClose={close}
          onOpenTask={() => undefined}
          onOpenHistory={() => undefined}
        />,
      );
    });

    expect(host.querySelector('[role="dialog"]')?.getAttribute("aria-label")).toBe(
      "全局搜索",
    );
    window.dispatchEvent(new KeyboardEvent("keydown", { key: "Escape" }));
    expect(close).toHaveBeenCalledOnce();
  });

  it("通知中心从桥接加载当天的重要任务变化", async () => {
    await act(async () => {
      root.render(
        <NotificationCenter
          open
          onClose={() => undefined}
          onOpenHistory={() => undefined}
          onOpenSettings={() => undefined}
        />,
      );
      await Promise.resolve();
    });

    expect(host.textContent).toContain("最近通知");
    expect(host.textContent).toContain("完成任务");
    expect(host.textContent).toContain("查看完整历史");
  });

  it("标题栏空白区域请求原生拖动，按钮区域不会误触发", async () => {
    const request = vi.spyOn(bridge, "request");
    await act(async () => {
      root.render(<TopBar reportError={(error) => { throw error; }} activePage="tasks" appearanceMode="dark" onSearch={() => undefined} onNotifications={() => undefined} onToggleAppearance={() => undefined} />);
    });
    host.querySelector<HTMLElement>(".top-bar")?.dispatchEvent(new MouseEvent("mousedown", { bubbles: true, button: 0 }));
    expect(request).toHaveBeenCalledWith("window.drag");
    request.mockClear();
    host.querySelector<HTMLElement>(".search-command")?.dispatchEvent(new MouseEvent("mousedown", { bubbles: true, button: 0 }));
    expect(request).not.toHaveBeenCalled();
  });

  it("标题栏提供带当前状态的日夜模式快捷切换", async () => {
    const toggle = vi.fn();
    await act(async () => {
      root.render(<TopBar reportError={(error) => { throw error; }} activePage="tasks" appearanceMode="dark" onSearch={() => undefined} onNotifications={() => undefined} onToggleAppearance={toggle} />);
    });

    const button = host.querySelector<HTMLButtonElement>('[aria-label="切换到日间模式"]');
    expect(button?.textContent).toContain("日间");
    button?.click();
    expect(toggle).toHaveBeenCalledOnce();
  });

  it("标题栏始终提供完整的原生窗口控制按钮", async () => {
    await act(async () => {
      root.render(<TopBar reportError={(error) => { throw error; }} activePage="tasks" appearanceMode="dark" onSearch={() => undefined} onNotifications={() => undefined} onToggleAppearance={() => undefined} />);
    });

    expect(host.querySelector('[aria-label="最小化"]')).not.toBeNull();
    expect(host.querySelector('[aria-label="最大化"]')).not.toBeNull();
    expect(host.querySelector('[aria-label="关闭到托盘"]')).not.toBeNull();
  });

  it("左侧导航始终收起且没有展开按钮", async () => {
    window.localStorage.setItem("taskato.navCollapsed", "false");
    await act(async () => {
      root.render(<App />);
      await new Promise((resolve) => window.setTimeout(resolve, 0));
    });

    expect(host.querySelector(".app-shell.nav-is-collapsed")).not.toBeNull();
    expect(host.querySelector('[aria-label="展开导航"]')).toBeNull();
    expect(host.querySelector('[aria-label="收起导航"]')).toBeNull();
  });

  it("切换菜单后恢复当前会话中选中的任务", async () => {
    await act(async () => {
      root.render(<App />);
      await new Promise((resolve) => window.setTimeout(resolve, 0));
    });

    const selectedTask = [...host.querySelectorAll<HTMLButtonElement>(".tree-title")]
      .find((button) => button.textContent?.includes("确定新版信息架构"))!;
    expect(selectedTask).not.toBeNull();
    await act(async () => {
      selectedTask.click();
      await new Promise((resolve) => window.setTimeout(resolve, 0));
    });
    expect(
      host.querySelector<HTMLTextAreaElement>('[aria-label="任务内容"]')?.value,
    ).toBe("确定新版信息架构");

    const navigate = (label: string) =>
      [...host.querySelectorAll<HTMLButtonElement>(".nav-item")].find(
        (button) => button.getAttribute("aria-label") === label,
      )!;
    await act(async () => {
      navigate("历史记录").click();
      await new Promise((resolve) => window.setTimeout(resolve, 0));
    });
    await act(async () => {
      navigate("任务").click();
      await new Promise((resolve) => window.setTimeout(resolve, 0));
    });

    expect(
      host.querySelector<HTMLTextAreaElement>('[aria-label="任务内容"]')?.value,
    ).toBe("确定新版信息架构");
    expect(host.querySelector(".tree-row.selected")?.textContent).toContain(
      "确定新版信息架构",
    );
  });

  it("新增任务视图和应用导航会实时同步完成记录上下文", async () => {
    const request = vi.spyOn(bridge, "request");
    await act(async () => {
      root.render(<App />);
      await new Promise((resolve) => window.setTimeout(resolve, 0));
    });

    await act(async () => {
      host.querySelector<HTMLButtonElement>(".open-task-create")!.click();
      await new Promise((resolve) => window.setTimeout(resolve, 0));
    });
    expect(request).toHaveBeenCalledWith("workspace.context.set", {
      activePage: "tasks",
      taskViewMode: "create",
      selectedTaskId: expect.any(Number),
      progressTargetTaskId: null,
    });

    const settingsButton = [...host.querySelectorAll<HTMLButtonElement>(".nav-item")]
      .find((button) => button.getAttribute("aria-label") === "设置")!;
    await act(async () => {
      settingsButton.click();
      await new Promise((resolve) => window.setTimeout(resolve, 0));
    });
    expect(request).toHaveBeenCalledWith("workspace.context.set", {
      activePage: "settings",
      taskViewMode: "create",
      selectedTaskId: expect.any(Number),
      progressTargetTaskId: null,
    });
  });

  it("任务详情选中的直属子任务会同步为番茄钟专注目标", async () => {
    const request = vi.spyOn(bridge, "request");
    await act(async () => {
      root.render(<App />);
      await new Promise((resolve) => window.setTimeout(resolve, 0));
    });

    const subtaskRow = [...host.querySelectorAll<HTMLElement>(".subtask-row")]
      .find((row) => row.textContent?.includes("重构任务工作区"))!;
    await act(async () => {
      subtaskRow.click();
      await new Promise((resolve) => window.setTimeout(resolve, 0));
    });

    expect(request).toHaveBeenCalledWith("workspace.context.set", {
      activePage: "tasks",
      taskViewMode: "detail",
      selectedTaskId: 1,
      progressTargetTaskId: 3,
    });
  });
});
