import type { HostMessage, WebMessage } from "./types";

// WebView2 宿主注入的全局对象；浏览器/测试环境不存在，一律走可选链
declare global {
  interface Window {
    chrome?: {
      webview?: {
        postMessage(message: unknown): void;
        addEventListener(type: "message", listener: (ev: { data: unknown }) => void): void;
      };
    };
  }
}

export function send(msg: WebMessage) {
  // 直接传对象：传字符串会被 WebView2 再做一次 JSON 编码，宿主 parse 出的是字符串基元
  window.chrome?.webview?.postMessage(msg);
}

export function onHostMessage(handler: (msg: HostMessage) => void) {
  window.chrome?.webview?.addEventListener("message", (e) => {
    try {
      // WebView2 的 PostWebMessageAsJson 送达时 data 已是解析后的对象；字符串形态兜底
      const data = e.data;
      handler(typeof data === "string" ? JSON.parse(data) : (data as HostMessage));
    } catch { /* 非 JSON 忽略 */ }
  });
}
