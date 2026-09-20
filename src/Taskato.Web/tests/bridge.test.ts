import { afterEach, describe, expect, it, vi } from "vitest";
import { WebViewBridge } from "../src/bridge/client";
import { MockBridge } from "../src/bridge/mock";
import type {
  SummaryResult,
  TaskDto,
  TaskListResult,
  TimerListResult,
} from "../src/bridge/types";

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
    const summary = await bridge.request<SummaryResult>("summary.get");
    expect(summary.groups[0]?.records[0]?.pathSnapshot).toBe(
      "发布 Taskato 3.0 / 确定新版信息架构",
    );
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

  it("番茄钟 Mock 仅在运行中按分钟档位调整剩余时间", async () => {
    const bridge = new MockBridge();
    await bridge.request("pomodoro.command", {
      command: "start",
      timerId: "timer-0",
    });
    let timers = await bridge.request<TimerListResult>("pomodoro.list");
    expect(timers.timers[0]).toEqual(
      expect.objectContaining({
        phase: "work",
        canAdjustRemaining: true,
        maximumAdjustableSeconds: 2250,
      }),
    );

    await bridge.request("pomodoro.command", {
      command: "adjustRemaining",
      timerId: "timer-0",
      remainingSeconds: 2250,
    });
    timers = await bridge.request<TimerListResult>("pomodoro.list");
    expect(timers.timers[0].display).toBe("37:30");
    expect(timers.timers[0].remainingSeconds).toBe(2250);

    await bridge.request("pomodoro.command", {
      command: "pause",
      timerId: "timer-0",
    });
    await expect(
      bridge.request("pomodoro.command", {
        command: "adjustRemaining",
        timerId: "timer-0",
        remainingSeconds: 60,
      }),
    ).rejects.toThrow("只能在专注或休息倒计时运行时调整剩余时间");
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

  it("同级任务顺序可持久化且完成状态切换不会改写排序号", async () => {
    const bridge = new MockBridge();
    const first = await bridge.request<TaskDto>("tasks.add", {
      title: "先新增的排序任务",
      parentId: 1,
      priority: 2,
    });
    const second = await bridge.request<TaskDto>("tasks.add", {
      title: "后新增的排序任务",
      parentId: 1,
      priority: 2,
    });
    expect(second.orderIndex).toBeLessThan(first.orderIndex);

    await bridge.request("tasks.reorder", { taskIds: [first.id, second.id] });
    let taskList = await bridge.request<TaskListResult>("tasks.list");
    let firstAfterReorder = taskList.detailTasks.find(
      (task) => task.id === first.id,
    )!;
    const secondAfterReorder = taskList.detailTasks.find(
      (task) => task.id === second.id,
    )!;
    expect(firstAfterReorder.orderIndex).toBeLessThan(
      secondAfterReorder.orderIndex,
    );

    const preservedOrderIndex = firstAfterReorder.orderIndex;
    await bridge.request("tasks.toggle", {
      taskId: first.id,
      completed: true,
    });
    await bridge.request("tasks.toggle", {
      taskId: first.id,
      completed: false,
    });
    taskList = await bridge.request<TaskListResult>("tasks.list");
    firstAfterReorder = taskList.detailTasks.find(
      (task) => task.id === first.id,
    )!;
    expect(firstAfterReorder.orderIndex).toBe(preservedOrderIndex);

    await expect(
      bridge.request("tasks.reorder", { taskIds: [first.id, 5] }),
    ).rejects.toThrow("同一优先级");
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
