import { act } from "react";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import {
  GlobalSearchDialog,
  NotificationCenter,
} from "../src/components/AppChrome";

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
});
