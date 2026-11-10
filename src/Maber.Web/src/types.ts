// ===== 宿主 → Web =====
export type HostMessage =
  | { type: "loadDocument"; name: string; content: string; contentBase?: string }
  | { type: "setMode"; mode: "render" | "source" }
  | { type: "setTheme"; theme: "light" | "dark" }
  | { type: "setTypography" } & TypographyPayload
  | { type: "setShortcuts"; shortcuts: { id: string; gesture: string }[] }
  | { type: "setEmptyState"; visible: boolean }
  | { type: "scrollToHeading"; line: number }
  | { type: "restoreReadingPosition"; ratio: number }
  | { type: "find"; query: string; direction: 1 | -1; wholeWord?: boolean };

export interface TypographyPayload {
  fontFamily: string;
  customFontFamily?: string;
  fontSize: number;
  lineHeight: number;
  contentWidth: number;
  codeFontFamily?: string;
  codeFontSize?: number;
  renderMath?: boolean;
  renderMermaid?: boolean;
  fontFaces?: { family: string; url: string }[];
}

// ===== Web → 宿主 =====
export type WebMessage =
  | { type: "ready" }
  | { type: "accelerator"; gesture: string }
  | { type: "outlineChanged"; items: OutlineItem[] }
  | { type: "checkboxToggled"; line: number; checked: boolean }
  | { type: "contentChanged"; content: string }
  | { type: "readingPosition"; ratio: number }
  | { type: "openLink"; href: string }
  | { type: "openWikilink"; target: string; anchor?: string }
  | { type: "diag"; text: string };

export interface OutlineItem { level: number; text: string; line: number }
export type ViewMode = "render" | "source";
