import type { BridgeApi } from "./types";
import { WebViewBridge } from "./client";
import { MockBridge } from "./mock";

export const bridge: BridgeApi = window.chrome?.webview ? new WebViewBridge() : new MockBridge();
export * from "./types";
