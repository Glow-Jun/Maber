// ===== 宿主 → Web =====
export type HostMessage =
  | { type: "loadDocument"; name: string; content: string; contentBase?: string }
  | { type: "setMode"; mode: "render" | "source" }
  | { type: "setTheme"; theme: "light" | "dark" }
  | { type: "scrollToHeading"; line: number }
  | { type: "find"; query: string; direction: 1 | -1 };

export type AcceleratorKey = "o" | "s" | "e" | "k" | "f";

// ===== Web → 宿主 =====
export type WebMessage =
  | { type: "ready" }
  | { type: "accelerator"; key: AcceleratorKey }
  | { type: "outlineChanged"; items: OutlineItem[] }
  | { type: "checkboxToggled"; line: number; checked: boolean }
  | { type: "contentChanged"; content: string }
  | { type: "openLink"; href: string }
  | { type: "openWikilink"; target: string };

export interface OutlineItem { level: number; text: string; line: number }
export type ViewMode = "render" | "source";
