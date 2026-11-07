import type { HostMessage, WebMessage } from "./types";

// WebView2 宿主注入的全局对象；浏览器/测试环境不存在，一律走可选链
declare global {
  interface Window {
    chrome?: {
      webview?: {
        postMessage(message: string): void;
        addEventListener(type: "message", listener: (ev: { data: unknown }) => void): void;
      };
    };
  }
}

export function send(msg: WebMessage) {
  window.chrome?.webview?.postMessage(JSON.stringify(msg));
}

export function onHostMessage(handler: (msg: HostMessage) => void) {
  window.chrome?.webview?.addEventListener("message", (e) => {
    try { handler(JSON.parse(e.data as string)); } catch { /* 非 JSON 忽略 */ }
  });
}
