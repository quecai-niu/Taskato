import type { BridgeApi, BridgeEvent } from "./types";

interface PendingRequest {
  resolve(value: unknown): void;
  reject(reason: Error): void;
  timeout: ReturnType<typeof setTimeout>;
}

interface BridgeResponse {
  id: string;
  ok: boolean;
  result?: unknown;
  error?: { code: string; message: string };
}

export class WebViewBridge implements BridgeApi {
  readonly isNative = true;
  private sequence = 0;
  private readonly pending = new Map<string, PendingRequest>();
  private readonly listeners = new Map<
    string,
    Set<(payload: unknown) => void>
  >();

  constructor() {
    window.chrome?.webview?.addEventListener("message", this.handleMessage);
  }

  request<T>(method: string, params: Record<string, unknown> = {}): Promise<T> {
    const id = `web-${Date.now()}-${++this.sequence}`;
    return new Promise<T>((resolve, reject) => {
      const timeout = setTimeout(() => {
        this.pending.delete(id);
        reject(new Error(`请求超时：${method}`));
      }, 15_000);
      this.pending.set(id, {
        resolve: resolve as (value: unknown) => void,
        reject,
        timeout,
      });
      try {
        window.chrome!.webview!.postMessage({ id, method, params });
      } catch (error) {
        clearTimeout(timeout);
        this.pending.delete(id);
        reject(
          error instanceof Error ? error : new Error("无法发送本地请求。"),
        );
      }
    });
  }

  subscribe(
    eventName: string,
    listener: (payload: unknown) => void,
  ): () => void {
    const group = this.listeners.get(eventName) ?? new Set();
    group.add(listener);
    this.listeners.set(eventName, group);
    return () => group.delete(listener);
  }

  private readonly handleMessage = (message: MessageEvent): void => {
    const data = message.data as BridgeResponse | BridgeEvent;
    if (!data || typeof data !== "object") return;
    if ("event" in data) {
      this.listeners
        .get(data.event)
        ?.forEach((listener) => listener(data.payload));
      return;
    }

    const pending = this.pending.get(data.id);
    if (!pending) return;
    clearTimeout(pending.timeout);
    this.pending.delete(data.id);
    if (data.ok) pending.resolve(data.result);
    else pending.reject(new Error(data.error?.message ?? "本地操作失败。"));
  };
}
