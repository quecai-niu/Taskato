import { act } from "react";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import {
  GlobalSearchDialog,
  NotificationCenter,
  TopBar,
} from "../src/components/AppChrome";
import { bridge } from "../src/bridge";

describe("应用框架交互", () => {
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
      root.render(<TopBar activePage="tasks" appearanceMode="dark" onSearch={() => undefined} onNotifications={() => undefined} onToggleAppearance={() => undefined} />);
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
      root.render(<TopBar activePage="tasks" appearanceMode="dark" onSearch={() => undefined} onNotifications={() => undefined} onToggleAppearance={toggle} />);
    });

    const button = host.querySelector<HTMLButtonElement>('[aria-label="切换到日间模式"]');
    expect(button?.textContent).toContain("日间");
    button?.click();
    expect(toggle).toHaveBeenCalledOnce();
  });
});
