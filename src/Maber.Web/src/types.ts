// ===== 宿主 → Web =====
export type HostMessage =
  | { type: "loadDocument"; name: string; content: string }
  | { type: "setMode"; mode: "render" | "source" }
  | { type: "setTheme"; theme: "light" | "dark" }
  | { type: "scrollToHeading"; line: number }
  | { type: "find"; query: string; direction: 1 | -1 };

// ===== Web → 宿主 =====
export type WebMessage =
  | { type: "ready" }
  | { type: "outlineChanged"; items: OutlineItem[] }
  | { type: "checkboxToggled"; line: number; checked: boolean }
  | { type: "contentChanged"; content: string }
  | { type: "openLink"; href: string };

export interface OutlineItem { level: number; text: string; line: number }
export type ViewMode = "render" | "source";
