/// <reference types="vite/client" />

interface TaskatoWebView {
  postMessage(message: unknown): void;
  addEventListener(type: "message", listener: (event: MessageEvent) => void): void;
  removeEventListener(type: "message", listener: (event: MessageEvent) => void): void;
}
interface Window {
  chrome?: {
    webview?: TaskatoWebView;
  };
}
