import { afterEach, describe, expect, it, vi } from "vitest";
import { WebViewBridge } from "../src/bridge/client";
import { MockBridge } from "../src/bridge/mock";

describe("MockBridge", () => {
  it("保持主任务与子任务输入状态分离", async () => {
    const bridge = new MockBridge();
    await bridge.request("tasks.add", { title: "新的子任务", parentId: 1 });
    const result = await bridge.request<{
      tasks: Array<{ title: string; parentId: number | null }>;
    }>("tasks.list");
    expect(result.tasks).toContainEqual(
      expect.objectContaining({ title: "新的子任务", parentId: 1 }),
    );
  });

  it("能够返回四个页面需要的基础数据", async () => {
    const bridge = new MockBridge();
    await expect(bridge.request("tasks.list")).resolves.toBeTruthy();
    await expect(bridge.request("summary.get")).resolves.toBeTruthy();
    await expect(bridge.request("history.events")).resolves.toBeTruthy();
    await expect(bridge.request("settings.get")).resolves.toBeTruthy();
  });

  it("日夜模式通过设置桥接持久化并返回最新状态", async () => {
    const bridge = new MockBridge();
    const saved = await bridge.request<{ appearanceMode: string }>(
      "settings.save",
      { appearanceMode: "light" },
    );
    expect(saved.appearanceMode).toBe("light");
    await expect(bridge.request("settings.get")).resolves.toEqual(
      expect.objectContaining({ appearanceMode: "light" }),
    );
  });

  it("提供历史时间线与设置系统能力的浏览器降级响应", async () => {
    const bridge = new MockBridge();
    const events =
      await bridge.request<Array<{ eventType: string }>>("history.events");
    expect(events.some((item) => item.eventType === "completed")).toBe(true);
    await expect(
      bridge.request("settings.previewSound", { notificationSoundChoice: 3 }),
    ).resolves.toEqual({ played: true });
    await expect(bridge.request("settings.chooseSound")).resolves.toEqual({
      selected: false,
      path: "",
    });
    await expect(bridge.request("app.backupData")).resolves.toEqual(
      expect.objectContaining({ created: true }),
    );
    await expect(bridge.request("app.restoreData")).resolves.toEqual(
      expect.objectContaining({ staged: true, restartRequired: true }),
    );
  });

  it("全局搜索同时覆盖任务笔记和活动日志", async () => {
    const bridge = new MockBridge();
    const noteResult = await bridge.request<{
      tasks: Array<{ id: number }>;
      activities: Array<{ id: string }>;
    }>("search.global", { keyword: "发布" });
    expect(noteResult.tasks.length).toBeGreaterThan(0);

    const activityResult = await bridge.request<{
      tasks: Array<{ id: number }>;
      activities: Array<{ id: string }>;
    }>("search.global", { keyword: "浏览器" });
    expect(activityResult.activities.length).toBeGreaterThan(0);
  });
});

describe("WebViewBridge", () => {
  afterEach(() => {
    vi.restoreAllMocks();
    Reflect.deleteProperty(window, "chrome");
  });

  it("为请求生成 id，并按相同 id 解析响应", async () => {
    const sent: Array<{ id: string; method: string }> = [];
    let messageListener: ((event: MessageEvent) => void) | undefined;
    Object.defineProperty(window, "chrome", {
      configurable: true,
      value: {
        webview: {
          postMessage(message: { id: string; method: string }) {
            sent.push(message);
          },
          addEventListener(
            _type: "message",
            listener: (event: MessageEvent) => void,
          ) {
            messageListener = listener;
          },
          removeEventListener() {},
        },
      },
    });

    const bridge = new WebViewBridge();
    const response = bridge.request<{ protocolVersion: number }>("app.ready");
    expect(sent).toHaveLength(1);
    expect(sent[0].id).toMatch(/^web-/);
    messageListener?.(
      new MessageEvent("message", {
        data: { id: sent[0].id, ok: true, result: { protocolVersion: 1 } },
      }),
    );
    await expect(response).resolves.toEqual({ protocolVersion: 1 });
  });

  it("把 C# 结构化错误转换为前端异常", async () => {
    let requestId = "";
    let messageListener: ((event: MessageEvent) => void) | undefined;
    Object.defineProperty(window, "chrome", {
      configurable: true,
      value: {
        webview: {
          postMessage(message: { id: string }) {
            requestId = message.id;
          },
          addEventListener(
            _type: "message",
            listener: (event: MessageEvent) => void,
          ) {
            messageListener = listener;
          },
          removeEventListener() {},
        },
      },
    });
    const bridge = new WebViewBridge();
    const response = bridge.request("unknown.method");
    messageListener?.(
      new MessageEvent("message", {
        data: {
          id: requestId,
          ok: false,
          error: { code: "method_not_found", message: "不支持的方法" },
        },
      }),
    );
    await expect(response).rejects.toThrow("不支持的方法");
  });
});
